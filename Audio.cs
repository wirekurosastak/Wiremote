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
        class SessionInfo
        {
            public AudioSessionControl Control { get; }
            public string Name { get; }
            public SessionInfo(AudioSessionControl control, string name) { Control = control; Name = name; }
        }
        static readonly Dictionary<uint, SessionInfo> _sessionCache = new();

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
            RegisterCommands();
            try
            {
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (_device != null)
                {
                    _device.AudioEndpointVolume.OnVolumeNotification += _volHandler;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("AUDIO", $"Default device init failed: {ex.Message}", ConsoleColor.DarkGray);
            }

            try
            {
                var method = typeof(MMDeviceEnumerator).GetMethod("CreateNotificationClient", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (method != null)
                {
                    _notifier = method.Invoke(_enumerator, new object[] { false }) as MMDeviceNotificationClient;
                    if (_notifier != null)
                    {
                        _notifier.DefaultDeviceChanged += (s, e) =>
                        {
                            if (e.Flow == DataFlow.Render && e.Role == Role.Multimedia)
                                ReacquireDefaultDevice();
                        };
                        _notifier.DeviceAdded += (s, e) => BroadcastDevices();
                        _notifier.DeviceRemoved += (s, e) => BroadcastDevices();
                        _notifier.DeviceStateChanged += (s, e) => BroadcastDevices();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("AUDIO", $"Notifier init failed: {ex.Message}", ConsoleColor.DarkGray);
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
                        catch (Exception ex)
                        {
                            Logger.Log("AUDIO", $"Session creation error: {ex.Message}", ConsoleColor.DarkGray);
                        }
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
                        string? name = GetSessionName(s, pid);
                        if (!string.IsNullOrEmpty(name))
                        {
                            _sessionCache[pid] = new SessionInfo(s, name);
                            s.RegisterEventClient(_sessionHandler);
                        }
                        else
                        {
                            s.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("AUDIO", $"Register session failed: {ex.Message}", ConsoleColor.DarkGray);
                }
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
                        if (kv.Value.Control.State == AudioSessionState.AudioSessionStateExpired)
                        {
                            kv.Value.Control.UnRegisterEventClient(_sessionHandler);
                            kv.Value.Control.Dispose();
                            toRemove.Add(kv.Key);
                        }
                    }
                    catch
                    {
                        toRemove.Add(kv.Key); // If there is an error with the COM object, discard it.
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
                        kv.Value.Control.UnRegisterEventClient(_sessionHandler);
                        kv.Value.Control.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("AUDIO", $"Session cache clear error: {ex.Message}", ConsoleColor.DarkGray);
                    }
                }
                _sessionCache.Clear();
            }
        }

        static void ReacquireDefaultDevice()
        {
            lock (_lock)
            {
                ClearSessionCache();

                if (_device != null)
                {
                    try { _device.AudioEndpointVolume.OnVolumeNotification -= _volHandler; }
                    catch { } // Suppressing exceptions is acceptable here because the device is destroyed
                }

                try 
                { 
                    _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); 
                }
                catch 
                { 
                    _device = null; 
                }

                if (_device != null)
                {
                    try { _device.AudioEndpointVolume.OnVolumeNotification += _volHandler; }
                    catch (Exception ex) { Logger.Log("AUDIO", $"Vol handler reattach error: {ex.Message}", ConsoleColor.DarkGray); }
                }

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
                catch (Exception ex)
                {
                    Logger.Log("AUDIO", $"GetVolumeState error: {ex.Message}", ConsoleColor.DarkGray);
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
            catch (Exception ex) 
            { 
                Logger.Log("AUDIO", $"GetSessionName display name error: {ex.Message}", ConsoleColor.DarkGray); 
            }

            if (string.IsNullOrEmpty(name))
            {
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    name = p.ProcessName;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Access Denied (e.g. elevated processes), ignore to prevent spam
                }
                catch (Exception ex)
                {
                    // Process is already dead or other error
                    Logger.Log("AUDIO", $"GetSessionName process name error: {ex.Message}", ConsoleColor.DarkGray);
                }
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
                        var info = kv.Value;
                        var s = info.Control;
                        if (s.State == AudioSessionState.AudioSessionStateExpired) continue;

                        int vol = (int)Math.Round(s.SimpleAudioVolume.Volume * 100);
                        bool mute = s.SimpleAudioVolume.Mute;
                        list.Add(new { id = kv.Key, name = info.Name, volume = vol, muted = mute });
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("AUDIO", $"Build session JSON error: {ex.Message}", ConsoleColor.DarkGray);
                    }
                }
            }
            return JsonSerializer.Serialize(new { type = "sessions", sessions = list });
        }

        public static void BroadcastSessions() => Server.Broadcast(BuildSessionsJson());
        public static void SendSessions(IWebSocketConnection socket) 
        { 
            try { socket.Send(BuildSessionsJson()); } 
            catch (Exception ex) { Logger.Log("WS", $"SendSessions failed: {ex.Message}", ConsoleColor.DarkGray); } 
        }

        public static void SetSessionVolume(uint pid, int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            lock (_lock)
            {
                if (_sessionCache.TryGetValue(pid, out var info))
                {
                    try
                    {
                        var s = info.Control;
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
                if (_sessionCache.TryGetValue(pid, out var info))
                {
                    try
                    {
                        var s = info.Control;
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
                try 
                { 
                    using var def = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); 
                    defId = def.ID; 
                } 
                catch (Exception ex) { Logger.Log("DEVICE", $"DefDevice get failed: {ex.Message}", ConsoleColor.DarkGray); }

                var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                for (int i = 0; i < devices.Count; i++)
                {
                    try
                    {
                        using var d = devices[i];
                        list.Add(new { id = d.ID, name = d.FriendlyName, isDefault = d.ID == defId });
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("DEVICE", $"Enumerate device iter failed: {ex.Message}", ConsoleColor.DarkGray);
                    }
                }
            }
            catch (Exception ex) { Logger.Log("DEVICE", $"enumerate failed: {ex.Message}", ConsoleColor.Red); }
            return JsonSerializer.Serialize(new { type = "devices", devices = list });
        }

        public static void BroadcastDevices() => Server.Broadcast(BuildDevicesJson());
        public static void SendDevices(IWebSocketConnection socket) 
        { 
            try { socket.Send(BuildDevicesJson()); } 
            catch (Exception ex) { Logger.Log("WS", $"SendDevices failed: {ex.Message}", ConsoleColor.DarkGray); } 
        }

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
            try { socket.Send(JsonSerializer.Serialize(new { type = "volume", value = state.volume, muted = state.muted })); } 
            catch { }
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
            } 
            catch (Exception ex) { Logger.Log("AUDIO", $"Device cleanup error: {ex.Message}", ConsoleColor.DarkGray); }

            try
            {
                _enumerator?.Dispose();
            } 
            catch (Exception ex) { Logger.Log("AUDIO", $"Enumerator cleanup error: {ex.Message}", ConsoleColor.DarkGray); }
        }

        private static void RegisterCommands()
        {
            Server.RegisterCommand("volume_up", (s, r) => { VolumeChange(5); var vs = BroadcastVolume(); Logger.Log("VOLUME", $"up  → {vs.volume}%", ConsoleColor.Yellow); return Task.CompletedTask; });
            Server.RegisterCommand("volume_down", (s, r) => { VolumeChange(-5); var vs = BroadcastVolume(); Logger.Log("VOLUME", $"down → {vs.volume}%", ConsoleColor.Yellow); return Task.CompletedTask; });
            Server.RegisterCommand("set_volume", (s, r) => { SetVolume(r.GetProperty("value").GetInt32()); var vs = BroadcastVolume(); Logger.Log("VOLUME", $"set  → {vs.volume}%", ConsoleColor.Yellow); return Task.CompletedTask; });
            Server.RegisterCommand("mute", (s, r) => { ToggleMute(); var vs = BroadcastVolume(); Logger.Log("VOLUME", vs.muted ? "muted" : "unmuted", ConsoleColor.Yellow); return Task.CompletedTask; });
            Server.RegisterCommand("get_sessions", (s, r) => { SendSessions(s); return Task.CompletedTask; });
            Server.RegisterCommand("set_session_volume", (s, r) => { var id = (uint)r.GetProperty("id").GetInt64(); int val = r.GetProperty("value").GetInt32(); SetSessionVolume(id, val); Logger.Log("APPVOL", $"pid {id} → {val}%", ConsoleColor.Yellow); return Task.CompletedTask; });
            Server.RegisterCommand("session_mute", (s, r) => { var id = (uint)r.GetProperty("id").GetInt64(); bool m = ToggleSessionMute(id); BroadcastSessions(); Logger.Log("APPVOL", $"pid {id} {(m ? "muted" : "unmuted")}", ConsoleColor.Yellow); return Task.CompletedTask; });
            Server.RegisterCommand("get_devices", (s, r) => { SendDevices(s); return Task.CompletedTask; });
            Server.RegisterCommand("set_device", (s, r) => { var id = r.GetProperty("id").GetString() ?? ""; SetDefaultDevice(id); Logger.Log("DEVICE", "switched output", ConsoleColor.Cyan); return Task.CompletedTask; });
        }
    }
}