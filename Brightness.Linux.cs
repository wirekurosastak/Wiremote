using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fleck;

namespace Wiremote
{
    public static class BrightnessService
    {
        private enum BrightnessBackend { None, KdeDbus, GnomeDbus, KscreenDoctor, Sysfs }
        private static BrightnessBackend _backend = BrightnessBackend.None;
        private static int _cachedKdeMax = 10000;
        private static string _cachedSysfsPath = "";
        private static string _cachedKscreenOutput = "";
        private static Timer? _pollTimer;
        private static int _lastReportedBrightness = -1;

        public static void Init()
        {
            RegisterCommands();
            DetectBackend();

            // Periodic poll to broadcast brightness if changed externally
            _pollTimer = new Timer(_ =>
            {
                var (supported, val) = GetBrightness();
                if (supported && val != _lastReportedBrightness)
                {
                    _lastReportedBrightness = val;
                    Server.Broadcast(JsonSerializer.Serialize(new { type = "brightness", supported = true, value = val }));
                }
            }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }

        private static string RunCmd(string cmd, string args)
        {
            Program.RefreshLinuxSessionEnvironment();
            if (cmd == "kscreen-doctor" && !DisplayManager.IsDisplayAvailable())
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
                p.WaitForExit(800);
                return outStr.Trim();
            }
            catch { return ""; }
        }

        private static void DetectBackend()
        {
            // 1. Try KDE PowerManagement D-Bus
            string kdeMax = RunCmd("busctl", "--user call org.kde.Solid.PowerManagement /org/kde/Solid/PowerManagement/Actions/BrightnessControl org.kde.Solid.PowerManagement.Actions.BrightnessControl brightnessMax");
            if (kdeMax.StartsWith("i ") && int.TryParse(kdeMax.Substring(2).Trim(), out int km) && km > 0)
            {
                _backend = BrightnessBackend.KdeDbus;
                _cachedKdeMax = km;
                Logger.Log("BRIGHT", $"Detected KDE BrightnessControl (max: {_cachedKdeMax})", ConsoleColor.Green);
                return;
            }

            // 2. Try GNOME Power SettingsDaemon D-Bus
            string gnomeProp = RunCmd("busctl", "--user get-property org.gnome.SettingsDaemon.Power /org/gnome/SettingsDaemon/Power org.gnome.SettingsDaemon.Power.Screen Brightness");
            if (gnomeProp.StartsWith("i "))
            {
                _backend = BrightnessBackend.GnomeDbus;
                Logger.Log("BRIGHT", "Detected GNOME Screen Brightness", ConsoleColor.Green);
                return;
            }

            // 3. Try kscreen-doctor (only if a display server is running)
            if (DisplayManager.IsDisplayAvailable())
            {
                string kdJson = RunCmd("kscreen-doctor", "-j");
                if (!string.IsNullOrEmpty(kdJson) && kdJson.Contains("\"brightness\""))
                {
                    var match = Regex.Match(kdJson, @"""name"":\s*""([^""]+)"".*?""brightness""", RegexOptions.Singleline);
                    if (match.Success)
                    {
                        _cachedKscreenOutput = match.Groups[1].Value;
                        _backend = BrightnessBackend.KscreenDoctor;
                        Logger.Log("BRIGHT", $"Detected kscreen-doctor output {_cachedKscreenOutput}", ConsoleColor.Green);
                        return;
                    }
                }
            }

            // 4. Try sysfs backlight
            try
            {
                if (Directory.Exists("/sys/class/backlight"))
                {
                    var dirs = Directory.GetDirectories("/sys/class/backlight");
                    if (dirs.Length > 0)
                    {
                        _cachedSysfsPath = dirs[0];
                        _backend = BrightnessBackend.Sysfs;
                        Logger.Log("BRIGHT", $"Detected sysfs backlight {_cachedSysfsPath}", ConsoleColor.Green);
                        return;
                    }
                }
            }
            catch { }

            Logger.Log("BRIGHT", "No supported brightness backend found on this system.", ConsoleColor.DarkGray);
        }

