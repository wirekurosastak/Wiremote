using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace PCRemote
{
    public static class InputService
    {
        public enum MouseButton { Left, Right, Middle }

        #region P/Invoke Structures
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx; public int dy; public uint mouseData;
            public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk; public ushort wScan; public uint dwFlags;
            public uint time; public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct INPUT_UNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public INPUT_UNION u; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        const uint INPUT_MOUSE = 0; const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_EXTENDEDKEY = 0x0001; const uint KEYEVENTF_KEYUP = 0x0002; const uint KEYEVENTF_UNICODE = 0x0004;
        const uint MOUSEEVENTF_MOVE = 0x0001; const uint MOUSEEVENTF_LEFTDOWN = 0x0002; const uint MOUSEEVENTF_LEFTUP = 0x0004;
        const uint MOUSEEVENTF_RIGHTDOWN = 0x0008; const uint MOUSEEVENTF_RIGHTUP = 0x0010; const uint MOUSEEVENTF_WHEEL = 0x0800;
        #endregion

        #region Virtual Keys
        public const ushort VK_MEDIA_NEXT_TRACK = 0xB0;
        public const ushort VK_MEDIA_PREV_TRACK = 0xB1;
        public const ushort VK_MEDIA_PLAY_PAUSE = 0xB3;
        public const ushort VK_LEFT = 0x25;
        public const ushort VK_RIGHT = 0x27;
        public const ushort VK_RETURN = 0x0D;
        public const ushort VK_BACK = 0x08;
        #endregion

        class MouseAccumulator { public double X; public double Y; }
        static readonly ConcurrentDictionary<Guid, MouseAccumulator> _mouseAcc = new();

        public static async Task KeyboardKey(ushort key, bool extended = false)
        {
            uint flags = extended ? KEYEVENTF_EXTENDEDKEY : 0;
            var press = new INPUT { type = INPUT_KEYBOARD, u = new INPUT_UNION { ki = new KEYBDINPUT { wVk = key, dwFlags = flags } } };
            var release = new INPUT { type = INPUT_KEYBOARD, u = new INPUT_UNION { ki = new KEYBDINPUT { wVk = key, dwFlags = flags | KEYEVENTF_KEYUP } } };

            SendInput(1, new[] { press }, Marshal.SizeOf<INPUT>());
            await Task.Delay(50);
            SendInput(1, new[] { release }, Marshal.SizeOf<INPUT>());
        }

        public static Task MediaKey(ushort key) => KeyboardKey(key, true);

        public static void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var inputs = new INPUT[text.Length * 2];
            for (int i = 0; i < text.Length; i++)
            {
                inputs[i * 2] = new INPUT { type = INPUT_KEYBOARD, u = new INPUT_UNION { ki = new KEYBDINPUT { wScan = text[i], dwFlags = KEYEVENTF_UNICODE } } };
                inputs[i * 2 + 1] = new INPUT { type = INPUT_KEYBOARD, u = new INPUT_UNION { ki = new KEYBDINPUT { wScan = text[i], dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } };
            }
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }

        public static void MouseMove(Guid sessionId, double dx, double dy)
        {
            var acc = _mouseAcc.GetOrAdd(sessionId, _ => new MouseAccumulator());
            acc.X += dx; acc.Y += dy;

            int moveX = (int)Math.Truncate(acc.X);
            int moveY = (int)Math.Truncate(acc.Y);

            acc.X -= moveX; acc.Y -= moveY;

            if (moveX == 0 && moveY == 0) return;

            var inp = new INPUT { type = INPUT_MOUSE, u = new INPUT_UNION { mi = new MOUSEINPUT { dx = moveX, dy = moveY, dwFlags = MOUSEEVENTF_MOVE } } };
            SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
        }

        public static void RemoveSession(Guid sessionId) => _mouseAcc.TryRemove(sessionId, out _);

        public static void MouseScroll(int dy)
        {
            var inp = new INPUT { type = INPUT_MOUSE, u = new INPUT_UNION { mi = new MOUSEINPUT { mouseData = (uint)dy, dwFlags = MOUSEEVENTF_WHEEL } } };
            SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
        }

        private static MouseButton ParseButton(string button) => button.ToLower() switch
        {
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => MouseButton.Left
        };

        public static async Task MouseClick(string buttonStr = "left")
        {
            var button = ParseButton(buttonStr);
            uint down = button == MouseButton.Right ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
            uint up = button == MouseButton.Right ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;

            var inpDown = new INPUT { type = INPUT_MOUSE, u = new INPUT_UNION { mi = new MOUSEINPUT { dwFlags = down } } };
            SendInput(1, new[] { inpDown }, Marshal.SizeOf<INPUT>());
            
            await Task.Delay(30);
            
            var inpUp = new INPUT { type = INPUT_MOUSE, u = new INPUT_UNION { mi = new MOUSEINPUT { dwFlags = up } } };
            SendInput(1, new[] { inpUp }, Marshal.SizeOf<INPUT>());
        }

        public static void MouseToggle(string buttonStr, bool isDown)
        {
            var button = ParseButton(buttonStr);
            uint flag = button == MouseButton.Right
                ? (isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP)
                : (isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP);

            var inp = new INPUT { type = INPUT_MOUSE, u = new INPUT_UNION { mi = new MOUSEINPUT { dwFlags = flag } } };
            SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
        }
    }
}