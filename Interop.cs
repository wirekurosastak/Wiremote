using System.Runtime.InteropServices;

namespace PCRemote
{
    public static class Interop
    {
        #region Core Audio Policy Config (COM)
        
        public enum ERole 
        { 
            eConsole = 0, 
            eMultimedia = 1, 
            eCommunications = 2 
        }

        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
        public class CPolicyConfigClient { }

        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPolicyConfig
        {
            // Slots 1-10 preserve exact COM IUnknown vtable layout alignment leading to SetDefaultEndpoint (slot 11)
            [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr fmt);
            [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string dev, bool def, IntPtr fmt);
            [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string dev);
            [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr endpointFmt, IntPtr mixFmt);
            [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string dev, bool def, IntPtr defPeriod, IntPtr minPeriod);
            [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr period);
            [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr mode);
            [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr mode);
            [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string dev, bool store, IntPtr key, IntPtr val);
            [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string dev, bool store, IntPtr key, IntPtr val);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string dev, ERole role);
        }
        
        #endregion

        #region Windows User32 & Kernel32 API
        
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        
        #endregion
    }
}