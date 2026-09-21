using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fleck;

namespace Wiremote
{
    public static class MediaService
    {
        public enum MediaAction { PlayPause, Next, Previous }

        private static byte[]? _currentThumbnailBytes;
        private static string _currentThumbnailMime = "image/png";
        private static long _thumbnailTimestamp = 0;
        private static readonly object _thumbLock = new();

        private static Timer? _pollTimer;
        private static string _lastNowPlayingJson = "";
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

        public static (byte[]? bytes, string mime) GetCurrentThumbnail()
        {
            lock (_thumbLock)
            {
                return (_currentThumbnailBytes, _currentThumbnailMime);
            }
        }

        public static Task InitAsync()
        {
            RegisterCommands();

            // Periodic poll to detect media changes across MPRIS players and PipeWire streams
            _pollTimer = new Timer(async _ =>
            {
                try
                {
                    string json = await BuildNowPlayingJsonAsync();
                    if (json != _lastNowPlayingJson)
                    {
                        _lastNowPlayingJson = json;
                        Server.Broadcast(json);
                    }
                }
                catch { }
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

            return Task.CompletedTask;
        }

        private static (bool success, string output) RunCmd(string cmd, string args, string? locale = null)
        {
            try
            {
                var psi = new ProcessStartInfo(cmd, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                if (!string.IsNullOrEmpty(locale))
                {
                    psi.EnvironmentVariables["LC_ALL"] = locale;
                }

                using var p = Process.Start(psi);
                if (p == null) return (false, "");
                string outStr = p.StandardOutput.ReadToEnd();
                p.WaitForExit(800);
                return (p.ExitCode == 0, outStr.Trim());
            }
            catch { return (false, ""); }
        }

        private static string DecodeBusctlString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains('\\'))
            {
                try
                {
                    var bytes = new List<byte>();
                    for (int i = 0; i < s.Length; i++)
                    {
                        if (s[i] == '\\' && i + 3 < s.Length &&
                            s[i + 1] >= '0' && s[i + 1] <= '7' &&
                            s[i + 2] >= '0' && s[i + 2] <= '7' &&
                            s[i + 3] >= '0' && s[i + 3] <= '7')
                        {
                            int b = ((s[i + 1] - '0') << 6) | ((s[i + 2] - '0') << 3) | (s[i + 3] - '0');
                            bytes.Add((byte)b);
                            i += 3;
                        }
                        else if (s[i] == '\\' && i + 1 < s.Length)
                        {
                            char next = s[i + 1];
                            switch (next)
                            {
                                case 'n': bytes.Add((byte)'\n'); i++; break;
                                case 'r': bytes.Add((byte)'\r'); i++; break;
                                case 't': bytes.Add((byte)'\t'); i++; break;
                                case '"': bytes.Add((byte)'"'); i++; break;
                                case '\\': bytes.Add((byte)'\\'); i++; break;
                                default:
                                    var utfBytes = System.Text.Encoding.UTF8.GetBytes(new[] { s[i] });
                                    bytes.AddRange(utfBytes);
                                    break;
                            }
                        }
                        else
                        {
                            var utfBytes = System.Text.Encoding.UTF8.GetBytes(new[] { s[i] });
                            bytes.AddRange(utfBytes);
                        }
                    }
                    return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
                }
                catch { }
            }
            return s;
        }

        private static List<string> GetMprisPlayers()
        {
            var players = new List<string>();
            try
            {
                var (_, raw) = RunCmd("busctl", "--user call org.freedesktop.DBus /org/freedesktop/DBus org.freedesktop.DBus ListNames");
                var matches = Regex.Matches(raw, @"""(org\.mpris\.MediaPlayer2\.[^""]+)""");
                foreach (Match m in matches)
                {
                    players.Add(m.Groups[1].Value);
                }
            }
            catch { }
            return players;
        }

        private static string? GetActivePlayer()
        {
            var players = GetMprisPlayers();
            if (players.Count == 0) return null;

            // Prioritize player currently playing
            foreach (var p in players)
            {
                var (_, status) = RunCmd("busctl", $"--user get-property {p} /org/mpris/MediaPlayer2 org.mpris.MediaPlayer2.Player PlaybackStatus");
                if (status.Contains("\"Playing\"", StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }

            // Fallback to first player that has non-empty metadata
            foreach (var p in players)
            {
                var (_, meta) = RunCmd("busctl", $"--user get-property {p} /org/mpris/MediaPlayer2 org.mpris.MediaPlayer2.Player Metadata");
                if (Regex.IsMatch(meta, @"(?:""xesam:title""|xesam:title)\s+s\s+""([^""]+)"""))
                {
                    return p;
                }
            }

            return players[0];
        }

        public static async Task MediaCommand(MediaAction action)
        {
            string? player = GetActivePlayer();
            if (!string.IsNullOrEmpty(player))
            {
                string method = action switch
                {
                    MediaAction.PlayPause => "PlayPause",
                    MediaAction.Next => "Next",
                    MediaAction.Previous => "Previous",
                    _ => "PlayPause"
                };

                var (ok, _) = RunCmd("busctl", $"--user call {player} /org/mpris/MediaPlayer2 org.mpris.MediaPlayer2.Player {method}");
                if (ok)
                {
                    Logger.Log("MEDIA", $"{action} → MPRIS ({player})", ConsoleColor.Magenta);
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(250);
                        await BroadcastNowPlayingAsync();
                    });
                    return;
                }
            }

            // KDE global media shortcut fallback
            string kdeShortcut = action switch
            {
                MediaAction.PlayPause => "playpausemedia",
                MediaAction.Next => "nextmedia",
                MediaAction.Previous => "previousmedia",
                _ => "playpausemedia"
            };

            var (kdeOk, _) = RunCmd("busctl", $"--user call org.kde.kglobalaccel /component/mediacontrol org.kde.kglobalaccel.Component invokeShortcut s \"{kdeShortcut}\"");
            if (kdeOk)
            {
                Logger.Log("MEDIA", $"{action} → KDE shortcut ({kdeShortcut})", ConsoleColor.Magenta);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(250);
                    await BroadcastNowPlayingAsync();
                });
                return;
            }

