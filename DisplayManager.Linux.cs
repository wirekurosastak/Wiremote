using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fleck;

namespace Wiremote
{
    public static class DisplayManager
    {
        public class DisplayInfo
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public bool IsActive { get; set; }
            public bool IsPrimary { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
        }

        private static bool _hasKscreenDoctor = true;
        private static bool _toolsChecked = false;
        static readonly JsonSerializerOptions _jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public static bool IsDisplayAvailable()
        {
            try
            {
                uint uid = Interop.geteuid();
                string runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? $"/run/user/{uid}";

                string? waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "wayland-0";
                if (File.Exists(Path.Combine(runtimeDir, waylandDisplay))) return true;

                if (Directory.Exists(runtimeDir) && Directory.GetFiles(runtimeDir, "wayland-*").Any(f => !f.EndsWith(".lock")))
                    return true;

                if (Directory.Exists("/tmp/.X11-unix") && Directory.GetFiles("/tmp/.X11-unix", "X*").Length > 0)
                    return true;
            }
            catch { }
            return false;
        }

        public static void Init()
        {
            RegisterCommands();
        }

        private static void EnsureToolsChecked()
        {
            if (_toolsChecked) return;
            if (!IsDisplayAvailable()) return;

            try
            {
                string test = RunCmd("kscreen-doctor", "-j");
                _hasKscreenDoctor = !string.IsNullOrEmpty(test) && test.Contains("\"outputs\"");
                _toolsChecked = true;
                if (_hasKscreenDoctor)
                {
                    Logger.Log("DISPLAY", "Using kscreen-doctor for display management", ConsoleColor.Green);
                }
                else
                {
                    Logger.Log("DISPLAY", "kscreen-doctor unavailable or failing, using xrandr", ConsoleColor.DarkYellow);
                }
            }
            catch
            {
                _hasKscreenDoctor = false;
                _toolsChecked = true;
            }
        }

