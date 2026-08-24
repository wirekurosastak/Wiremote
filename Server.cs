using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Fleck;

namespace PCRemote
{
    public static class Server
    {
        public const int HTTP_PORT = 8765;
        public const int WS_PORT = 8766;

        static readonly ConcurrentDictionary<Guid, IWebSocketConnection> allSockets = new();
        static HttpListener? _httpListener;
        static WebSocketServer? _wsServer;

        delegate Task CommandHandler(IWebSocketConnection socket, JsonElement root);
        static readonly Dictionary<string, CommandHandler> _commandRouter = new();

        static Server()
        {
            RegisterCommands();
        }

        static void RegisterCommands()
        {
            // Audio & Volume
            _commandRouter["volume_up"] = HandleVolumeUp;
            _commandRouter["volume_down"] = HandleVolumeDown;
            _commandRouter["set_volume"] = HandleSetVolume;
            _commandRouter["mute"] = HandleMute;
            _commandRouter["get_sessions"] = HandleGetSessions;
            _commandRouter["set_session_volume"] = HandleSetSessionVolume;
            _commandRouter["session_mute"] = HandleSessionMute;
            _commandRouter["get_devices"] = HandleGetDevices;
            _commandRouter["set_device"] = HandleSetDevice;

            // Display & Brightness
            _commandRouter["get_brightness"] = HandleGetBrightness;
            _commandRouter["set_brightness"] = HandleSetBrightness;
            _commandRouter["display_switch"] = HandleDisplaySwitch;
            _commandRouter["displays_off"] = HandleDisplaysOff;
            _commandRouter["get_displays"] = HandleGetDisplays;
            _commandRouter["set_display"] = HandleSetDisplay;
            _commandRouter["set_primary_display"] = HandleSetPrimaryDisplay;

            // Media
            _commandRouter["media_play_pause"] = (s, r) => MediaService.MediaCommand(MediaService.MediaAction.PlayPause);
            _commandRouter["media_previous"] = (s, r) => MediaService.MediaCommand(MediaService.MediaAction.Previous);
            _commandRouter["media_next"] = (s, r) => MediaService.MediaCommand(MediaService.MediaAction.Next);
            _commandRouter["web_space"] = (s, r) => MediaService.MediaCommand(MediaService.MediaAction.PlayPause);
            _commandRouter["web_left"] = (s, r) => MediaService.MediaSeek(-15);
            _commandRouter["web_right"] = (s, r) => MediaService.MediaSeek(15);

            // Input (Mouse & Keyboard)
            _commandRouter["mouse_move"] = HandleMouseMove;
            _commandRouter["mouse_scroll"] = HandleMouseScroll;
            _commandRouter["mouse_down"] = HandleMouseDown;
            _commandRouter["mouse_up"] = HandleMouseUp;
            _commandRouter["mouse_click"] = HandleMouseClick;
            _commandRouter["type_text"] = HandleTypeText;
            _commandRouter["key_press"] = HandleKeyPress;

            // System Power
            _commandRouter["power"] = HandlePower;
        }

        #region Command Handlers

        private static Task HandleVolumeUp(IWebSocketConnection s, JsonElement r) { AudioService.VolumeChange(5); var vs = AudioService.BroadcastVolume(); Logger.Log("VOLUME", $"up  → {vs.volume}%", ConsoleColor.Yellow); return Task.CompletedTask; }
        private static Task HandleVolumeDown(IWebSocketConnection s, JsonElement r) { AudioService.VolumeChange(-5); var vs = AudioService.BroadcastVolume(); Logger.Log("VOLUME", $"down → {vs.volume}%", ConsoleColor.Yellow); return Task.CompletedTask; }
        private static Task HandleSetVolume(IWebSocketConnection s, JsonElement r) { AudioService.SetVolume(r.GetProperty("value").GetInt32()); var vs = AudioService.BroadcastVolume(); Logger.Log("VOLUME", $"set  → {vs.volume}%", ConsoleColor.Yellow); return Task.CompletedTask; }
        private static Task HandleMute(IWebSocketConnection s, JsonElement r) { AudioService.ToggleMute(); var vs = AudioService.BroadcastVolume(); Logger.Log("VOLUME", vs.muted ? "muted" : "unmuted", ConsoleColor.Yellow); return Task.CompletedTask; }
        
