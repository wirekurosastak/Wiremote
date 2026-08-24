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

            try { await Task.Delay(-1, cts.Token); } catch (TaskCanceledException) { }

            Console.WriteLine("\nExiting...");
            Server.Stop();
            AudioService.Cleanup();
            PowerService.Cleanup();
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

                RunCmd("http delete urlacl url=http://*:8765/");
                RunCmd("http add urlacl url=http://*:8765/ user=Everyone");
                
                RunCmd("advfirewall firewall delete rule name=\"RemoteControl HTTP 8765\"");
                RunCmd("advfirewall firewall add rule name=\"RemoteControl HTTP 8765\" dir=in action=allow protocol=TCP localport=8765");

                RunCmd("advfirewall firewall delete rule name=\"RemoteControl WS 8766\"");
                RunCmd("advfirewall firewall add rule name=\"RemoteControl WS 8766\" dir=in action=allow protocol=TCP localport=8766");
            }
            catch { }
        }

        static string GetLocalIp()
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                    var ip = addr.Address;
                    var bytes = ip.GetAddressBytes();

                    bool isPrivate =
                        bytes[0] == 10 ||
                        (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                        (bytes[0] == 192 && bytes[1] == 168);

                    if (isPrivate) return ip.ToString();
                }
            }
            return "127.0.0.1";
        }
    }
}