        private static string RunCmd(string cmd, string args)
        {
            if (cmd == "kscreen-doctor" && !IsDisplayAvailable())
            {
                return "";
            }

            try
            {
                var psi = new ProcessStartInfo(cmd, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // Propagate GUI / Wayland environment variables
                uint uid = Interop.geteuid();
                string runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? $"/run/user/{uid}";
                psi.EnvironmentVariables["XDG_RUNTIME_DIR"] = runtimeDir;
                string? wd = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
                if (!string.IsNullOrEmpty(wd)) psi.EnvironmentVariables["WAYLAND_DISPLAY"] = wd;
                psi.EnvironmentVariables["QT_QPA_PLATFORM"] = "wayland;xcb";
                string? disp = Environment.GetEnvironmentVariable("DISPLAY");
                if (!string.IsNullOrEmpty(disp)) psi.EnvironmentVariables["DISPLAY"] = disp;
                string? xauth = Environment.GetEnvironmentVariable("XAUTHORITY");
                if (!string.IsNullOrEmpty(xauth)) psi.EnvironmentVariables["XAUTHORITY"] = xauth;

                using var p = Process.Start(psi);
                if (p == null) return "";
                string outStr = p.StandardOutput.ReadToEnd();
                bool exited = p.WaitForExit(1500);
                if (!exited)
                {
                    try { p.Kill(); } catch { }
                    return "";
                }
                if (p.ExitCode != 0)
                {
                    return "";
                }
                return outStr;
            }
            catch { return ""; }
        }

        private static DateTime _lastDisplayFetch = DateTime.MinValue;
        private static List<DisplayInfo> _cachedDisplays = new();
        private static readonly object _displayLock = new();

        public static List<DisplayInfo> GetDisplays(bool allowRefresh = true)
        {
            lock (_displayLock)
            {
                if (!IsDisplayAvailable())
                {
                    return _cachedDisplays;
                }

                EnsureToolsChecked();

                if (!allowRefresh && _cachedDisplays.Count > 0 && (DateTime.UtcNow - _lastDisplayFetch).TotalMilliseconds < 2000)
                {
                    return _cachedDisplays;
                }

                List<DisplayInfo> list = new();
                if (_hasKscreenDoctor)
                {
                    list = GetDisplaysKscreen();
                    if (list.Count == 0)
                    {
                        // Fallback to xrandr if kscreen-doctor returned nothing
                        list = GetDisplaysXrandr();
                    }
                }
                else
                {
                    list = GetDisplaysXrandr();
                }

                if (list.Count > 0)
                {
                    _cachedDisplays = list;
                    _lastDisplayFetch = DateTime.UtcNow;
                }
                return list.Count > 0 ? list : _cachedDisplays;
            }
        }

        private static List<DisplayInfo> GetDisplaysKscreen()
        {
            var displays = new List<DisplayInfo>();
            try
            {
                string json = RunCmd("kscreen-doctor", "-j");
                if (string.IsNullOrEmpty(json)) return displays;

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("outputs", out var outputs) && outputs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var outEl in outputs.EnumerateArray())
                    {
                        string id = outEl.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (string.IsNullOrEmpty(id)) continue;

                        bool enabled = outEl.TryGetProperty("enabled", out var en) && en.GetBoolean();
                        bool connected = outEl.TryGetProperty("connected", out var conn) && conn.GetBoolean();
                        int priority = outEl.TryGetProperty("priority", out var pri) ? pri.GetInt32() : 99;

                        int w = 0, h = 0, x = 0, y = 0;
                        if (outEl.TryGetProperty("size", out var s))
                        {
                            w = s.TryGetProperty("width", out var sw) ? sw.GetInt32() : 0;
                            h = s.TryGetProperty("height", out var sh) ? sh.GetInt32() : 0;
                        }
                        if (outEl.TryGetProperty("pos", out var pos))
                        {
                            x = pos.TryGetProperty("x", out var px) ? px.GetInt32() : 0;
                            y = pos.TryGetProperty("y", out var py) ? py.GetInt32() : 0;
                        }

                        string name = (w > 0 && h > 0) ? $"{id} ({w}x{h})" : id;

                        displays.Add(new DisplayInfo
                        {
                            Id = id,
                            Name = name,
                            IsActive = enabled && connected,
                            IsPrimary = priority == 1,
                            X = x,
                            Y = y
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("DISPLAY", $"KScreen parse error: {ex.Message}", ConsoleColor.DarkGray);
            }
            return displays;
        }

        private static List<DisplayInfo> GetDisplaysXrandr()
        {
            var displays = new List<DisplayInfo>();
            try
            {
                string output = RunCmd("xrandr", "--query");
                var lines = output.Split('\n');

                foreach (var line in lines)
                {
                    var match = Regex.Match(line, @"^(\S+)\s+(connected|disconnected)\s+(primary\s+)?(\d+x\d+\+(\d+)\+(\d+))?");
                    if (match.Success)
                    {
                        string id = match.Groups[1].Value;
                        bool connected = match.Groups[2].Value == "connected";
                        bool isPrimary = match.Groups[3].Success;
                        bool hasGeometry = match.Groups[4].Success;
                        int x = hasGeometry && int.TryParse(match.Groups[5].Value, out int gx) ? gx : 0;
                        int y = hasGeometry && int.TryParse(match.Groups[6].Value, out int gy) ? gy : 0;

                        displays.Add(new DisplayInfo
                        {
                            Id = id,
                            Name = hasGeometry ? $"{id} ({match.Groups[4].Value.Split('+')[0]})" : id,
                            IsActive = connected && hasGeometry,
                            IsPrimary = isPrimary,
                            X = x,
                            Y = y
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("DISPLAY", $"xrandr parse error: {ex.Message}", ConsoleColor.DarkGray);
            }
            return displays;
        }

        public static void ToggleDisplay(string deviceName, bool enable)
        {
            if (_hasKscreenDoctor)
            {
                RunCmd("kscreen-doctor", $"output.{deviceName}.{(enable ? "enable" : "disable")}");
            }
            else
            {
                RunCmd("xrandr", $"--output {deviceName} {(enable ? "--auto" : "--off")}");
            }
        }

        public static void SetPrimaryDisplay(string deviceName)
        {
            if (_hasKscreenDoctor)
            {
                RunCmd("kscreen-doctor", $"output.{deviceName}.priority.1");
            }
            else
            {
                RunCmd("xrandr", $"--output {deviceName} --primary");
            }
        }

        public static string GetCurrentProjectionMode(List<DisplayInfo> displays)
        {
            var activeDisplays = displays.Where(d => d.IsActive).ToList();
            if (activeDisplays.Count <= 1) return "extend";

            var positionCounts = activeDisplays.GroupBy(d => new { d.X, d.Y }).Select(g => g.Count()).ToList();
            int clonedMonitors = positionCounts.Where(c => c > 1).Sum();
            int extendedMonitors = positionCounts.Where(c => c == 1).Sum();

            return extendedMonitors >= clonedMonitors ? "extend" : "clone";
        }

        public static void SendDisplays(IWebSocketConnection socket)
        {
            try
            {
                var displays = GetDisplays();
                var msg = JsonSerializer.Serialize(new { type = "displays", displays = displays, mode = GetCurrentProjectionMode(displays) }, _jsonOpts);
                socket.Send(msg);
            }
            catch (Exception ex)
            {
                Logger.Log("DISPLAY", $"Send error: {ex.Message}", ConsoleColor.Red);
            }
        }

        public static void BroadcastDisplays()
        {
            try
            {
                var displays = GetDisplays();
                var msg = JsonSerializer.Serialize(new { type = "displays", displays = displays, mode = GetCurrentProjectionMode(displays) }, _jsonOpts);
                Server.Broadcast(msg);
            }
            catch (Exception ex)
            {
                Logger.Log("DISPLAY", $"Broadcast error: {ex.Message}", ConsoleColor.Red);
            }
        }

        public static void SwitchDisplayMode(string mode)
        {
            var displays = GetDisplays();
            var active = displays.Where(d => d.IsActive).ToList();
            if (active.Count <= 1) return;

            if (_hasKscreenDoctor)
            {
                if (mode == "clone")
                {
                    string args = string.Join(" ", active.Select(d => $"output.{d.Id}.position.0,0"));
                    RunCmd("kscreen-doctor", args);
                }
                else if (mode == "extend")
                {
                    int currentX = 0;
                    var parts = new List<string>();
                    foreach (var d in active)
                    {
                        parts.Add($"output.{d.Id}.position.{currentX},0");
                        currentX += 1920; // safe default step
                    }
                    RunCmd("kscreen-doctor", string.Join(" ", parts));
                }
            }
            else
            {
                if (mode == "clone" && active.Count >= 2)
                {
                    RunCmd("xrandr", $"--output {active[1].Id} --same-as {active[0].Id}");
                }
                else if (mode == "extend" && active.Count >= 2)
                {
                    RunCmd("xrandr", $"--output {active[1].Id} --right-of {active[0].Id}");
                }
            }
        }

        public static void TurnOffDisplays()
        {
            if (_hasKscreenDoctor)
            {
                RunCmd("kscreen-doctor", "--dpms off");
            }
            else
            {
                RunCmd("xset", "dpms force off");
            }
        }

        private static void RegisterCommands()
        {
            Server.RegisterCommand("display_switch", (s, r) =>
            {
                var mode = r.TryGetProperty("mode", out var mProp) ? mProp.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(mode))
                {
                    SwitchDisplayMode(mode);
                    BroadcastDisplays();
                    Logger.Log("DISPLAY", $"switch {mode}", ConsoleColor.Cyan);
                }
                return Task.CompletedTask;
            });
            Server.RegisterCommand("displays_off", (s, r) =>
            {
                TurnOffDisplays();
                Logger.Log("DISPLAY", "turn off", ConsoleColor.Cyan);
                return Task.CompletedTask;
            });
            Server.RegisterCommand("get_displays", (s, r) => { SendDisplays(s); return Task.CompletedTask; });
            Server.RegisterCommand("set_display", (s, r) =>
            {
                var id = r.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                var active = r.TryGetProperty("active", out var actProp) && actProp.GetBoolean();
                ToggleDisplay(id, active);
                BroadcastDisplays();
                Logger.Log("DISPLAY", $"{id} -> {(active ? "on" : "off")}", ConsoleColor.Cyan);
                return Task.CompletedTask;
            });
            Server.RegisterCommand("set_primary_display", (s, r) =>
            {
                var id = r.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                SetPrimaryDisplay(id);
                BroadcastDisplays();
                Logger.Log("DISPLAY", "Primary monitor changed", ConsoleColor.Cyan);
                return Task.CompletedTask;
            });
        }
    }
}
