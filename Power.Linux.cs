using System.Diagnostics;
using System.Text.Json;
using Fleck;

namespace Wiremote
{
    public static class PowerService
    {
        private static readonly object _powerLock = new();
        private static DateTime? _lastCancelTime = null;
        private static CancellationTokenSource? _timerCts;

        public static string? ActiveAction { get; private set; }
        public static DateTime? TargetTime { get; private set; }

        public static void Init()
        {
            RegisterCommands();
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
                    _timerCts = new CancellationTokenSource();
                    _ = FallbackTimer(action, TargetTime.Value, _timerCts.Token);
                    BroadcastState();
                }
                else
                {
                    ClearState();
                    BroadcastState();
                    ExecutePowerAction(action);
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
                    ExecutePowerAction(action);
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

        private static void ExecutePowerAction(string action)
        {
            try
            {
                switch (action)
                {
                    case "shutdown":
                        if (!RunCmd("systemctl", "poweroff")) RunCmd("shutdown", "-h now");
                        break;
                    case "restart":
                        if (!RunCmd("systemctl", "reboot")) RunCmd("reboot", "");
                        break;
                    case "restart_bios":
                        RunCmd("systemctl", "reboot --firmware-setup");
                        break;
                    case "sleep":
                        RunCmd("systemctl", "suspend");
                        break;
                    case "hibernate":
                        RunCmd("systemctl", "hibernate");
                        break;
                    case "cancel":
                        RunCmd("shutdown", "-c");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("POWER", $"Error executing {action}: {ex.Message}", ConsoleColor.Red);
            }
        }

        private static bool RunCmd(string cmd, string args)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(cmd, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return p != null;
            }
            catch { return false; }
        }

        public static void Cleanup()
        {
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
            Server.RegisterCommand("power", (s, r) =>
            {
                var action = r.TryGetProperty("action", out var act) ? act.GetString() ?? "" : "";
                var seconds = r.TryGetProperty("seconds", out var secs) ? secs.GetInt32() : 0;
                if (action == "cancel") seconds = 0;
                Logger.Log("POWER", seconds > 0 ? $"{action} in {seconds}s" : action, ConsoleColor.Red);
                HandleCommand(action, seconds);
                return Task.CompletedTask;
            });
        }
    }
}
