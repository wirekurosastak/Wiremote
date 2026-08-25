using System.Net;

namespace Wiremote
{
    public static class HttpServer
    {
        static HttpListener? _httpListener;

        public static async Task Start(CancellationToken token)
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://*:{Server.HTTP_PORT}/");

            try { _httpListener.Start(); }
            catch (HttpListenerException ex)
            {
                Logger.Log("HTTP", $"Failed to start on port {Server.HTTP_PORT}: {ex.Message}", ConsoleColor.Red);
                return;
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    using var response = context.Response;
                    var requestUrl = context.Request.Url;
                    if (requestUrl == null)
                    {
                        response.StatusCode = 400;
                        continue;
                    }
                    var path = requestUrl.AbsolutePath;

                    if (path.StartsWith("/thumbnail"))
                    {
                        var thumbInfo = MediaService.GetCurrentThumbnail();
                        if (thumbInfo.bytes != null)
                        {
                            response.ContentType = thumbInfo.mime;
                            response.ContentLength64 = thumbInfo.bytes.Length;
                            response.Headers.Add("Cache-Control", "public, max-age=31536000");
                            await response.OutputStream.WriteAsync(thumbInfo.bytes, 0, thumbInfo.bytes.Length);
                        }
                        else
                        {
                            response.StatusCode = 404;
                        }
                    }
                    else
                    {
                        var resource = WebUI.GetStaticResource(path);

                        if (resource.HasValue)
                        {
                            var (buffer, contentType) = resource.Value;
                            response.ContentType = contentType;
                            response.ContentLength64 = buffer.Length;
                            if (path == "/" || path == "/index.html")
                                response.Headers.Add("Cache-Control", "no-cache");
                            else
                                response.Headers.Add("Cache-Control", "public, max-age=31536000");

                            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        }
                        else
                        {
                            response.StatusCode = 404;
                        }
                    }
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Logger.Log("HTTP", $"Request error: {ex.Message}", ConsoleColor.Red); }
            }
        }

        public static void Stop()
        {
            try { _httpListener?.Stop(); } catch { }
        }
    }
}
