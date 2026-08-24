using System.Reflection;

namespace PCRemote
{
    public static class WebUI
    {
        public static (byte[] content, string contentType)? GetStaticResource(string path)
        {
            string fileName = path.TrimStart('/');
            if (string.IsNullOrEmpty(fileName) || fileName == "index.html")
                fileName = "index.html";

            string resourceName = $"PCRemote.wwwroot.{fileName.Replace('/', '.')}";
            var assembly = Assembly.GetExecutingAssembly();

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] bytes = ms.ToArray();

            string contentType = fileName switch
            {
                "index.html" => "text/html; charset=utf-8",
                "style.css" => "text/css; charset=utf-8",
                "app.js" => "text/javascript; charset=utf-8",
                _ => "application/octet-stream"
            };

            return (bytes, contentType);
        }
    }
}
