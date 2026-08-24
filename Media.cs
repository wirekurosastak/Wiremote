using System.Text.Json;
using Fleck;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace PCRemote
{
    public static class MediaService
    {
        static GlobalSystemMediaTransportControlsSessionManager? _mediaManager;
        static GlobalSystemMediaTransportControlsSession? _mediaSession;

        static readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs>
            _mediaPropsHandler = (s, e) => { _ = BroadcastNowPlayingAsync(); };
        static readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs>
            _mediaPlaybackHandler = (s, e) => { _ = BroadcastNowPlayingAsync(); };

        public static async Task InitAsync()
        {
            try
            {
                _mediaManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _mediaManager.CurrentSessionChanged += OnCurrentMediaSessionChangedEvent;
                OnCurrentMediaSessionChanged();
            }
            catch (Exception ex)
            {
                Logger.Log("MEDIA", $"Now-Playing init failed: {ex.Message}", ConsoleColor.Red);
            }
        }

        static void OnCurrentMediaSessionChangedEvent(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            OnCurrentMediaSessionChanged();
        }

        static void OnCurrentMediaSessionChanged()
        {
            try
            {
                if (_mediaSession != null)
                {
                    try { _mediaSession.MediaPropertiesChanged -= _mediaPropsHandler; } catch { }
                    try { _mediaSession.PlaybackInfoChanged -= _mediaPlaybackHandler; } catch { }
                }

                _mediaSession = _mediaManager?.GetCurrentSession();

                if (_mediaSession != null)
                {
                    _mediaSession.MediaPropertiesChanged += _mediaPropsHandler;
                    _mediaSession.PlaybackInfoChanged += _mediaPlaybackHandler;
                }

                _ = BroadcastNowPlayingAsync();
            }
            catch (Exception ex)
            {
                Logger.Log("MEDIA", $"session change failed: {ex.Message}", ConsoleColor.Red);
            }
        }

        static async Task<string> BuildNowPlayingJsonAsync()
        {
            try
            {
                var session = _mediaSession;
                if (session == null)
                    return JsonSerializer.Serialize(new { type = "nowplaying", playing = false });

                var props = await session.TryGetMediaPropertiesAsync();
                var info = session.GetPlaybackInfo();

                string title = props?.Title ?? "";
                string artist = props?.Artist ?? "";
                if (string.IsNullOrWhiteSpace(artist)) artist = props?.AlbumArtist ?? "";

                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
                    return JsonSerializer.Serialize(new { type = "nowplaying", playing = false });

                string status =
                    info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                        ? "playing" : "paused";

                string? thumb = await ReadThumbnailAsync(props?.Thumbnail);

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
                Logger.Log("MEDIA", $"build json failed: {ex.Message}", ConsoleColor.Red);
                return JsonSerializer.Serialize(new { type = "nowplaying", playing = false });
            }
        }

        static async Task<string?> ReadThumbnailAsync(IRandomAccessStreamReference? thumbRef)
        {
            if (thumbRef == null) return null;
            try
            {
                using var stream = await thumbRef.OpenReadAsync();
                uint size = (uint)stream.Size;
                if (size == 0 || size > 2_000_000) return null;

                using var reader = new DataReader(stream.GetInputStreamAt(0));
                await reader.LoadAsync(size);
                var bytes = new byte[size];
                reader.ReadBytes(bytes);

                string mime = (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)
                    ? "image/jpeg" : "image/png";
                return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
            }
            catch (Exception ex)
            {
                Logger.Log("MEDIA", $"Thumbnail read error: {ex.Message}", ConsoleColor.DarkGray);
                return null;
            }
        }

        public enum MediaAction { PlayPause, Next, Previous }

        public static async Task MediaCommand(MediaAction action)
        {
            var session = _mediaSession;
            if (session != null)
            {
                try
                {
                    bool ok = action switch
                    {
                        MediaAction.PlayPause => await session.TryTogglePlayPauseAsync(),
                        MediaAction.Next => await session.TrySkipNextAsync(),
                        MediaAction.Previous => await session.TrySkipPreviousAsync(),
                        _ => false
                    };
                    if (ok)
                    {
                        Logger.Log("MEDIA", $"{action} → session", ConsoleColor.Magenta);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("MEDIA", $"session {action} failed: {ex.Message}", ConsoleColor.DarkGray);
                }
            }

            ushort key = action switch
            {
                MediaAction.Next => InputService.VK_MEDIA_NEXT_TRACK,
                MediaAction.Previous => InputService.VK_MEDIA_PREV_TRACK,
                _ => InputService.VK_MEDIA_PLAY_PAUSE
            };
            await InputService.MediaKey(key);
            Logger.Log("MEDIA", $"{action} → media key", ConsoleColor.Magenta);
        }

        static DateTime _lastSeekTime = DateTime.MinValue;

        public static async Task MediaSeek(int deltaSeconds)
        {
            if ((DateTime.UtcNow - _lastSeekTime).TotalMilliseconds < 500)
                return;
            _lastSeekTime = DateTime.UtcNow;

            var session = _mediaSession;
            if (session != null)
            {
                try
                {
                    var timeline = session.GetTimelineProperties();
                    var end = timeline.EndTime;

                    if (end > TimeSpan.Zero)
                    {
                        var target = timeline.Position + TimeSpan.FromSeconds(deltaSeconds);
                        if (target < timeline.StartTime) target = timeline.StartTime;
                        if (target > end) target = end;

                        if (await session.TryChangePlaybackPositionAsync(target.Ticks))
                        {
                            Logger.Log("MEDIA", $"seek {deltaSeconds:+#;-#}s → {target:hh\\:mm\\:ss}", ConsoleColor.Magenta);
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("MEDIA", $"seek failed: {ex.Message}", ConsoleColor.DarkGray);
                }
            }

            await InputService.KeyboardKey(deltaSeconds < 0 ? InputService.VK_LEFT : InputService.VK_RIGHT);
            Logger.Log("MEDIA", $"seek {deltaSeconds:+#;-#}s → arrow key", ConsoleColor.Magenta);
        }

        static async Task BroadcastNowPlayingAsync() => Server.Broadcast(await BuildNowPlayingJsonAsync());

        public static async Task SendNowPlayingAsync(IWebSocketConnection socket)
        {
            var json = await BuildNowPlayingJsonAsync();
            try { _ = socket.Send(json); } catch { }
        }

        // ÚJ: Eseménykezelők leválasztása a szivárgás ellen
        public static void Cleanup()
        {
            try
            {
                if (_mediaSession != null)
                {
                    _mediaSession.MediaPropertiesChanged -= _mediaPropsHandler;
                    _mediaSession.PlaybackInfoChanged -= _mediaPlaybackHandler;
                    _mediaSession = null;
                }
                
                if (_mediaManager != null)
                {
                    _mediaManager.CurrentSessionChanged -= OnCurrentMediaSessionChangedEvent;
                    _mediaManager = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("MEDIA", $"Cleanup error: {ex.Message}", ConsoleColor.DarkGray);
            }
        }
    }
}