        private static Task HandleGetSessions(IWebSocketConnection s, JsonElement r) { AudioService.SendSessions(s); return Task.CompletedTask; }
        private static Task HandleSetSessionVolume(IWebSocketConnection s, JsonElement r)
        {
            var id = (uint)r.GetProperty("id").GetInt64();
            int val = r.GetProperty("value").GetInt32();
            AudioService.SetSessionVolume(id, val);
            Logger.Log("APPVOL", $"pid {id} → {val}%", ConsoleColor.Yellow);
            return Task.CompletedTask;
        }
        private static Task HandleSessionMute(IWebSocketConnection s, JsonElement r)
        {
            var id = (uint)r.GetProperty("id").GetInt64();
            bool m = AudioService.ToggleSessionMute(id);
            AudioService.BroadcastSessions();
            Logger.Log("APPVOL", $"pid {id} {(m ? "muted" : "unmuted")}", ConsoleColor.Yellow);
            return Task.CompletedTask;
        }

        private static Task HandleGetDevices(IWebSocketConnection s, JsonElement r) { AudioService.SendDevices(s); return Task.CompletedTask; }
        private static Task HandleSetDevice(IWebSocketConnection s, JsonElement r)
        {
            var id = r.GetProperty("id").GetString() ?? "";
            AudioService.SetDefaultDevice(id);
            Logger.Log("DEVICE", "switched output", ConsoleColor.Cyan);
            return Task.CompletedTask;
        }

        private static Task HandleGetBrightness(IWebSocketConnection s, JsonElement r) { BrightnessService.SendBrightness(s); return Task.CompletedTask; }
        private static Task HandleSetBrightness(IWebSocketConnection s, JsonElement r)
        {
            var v = r.GetProperty("value").GetInt32();
            BrightnessService.SetBrightness(v);
            Logger.Log("BRIGHT", $"→ {Math.Max(0, Math.Min(100, v))}%", ConsoleColor.DarkYellow);
            return Task.CompletedTask;
        }

