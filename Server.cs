using System.Collections.Concurrent;
using System.Text.Json;
using Fleck;

namespace Wiremote
{
    public static class Server
    {
        public const int HTTP_PORT = 8765;
        public const int WS_PORT = 8766;

        public static string AuthToken { get; private set; } = "";

        static readonly ConcurrentDictionary<Guid, IWebSocketConnection> allSockets = new();
        static readonly ConcurrentDictionary<Guid, bool> authenticatedSockets = new();
        static WebSocketServer? _wsServer;
        static Timer? _heartbeatTimer;

        public delegate Task CommandHandler(IWebSocketConnection socket, JsonElement root);
        private static readonly Dictionary<string, CommandHandler> _commandRouter = new();

        public static void InitAuthToken()
        {
            try
            {
                string tokenPath = Path.Combine(AppContext.BaseDirectory, "wiremote.token");
                if (File.Exists(tokenPath))
                {
                    var existing = File.ReadAllText(tokenPath).Trim();
                    if (!string.IsNullOrEmpty(existing))
                    {
                        AuthToken = existing;
                        return;
                    }
                }

                // Generate a cryptographically random 16-character alphanumeric token
                AuthToken = Guid.NewGuid().ToString("N")[..16];
                File.WriteAllText(tokenPath, AuthToken);
            }
            catch
            {
                AuthToken = Guid.NewGuid().ToString("N")[..16];
            }
        }

        public static void RegisterCommand(string command, CommandHandler handler)
        {
            _commandRouter[command] = handler;
        }

        public static void Broadcast(string msg)
        {
            foreach (var kv in allSockets)
            {
                if (!kv.Value.IsAvailable)
                {
                    continue;
                }

                try
                {
                    _ = kv.Value.Send(msg);
                }
                catch { }
            }
        }

        public static void StartWebSocketServer()
        {
            try
            {
                _wsServer = new WebSocketServer($"ws://0.0.0.0:{WS_PORT}");
                _wsServer.RestartAfterListenError = true;
                _wsServer.Start(socket =>
                {
                    socket.OnOpen = () =>
                    {
                        allSockets[socket.ConnectionInfo.Id] = socket;
                        Logger.Log("CONNECT", $"{socket.ConnectionInfo.ClientIpAddress} connected (awaiting auth)", ConsoleColor.Cyan);
                    };

                    socket.OnClose = () =>
                    {
                        allSockets.TryRemove(socket.ConnectionInfo.Id, out _);
                        authenticatedSockets.TryRemove(socket.ConnectionInfo.Id, out _);
                        InputService.RemoveSession(socket.ConnectionInfo.Id);
                        Logger.Log("DISCONNECT", $"{socket.ConnectionInfo.ClientIpAddress} disconnected ({authenticatedSockets.Count} active)", ConsoleColor.DarkCyan);
                    };

                    socket.OnError = _ =>
                    {
                        if (!socket.IsAvailable)
                        {
                            allSockets.TryRemove(socket.ConnectionInfo.Id, out var _);
                            authenticatedSockets.TryRemove(socket.ConnectionInfo.Id, out var _);
                            InputService.RemoveSession(socket.ConnectionInfo.Id);
                        }
                    };

                    socket.OnMessage = async message =>
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(message);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("command", out var cmdProp))
                            {
                                var command = cmdProp.GetString();

                                if (command == "ping")
                                {
                                    try { _ = socket.Send("{\"type\":\"pong\"}"); } catch { }
                                    return;
                                }

                                if (command == "auth")
                                {
                                    string? clientToken = null;
                                    if (root.TryGetProperty("token", out var tokProp))
                                    {
                                        clientToken = tokProp.GetString();
                                    }

                                    bool tokenMatches = !string.IsNullOrEmpty(clientToken) && clientToken == AuthToken;
                                    bool isFirstDevice = authenticatedSockets.IsEmpty;

                                    if (tokenMatches || isFirstDevice)
                                    {
                                        authenticatedSockets[socket.ConnectionInfo.Id] = true;

                                        // Session Takeover: Close any previous/stale sockets (Single Active Controller)
                                        foreach (var kv in allSockets)
                                        {
                                            if (kv.Key != socket.ConnectionInfo.Id)
                                            {
                                                try { kv.Value.Close(); } catch { }
                                                allSockets.TryRemove(kv.Key, out var _);
                                                authenticatedSockets.TryRemove(kv.Key, out var _);
                                                InputService.RemoveSession(kv.Key);
                                            }
                                        }

                                        try { _ = socket.Send($"{{\"type\":\"auth_ok\",\"token\":\"{AuthToken}\"}}"); } catch { }
                                        AudioService.SendInitialState(socket);
                                        BrightnessService.SendBrightness(socket);
                                        DisplayManager.SendDisplays(socket);
                                        _ = MediaService.SendNowPlayingAsync(socket);
                                        PowerService.SendInitialState(socket);
                                        Logger.Log("AUTH", $"{socket.ConnectionInfo.ClientIpAddress} authenticated (Active Controller)", ConsoleColor.Green);
                                    }
                                    else
                                    {
                                        Logger.Log("AUTH", $"Unauthorized connection attempt from {socket.ConnectionInfo.ClientIpAddress}", ConsoleColor.Red);
                                        try { _ = socket.Send("{\"type\":\"auth_error\",\"message\":\"Invalid or missing token\"}"); } catch { }
                                    }
                                    return;
                                }

                                // Reject unauthenticated commands
                                if (!authenticatedSockets.ContainsKey(socket.ConnectionInfo.Id))
                                {
                                    // If no controller is active, auto-pair this connection so user is not locked out
                                    if (authenticatedSockets.IsEmpty)
                                    {
                                        authenticatedSockets[socket.ConnectionInfo.Id] = true;
                                        try { _ = socket.Send($"{{\"type\":\"auth_ok\",\"token\":\"{AuthToken}\"}}"); } catch { }
                                        Logger.Log("AUTH", $"{socket.ConnectionInfo.ClientIpAddress} auto-authenticated", ConsoleColor.Green);
                                    }
                                    else
                                    {
                                        Logger.Log("SECURITY", $"Blocked unauthenticated command '{command}' from {socket.ConnectionInfo.ClientIpAddress}", ConsoleColor.Red);
                                        try { _ = socket.Send("{\"type\":\"auth_required\"}"); } catch { }
                                        return;
                                    }
                                }

                                if (command != null && _commandRouter.TryGetValue(command, out var handler))
                                {
                                    await handler(socket, root);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log("ERROR", $"WS message handling failed: {ex.Message}", ConsoleColor.Red);
                        }
                    };
                });

                // Heartbeat to clean up dead half-open sockets every 10 seconds
                _heartbeatTimer = new Timer(state =>
                {
                    foreach (var kv in allSockets)
                    {
                        if (!kv.Value.IsAvailable)
                        {
                            allSockets.TryRemove(kv.Key, out var _);
                            authenticatedSockets.TryRemove(kv.Key, out var _);
                            try { kv.Value.Close(); } catch { }
                        }
                    }
                }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                Logger.Log("WS", $"Failed to start WebSocket server on port {WS_PORT}: {ex.Message}", ConsoleColor.Red);
            }
        }

        public static void Stop()
        {
            _heartbeatTimer?.Dispose();
            try { _wsServer?.Dispose(); } catch { }
            foreach (var socket in allSockets.Values)
            {
                try { socket.Close(); } catch { }
            }
            allSockets.Clear();
            authenticatedSockets.Clear();
        }
    }
}