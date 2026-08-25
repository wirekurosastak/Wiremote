
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

        [DllImport("user32.dll", CharSet = CharSet.Ansi, EntryPoint = "ChangeDisplaySettingsEx")]
        static extern int ChangeDisplaySettingsExPtr(string? lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        const uint CDS_SET_PRIMARY = 0x00000010;

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

        public static void Init()
        {
            RegisterCommands();
        }

        static Dictionary<string, string>? _cachedWmiNames = null;
        static readonly object _wmiCacheLock = new object();

        public static List<DisplayInfo> GetDisplays()
        {
            var displays = new List<DisplayInfo>();
            var seenHwIds = new Dictionary<string, DisplayInfo>(StringComparer.OrdinalIgnoreCase);
            
            if (_cachedWmiNames == null)
            {
                lock (_wmiCacheLock)
                {
                    if (_cachedWmiNames == null)
                    {
                        var tempCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        try
                        {
                            using var searcher = new System.Management.ManagementObjectSearcher("root\\WMI", "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID");
                            using var collection = searcher.Get();
                            foreach (System.Management.ManagementObject mo in collection)
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
                                    
                                    tempCache[key] = friendlyName;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log("DISPLAY", $"WMI cache error: {ex.Message}", ConsoleColor.DarkGray);
                        }
                        _cachedWmiNames = tempCache;
                    }
                }
            }
            
            var wmiNames = _cachedWmiNames;

            uint i = 0;
            DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));

            while (EnumDisplayDevices(null, i, ref dd, 0))
            {
                if ((dd.StateFlags & 0x00000008) == 0) // DISPLAY_DEVICE_MIRRORING_DRIVER
                {
                    bool isActive = (dd.StateFlags & 0x00000001) != 0; // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP
                    bool isPrimary = (dd.StateFlags & 0x00000004) != 0; // DISPLAY_DEVICE_PRIMARY_DEVICE
                    
                    if (isActive)
                    {
                        DISPLAY_DEVICE monitor = new DISPLAY_DEVICE();
                        monitor.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                        bool hasMonitor = EnumDisplayDevices(dd.DeviceName, 0, ref monitor, 1);
                        
                        if (!hasMonitor)
                        {
                            monitor = new DISPLAY_DEVICE();
                            monitor.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                            hasMonitor = EnumDisplayDevices(dd.DeviceName, 0, ref monitor, 0);
                        }

                        if (hasMonitor)
                        {
                            string displayName = monitor.DeviceString;
                            string devId = monitor.DeviceID;
                            string? hwId = null;
                            
                            if (!string.IsNullOrWhiteSpace(devId) && devId.StartsWith(@"\\?\"))
                            {
                                string[] parts = devId.Substring(4).Split('#');
                                if (parts.Length >= 3)
                                {
                                    hwId = $"{parts[0]}#{parts[1]}#{parts[2]}";
                                    if (wmiNames.ContainsKey(hwId) && !string.IsNullOrWhiteSpace(wmiNames[hwId]))
                                        displayName = wmiNames[hwId];
                                }
                            }
                            
                            if (string.IsNullOrWhiteSpace(displayName))
                            {
                                displayName = dd.DeviceString;
                            }

                            var info = new DisplayInfo
                            {
                                Id = dd.DeviceName,
                                Name = displayName,
                                IsActive = true,
                                IsPrimary = isPrimary
                            };

                            if (hwId != null)
                            {
                                if (!seenHwIds.ContainsKey(hwId))
                                {
                                    seenHwIds[hwId] = info;
                                    displays.Add(info);
                                }
                            }
                            else
                            {
                                displays.Add(info);
                            }
                        }
                    }
                }
                
                i++;
                dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            }

            var activeHwIds = seenHwIds.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in wmiNames)
            {
                if (!activeHwIds.Contains(kvp.Key))
                {
                    displays.Add(new DisplayInfo
                    {
                        Id = "DISABLED_" + kvp.Key,
                        Name = kvp.Value + " (Disconnected)",
                        IsActive = false,
                        IsPrimary = false
                    });
                }
            }

            return displays;
        }

        private static List<string> GetInactiveAdaptersForHwId(string targetHwId)
        {
            var list = new List<string>();
            var fallbackList = new List<string>();

            uint i = 0;
            DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));

            while (EnumDisplayDevices(null, i, ref dd, 0))
            {
                if ((dd.StateFlags & 0x00000008) == 0 && (dd.StateFlags & 0x00000001) == 0)
                {
                    fallbackList.Add(dd.DeviceName);

                    DISPLAY_DEVICE monitor = new DISPLAY_DEVICE();
                    monitor.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                    bool hasMonitor = EnumDisplayDevices(dd.DeviceName, 0, ref monitor, 1);
                    if (!hasMonitor)
                    {
                        monitor = new DISPLAY_DEVICE();
                        monitor.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                        hasMonitor = EnumDisplayDevices(dd.DeviceName, 0, ref monitor, 0);
                    }

                    if (hasMonitor && !string.IsNullOrWhiteSpace(monitor.DeviceID) && monitor.DeviceID.StartsWith(@"\\?\"))
                    {
                        string[] parts = monitor.DeviceID.Substring(4).Split('#');
                        if (parts.Length >= 3)
                        {
                            string hwId = $"{parts[0]}#{parts[1]}#{parts[2]}";
                            if (string.Equals(hwId, targetHwId, StringComparison.OrdinalIgnoreCase))
                            {
                                list.Add(dd.DeviceName);
                            }
                        }
                    }
                }
                i++;
                dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            }

            return list.Count > 0 ? list : fallbackList;
        }

        private static bool TryEnableAdapter(string deviceName)
        {
            DEVMODE devMode = new DEVMODE();
            devMode.dmSize = (ushort)Marshal.SizeOf(devMode);

            if (!EnumDisplaySettings(deviceName, ENUM_REGISTRY_SETTINGS, ref devMode)) return false;
            
            if (devMode.dmPelsWidth == 0 || devMode.dmPelsHeight == 0)
            {
                DEVMODE maxMode = new DEVMODE();
                maxMode.dmSize = (ushort)Marshal.SizeOf(maxMode);
                int modeNum = 0;
                uint maxWidth = 0;
                while (EnumDisplaySettings(deviceName, modeNum, ref maxMode))
                {
                    if (maxMode.dmPelsWidth > maxWidth)
                    {
                        maxWidth = maxMode.dmPelsWidth;
                        devMode = maxMode;
                    }
                    modeNum++;
                }
                if (devMode.dmPelsWidth == 0) return false;
            }

            var displays = GetDisplays();
            var primary = displays.FirstOrDefault(d => d.IsPrimary);
            if (primary != null && primary.Id != deviceName)
            {
                DEVMODE primaryMode = new DEVMODE();
                primaryMode.dmSize = (ushort)Marshal.SizeOf(primaryMode);
                if (EnumDisplaySettings(primary.Id, ENUM_CURRENT_SETTINGS, ref primaryMode))
                {
                    if (devMode.dmPosition.x == primaryMode.dmPosition.x && devMode.dmPosition.y == primaryMode.dmPosition.y)
                    {
                        devMode.dmPosition.x = primaryMode.dmPosition.x + (int)primaryMode.dmPelsWidth;
                        devMode.dmPosition.y = primaryMode.dmPosition.y;
                    }
                }
            }

            devMode.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT;
            int ret = ChangeDisplaySettingsEx(deviceName, ref devMode, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_NORESET, IntPtr.Zero);
            return ret == 0;
        }

        private static bool TryDisableAdapter(string deviceName)
        {
            DEVMODE devMode = new DEVMODE();
            devMode.dmSize = (ushort)Marshal.SizeOf(devMode);
            if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref devMode)) return false;
            devMode.dmPelsWidth = 0;
            devMode.dmPelsHeight = 0;
            devMode.dmPosition.x = 0;
            devMode.dmPosition.y = 0;
            devMode.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT;
            int ret = ChangeDisplaySettingsEx(deviceName, ref devMode, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_NORESET, IntPtr.Zero);
            return ret == 0;
        }

        public static void ToggleDisplay(string deviceName, bool enable)
        {
            if (deviceName.StartsWith("DISABLED_") && enable)
            {
                string hwId = deviceName.Substring("DISABLED_".Length);
                var inactiveAdapters = GetInactiveAdaptersForHwId(hwId);
                
                foreach (var adapter in inactiveAdapters)
                {
                    if (TryEnableAdapter(adapter))
                    {
                        ChangeDisplaySettingsExPtr(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
                        return;
                    }
                }
                return;
            }

            if (!deviceName.StartsWith("DISABLED_"))
            {
                bool success = enable ? TryEnableAdapter(deviceName) : TryDisableAdapter(deviceName);
                if (success)
                {
                    ChangeDisplaySettingsExPtr(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
                }
            }
        }
        
        public static void SetPrimaryDisplay(string deviceName)
        {
            var displays = GetDisplays();
            var currentPrimary = displays.FirstOrDefault(d => d.IsPrimary);
            if (currentPrimary != null && currentPrimary.Id != deviceName)
            {
                DEVMODE targetMode = new DEVMODE();
                targetMode.dmSize = (ushort)Marshal.SizeOf(targetMode);
                if (EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref targetMode))
                {
                    int offsetX = targetMode.dmPosition.x;
                    int offsetY = targetMode.dmPosition.y;

                    targetMode.dmPosition.x = 0;
                    targetMode.dmPosition.y = 0;
                    targetMode.dmFields = DM_POSITION;
                    ChangeDisplaySettingsEx(deviceName, ref targetMode, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_NORESET | CDS_SET_PRIMARY, IntPtr.Zero);

                    DEVMODE oldMode = new DEVMODE();
                    oldMode.dmSize = (ushort)Marshal.SizeOf(oldMode);
                    if (EnumDisplaySettings(currentPrimary.Id, ENUM_CURRENT_SETTINGS, ref oldMode))
                    {
                        oldMode.dmPosition.x -= offsetX;
                        oldMode.dmPosition.y -= offsetY;
                        oldMode.dmFields = DM_POSITION;
                        ChangeDisplaySettingsEx(currentPrimary.Id, ref oldMode, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_NORESET, IntPtr.Zero);
                    }
                }
                ChangeDisplaySettingsExPtr(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
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

        private static void RegisterCommands()
        {
            Server.RegisterCommand("display_switch", (s, r) => { var mode = r.GetProperty("mode").GetString() ?? ""; string arg = mode switch { "clone" => "/clone", "extend" => "/extend", _ => "" }; if (!string.IsNullOrEmpty(arg)) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("DisplaySwitch.exe", arg) { UseShellExecute = true, CreateNoWindow = true }); } catch (Exception ex) { Logger.Log("DISPLAY", $"Switch process failed: {ex.Message}", ConsoleColor.Red); } Logger.Log("DISPLAY", $"switch {mode}", ConsoleColor.Cyan); } return Task.CompletedTask; });
            Server.RegisterCommand("displays_off", (s, r) => { Interop.SendMessage((IntPtr)0xFFFF, 0x0112, (IntPtr)0xF170, (IntPtr)2); Logger.Log("DISPLAY", "turn off", ConsoleColor.Cyan); return Task.CompletedTask; });
            Server.RegisterCommand("get_displays", (s, r) => { SendDisplays(s); return Task.CompletedTask; });
            Server.RegisterCommand("set_display", (s, r) => { var id = r.GetProperty("id").GetString() ?? ""; var active = r.GetProperty("active").GetBoolean(); ToggleDisplay(id, active); BroadcastDisplays(); Logger.Log("DISPLAY", $"{id} -> {(active ? "on" : "off")}", ConsoleColor.Cyan); return Task.CompletedTask; });
            Server.RegisterCommand("set_primary_display", (s, r) => { var id = r.GetProperty("id").GetString() ?? ""; SetPrimaryDisplay(id); BroadcastDisplays(); Logger.Log("DISPLAY", $"Primary monitor changed", ConsoleColor.Cyan); return Task.CompletedTask; });
        }
    }
}
