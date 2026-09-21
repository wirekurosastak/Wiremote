using System.Collections.Concurrent;
using System.Diagnostics;

namespace Wiremote
{
    public static class InputService
    {
        public enum MouseButton { Left, Right, Middle }

        #region Virtual Keys
        public const ushort VK_MEDIA_NEXT_TRACK = 0xB0;
        public const ushort VK_MEDIA_PREV_TRACK = 0xB1;
        public const ushort VK_MEDIA_PLAY_PAUSE = 0xB3;
        public const ushort VK_LEFT = 0x25;
        public const ushort VK_RIGHT = 0x27;
        public const ushort VK_RETURN = 0x0D;
        public const ushort VK_BACK = 0x08;
        #endregion

        #region X11 Keysym Constants
        private const ulong XK_Return = 0xFF0D;
        private const ulong XK_BackSpace = 0xFF08;
        private const ulong XK_Left = 0xFF51;
        private const ulong XK_Right = 0xFF53;
        private const ulong XK_Shift_L = 0xFFE1;
        private const ulong XK_Control_L = 0xFFE3;
        private const ulong XK_v = 0x0076;
        private const ulong XF86XK_AudioPlay = 0x1008FF14;
        private const ulong XF86XK_AudioNext = 0x1008FF17;
        private const ulong XF86XK_AudioPrev = 0x1008FF16;
        #endregion

        class MouseAccumulator { public double X; public double Y; }
        private static readonly ConcurrentDictionary<Guid, MouseAccumulator> _mouseAcc = new();

        private static IntPtr _display = IntPtr.Zero;
        private static readonly object _displayLock = new();
        private static DateTime _lastDisplayErrorLog = DateTime.MinValue;
        private static readonly Interop.XErrorHandler _errorHandler = (d, e) => 0;

        public static void Init()
        {
            EnsureDisplay();
            RegisterCommands();
        }

        private static void SetEnvBoth(string name, string value)
        {
            try
            {
                Environment.SetEnvironmentVariable(name, value);
                Interop.setenv(name, value, 1);
            }
            catch { }
        }

        private static void UpdateXAuthority()
        {
            // 1. Search XDG_RUNTIME_DIR or /run/user/{UID} for xauth_*
            try
            {
                uint uid = Interop.geteuid();
                string runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? $"/run/user/{uid}";
                if (Directory.Exists(runtimeDir))
                {
                    var files = Directory.GetFiles(runtimeDir, "xauth_*");
                    if (files.Length > 0)
                    {
                        var latest = files.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
                        SetEnvBoth("XAUTHORITY", latest);
                        return;
                    }
                }
            }
            catch { }

            // 2. Check ~/.Xauthority
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string userXAuth = Path.Combine(home, ".Xauthority");
                if (File.Exists(userXAuth))
                {
                    SetEnvBoth("XAUTHORITY", userXAuth);
                    return;
                }
            }
            catch { }

