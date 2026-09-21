using System.Management;
using System.Text.Json;
using Fleck;

namespace Wiremote
{
    public static class BrightnessService
    {
        static ManagementEventWatcher? _watcher;
        
        public static void Init()
        {
            RegisterCommands();
            try
            {
                var query = new WqlEventQuery("SELECT * FROM __InstanceModificationEvent WITHIN 2 WHERE TargetInstance ISA 'WmiMonitorBrightness'");
                _watcher = new ManagementEventWatcher(new ManagementScope("root\\WMI"), query);
                _watcher.EventArrived += OnBrightnessChanged;
                _watcher.Start();
            }
            catch (Exception ex)
            {
                // Desktop computers often lack WmiMonitorBrightness, so this is not necessarily a critical error.
                Logger.Log("BRIGHT", $"Watcher init skipped (Not supported or error): {ex.Message}", ConsoleColor.DarkGray);
            }
        }

        private static void OnBrightnessChanged(object sender, EventArrivedEventArgs e)
        {
            try
            {
                if (e.NewEvent.Properties["TargetInstance"]?.Value is ManagementBaseObject mo)
                {
                    var val = Convert.ToInt32(mo["CurrentBrightness"]);
                    Server.Broadcast(JsonSerializer.Serialize(new { type = "brightness", supported = true, value = val }));
                }
            }
            catch (Exception ex)
            {
                Logger.Log("BRIGHT", $"Event parse failed: {ex.Message}", ConsoleColor.Red);
            }
        }

        public static (bool supported, int value) GetBrightness()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                using var collection = searcher.Get();
                foreach (ManagementObject o in collection)
                {
                    using (o)
                    {
                        return (true, Convert.ToInt32(o["CurrentBrightness"]));
                    }
                }
            }
            catch (ManagementException)
            {
                // No WMI brightness support (e.g., desktop monitor)
            }
            catch (Exception ex)
            {
                Logger.Log("BRIGHT", $"GetBrightness error: {ex.Message}", ConsoleColor.DarkGray);
            }
            return (false, 0);
        }

        private static readonly object _setLock = new object();
        private static int _targetBrightness = -1;
        private static bool _isProcessingSet = false;

        public static void SetBrightness(int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            lock (_setLock)
            {
                _targetBrightness = percent;
                if (_isProcessingSet) return;
                _isProcessingSet = true;
            }

            Task.Run(() =>
            {
                while (true)
                {
                    int val;
                    lock (_setLock)
                    {
                        if (_targetBrightness < 0)
                        {
                            _isProcessingSet = false;
                            break;
                        }
                        val = _targetBrightness;
                        _targetBrightness = -1;
                    }

                    try
                    {
                        using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
                        using var collection = searcher.Get();
                        foreach (ManagementObject o in collection)
                        {
                            using (o)
                            {
                                o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)val });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("BRIGHT", $"SetBrightness failed: {ex.Message}", ConsoleColor.Red);
                    }
                }
            });
        }

        public static void SendBrightness(IWebSocketConnection socket)
        {
            var (supported, value) = GetBrightness();
            var msg = JsonSerializer.Serialize(new { type = "brightness", supported, value });
            try { socket.Send(msg); } catch (Exception ex) { Logger.Log("BRIGHT", $"WS send error: {ex.Message}", ConsoleColor.Red); }
        }

        // NEW: Prevent memory leaks
        public static void Cleanup()
        {
            if (_watcher != null)
            {
                try
                {
                    _watcher.Stop();
                    _watcher.EventArrived -= OnBrightnessChanged;
                    _watcher.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Log("BRIGHT", $"Cleanup error: {ex.Message}", ConsoleColor.Red);
                }
                finally
                {
                    _watcher = null;
                }
            }
        }

        private static void RegisterCommands()
        {
            Server.RegisterCommand("get_brightness", (s, r) => { SendBrightness(s); return Task.CompletedTask; });
            Server.RegisterCommand("set_brightness", (s, r) => { var v = r.GetProperty("value").GetInt32(); SetBrightness(v); Logger.Log("BRIGHT", $"→ {Math.Max(0, Math.Min(100, v))}%", ConsoleColor.DarkYellow); return Task.CompletedTask; });
        }
    }
}