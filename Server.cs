using System.Collections.Concurrent;
using System.Text.Json;
using Fleck;

namespace Winremote
{
    public static class Server
    {
        public const int HTTP_PORT = 8765;
        public const int WS_PORT = 8766;

        static readonly ConcurrentDictionary<Guid, IWebSocketConnection> allSockets = new();
        static WebSocketServer? _wsServer;
        public delegate Task CommandHandler(IWebSocketConnection socket, JsonElement root);
        private static readonly Dictionary<string, CommandHandler> _commandRouter = new();

        public static void RegisterCommand(string command, CommandHandler handler)
        {
            _commandRouter[command] = handler;
        }

        public static void Broadcast(string msg)
        {
            foreach (var kv in allSockets)
            {
                try { kv.Value.Send(msg); } catch { }
            }
        }

        public static void StartWebSocketServer()
        {
            try
            {
                _wsServer = new WebSocketServer($"ws://0.0.0.0:{WS_PORT}");
                _wsServer.Start(socket =>
                {
                    socket.OnOpen = () =>
                    {
                        allSockets[socket.ConnectionInfo.Id] = socket;
                        AudioService.SendInitialState(socket);
                        BrightnessService.SendBrightness(socket);
                        DisplayManager.SendDisplays(socket);
                        _ = MediaService.SendNowPlayingAsync(socket);
                        PowerService.SendInitialState(socket);
                        Logger.Log("CONNECT", $"{socket.ConnectionInfo.ClientIpAddress}  ({allSockets.Count} connected)", ConsoleColor.Cyan);
                    };
                    socket.OnClose = () =>
                    {
                        allSockets.TryRemove(socket.ConnectionInfo.Id, out _);
                        InputService.RemoveSession(socket.ConnectionInfo.Id);
                        Logger.Log("DISCONNECT", $"{socket.ConnectionInfo.ClientIpAddress}  ({allSockets.Count} connected)", ConsoleColor.DarkCyan);
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
            }
            catch (Exception ex)
            {
                Logger.Log("WS", $"Failed to start WebSocket server on port {WS_PORT}: {ex.Message}", ConsoleColor.Red);
            }
        }

        public static void Stop()
        {
            try { _wsServer?.Dispose(); } catch { }
            foreach (var socket in allSockets.Values)
            {
                try { socket.Close(); } catch { }
            }
            allSockets.Clear();
        }
    }
}