            // 3. Fallback: check systemctl --user show-environment
            try
            {
                using var p = Process.Start(new ProcessStartInfo("systemctl", "--user show-environment")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (p != null)
                {
                    string outStr = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(500);
                    foreach (var line in outStr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.StartsWith("XAUTHORITY="))
                        {
                            string path = line.Substring("XAUTHORITY=".Length).Trim();
                            if (File.Exists(path))
                            {
                                SetEnvBoth("XAUTHORITY", path);
                                return;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static void UpdateDisplayEnv()
        {
            string? disp = Environment.GetEnvironmentVariable("DISPLAY");
            if (string.IsNullOrEmpty(disp))
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo("systemctl", "--user show-environment")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    if (p != null)
                    {
                        string outStr = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(500);
                        foreach (var line in outStr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (line.StartsWith("DISPLAY="))
                            {
                                disp = line.Substring("DISPLAY=".Length).Trim();
                                break;
                            }
                        }
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(disp)) disp = ":0";
            }

            SetEnvBoth("DISPLAY", disp);
        }

        private static IntPtr EnsureDisplay()
        {
            lock (_displayLock)
            {
                if (_display != IntPtr.Zero) return _display;

                UpdateXAuthority();
                UpdateDisplayEnv();

                string? dispName = Environment.GetEnvironmentVariable("DISPLAY") ?? ":0";
                _display = Interop.XOpenDisplay(dispName);
                if (_display == IntPtr.Zero && dispName != ":0")
                {
                    _display = Interop.XOpenDisplay(":0");
                }

                if (_display != IntPtr.Zero)
                {
                    Interop.XSetErrorHandler(_errorHandler);
                    Logger.Log("INPUT", $"Successfully connected to X11 display ({dispName})", ConsoleColor.Green);
                }
                else
                {
                    if ((DateTime.UtcNow - _lastDisplayErrorLog).TotalSeconds >= 5)
                    {
                        _lastDisplayErrorLog = DateTime.UtcNow;
                        Logger.Log("INPUT", "Failed to open X11 display. Mouse/keyboard simulation may not work.", ConsoleColor.Red);
                    }
                }
                return _display;
            }
        }

        public static async Task KeyboardKey(ushort key, bool extended = false)
        {
            ulong keysym = key switch
            {
                VK_RETURN => XK_Return,
                VK_BACK => XK_BackSpace,
                VK_LEFT => XK_Left,
                VK_RIGHT => XK_Right,
                VK_MEDIA_PLAY_PAUSE => XF86XK_AudioPlay,
                VK_MEDIA_NEXT_TRACK => XF86XK_AudioNext,
                VK_MEDIA_PREV_TRACK => XF86XK_AudioPrev,
                _ => key
            };

            lock (_displayLock)
            {
                var d = EnsureDisplay();
                if (d == IntPtr.Zero) return;

                byte kc = Interop.XKeysymToKeycode(d, keysym);
                if (kc == 0) return;

                Interop.XTestFakeKeyEvent(d, kc, 1, 0);
                Interop.XFlush(d);
            }

            await Task.Delay(50);

            lock (_displayLock)
            {
                var d = EnsureDisplay();
                if (d == IntPtr.Zero) return;

                byte kc = Interop.XKeysymToKeycode(d, keysym);
                if (kc == 0) return;

                Interop.XTestFakeKeyEvent(d, kc, 0, 0);
                Interop.XFlush(d);
            }
        }

        public static Task MediaKey(ushort key) => KeyboardKey(key, true);

        public static void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            lock (_displayLock)
            {
                var d = EnsureDisplay();
                if (d == IntPtr.Zero) return;

                byte shiftKc = Interop.XKeysymToKeycode(d, XK_Shift_L);

                foreach (char ch in text)
                {
                    ulong keysym = (ulong)ch;
                    byte kc = Interop.XKeysymToKeycode(d, keysym);

                    if (kc != 0)
                    {
                        ulong unshifted = Interop.XKeycodeToKeysym(d, kc, 0);
                        ulong shifted = Interop.XKeycodeToKeysym(d, kc, 1);
                        bool needShift = (shifted == keysym && unshifted != keysym);

                        if (needShift && shiftKc != 0)
                        {
                            Interop.XTestFakeKeyEvent(d, shiftKc, 1, 0);
                        }

                        Interop.XTestFakeKeyEvent(d, kc, 1, 0);
                        Interop.XTestFakeKeyEvent(d, kc, 0, 0);

                        if (needShift && shiftKc != 0)
                        {
                            Interop.XTestFakeKeyEvent(d, shiftKc, 0, 0);
                        }
                    }
                    else
                    {
                        // Fallback: paste character via wl-copy or xclip if available
                        PasteString(ch.ToString(), d);
                    }
                }
                Interop.XFlush(d);
            }
        }

        private static void PasteString(string s, IntPtr d)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("wl-copy")
                {
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (p != null)
                {
                    p.StandardInput.Write(s);
                    p.StandardInput.Close();
                    p.WaitForExit(500);

                    byte ctrlKc = Interop.XKeysymToKeycode(d, XK_Control_L);
                    byte vKc = Interop.XKeysymToKeycode(d, XK_v);
                    if (ctrlKc != 0 && vKc != 0)
                    {
                        Interop.XTestFakeKeyEvent(d, ctrlKc, 1, 0);
                        Interop.XTestFakeKeyEvent(d, vKc, 1, 0);
                        Interop.XTestFakeKeyEvent(d, vKc, 0, 0);
                        Interop.XTestFakeKeyEvent(d, ctrlKc, 0, 0);
                    }
                }
            }
            catch { }
        }

        public static void MouseMove(Guid sessionId, double dx, double dy)
        {
            var acc = _mouseAcc.GetOrAdd(sessionId, _ => new MouseAccumulator());
            acc.X += dx; acc.Y += dy;

            int moveX = (int)Math.Truncate(acc.X);
            int moveY = (int)Math.Truncate(acc.Y);

            acc.X -= moveX; acc.Y -= moveY;

            if (moveX == 0 && moveY == 0) return;

            lock (_displayLock)
            {
                var d = EnsureDisplay();
                if (d == IntPtr.Zero) return;

                Interop.XTestFakeRelativeMotionEvent(d, moveX, moveY, 0);
                Interop.XFlush(d);
            }
        }

        public static void RemoveSession(Guid sessionId) => _mouseAcc.TryRemove(sessionId, out _);

        public static void MouseScroll(int dy)
        {
            if (dy == 0) return;
            uint button = dy > 0 ? 4u : 5u;
            int steps = Math.Max(1, Math.Abs(dy) / 40);

            lock (_displayLock)
            {
                var d = EnsureDisplay();
                if (d == IntPtr.Zero) return;

                for (int i = 0; i < steps; i++)
                {
                    Interop.XTestFakeButtonEvent(d, button, 1, 0);
                    Interop.XTestFakeButtonEvent(d, button, 0, 0);
                }
                Interop.XFlush(d);
            }
        }

        private static uint ParseButton(string button) => button.ToLower() switch
        {
            "right" => 3u,
            "middle" => 2u,
            _ => 1u
        };

        public static async Task MouseClick(string buttonStr = "left")
        {
            uint button = ParseButton(buttonStr);

            lock (_displayLock)
            {
                var d = EnsureDisplay();
                if (d != IntPtr.Zero)
                {
                    Interop.XTestFakeButtonEvent(d, button, 1, 0);
                    Interop.XFlush(d);
                }
            }

            await Task.Delay(30);

            lock (_displayLock)
            {
                var d = EnsureDisplay();
                if (d != IntPtr.Zero)
                {
                    Interop.XTestFakeButtonEvent(d, button, 0, 0);
                    Interop.XFlush(d);
                }
            }
        }

        public static void MouseToggle(string buttonStr, bool isDown)
        {
            uint button = ParseButton(buttonStr);
            lock (_displayLock)
            {
                var d = EnsureDisplay();
                if (d == IntPtr.Zero) return;

                Interop.XTestFakeButtonEvent(d, button, isDown ? 1 : 0, 0);
                Interop.XFlush(d);
            }
        }

        private static void RegisterCommands()
        {
            Server.RegisterCommand("mouse_move", (s, r) => { var dx = r.TryGetProperty("dx", out var dxProp) ? dxProp.GetDouble() : 0.0; var dy = r.TryGetProperty("dy", out var dyProp) ? dyProp.GetDouble() : 0.0; MouseMove(s.ConnectionInfo.Id, dx, dy); return Task.CompletedTask; });
            Server.RegisterCommand("mouse_scroll", (s, r) => { MouseScroll(r.TryGetProperty("dy", out var dyScroll) ? (int)dyScroll.GetDouble() : 0); return Task.CompletedTask; });
            Server.RegisterCommand("mouse_down", (s, r) => { var btn = r.TryGetProperty("button", out var b) ? (b.GetString() ?? "left") : "left"; MouseToggle(btn, true); Logger.Log("MOUSE", $"{btn} down", ConsoleColor.Green); return Task.CompletedTask; });
            Server.RegisterCommand("mouse_up", (s, r) => { var btn = r.TryGetProperty("button", out var b) ? (b.GetString() ?? "left") : "left"; MouseToggle(btn, false); Logger.Log("MOUSE", $"{btn} up", ConsoleColor.Green); return Task.CompletedTask; });
            Server.RegisterCommand("mouse_click", (s, r) => { var btn = r.TryGetProperty("button", out var b) ? (b.GetString() ?? "left") : "left"; Logger.Log("MOUSE", $"{btn} click", ConsoleColor.Green); return MouseClick(btn); });
            Server.RegisterCommand("type_text", (s, r) => { var text = r.TryGetProperty("text", out var txt) ? (txt.GetString() ?? "") : ""; TypeText(text); Logger.Log("TYPE", $"{text.Length} char(s)", ConsoleColor.Green); return Task.CompletedTask; });
            Server.RegisterCommand("key_press", (s, r) => { var key = r.GetProperty("key").GetString(); if (key == "enter") { Logger.Log("KEY", "enter", ConsoleColor.Green); return KeyboardKey(VK_RETURN); } else if (key == "backspace") { Logger.Log("KEY", "backspace", ConsoleColor.Green); return KeyboardKey(VK_BACK); } return Task.CompletedTask; });
        }
    }
}
