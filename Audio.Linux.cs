using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fleck;

namespace Wiremote
{
    public static class AudioService
    {
        private static Process? _subscriberProcess;
        private static CancellationTokenSource? _subCts;
        private static readonly object _lock = new();

        public static void Init()
        {
            RegisterCommands();
            StartSubscriber();
        }

        private static string RunPactl(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("pactl", args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.EnvironmentVariables["LC_ALL"] = "C";

                using var p = Process.Start(psi);
                if (p == null) return "";
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(1000);
                return output;
            }
            catch (Exception ex)
            {
                Logger.Log("AUDIO", $"pactl error ({args}): {ex.Message}", ConsoleColor.DarkGray);
                return "";
            }
        }

        public static (int volume, bool muted) GetVolumeState()
        {
            lock (_lock)
            {
                try
                {
                    string volOut = RunPactl("get-sink-volume @DEFAULT_SINK@");
                    string muteOut = RunPactl("get-sink-mute @DEFAULT_SINK@");

                    int volume = 50;
                    var match = Regex.Match(volOut, @"/\s*(\d+)%");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int v))
                    {
                        volume = Math.Clamp(v, 0, 100);
                    }

                    bool muted = muteOut.Contains("Mute: yes", StringComparison.OrdinalIgnoreCase);
                    return (volume, muted);
                }
                catch
                {
                    return (50, false);
                }
            }
        }

        public static (int volume, bool muted) BroadcastVolume()
        {
            var state = GetVolumeState();
            var msg = JsonSerializer.Serialize(new { type = "volume", value = state.volume, muted = state.muted });
            Server.Broadcast(msg);
            return state;
        }

        public static void SetVolume(int percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            RunPactl($"set-sink-volume @DEFAULT_SINK@ {percent}%");
            if (percent == 0)
            {
                RunPactl("set-sink-mute @DEFAULT_SINK@ 1");
            }
            else
            {
                RunPactl("set-sink-mute @DEFAULT_SINK@ 0");
            }
        }

        public static void VolumeChange(int delta)
        {
            var (volume, _) = GetVolumeState();
            SetVolume(volume + delta);
        }

        public static void ToggleMute()
        {
            RunPactl("set-sink-mute @DEFAULT_SINK@ toggle");
        }

        public static string BuildSessionsJson()
        {
            var list = new List<object>();
            try
            {
                string raw = RunPactl("list sink-inputs");
                var blocks = raw.Split(new[] { "Sink Input #" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var b in blocks)
                {
                    var lines = b.Split('\n');
                    if (lines.Length == 0) continue;

                    string firstLine = lines[0].Trim();
                    if (!uint.TryParse(firstLine, out uint id)) continue;

                    string name = $"Stream {id}";
                    int vol = 100;
                    bool muted = false;

                    var appMatch = Regex.Match(b, @"application\.name\s*=\s*""([^""]+)""");
                    if (appMatch.Success)
                    {
                        name = appMatch.Groups[1].Value;
                    }
                    else
                    {
                        var mediaMatch = Regex.Match(b, @"media\.name\s*=\s*""([^""]+)""");
                        if (mediaMatch.Success) name = mediaMatch.Groups[1].Value;
                        else
                        {
                            var binMatch = Regex.Match(b, @"application\.process\.binary\s*=\s*""([^""]+)""");
                            if (binMatch.Success) name = binMatch.Groups[1].Value;
                        }
                    }

                    var volMatch = Regex.Match(b, @"/\s*(\d+)%");
                    if (volMatch.Success && int.TryParse(volMatch.Groups[1].Value, out int v))
                    {
                        vol = Math.Clamp(v, 0, 100);
                    }

                    if (b.Contains("Mute: yes", StringComparison.OrdinalIgnoreCase))
                    {
                        muted = true;
                    }

                    list.Add(new { id, name, volume = vol, muted });
                }
            }
            catch (Exception ex)
            {
                Logger.Log("AUDIO", $"BuildSessionsJson error: {ex.Message}", ConsoleColor.DarkGray);
            }
            return JsonSerializer.Serialize(new { type = "sessions", sessions = list });
        }

        public static void BroadcastSessions() => Server.Broadcast(BuildSessionsJson());

        public static void SendSessions(IWebSocketConnection socket)
        {
            try { socket.Send(BuildSessionsJson()); }
            catch (Exception ex) { Logger.Log("WS", $"SendSessions failed: {ex.Message}", ConsoleColor.DarkGray); }
        }

        public static void SetSessionVolume(uint id, int percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            RunPactl($"set-sink-input-volume {id} {percent}%");
        }

        public static bool ToggleSessionMute(uint id)
        {
            RunPactl($"set-sink-input-mute {id} toggle");
            return false;
        }

        public static string BuildDevicesJson()
        {
            var list = new List<object>();
            try
            {
                string defSink = RunPactl("get-default-sink").Trim();
                string raw = RunPactl("list sinks");
                var blocks = raw.Split(new[] { "Sink #" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var b in blocks)
                {
                    var nameMatch = Regex.Match(b, @"Name:\s*(\S+)");
                    if (!nameMatch.Success) continue;
                    string sinkName = nameMatch.Groups[1].Value;

                    string desc = sinkName;
                    var descMatch = Regex.Match(b, @"Description:\s*(.+)");
                    if (descMatch.Success)
                    {
                        desc = descMatch.Groups[1].Value.Trim();
                    }

                    bool isDef = string.Equals(sinkName, defSink, StringComparison.OrdinalIgnoreCase);
                    list.Add(new { id = sinkName, name = desc, isDefault = isDef });
                }
            }
            catch (Exception ex)
            {
                Logger.Log("AUDIO", $"BuildDevicesJson error: {ex.Message}", ConsoleColor.DarkGray);
            }
            return JsonSerializer.Serialize(new { type = "devices", devices = list });
        }

        public static void BroadcastDevices() => Server.Broadcast(BuildDevicesJson());

        public static void SendDevices(IWebSocketConnection socket)
        {
            try { socket.Send(BuildDevicesJson()); }
            catch (Exception ex) { Logger.Log("WS", $"SendDevices failed: {ex.Message}", ConsoleColor.DarkGray); }
        }

        public static async Task SetDefaultDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            RunPactl($"set-default-sink \"{deviceId}\"");

            // Move existing streams to the new default sink
            try
            {
                string inputs = RunPactl("list short sink-inputs");
                foreach (var line in inputs.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('\t', ' ');
                    if (parts.Length > 0 && uint.TryParse(parts[0], out uint streamId))
                    {
                        RunPactl($"move-sink-input {streamId} \"{deviceId}\"");
                    }
                }
            }
            catch { }

            await Task.Delay(100);
            BroadcastDevices();
            BroadcastVolume();
            BroadcastSessions();
        }

        public static void SendInitialState(IWebSocketConnection socket)
        {
            var state = GetVolumeState();
            try { socket.Send(JsonSerializer.Serialize(new { type = "volume", value = state.volume, muted = state.muted })); }
            catch { }
            SendSessions(socket);
            SendDevices(socket);
        }

        private static void StartSubscriber()
        {
            _subCts = new CancellationTokenSource();
            var token = _subCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var psi = new ProcessStartInfo("pactl", "subscribe")
                        {
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        psi.EnvironmentVariables["LC_ALL"] = "C";

                        using var p = Process.Start(psi);
                        if (p == null)
                        {
                            await Task.Delay(5000, token);
                            continue;
                        }

                        _subscriberProcess = p;

                        DateTime lastEvent = DateTime.MinValue;
                        while (!token.IsCancellationRequested && !p.HasExited)
                        {
                            string? line = await p.StandardOutput.ReadLineAsync(token);
                            if (line == null) break;

                            if (line.Contains("sink", StringComparison.OrdinalIgnoreCase) ||
                                line.Contains("server", StringComparison.OrdinalIgnoreCase))
                            {
                                // Debounce rapid bursts
                                if ((DateTime.UtcNow - lastEvent).TotalMilliseconds > 50)
                                {
                                    lastEvent = DateTime.UtcNow;
                                    _ = Task.Run(async () =>
                                    {
                                        await Task.Delay(40);
                                        BroadcastVolume();
                                        BroadcastSessions();
                                        BroadcastDevices();
                                    });
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        Logger.Log("AUDIO", $"pactl subscribe watcher restart: {ex.Message}", ConsoleColor.DarkGray);
                        await Task.Delay(3000, token);
                    }
                }
            }, token);
        }

        public static void Cleanup()
        {
            try
            {
                _subCts?.Cancel();
                _subCts?.Dispose();
                _subCts = null;
            }
            catch { }

            try
            {
                if (_subscriberProcess != null && !_subscriberProcess.HasExited)
                {
                    _subscriberProcess.Kill();
                    _subscriberProcess.Dispose();
                    _subscriberProcess = null;
                }
            }
            catch { }
        }

        private static void RegisterCommands()
        {
            Server.RegisterCommand("volume_up", (s, r) => { VolumeChange(5); var vs = BroadcastVolume(); Logger.Log("VOLUME", $"up  → {vs.volume}%", ConsoleColor.Yellow); return Task.CompletedTask; });
            Server.RegisterCommand("volume_down", (s, r) => { VolumeChange(-5); var vs = BroadcastVolume(); Logger.Log("VOLUME", $"down → {vs.volume}%", ConsoleColor.Yellow); return Task.CompletedTask; });
            Server.RegisterCommand("set_volume", (s, r) => { SetVolume(r.GetProperty("value").GetInt32()); var vs = BroadcastVolume(); Logger.Log("VOLUME", $"set  → {vs.volume}%", ConsoleColor.Yellow); return Task.CompletedTask; });
            Server.RegisterCommand("mute", (s, r) => { ToggleMute(); var vs = BroadcastVolume(); Logger.Log("VOLUME", vs.muted ? "muted" : "unmuted", ConsoleColor.Yellow); return Task.CompletedTask; });
            Server.RegisterCommand("get_sessions", (s, r) => { SendSessions(s); return Task.CompletedTask; });
            Server.RegisterCommand("set_session_volume", (s, r) => { var id = (uint)r.GetProperty("id").GetInt64(); int val = r.GetProperty("value").GetInt32(); SetSessionVolume(id, val); Logger.Log("APPVOL", $"pid {id} → {val}%", ConsoleColor.Yellow); return Task.CompletedTask; });
            Server.RegisterCommand("session_mute", (s, r) => { var id = (uint)r.GetProperty("id").GetInt64(); bool m = ToggleSessionMute(id); BroadcastSessions(); Logger.Log("APPVOL", $"pid {id} {(m ? "muted" : "unmuted")}", ConsoleColor.Yellow); return Task.CompletedTask; });
            Server.RegisterCommand("get_devices", (s, r) => { SendDevices(s); return Task.CompletedTask; });
            Server.RegisterCommand("set_device", (s, r) => { var id = r.GetProperty("id").GetString() ?? ""; _ = SetDefaultDevice(id); Logger.Log("DEVICE", "switched output", ConsoleColor.Cyan); return Task.CompletedTask; });
        }
    }
}