            // Virtual media key fallback
            ushort key = action switch
            {
                MediaAction.Next => InputService.VK_MEDIA_NEXT_TRACK,
                MediaAction.Previous => InputService.VK_MEDIA_PREV_TRACK,
                _ => InputService.VK_MEDIA_PLAY_PAUSE
            };
            await InputService.MediaKey(key);
            Logger.Log("MEDIA", $"{action} → media key", ConsoleColor.Magenta);
        }

        private static DateTime _lastSeekTime = DateTime.MinValue;

        public static async Task MediaSeek(int deltaSeconds)
        {
            if ((DateTime.UtcNow - _lastSeekTime).TotalMilliseconds < 300)
                return;
            _lastSeekTime = DateTime.UtcNow;

            string? player = GetActivePlayer();
            if (!string.IsNullOrEmpty(player))
            {
                long offsetUs = (long)deltaSeconds * 1_000_000L;
                var (ok, _) = RunCmd("busctl", $"--user call {player} /org/mpris/MediaPlayer2 org.mpris.MediaPlayer2.Player Seek x {offsetUs}");
                if (ok)
                {
                    Logger.Log("MEDIA", $"seek {deltaSeconds:+#;-#}s → MPRIS ({player})", ConsoleColor.Magenta);
                    return;
                }
            }

            // KDE global media shortcut fallback
            string kdeSeekShortcut = deltaSeconds < 0 ? "seekbackwardmedia" : "seekforwardmedia";
            var (kdeOk, _) = RunCmd("busctl", $"--user call org.kde.kglobalaccel /component/mediacontrol org.kde.kglobalaccel.Component invokeShortcut s \"{kdeSeekShortcut}\"");
            if (kdeOk)
            {
                Logger.Log("MEDIA", $"seek {deltaSeconds:+#;-#}s → KDE shortcut ({kdeSeekShortcut})", ConsoleColor.Magenta);
                return;
            }

            // Keyboard arrow key fallback
            ushort key = deltaSeconds < 0 ? InputService.VK_LEFT : InputService.VK_RIGHT;
            for (int i = 0; i < 3; i++)
            {
                await InputService.KeyboardKey(key);
                if (i < 2) await Task.Delay(30);
            }
            Logger.Log("MEDIA", $"seek {deltaSeconds:+#;-#}s → arrow keys (x3)", ConsoleColor.Magenta);
        }

