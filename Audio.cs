using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fleck;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace PCRemote
{
    public static class AudioService
    {
        static readonly MMDeviceEnumerator _enumerator = new();
        static MMDevice? _device;
        static readonly object _lock = new();
        static MMDeviceNotificationClient? _notifier;

        static readonly SessionEventHandler _sessionHandler = new();

        static readonly Dictionary<uint, AudioSessionControl> _sessionCache = new();

        class SessionEventHandler : IAudioSessionEventsHandler
        {
            public void OnVolumeChanged(float volume, bool isMuted) => BroadcastSessions();
            public void OnDisplayNameChanged(string displayName) => BroadcastSessions();
            public void OnIconPathChanged(string iconPath) { }
            public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
            public void OnGroupingParamChanged(ref Guid groupingId) { }
            public void OnStateChanged(AudioSessionState state)
            {
                if (state == AudioSessionState.AudioSessionStateExpired)
                    CleanupExpiredSessions();
                BroadcastSessions();
            }
            public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
            {
                CleanupExpiredSessions();
                BroadcastSessions();
            }
        }

        static readonly AudioEndpointVolumeNotificationDelegate _volHandler = data =>
        {
            int vol = (int)Math.Round(data.MasterVolume * 100);
            var msg = JsonSerializer.Serialize(new { type = "volume", value = vol, muted = data.Muted });
            Server.Broadcast(msg);
            Logger.Log("AUDIO", $"system → {vol}%{(data.Muted ? " (muted)" : "")}", ConsoleColor.DarkYellow);
        };

        public static void Init()
        {
            try
            {
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (_device != null)
                {
                    _device.AudioEndpointVolume.OnVolumeNotification += _volHandler;
                }
            }
            catch { }

            var method = typeof(MMDeviceEnumerator).GetMethod("CreateNotificationClient", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (method != null)
            {
                _notifier = method.Invoke(_enumerator, new object[] { false }) as MMDeviceNotificationClient;
                if (_notifier != null)
                {
                    _notifier.DefaultDeviceChanged += (s, e) =>
                    {
                        if (e.Flow == DataFlow.Render && e.Role == Role.Multimedia)
                        {
                            ReacquireDefaultDevice();
                        }
                    };
                    _notifier.DeviceAdded += (s, e) => BroadcastDevices();
                    _notifier.DeviceRemoved += (s, e) => BroadcastDevices();
                    _notifier.DeviceStateChanged += (s, e) => BroadcastDevices();
                }
            }

            AttachSessionListeners();
        }

        static void AttachSessionListeners()
        {
            lock (_lock)
            {
                if (_device == null) return;
                try
                {
                    var mgr = _device.AudioSessionManager;
                    mgr.OnSessionCreated += (s, e) =>
                    {
                        try
                        {
                            RegisterAllSessions(mgr);
                            BroadcastSessions();
                        }
                        catch { }
                    };
                    RegisterAllSessions(mgr);
                }
                catch (Exception ex)
                {
                    Logger.Log("AUDIO", $"AttachSessionListeners failed: {ex.Message}", ConsoleColor.Red);
                }
            }
        }

        static void RegisterAllSessions(AudioSessionManager mgr)
        {
            mgr.RefreshSessions();
            var sessions = mgr.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                if (s == null) continue;
                try
                {
                    uint pid = s.GetProcessID;
                    if (!_sessionCache.ContainsKey(pid))
                    {
                        _sessionCache[pid] = s;
                        s.RegisterEventClient(_sessionHandler);
                    }
                }
                catch { }
            }
        }

        static void CleanupExpiredSessions()
        {
            lock (_lock)
            {
                var toRemove = new List<uint>();
                foreach (var kv in _sessionCache)
                {
                    try
                    {
                        if (kv.Value.State == AudioSessionState.AudioSessionStateExpired)
                        {
                            kv.Value.UnRegisterEventClient(_sessionHandler);
                            kv.Value.Dispose();
                            toRemove.Add(kv.Key);
                        }
                    }
                    catch
                    {
                        toRemove.Add(kv.Key);
                    }
                }
                foreach (var id in toRemove) _sessionCache.Remove(id);
            }
        }

        static void ClearSessionCache()
        {
            lock (_lock)
            {
                foreach (var kv in _sessionCache)
                {
                    try
                    {
                        kv.Value.UnRegisterEventClient(_sessionHandler);
                        kv.Value.Dispose();
                    }
                    catch { }
                }
                _sessionCache.Clear();
            }
        }

        static void ReacquireDefaultDevice()
        {
            lock (_lock)
            {
                ClearSessionCache();

                try { if (_device != null) _device.AudioEndpointVolume.OnVolumeNotification -= _volHandler; } catch { }

                try { _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
                catch { _device = null; }

                try { if (_device != null) _device.AudioEndpointVolume.OnVolumeNotification += _volHandler; } catch { }

                AttachSessionListeners();
            }

            BroadcastVolume();
            BroadcastSessions();
            BroadcastDevices();
        }

        public static (int volume, bool muted) GetVolumeState()
        {
            lock (_lock)
            {
                if (_device == null) return (50, false);
                try
                {
                    int vol = (int)Math.Round(_device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
                    bool mute = _device.AudioEndpointVolume.Mute;
                    return (vol, mute);
                }
                catch
                {
                    return (50, false);
                }
            }
        }

        public static (int volume, bool muted) BroadcastVolume()
        {
            var state = GetVolumeState();
            var msg = JsonSerializer.Serialize(new { type = "volume", value = state.volume, muted = state.muted });
            Server.Broadcast(msg);
            return state;
        }

        public static void SetVolume(int percent)
        {
            lock (_lock)
            {
                if (_device == null) return;
                try
                {
                    percent = Math.Max(0, Math.Min(100, percent));
                    _device.AudioEndpointVolume.MasterVolumeLevelScalar = percent / 100.0f;
                    if (percent == 0) _device.AudioEndpointVolume.Mute = true;
                    else if (_device.AudioEndpointVolume.Mute) _device.AudioEndpointVolume.Mute = false;
                }
                catch (Exception ex) { Logger.Log("AUDIO", $"SetVolume failed: {ex.Message}", ConsoleColor.Red); }
            }
        }

        public static void VolumeChange(int delta)
        {
            lock (_lock)
            {
                if (_device == null) return;
                try
                {
                    int current = (int)Math.Round(_device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
                    SetVolume(current + delta);
                }
                catch (Exception ex) { Logger.Log("AUDIO", $"VolumeChange failed: {ex.Message}", ConsoleColor.Red); }
            }
        }

        public static void ToggleMute()
        {
            lock (_lock)
            {
                if (_device == null) return;
                try
                {
                    _device.AudioEndpointVolume.Mute = !_device.AudioEndpointVolume.Mute;
                }
                catch (Exception ex) { Logger.Log("AUDIO", $"ToggleMute failed: {ex.Message}", ConsoleColor.Red); }
            }
        }

        static string? GetSessionName(AudioSessionControl s, uint pid)
        {
            if (pid == 0) return "System Sounds";
            string? name = null;
            try
            {
                var dn = s.DisplayName;
                if (!string.IsNullOrWhiteSpace(dn) && !dn.StartsWith("@")) name = dn;
            }
            catch { }

            if (string.IsNullOrEmpty(name))
            {
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    name = p.ProcessName;
                }
                catch { }
            }

            if (string.IsNullOrEmpty(name)) name = $"PID {pid}";

            string lower = name.ToLower();
            if (lower == "rtkuwp" || lower == "searchhost" || lower == "startmenuexperiencehost" || lower == "shellexperiencehost")
                return null;

            return name;
        }

        public static string BuildSessionsJson()
        {
            var list = new List<object>();
            lock (_lock)
            {
                CleanupExpiredSessions();

                foreach (var kv in _sessionCache)
                {
                    try
                    {
                        var s = kv.Value;
                        if (s.State == AudioSessionState.AudioSessionStateExpired) continue;

                        string? sName = GetSessionName(s, kv.Key);
                        if (string.IsNullOrEmpty(sName)) continue;

                        int vol = (int)Math.Round(s.SimpleAudioVolume.Volume * 100);
                        bool mute = s.SimpleAudioVolume.Mute;
                        list.Add(new { id = kv.Key, name = sName, volume = vol, muted = mute });
                    }
                    catch { }
                }
            }
            return JsonSerializer.Serialize(new { type = "sessions", sessions = list });
        }

        public static void BroadcastSessions() => Server.Broadcast(BuildSessionsJson());
        public static void SendSessions(IWebSocketConnection socket) { try { socket.Send(BuildSessionsJson()); } catch { } }

        public static void SetSessionVolume(uint pid, int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            lock (_lock)
            {
                if (_sessionCache.TryGetValue(pid, out var s))
                {
                    try
                    {
                        s.SimpleAudioVolume.Volume = percent / 100f;
                        if (percent == 0) s.SimpleAudioVolume.Mute = true;
                        else if (s.SimpleAudioVolume.Mute) s.SimpleAudioVolume.Mute = false;
                    }
                    catch (Exception ex) { Logger.Log("APPVOL", $"SetSessionVolume failed: {ex.Message}", ConsoleColor.Red); }
                }
            }
        }

        public static bool ToggleSessionMute(uint pid)
        {
            lock (_lock)
            {
                if (_sessionCache.TryGetValue(pid, out var s))
                {
                    try
                    {
                        s.SimpleAudioVolume.Mute = !s.SimpleAudioVolume.Mute;
                        return s.SimpleAudioVolume.Mute;
                    }
                    catch (Exception ex) { Logger.Log("APPVOL", $"ToggleSessionMute failed: {ex.Message}", ConsoleColor.Red); }
                }
            }
            return false;
        }

        public static string BuildDevicesJson()
        {
            var list = new List<object>();
            try
            {
                string defId = "";
                try { using var def = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); defId = def.ID; } catch { }

                var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                for (int i = 0; i < devices.Count; i++)
                {
                    try
                    {
                        using var d = devices[i];
                        list.Add(new { id = d.ID, name = d.FriendlyName, isDefault = d.ID == defId });
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Logger.Log("DEVICE", $"enumerate failed: {ex.Message}", ConsoleColor.Red); }
            return JsonSerializer.Serialize(new { type = "devices", devices = list });
        }

        public static void BroadcastDevices() => Server.Broadcast(BuildDevicesJson());
        public static void SendDevices(IWebSocketConnection socket) { try { socket.Send(BuildDevicesJson()); } catch { } }

        public static async void SetDefaultDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            try
            {
                var cfg = (Interop.IPolicyConfig)new Interop.CPolicyConfigClient();
                cfg.SetDefaultEndpoint(deviceId, Interop.ERole.eConsole);
                cfg.SetDefaultEndpoint(deviceId, Interop.ERole.eMultimedia);
                cfg.SetDefaultEndpoint(deviceId, Interop.ERole.eCommunications);
                Marshal.ReleaseComObject(cfg);
            }
            catch (Exception ex)
            {
                Logger.Log("DEVICE", $"SetDefaultDevice failed: {ex.Message}", ConsoleColor.Red);
                return;
            }

            await Task.Delay(100);
            ReacquireDefaultDevice();
        }

        public static void SendInitialState(IWebSocketConnection socket)
        {
            var state = GetVolumeState();
            try { socket.Send(JsonSerializer.Serialize(new { type = "volume", value = state.volume, muted = state.muted })); } catch { }
            SendSessions(socket);
            SendDevices(socket);
        }

        public static void Cleanup()
        {
            ClearSessionCache();
            try
            {
                if (_device != null)
                {
                    _device.AudioEndpointVolume.OnVolumeNotification -= _volHandler;
                    _device.Dispose();
                    _device = null;
                }
            } catch { }
            try
            {
                _enumerator?.Dispose();
            } catch { }
        }
    }
}
