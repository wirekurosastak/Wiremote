namespace Winremote
{
    public static class Logger
    {
        static readonly object logLock = new object();

        public static void Log(string tag, string msg, ConsoleColor color = ConsoleColor.Gray)
        {
            var t = DateTime.Now.ToString("HH:mm:ss");
            lock (logLock)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"[{t}] ");
                Console.ForegroundColor = color;
                Console.Write($"{tag,-10}");
                Console.ResetColor();
                Console.WriteLine(msg);
            }
        }
    }
}
