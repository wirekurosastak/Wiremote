using System.Runtime.InteropServices;

namespace Wiremote
{
    public static class Interop
    {
        #region Libc API

        [DllImport("libc", EntryPoint = "geteuid")]
        public static extern uint geteuid();

        [DllImport("libc", EntryPoint = "setenv")]
        public static extern int setenv([MarshalAs(UnmanagedType.LPStr)] string name, [MarshalAs(UnmanagedType.LPStr)] string value, int overwrite);

        #endregion

        #region X11 & XTest Native API

        private const string LibX11 = "libX11.so.6";
        private const string LibXtst = "libXtst.so.6";

        [DllImport(LibX11)]
        public static extern IntPtr XOpenDisplay([MarshalAs(UnmanagedType.LPStr)] string? displayName);

        [DllImport(LibX11)]
        public static extern int XCloseDisplay(IntPtr display);

        [DllImport(LibX11)]
        public static extern int XFlush(IntPtr display);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

        [DllImport(LibX11)]
        public static extern IntPtr XSetErrorHandler(XErrorHandler? handler);

        [DllImport(LibX11)]
        public static extern ulong XStringToKeysym([MarshalAs(UnmanagedType.LPStr)] string str);

        [DllImport(LibX11)]
        public static extern byte XKeysymToKeycode(IntPtr display, ulong keysym);

        [DllImport(LibX11)]
        public static extern ulong XKeycodeToKeysym(IntPtr display, byte keycode, int index);

        [DllImport(LibXtst)]
        public static extern int XTestFakeRelativeMotionEvent(IntPtr display, int x, int y, ulong delay);

        [DllImport(LibXtst)]
        public static extern int XTestFakeButtonEvent(IntPtr display, uint button, int isPress, ulong delay);

        [DllImport(LibXtst)]
        public static extern int XTestFakeKeyEvent(IntPtr display, uint keycode, int isPress, ulong delay);

        #endregion
    }
}
