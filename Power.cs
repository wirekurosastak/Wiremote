using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using Fleck;

namespace Wiremote
{
    public static class PowerService
    {
        static EventLogWatcher? _shutdownEventWatcher;
        static readonly object _powerLock = new object();
        static DateTime? _lastCancelTime = null;
        static CancellationTokenSource? _timerCts;

        public static string? ActiveAction { get; private set; }
        public static DateTime? TargetTime { get; private set; }

        public static void Init()
        {
            RegisterCommands();
            try
            {
                SystemEvents.SessionEnding += OnSessionEnding;
                var query = new EventLogQuery("System", PathType.LogName, "*[System[(EventID=1075)]]");
                _shutdownEventWatcher = new EventLogWatcher(query);
                _shutdownEventWatcher.EventRecordWritten += (s, e) =>
                {
                    lock (_powerLock)
                    {
                        if (ActiveAction == "shutdown" || ActiveAction == "restart" || ActiveAction == "restart_bios")
                        {
                            if (_lastCancelTime.HasValue && (DateTime.UtcNow - _lastCancelTime.Value).TotalSeconds < 3) return;
                            Logger.Log("POWER", "OS shutdown cancelled externally (Event 1075)", ConsoleColor.Yellow);
                            ClearState();
                            BroadcastState();
                        }
                    }
                };
                _shutdownEventWatcher.Enabled = true;
            }
            catch (Exception ex)
            {
                Logger.Log("POWER", $"Error registering Power events: {ex.Message}", ConsoleColor.Red);
            }
        }

        private static void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            Logger.Log("POWER", $"OS Session ending ({e.Reason})", ConsoleColor.Yellow);
            lock (_powerLock) { ClearState(); }
        }

        public static void HandleCommand(string action, int seconds)
        {
            seconds = Math.Max(0, seconds);
            lock (_powerLock)
            {
                _timerCts?.Cancel();
                _timerCts?.Dispose();
                _timerCts = null;

                if (ActiveAction == "shutdown" || ActiveAction == "restart" || ActiveAction == "restart_bios" || action == "cancel")
                {
                    _lastCancelTime = DateTime.UtcNow;
                    ExecutePowerAction("cancel");
                }

                if (action == "cancel")
                {
                    ClearState();
                    BroadcastState();
                    return;
                }

                if (seconds > 0)
                {
                    ActiveAction = action;
                    TargetTime = DateTime.UtcNow.AddSeconds(seconds);
                    if (action == "shutdown" || action == "restart" || action == "restart_bios")
                    {
                        ExecutePowerAction(action, seconds);
                    }
                    else
                    {
                        // Sleep/Hibernate custom timer
                        _timerCts = new CancellationTokenSource();
                        _ = FallbackTimer(action, TargetTime.Value, _timerCts.Token);
                    }
                    BroadcastState();
                }
                else
                {
                    ClearState();
                    BroadcastState();
                    ExecutePowerAction(action, 0);
                }
            }
        }

        private static async Task FallbackTimer(string action, DateTime target, CancellationToken token)
        {
            var delay = target - DateTime.UtcNow;
            if (delay.TotalMilliseconds > 0)
            {
                try 
                { 
                    await Task.Delay(delay, token); 
                }
                catch (TaskCanceledException) 
                { 
                    return; 
                }
            }

            lock (_powerLock)
            {
                if (ActiveAction == action && TargetTime == target)
                {
                    ClearState();
                    BroadcastState();
                    ExecutePowerAction(action, 0);
                }
            }
        }

        private static void ClearState()
        {
            ActiveAction = null;
            TargetTime = null;
        }

        private static void BroadcastState()
        {
            long ms = TargetTime.HasValue ? new DateTimeOffset(TargetTime.Value).ToUnixTimeMilliseconds() : -1;
            int rem = TargetTime.HasValue ? (int)(TargetTime.Value - DateTime.UtcNow).TotalSeconds : -1;
            var msg = JsonSerializer.Serialize(new { type = "timer", targetMs = ms, remaining = rem, action = ActiveAction ?? "cancelled" });
            Server.Broadcast(msg);
        }

        public static void SendInitialState(IWebSocketConnection socket)
        {
            lock (_powerLock)
            {
                if (ActiveAction != null && TargetTime.HasValue)
                {
                    long ms = new DateTimeOffset(TargetTime.Value).ToUnixTimeMilliseconds();
                    int rem = (int)(TargetTime.Value - DateTime.UtcNow).TotalSeconds;
                    var msg = JsonSerializer.Serialize(new { type = "timer", targetMs = ms, remaining = rem, action = ActiveAction });
                    try { socket.Send(msg); } catch (Exception ex) { Logger.Log("POWER", $"WS send error: {ex.Message}", ConsoleColor.Red); }
                }
            }
        }

        [DllImport("PowrProf.dll", SetLastError = true)]
        static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        static void ExecutePowerAction(string action, int seconds = 0)
        {
            try
            {
                switch (action)
                {
                    case "shutdown": Process.Start(new ProcessStartInfo("shutdown", $"/s /f /t {seconds}") { CreateNoWindow = true }); break;
                    case "restart": Process.Start(new ProcessStartInfo("shutdown", $"/r /f /t {seconds}") { CreateNoWindow = true }); break;
                    case "restart_bios": Process.Start(new ProcessStartInfo("shutdown", $"/r /fw /f /t {seconds}") { CreateNoWindow = true }); break;
                    case "sleep": SetSuspendState(false, false, false); break;
                    case "hibernate": Process.Start(new ProcessStartInfo("shutdown", "/h") { CreateNoWindow = true }); break;
                    case "cancel": Process.Start(new ProcessStartInfo("shutdown", "/a") { CreateNoWindow = true }); break;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("POWER", $"Error executing {action}: {ex.Message}", ConsoleColor.Red);
            }
        }

        public static void Cleanup()
        {
            SystemEvents.SessionEnding -= OnSessionEnding;
            
            if (_shutdownEventWatcher != null)
            {
                try 
                {
                    _shutdownEventWatcher.Enabled = false;
                    _shutdownEventWatcher.Dispose(); 
                } 
                catch (Exception ex) 
                { 
                    Logger.Log("POWER", $"Watcher cleanup error: {ex.Message}", ConsoleColor.Red);
                }
                finally { _shutdownEventWatcher = null; }
            }

            if (_timerCts != null)
            {
                try 
                { 
                    _timerCts.Cancel(); 
                    _timerCts.Dispose(); 
                } 
                catch { }
                finally { _timerCts = null; }
            }
        }

        private static void RegisterCommands()
        {
            Server.RegisterCommand("power", (s, r) => { var action = r.TryGetProperty("action", out var act) ? act.GetString() ?? "" : ""; var seconds = r.TryGetProperty("seconds", out var secs) ? secs.GetInt32() : 0; if (action == "cancel") seconds = 0; Logger.Log("POWER", seconds > 0 ? $"{action} in {seconds}s" : action, ConsoleColor.Red); HandleCommand(action, seconds); return Task.CompletedTask; });
        }
    }
}