        private static (string title, string artist, string status)? GetSinkInputNowPlaying()
        {
            try
            {
                var (ok, raw) = RunCmd("pactl", "list sink-inputs", locale: "C");
                if (!ok || string.IsNullOrEmpty(raw)) return null;

                var blocks = raw.Split(new[] { "Sink Input #" }, StringSplitOptions.RemoveEmptyEntries);
                var candidates = new List<(string title, string artist, string status, bool isLive)>();

                foreach (var b in blocks)
                {
                    var appMatch = Regex.Match(b, @"application\.name\s*=\s*""([^""]+)""");
                    string appName = appMatch.Success ? appMatch.Groups[1].Value.Trim() : "";

                    if (string.Equals(appName, "Wiremote", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(appName, "libcanberra", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(appName, "speech-dispatcher", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(appName, "gsound", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string title = "";
                    var titleMatch = Regex.Match(b, @"media\.title\s*=\s*""([^""]+)""");
                    if (titleMatch.Success)
                    {
                        title = titleMatch.Groups[1].Value.Trim();
                    }
                    else
                    {
                        var nameMatch = Regex.Match(b, @"media\.name\s*=\s*""([^""]+)""");
                        if (nameMatch.Success) title = nameMatch.Groups[1].Value.Trim();
                    }

                    if (title.Contains('/') || title.Contains('\\'))
                    {
                        try { title = Path.GetFileNameWithoutExtension(title); } catch { }
                    }

                    if (string.IsNullOrWhiteSpace(title) || title.StartsWith("Playback Stream", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(appName)) continue;
                        title = appName;
                    }

                    string artist = "";
                    var artistMatch = Regex.Match(b, @"media\.artist\s*=\s*""([^""]+)""");
                    if (artistMatch.Success)
                    {
                        artist = artistMatch.Groups[1].Value.Trim();
                    }
                    else
                    {
                        artist = appName;
                    }

                    bool isCorked = b.Contains("Corked: yes", StringComparison.OrdinalIgnoreCase) ||
                                    b.Contains("pulse.corked = \"true\"", StringComparison.OrdinalIgnoreCase);

                    string status = isCorked ? "paused" : "playing";
                    candidates.Add((title, artist, status, !isCorked));
                }

                if (candidates.Count == 0) return null;

                var active = candidates.FirstOrDefault(c => c.isLive);
                if (!string.IsNullOrEmpty(active.title)) return (active.title, active.artist, active.status);

                var fallback = candidates[0];
                if (!string.IsNullOrEmpty(fallback.title)) return (fallback.title, fallback.artist, fallback.status);

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<string> BuildNowPlayingJsonAsync()
        {
            try
            {
                var players = GetMprisPlayers();
                string title = "";
                string artist = "";
                string status = "paused";
                string? artUrl = null;

                // Sort players: playing players first
                var orderedPlayers = players.OrderByDescending(p =>
                {
                    var (_, st) = RunCmd("busctl", $"--user get-property {p} /org/mpris/MediaPlayer2 org.mpris.MediaPlayer2.Player PlaybackStatus");
                    return st.Contains("\"Playing\"", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                }).ToList();

                foreach (var p in orderedPlayers)
                {
                    var (_, statusProp) = RunCmd("busctl", $"--user get-property {p} /org/mpris/MediaPlayer2 org.mpris.MediaPlayer2.Player PlaybackStatus");
                    var (_, metaProp) = RunCmd("busctl", $"--user get-property {p} /org/mpris/MediaPlayer2 org.mpris.MediaPlayer2.Player Metadata");

                    var titleMatch = Regex.Match(metaProp, @"(?:""xesam:title""|xesam:title)\s+s\s+""((?:\\.|[^""\\])*)""");
                    string pTitle = titleMatch.Success ? DecodeBusctlString(titleMatch.Groups[1].Value).Trim() : "";

                    string pArtist = "";
                    var artistArrayMatch = Regex.Match(metaProp, @"(?:""xesam:artist""|xesam:artist)\s+as\s+(\d+)\s+((?:""(?:\\.|[^""\\])*""\s*)+)");
                    if (artistArrayMatch.Success && int.TryParse(artistArrayMatch.Groups[1].Value, out int count) && count > 0)
                    {
                        var artists = new List<string>();
                        var strMatches = Regex.Matches(artistArrayMatch.Groups[2].Value, @"""((?:\\.|[^""\\])*)""");
                        foreach (Match sm in strMatches)
                        {
                            artists.Add(DecodeBusctlString(sm.Groups[1].Value));
                            if (artists.Count >= count) break;
                        }
                        pArtist = string.Join(", ", artists).Trim();
                    }
                    else
                    {
                        var artistStrMatch = Regex.Match(metaProp, @"(?:""xesam:artist""|xesam:artist)\s+s\s+""((?:\\.|[^""\\])*)""");
                        if (artistStrMatch.Success)
                        {
                            pArtist = DecodeBusctlString(artistStrMatch.Groups[1].Value).Trim();
                        }
                    }

                    if (!string.IsNullOrEmpty(pTitle) || !string.IsNullOrEmpty(pArtist))
                    {
                        title = pTitle;
                        artist = pArtist;
                        status = statusProp.Contains("\"Playing\"", StringComparison.OrdinalIgnoreCase) ? "playing" : "paused";

                        var artMatch = Regex.Match(metaProp, @"(?:""mpris:artUrl""|mpris:artUrl)\s+s\s+""((?:\\.|[^""\\])*)""");
                        if (artMatch.Success) artUrl = DecodeBusctlString(artMatch.Groups[1].Value).Trim();

                        break;
                    }
                }

                // Fallback: If no MPRIS metadata found, check PipeWire / PulseAudio sink-inputs
                if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(artist))
                {
                    var sinkNowPlaying = GetSinkInputNowPlaying();
                    if (sinkNowPlaying != null)
                    {
                        title = sinkNowPlaying.Value.title;
                        artist = sinkNowPlaying.Value.artist;
                        status = sinkNowPlaying.Value.status;
                        artUrl = null;
                    }
                }

                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
                {
                    lock (_thumbLock) { _currentThumbnailBytes = null; }
                    return JsonSerializer.Serialize(new { type = "nowplaying", playing = false });
                }

                string? thumb = await UpdateThumbnailAsync(artUrl);

                return JsonSerializer.Serialize(new
                {
                    type = "nowplaying",
                    playing = true,
                    title,
                    artist,
                    status,
                    thumb
                });
            }
            catch (Exception ex)
            {
                Logger.Log("MEDIA", $"BuildNowPlaying error: {ex.Message}", ConsoleColor.DarkGray);
                return JsonSerializer.Serialize(new { type = "nowplaying", playing = false });
            }
        }

        private static async Task<string?> UpdateThumbnailAsync(string? artUrl)
        {
            if (string.IsNullOrWhiteSpace(artUrl))
            {
                lock (_thumbLock) { _currentThumbnailBytes = null; }
                return null;
            }

            try
            {
                byte[]? bytes = null;
                string mime = "image/png";

                if (artUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    int comma = artUrl.IndexOf(',');
                    if (comma > 0)
                    {
                        bytes = Convert.FromBase64String(artUrl.Substring(comma + 1));
                        int colon = artUrl.IndexOf(':');
                        int semi = artUrl.IndexOf(';');
                        if (colon >= 0 && semi > colon)
                        {
                            mime = artUrl.Substring(colon + 1, semi - colon - 1);
                        }
                    }
                }
                else if (artUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(artUrl);
                    if (File.Exists(uri.LocalPath))
                    {
                        bytes = await File.ReadAllBytesAsync(uri.LocalPath);
                        mime = uri.LocalPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                               uri.LocalPath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                            ? "image/jpeg" : "image/png";
                    }
                }
                else if (File.Exists(artUrl))
                {
                    bytes = await File.ReadAllBytesAsync(artUrl);
                    mime = artUrl.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                           artUrl.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                        ? "image/jpeg" : "image/png";
                }
                else if (artUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         artUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    bytes = await _http.GetByteArrayAsync(artUrl);
                    mime = (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8) ? "image/jpeg" : "image/png";
                }

                if (bytes == null || bytes.Length == 0 || bytes.Length > 5_000_000)
                {
                    lock (_thumbLock) { _currentThumbnailBytes = null; }
                    return null;
                }

                long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                lock (_thumbLock)
                {
                    if (_currentThumbnailBytes != null && _currentThumbnailBytes.Length == bytes.Length && _currentThumbnailBytes.SequenceEqual(bytes))
                    {
                        return $"/thumbnail?ts={_thumbnailTimestamp}";
                    }
                    _currentThumbnailBytes = bytes;
                    _currentThumbnailMime = mime;
                    _thumbnailTimestamp = ts;
                }
                return $"/thumbnail?ts={ts}";
            }
            catch
            {
                lock (_thumbLock) { _currentThumbnailBytes = null; }
                return null;
            }
        }

        public static async Task BroadcastNowPlayingAsync()
        {
            string json = await BuildNowPlayingJsonAsync();
            _lastNowPlayingJson = json;
            Server.Broadcast(json);
        }

        public static async Task SendNowPlayingAsync(IWebSocketConnection socket)
        {
            string json = await BuildNowPlayingJsonAsync();
            try { _ = socket.Send(json); } catch { }
        }

        public static void Cleanup()
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
            lock (_thumbLock) { _currentThumbnailBytes = null; }
        }

        private static void RegisterCommands()
        {
            Server.RegisterCommand("media_play_pause", (s, r) => MediaCommand(MediaAction.PlayPause));
            Server.RegisterCommand("media_previous", (s, r) => MediaCommand(MediaAction.Previous));
            Server.RegisterCommand("media_next", (s, r) => MediaCommand(MediaAction.Next));
            Server.RegisterCommand("web_left", (s, r) => MediaSeek(-15));
            Server.RegisterCommand("web_right", (s, r) => MediaSeek(15));
        }
    }
}
