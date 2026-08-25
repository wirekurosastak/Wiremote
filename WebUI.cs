using System.Reflection;

namespace Wiremote
{
    public static class WebUI
    {
        // Thread-safe memória cache a statikus fájlokhoz (előre betöltve)
        private static readonly Dictionary<string, (byte[] content, string contentType)> _resourceCache = new(StringComparer.OrdinalIgnoreCase);

        static WebUI()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resources = assembly.GetManifestResourceNames();
            foreach (var res in resources)
            {
                if (res.StartsWith("Wiremote.wwwroot."))
                {
                    string fileName = res.Substring("Wiremote.wwwroot.".Length);
                    LoadAndCache(fileName, res, assembly);
                }
            }
        }

        private static void LoadAndCache(string fileName, string resourceName, Assembly assembly)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return;

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] bytes = ms.ToArray();

            string contentType = fileName switch
            {
                var f when f.EndsWith(".html") => "text/html; charset=utf-8",
                var f when f.EndsWith(".css") => "text/css; charset=utf-8",
                var f when f.EndsWith(".js") => "text/javascript; charset=utf-8",
                _ => "application/octet-stream"
            };

            _resourceCache[fileName] = (bytes, contentType);
        }

        public static (byte[] content, string contentType)? GetStaticResource(string path)
        {
            string fileName = path.TrimStart('/');
            if (string.IsNullOrEmpty(fileName) || fileName == "index.html")
            {
                fileName = "index.html";
            }

            if (_resourceCache.TryGetValue(fileName, out var cached))
                return cached;
            
            return null;
        }
    }
}