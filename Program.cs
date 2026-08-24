using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Diagnostics;

namespace PCRemote
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
            Console.WriteLine("            PC REMOTE CONTROL");
            Console.WriteLine("https://github.com/wirekurosastak/PCRemote");
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
            _ = MediaService.InitAsync();

            _ = Server.StartHttpServer(cts.Token);
            Server.StartWebSocketServer();

            try { await Task.Delay(-1, cts.Token); } 
            catch (TaskCanceledException) { }

            Console.WriteLine("\nExiting...");
            Server.Stop();
            
            // Tisztességes erőforrás-felszabadítás
            AudioService.Cleanup();
            PowerService.Cleanup();
            BrightnessService.Cleanup(); // Hozzáadva a WMI szivárgások miatt
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
                RunCmd($"http add urlacl url=http://*:{Server.HTTP_PORT}/ user=Everyone");
                
                RunCmd($"advfirewall firewall delete rule name=\"RemoteControl HTTP {Server.HTTP_PORT}\"");
                RunCmd($"advfirewall firewall add rule name=\"RemoteControl HTTP {Server.HTTP_PORT}\" dir=in action=allow protocol=TCP localport={Server.HTTP_PORT}");

                RunCmd($"advfirewall firewall delete rule name=\"RemoteControl WS {Server.WS_PORT}\"");
                RunCmd($"advfirewall firewall add rule name=\"RemoteControl WS {Server.WS_PORT}\" dir=in action=allow protocol=TCP localport={Server.WS_PORT}");
            }
            catch { }
        }

        static string GetLocalIp()
        {
            var ipv4Addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address);

            foreach (var ip in ipv4Addresses)
            {
                var bytes = ip.GetAddressBytes();
                bool isPrivate = bytes[0] == 10 ||
                                (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                                (bytes[0] == 192 && bytes[1] == 168);

                if (isPrivate) return ip.ToString();
            }
            return "127.0.0.1";
        }
    }
}