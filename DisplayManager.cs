
using System.Runtime.InteropServices;
using System.Text.Json;
using Fleck;

namespace PCRemote
{
    public static class DisplayManager
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINTL
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint dmFields;
            public POINTL dmPosition;
            public uint dmDisplayOrientation;
            public uint dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public uint dmPelsWidth;
            public uint dmPelsHeight;
            public uint dmDisplayFlags;
            public uint dmDisplayFrequency;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
        const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;
        const int ENUM_CURRENT_SETTINGS = -1;
        const int ENUM_REGISTRY_SETTINGS = -2;
        const uint DM_POSITION = 0x00000020;
        const uint DM_PELSWIDTH = 0x00080000;
        const uint DM_PELSHEIGHT = 0x00100000;
        const uint CDS_UPDATEREGISTRY = 0x00000001;
        const uint CDS_NORESET = 0x10000000;

        public class DisplayInfo
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public bool IsActive { get; set; }
            public bool IsPrimary { get; set; }
        }

        public static List<DisplayInfo> GetDisplays()
        {
            var list = new List<DisplayInfo>();
            
            var wmiNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher("root\\WMI", "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID");
                foreach (System.Management.ManagementObject mo in searcher.Get())
                {
                    using (mo)
                    {
                        string instanceName = (string)mo["InstanceName"];
                        ushort[] nameData = (ushort[])mo["UserFriendlyName"];
                        string friendlyName = "";
                        foreach (ushort c in nameData)
                        {
                            if (c == 0) break;
                            friendlyName += (char)c;
                        }
                        
                        string key = instanceName.Replace('\\', '#');
                        int lastUnderscore = key.LastIndexOf('_');
                        if (lastUnderscore > 0) key = key.Substring(0, lastUnderscore);
                        
                        wmiNames[key] = friendlyName;
                    }
                }
            }
            catch { }

            var seenHwIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            uint i = 0;
            DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));

            while (EnumDisplayDevices(null, i, ref dd, 0))
            {
                if ((dd.StateFlags & 0x00000008) == 0) // DISPLAY_DEVICE_MIRRORING_DRIVER
                {
                    bool isActive = (dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0;
                    bool isPrimary = (dd.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;
                    
                    string displayName = dd.DeviceString;
                    
                    DISPLAY_DEVICE monitor = new DISPLAY_DEVICE();
                    monitor.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                    if (EnumDisplayDevices(dd.DeviceName, 0, ref monitor, 1)) // EDD_GET_DEVICE_INTERFACE_NAME
                    {
                        string devId = monitor.DeviceID;
                        if (devId.StartsWith(@"\\?\"))
                        {
                            string[] parts = devId.Substring(4).Split('#');
                            if (parts.Length >= 3)
                            {
                                string hwId = $"{parts[0]}#{parts[1]}#{parts[2]}";
                                
                                if (seenHwIds.Contains(hwId))
                                {
                                    i++;
                                    dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                                    continue;
                                }
                                seenHwIds.Add(hwId);
                                
                                if (wmiNames.ContainsKey(hwId) && !string.IsNullOrWhiteSpace(wmiNames[hwId]))
                                    displayName = wmiNames[hwId];
                                else if (!string.IsNullOrWhiteSpace(monitor.DeviceString))
                                    displayName = monitor.DeviceString;
                            }
                        }
                    }

                    list.Add(new DisplayInfo
                    {
                        Id = dd.DeviceName,
                        Name = displayName,
                        IsActive = isActive,
                        IsPrimary = isPrimary
                    });
                }
                
                i++;
                dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            }
            return list;
        }

        public static void ToggleDisplay(string deviceName, bool enable)
        {
            DEVMODE devMode = new DEVMODE();
            devMode.dmSize = (ushort)Marshal.SizeOf(devMode);

            if (enable)
            {
                if (!EnumDisplaySettings(deviceName, ENUM_REGISTRY_SETTINGS, ref devMode)) return;
                devMode.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT;
            }
            else
            {
                if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref devMode)) return;
                devMode.dmPelsWidth = 0;
                devMode.dmPelsHeight = 0;
                devMode.dmPosition.x = 0;
                devMode.dmPosition.y = 0;
                devMode.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT;
            }

            int ret = ChangeDisplaySettingsEx(deviceName, ref devMode, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_NORESET, IntPtr.Zero);
            if (ret == 0)
            {
                DEVMODE emptyMode = new DEVMODE();
                emptyMode.dmSize = (ushort)Marshal.SizeOf(emptyMode);
                // Apply the changes
                ChangeDisplaySettingsEx(null, ref emptyMode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            }
        }

        static readonly JsonSerializerOptions _jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public static void SendDisplays(IWebSocketConnection socket)
        {
            try 
            {
                var msg = JsonSerializer.Serialize(new { type = "displays", displays = GetDisplays() }, _jsonOpts);
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
                var msg = JsonSerializer.Serialize(new { type = "displays", displays = GetDisplays() }, _jsonOpts);
                Server.Broadcast(msg);
            }
            catch (Exception ex)
            {
                Logger.Log("DISPLAY", $"Broadcast error: {ex.Message}", ConsoleColor.Red);
            }
        }
    }
}
