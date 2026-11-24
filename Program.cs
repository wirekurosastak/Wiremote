using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Wiremote
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (OperatingSystem.IsLinux())
            {
                RefreshLinuxSessionEnvironment();
            }

            if (OperatingSystem.IsWindows())
            {
                if (IsAdministrator())
                {
                    SetupFirewallAndHttp();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Warning: Not running as Administrator! If you cannot connect, run as Administrator once to configure firewall rules.");
                    Console.ResetColor();
                }
            }
            else if (OperatingSystem.IsLinux() && IsAdministrator())
            {
                SetupFirewallAndHttp();
            }

            Fleck.FleckLog.LogAction = (level, msg, ex) =>
            {
                if (level >= Fleck.LogLevel.Warn && !msg.Contains("Data sent while closing"))
                    Console.WriteLine($"[FLECK {level}] {msg}");
            };

            Console.WriteLine("\n==========================================");
            Console.WriteLine("                Wiremote");
            Console.WriteLine("https://github.com/wirekurosastak/Wiremote");
            Console.WriteLine("==========================================\n");

            Server.InitAuthToken();

            var ip = GetLocalIp();
            Console.WriteLine($"Open on phone:   http://{ip}:{Server.HTTP_PORT}/?token={Server.AuthToken}");
            Console.WriteLine($"Auth Token:      {Server.AuthToken}");
            Console.WriteLine($"WebSocket:       ws://{ip}:{Server.WS_PORT}");
            Console.WriteLine();
            Console.WriteLine("Press CTRL+C to stop.\n");

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            AudioService.Init();
            BrightnessService.Init();
            PowerService.Init();
            InputService.Init();
            DisplayManager.Init();
            _ = MediaService.InitAsync();

            _ = HttpServer.Start(cts.Token);
            Server.StartWebSocketServer();

            try { await Task.Delay(-1, cts.Token); } 
            catch (TaskCanceledException) { }

            Console.WriteLine("\nExiting...");
            Server.Stop();
            HttpServer.Stop();
            
            // Proper resource cleanup
            AudioService.Cleanup();
            PowerService.Cleanup();
            BrightnessService.Cleanup();
            MediaService.Cleanup();
        }

        static bool IsAdministrator()
        {
            if (OperatingSystem.IsWindows())
            {
                return CheckWindowsAdmin();
            }
            if (OperatingSystem.IsLinux())
            {
                return Interop.geteuid() == 0;
            }
            return false;
        }

        [SupportedOSPlatform("windows")]
        static bool CheckWindowsAdmin()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        static void SetupFirewallAndHttp()
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    void RunCmd(string args)
                    {
                        using var p = Process.Start(new ProcessStartInfo("netsh", args) { CreateNoWindow = true, UseShellExecute = false });
                        p?.WaitForExit();
                    }

                    RunCmd($"http delete urlacl url=http://*:{Server.HTTP_PORT}/");
                    // Use Well-Known SID S-1-1-0 (Everyone) to support localized Windows OS (German, Spanish, etc.)
                    RunCmd($"http add urlacl url=http://*:{Server.HTTP_PORT}/ user=\"S-1-1-0\"");
                    RunCmd($"http add urlacl url=http://*:{Server.HTTP_PORT}/ user=Everyone");
                    
                    RunCmd($"advfirewall firewall delete rule name=\"RemoteControl HTTP {Server.HTTP_PORT}\"");
                    RunCmd($"advfirewall firewall add rule name=\"RemoteControl HTTP {Server.HTTP_PORT}\" dir=in action=allow protocol=TCP localport={Server.HTTP_PORT}");

                    RunCmd($"advfirewall firewall delete rule name=\"RemoteControl WS {Server.WS_PORT}\"");
                    RunCmd($"advfirewall firewall add rule name=\"RemoteControl WS {Server.WS_PORT}\" dir=in action=allow protocol=TCP localport={Server.WS_PORT}");
                }
                catch (Exception ex)
                {
                    Logger.Log("FIREWALL", $"Failed to setup firewall rules: {ex.Message}", ConsoleColor.Red);
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo("firewall-cmd", $"--add-port={Server.HTTP_PORT}/tcp --add-port={Server.WS_PORT}/tcp") { CreateNoWindow = true });
                    p?.WaitForExit(1000);
                }
                catch { }
            }
        }

        static string GetLocalIp()
        {
            var ip = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .FirstOrDefault(ipStr => ipStr.StartsWith("192.168.") || ipStr.StartsWith("10.") || ipStr.StartsWith("172."));

            return ip ?? "127.0.0.1";
        }

        static void SetEnvBoth(string name, string value)
        {
            try
            {
                Environment.SetEnvironmentVariable(name, value);
                Interop.setenv(name, value, 1);
            }
            catch { }
        }

        // Safe to call repeatedly. With systemd linger the service starts at boot, before the
        // graphical session exists, so the Wayland socket and xauth file only appear later
        // (and can change on every re-login). Callers refresh before spawning GUI tools.
        internal static void RefreshLinuxSessionEnvironment()
        {
            try
            {
                uint uid = Interop.geteuid();
                string runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? $"/run/user/{uid}";
                SetEnvBoth("XDG_RUNTIME_DIR", runtimeDir);

                string? wd = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
                if (string.IsNullOrEmpty(wd) || !File.Exists(Path.Combine(runtimeDir, wd)))
                {
                    string? socket = Directory.Exists(runtimeDir)
                        ? Directory.GetFiles(runtimeDir, "wayland-*")
                            .Select(Path.GetFileName)
                            .Where(f => f != null && !f.EndsWith(".lock"))
                            .OrderBy(f => f, StringComparer.Ordinal)
                            .FirstOrDefault()
                        : null;
                    if (socket != null) SetEnvBoth("WAYLAND_DISPLAY", socket);
                }

                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
                {
                    SetEnvBoth("DISPLAY", ":0");
                }
                else
                {
                    SetEnvBoth("DISPLAY", Environment.GetEnvironmentVariable("DISPLAY")!);
                }

                var authFiles = Directory.Exists(runtimeDir) 
                    ? Directory.GetFiles(runtimeDir, "xauth_*").OrderByDescending(f => File.GetLastWriteTimeUtc(f)).ToArray()
                    : Array.Empty<string>();

                if (authFiles.Length > 0)
                {
                    SetEnvBoth("XAUTHORITY", authFiles[0]);
                }
                else
                {
                    string homeAuth = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Xauthority");
                    if (File.Exists(homeAuth))
                        SetEnvBoth("XAUTHORITY", homeAuth);
                }

                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("QT_QPA_PLATFORM")))
                {
                    SetEnvBoth("QT_QPA_PLATFORM", "wayland;xcb");
                }
            }
            catch { }
        }
    }
}