        public static (bool supported, int value) GetBrightness()
        {
            try
            {
                if (_backend == BrightnessBackend.None && DisplayManager.IsDisplayAvailable())
                {
                    DetectBackend();
                }

                switch (_backend)
                {
                    case BrightnessBackend.KdeDbus:
                    {
                        string outStr = RunCmd("busctl", "--user call org.kde.Solid.PowerManagement /org/kde/Solid/PowerManagement/Actions/BrightnessControl org.kde.Solid.PowerManagement.Actions.BrightnessControl brightness");
                        if (outStr.StartsWith("i ") && int.TryParse(outStr.Substring(2).Trim(), out int cur))
                        {
                            int val = (int)Math.Round((double)cur / _cachedKdeMax * 100);
                            return (true, Math.Clamp(val, 0, 100));
                        }
                        break;
                    }
                    case BrightnessBackend.GnomeDbus:
                    {
                        string outStr = RunCmd("busctl", "--user get-property org.gnome.SettingsDaemon.Power /org/gnome/SettingsDaemon/Power org.gnome.SettingsDaemon.Power.Screen Brightness");
                        if (outStr.StartsWith("i ") && int.TryParse(outStr.Substring(2).Trim(), out int cur))
                        {
                            return (true, Math.Clamp(cur, 0, 100));
                        }
                        break;
                    }
                    case BrightnessBackend.KscreenDoctor:
                    {
                        string kdJson = RunCmd("kscreen-doctor", "-j");
                        var match = Regex.Match(kdJson, @"""name"":\s*""" + Regex.Escape(_cachedKscreenOutput) + @""".*?""brightness"":\s*([0-9.]+)", RegexOptions.Singleline);
                        if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double bVal))
                        {
                            return (true, Math.Clamp((int)Math.Round(bVal), 0, 100));
                        }
                        break;
                    }
                    case BrightnessBackend.Sysfs:
                    {
                        string curStr = File.ReadAllText(Path.Combine(_cachedSysfsPath, "brightness")).Trim();
                        string maxStr = File.ReadAllText(Path.Combine(_cachedSysfsPath, "max_brightness")).Trim();
                        if (int.TryParse(curStr, out int cur) && int.TryParse(maxStr, out int max) && max > 0)
                        {
                            int val = (int)Math.Round((double)cur / max * 100);
                            return (true, Math.Clamp(val, 0, 100));
                        }
                        break;
                    }
                }
            }
            catch { }

            return (false, 0);
        }

        public static void SetBrightness(int percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            _lastReportedBrightness = percent;

            Task.Run(() =>
            {
                try
                {
                    switch (_backend)
                    {
                        case BrightnessBackend.KdeDbus:
                        {
                            int target = (int)Math.Round((double)percent / 100.0 * _cachedKdeMax);
                            RunCmd("busctl", $"--user call org.kde.Solid.PowerManagement /org/kde/Solid/PowerManagement/Actions/BrightnessControl org.kde.Solid.PowerManagement.Actions.BrightnessControl setBrightness i {target}");
                            break;
                        }
                        case BrightnessBackend.GnomeDbus:
                        {
                            RunCmd("busctl", $"--user set-property org.gnome.SettingsDaemon.Power /org/gnome/SettingsDaemon/Power org.gnome.SettingsDaemon.Power.Screen Brightness i {percent}");
                            break;
                        }
                        case BrightnessBackend.KscreenDoctor:
                        {
                            if (!string.IsNullOrEmpty(_cachedKscreenOutput))
                            {
                                RunCmd("kscreen-doctor", $"output.{_cachedKscreenOutput}.brightness.{percent}");
                            }
                            break;
                        }
                        case BrightnessBackend.Sysfs:
                        {
                            RunCmd("brightnessctl", $"set {percent}%");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("BRIGHT", $"SetBrightness error: {ex.Message}", ConsoleColor.Red);
                }
            });
        }

        public static void SendBrightness(IWebSocketConnection socket)
        {
            var (supported, value) = GetBrightness();
            _lastReportedBrightness = value;
            var msg = JsonSerializer.Serialize(new { type = "brightness", supported, value });
            try { socket.Send(msg); } catch (Exception ex) { Logger.Log("BRIGHT", $"WS send error: {ex.Message}", ConsoleColor.Red); }
        }

        public static void Cleanup()
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
        }

        private static void RegisterCommands()
        {
            Server.RegisterCommand("get_brightness", (s, r) => { SendBrightness(s); return Task.CompletedTask; });
            Server.RegisterCommand("set_brightness", (s, r) => { var v = r.GetProperty("value").GetInt32(); SetBrightness(v); Logger.Log("BRIGHT", $"→ {Math.Clamp(v, 0, 100)}%", ConsoleColor.DarkYellow); return Task.CompletedTask; });
        }
    }
}
