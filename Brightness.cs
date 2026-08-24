using System.Management;
using System.Text.Json;
using Fleck;

namespace PCRemote
{
    public static class BrightnessService
    {
        static ManagementEventWatcher? _watcher;
        
        public static void Init()
        {
            try
            {
                var query = new WqlEventQuery("SELECT * FROM __InstanceModificationEvent WITHIN 2 WHERE TargetInstance ISA 'WmiMonitorBrightness'");
                _watcher = new ManagementEventWatcher(new ManagementScope("root\\WMI"), query);
                _watcher.EventArrived += (s, e) =>
                {
                    if (e.NewEvent.Properties["TargetInstance"]?.Value is ManagementBaseObject mo)
                    {
                        var val = Convert.ToInt32(mo["CurrentBrightness"]);
                        Server.Broadcast(JsonSerializer.Serialize(new { type = "brightness", supported = true, value = val }));
                    }
                };
                _watcher.Start();
            }
            catch (Exception ex)
            {
                Logger.Log("BRIGHT", $"Watcher init failed: {ex.Message}", ConsoleColor.DarkGray);
            }
        }

        public static (bool supported, int value) GetBrightness()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (ManagementObject o in searcher.Get())
                {
                    using (o)
                        return (true, Convert.ToInt32(o["CurrentBrightness"]));
                }
            }
            catch (Exception ex)
            {
                Logger.Log("BRIGHT", $"GetBrightness unsupported: {ex.Message}", ConsoleColor.DarkGray);
            }
            return (false, 0);
        }

        public static void SetBrightness(int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
                foreach (ManagementObject o in searcher.Get())
                {
                    using (o)
                        o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
                }
            }
            catch (Exception ex)
            {
                Logger.Log("BRIGHT", $"SetBrightness failed: {ex.Message}", ConsoleColor.Red);
            }
        }

        public static void SendBrightness(IWebSocketConnection socket)
        {
            var (supported, value) = GetBrightness();
            var msg = JsonSerializer.Serialize(new { type = "brightness", supported, value });
            try { socket.Send(msg); } catch { }
        }
    }
}
