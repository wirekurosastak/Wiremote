using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Diagnostics;

namespace Wiremote
{
    class Program
    {
        static async Task Main(string[] args)
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

            Fleck.FleckLog.LogAction = (level, msg, ex) =>
            {
                if (level >= Fleck.LogLevel.Warn && !msg.Contains("Data sent while closing"))
                    Console.WriteLine($"[FLECK {level}] {msg}");
            };

            Console.WriteLine("\n==========================================");
            Console.WriteLine("                Wiremote");
            Console.WriteLine("https://github.com/wirekurosastak/Wiremote");
            Console.WriteLine("==========================================\n");

            var ip = GetLocalIp();
            Console.WriteLine($"Open on phone:   http://{ip}:{Server.HTTP_PORT}");
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
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        static void SetupFirewallAndHttp()
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
    }
}