        private static Task HandleDisplaySwitch(IWebSocketConnection s, JsonElement r)
        {
            var mode = r.GetProperty("mode").GetString() ?? "";
            string arg = mode switch { "clone" => "/clone", "extend" => "/extend", _ => "" };
            if (!string.IsNullOrEmpty(arg))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("DisplaySwitch.exe", arg) { UseShellExecute = true, CreateNoWindow = true }); } catch { }
                Logger.Log("DISPLAY", $"switch {mode}", ConsoleColor.Cyan);
            }
            return Task.CompletedTask;
        }
        private static Task HandleDisplaysOff(IWebSocketConnection s, JsonElement r) { Interop.SendMessage(Interop.GetConsoleWindow(), 0x0112, (IntPtr)0xF170, (IntPtr)2); Logger.Log("DISPLAY", "turn off", ConsoleColor.Cyan); return Task.CompletedTask; }
        private static Task HandleGetDisplays(IWebSocketConnection s, JsonElement r) { DisplayManager.SendDisplays(s); return Task.CompletedTask; }
        private static Task HandleSetDisplay(IWebSocketConnection s, JsonElement r)
        {
            var id = r.GetProperty("id").GetString() ?? "";
            var active = r.GetProperty("active").GetBoolean();
            DisplayManager.ToggleDisplay(id, active);
            DisplayManager.BroadcastDisplays();
            Logger.Log("DISPLAY", $"{id} -> {(active ? "on" : "off")}", ConsoleColor.Cyan);
            return Task.CompletedTask;
        }
        private static Task HandleSetPrimaryDisplay(IWebSocketConnection s, JsonElement r)
        {
            var id = r.GetProperty("id").GetString() ?? "";
            DisplayManager.SetPrimaryDisplay(id);
            DisplayManager.BroadcastDisplays();
            Logger.Log("DISPLAY", $"Primary monitor changed", ConsoleColor.Cyan);
            return Task.CompletedTask;
        }

        private static Task HandleMouseMove(IWebSocketConnection s, JsonElement r)
        {
            var dx = r.TryGetProperty("dx", out var dxProp) ? dxProp.GetDouble() : 0.0;
            var dy = r.TryGetProperty("dy", out var dyProp) ? dyProp.GetDouble() : 0.0;
            InputService.MouseMove(s.ConnectionInfo.Id, dx, dy);
            return Task.CompletedTask;
        }
        private static Task HandleMouseScroll(IWebSocketConnection s, JsonElement r) { InputService.MouseScroll(r.TryGetProperty("dy", out var dyScroll) ? (int)dyScroll.GetDouble() : 0); return Task.CompletedTask; }
        private static Task HandleMouseDown(IWebSocketConnection s, JsonElement r) { var btn = r.TryGetProperty("button", out var b) ? (b.GetString() ?? "left") : "left"; InputService.MouseToggle(btn, true); Logger.Log("MOUSE", $"{btn} down", ConsoleColor.Green); return Task.CompletedTask; }
        private static Task HandleMouseUp(IWebSocketConnection s, JsonElement r) { var btn = r.TryGetProperty("button", out var b) ? (b.GetString() ?? "left") : "left"; InputService.MouseToggle(btn, false); Logger.Log("MOUSE", $"{btn} up", ConsoleColor.Green); return Task.CompletedTask; }
        private static Task HandleMouseClick(IWebSocketConnection s, JsonElement r) { var btn = r.TryGetProperty("button", out var b) ? (b.GetString() ?? "left") : "left"; Logger.Log("MOUSE", $"{btn} click", ConsoleColor.Green); return InputService.MouseClick(btn); }
        private static Task HandleTypeText(IWebSocketConnection s, JsonElement r) { var text = r.TryGetProperty("text", out var txt) ? (txt.GetString() ?? "") : ""; InputService.TypeText(text); Logger.Log("TYPE", $"{text.Length} char(s)", ConsoleColor.Green); return Task.CompletedTask; }
        private static Task HandleKeyPress(IWebSocketConnection s, JsonElement r)
        {
            var key = r.GetProperty("key").GetString();
            if (key == "enter") { Logger.Log("KEY", "enter", ConsoleColor.Green); return InputService.KeyboardKey(InputService.VK_RETURN); }
            else if (key == "backspace") { Logger.Log("KEY", "backspace", ConsoleColor.Green); return InputService.KeyboardKey(InputService.VK_BACK); }
            return Task.CompletedTask;
        }

        private static Task HandlePower(IWebSocketConnection s, JsonElement r)
        {
            var action = r.GetProperty("action").GetString() ?? "";
            var seconds = r.TryGetProperty("seconds", out var secs) ? secs.GetInt32() : 0;
            if (action == "cancel") seconds = 0;
            Logger.Log("POWER", seconds > 0 ? $"{action} in {seconds}s" : action, ConsoleColor.Red);
            PowerService.HandleCommand(action, seconds);
            return Task.CompletedTask;
        }
        
        #endregion

        public static void Broadcast(string msg)
        {
            foreach (var kv in allSockets)
            {
                try { kv.Value.Send(msg); } catch { }
            }
        }

        public static async Task StartHttpServer(CancellationToken token)
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://*:{HTTP_PORT}/");

            try { _httpListener.Start(); }
            catch (HttpListenerException ex)
            {
                Logger.Log("HTTP", $"Failed to start on port {HTTP_PORT}: {ex.Message}", ConsoleColor.Red);
                return;
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    var response = context.Response;
                    var path = context.Request.Url?.AbsolutePath ?? "/";
                    var resource = WebUI.GetStaticResource(path);

                    if (resource.HasValue)
                    {
                        var (buffer, contentType) = resource.Value;
                        response.ContentType = contentType;
                        response.ContentLength64 = buffer.Length;
                        if (path == "/" || path == "/index.html")
                            response.Headers.Add("Cache-Control", "no-cache");
                        else
                            response.Headers.Add("Cache-Control", "public, max-age=31536000");

                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else
                    {
                        response.StatusCode = 404;
                    }
                    response.OutputStream.Close();
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Logger.Log("HTTP", $"Request error: {ex.Message}", ConsoleColor.Red); }
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
            try { _httpListener?.Stop(); } catch { }
            try { _wsServer?.Dispose(); } catch { }
            foreach (var socket in allSockets.Values)
            {
                try { socket.Close(); } catch { }
            }
            allSockets.Clear();
        }
    }
}