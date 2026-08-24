using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace PCRemote
{
    public static class PowerService
    {
        static EventLogWatcher? shutdownEventWatcher;
        static readonly object powerLock = new object();
        static DateTime? _lastCancelTime = null;
        public static string? ActiveAction { get; private set; }
        public static DateTime? TargetTime { get; private set; }

        static CancellationTokenSource? _timerCts;

        public static void Init()
        {
            try
            {
                SystemEvents.SessionEnding += OnSessionEnding;
                var query = new EventLogQuery("System", PathType.LogName, "*[System[(EventID=1075)]]");
                shutdownEventWatcher = new EventLogWatcher(query);
                shutdownEventWatcher.EventRecordWritten += (s, e) =>
                {
                    lock (powerLock)
                    {
                        if (ActiveAction == "shutdown" || ActiveAction == "restart")
                        {
                            if (_lastCancelTime.HasValue && (DateTime.UtcNow - _lastCancelTime.Value).TotalSeconds < 3) return;
                            Logger.Log("POWER", "OS shutdown cancelled externally (Event 1075)", ConsoleColor.Yellow);
                            ClearState();
                            BroadcastState();
                        }
                    }
                };
                shutdownEventWatcher.Enabled = true;
            }
            catch (Exception ex)
            {
                Logger.Log("POWER", $"Error registering Power events: {ex.Message}", ConsoleColor.Red);
            }
        }

        private static void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            Logger.Log("POWER", $"OS Session ending ({e.Reason})", ConsoleColor.Yellow);
            lock (powerLock) { ClearState(); }
        }

        public static void HandleCommand(string action, int seconds)
        {
            lock (powerLock)
            {
                _timerCts?.Cancel();
                _timerCts?.Dispose();
                _timerCts = null;

                if (ActiveAction == "shutdown" || ActiveAction == "restart" || action == "cancel")
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
                    if (action == "shutdown" || action == "restart")
                    {
                        ExecutePowerAction(action, seconds);
                    }
                    else
                    {
                        // Sleep/Hibernate need custom timer
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

        private static async System.Threading.Tasks.Task FallbackTimer(string action, DateTime target, CancellationToken token)
        {
            var delay = target - DateTime.UtcNow;
            if (delay.TotalMilliseconds > 0)
            {

                try { await System.Threading.Tasks.Task.Delay(delay, token); }
                catch (TaskCanceledException) { return; }

            }

            lock (powerLock)
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

        public static void SendInitialState(Fleck.IWebSocketConnection socket)
        {
            if (ActiveAction != null && TargetTime.HasValue)
            {
                long ms = new DateTimeOffset(TargetTime.Value).ToUnixTimeMilliseconds();
                int rem = (int)(TargetTime.Value - DateTime.UtcNow).TotalSeconds;
                var msg = JsonSerializer.Serialize(new { type = "timer", targetMs = ms, remaining = rem, action = ActiveAction });
                try { socket.Send(msg); } catch { }
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
            try { shutdownEventWatcher?.Dispose(); } catch { }
            try { _timerCts?.Cancel(); _timerCts?.Dispose(); } catch { }
        }
    }
}
