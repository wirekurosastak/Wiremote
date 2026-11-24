// Message models for the Wiremote server protocol.
//
// The server (C#, repo root) speaks JSON over a WebSocket on [wsPort]:
// the client sends `{"command": ...}` objects, the server pushes
// `{"type": ...}` objects. Album art is served over HTTP on [httpPort].

const int httpPort = 8765;
const int wsPort = 8766;

int _int(Object? v, [int fallback = 0]) => v is num ? v.toInt() : fallback;
bool _bool(Object? v) => v == true;
String _str(Object? v) => v == null ? '' : v.toString();

/// A PC address plus an optional auth token, as typed or pasted by the user.
class ServerAddress {
  const ServerAddress(this.host, [this.token]);

  final String host;
  final String? token;

  /// Accepts a bare host (`192.168.1.50`, `mypc.local`), a host with port, or
  /// the full link the server prints (`http://<ip>:8765/?token=<TOKEN>`).
  /// Ports are ignored: the server's ports are fixed.
  static ServerAddress? parse(String raw) {
    var s = raw.trim();
    if (s.isEmpty || s.contains(RegExp(r'\s'))) return null;
    if (!s.contains('://')) s = 'http://$s';
    final uri = Uri.tryParse(s);
    if (uri == null || uri.host.isEmpty) return null;
    final token = uri.queryParameters['token']?.trim();
    return ServerAddress(
      uri.host,
      token == null || token.isEmpty ? null : token,
    );
  }
}

Uri webSocketUri(String host, {int port = wsPort}) =>
    Uri(scheme: 'ws', host: host, port: port);

Uri httpUri(String host, String path, {int port = httpPort}) =>
    Uri(scheme: 'http', host: host, port: port).resolve(path);

class VolumeState {
  const VolumeState(this.value, this.muted);
  factory VolumeState.fromJson(Map<String, Object?> j) =>
      VolumeState(_int(j['value']).clamp(0, 100), _bool(j['muted']));

  final int value;
  final bool muted;
}

class AudioDevice {
  const AudioDevice(this.id, this.name, this.isDefault);
  factory AudioDevice.fromJson(Map<String, Object?> j) =>
      AudioDevice(_str(j['id']), _str(j['name']), _bool(j['isDefault']));

  final String id;
  final String name;
  final bool isDefault;
}

/// One app in the volume mixer. [id] is the app's process id.
class AudioSession {
  const AudioSession(this.id, this.name, this.volume, this.muted);
  factory AudioSession.fromJson(Map<String, Object?> j) => AudioSession(
    _int(j['id']),
    _str(j['name']),
    _int(j['volume']).clamp(0, 100),
    _bool(j['muted']),
  );

  final int id;
  final String name;
  final int volume;
  final bool muted;
}

class DisplayInfo {
  const DisplayInfo(this.id, this.name, this.isActive, this.isPrimary);
  factory DisplayInfo.fromJson(Map<String, Object?> j) => DisplayInfo(
    _str(j['id']),
    _str(j['name']),
    _bool(j['isActive']),
    _bool(j['isPrimary']),
  );

  final String id;
  final String name;
  final bool isActive;
  final bool isPrimary;

  String get label => name.isEmpty ? id : name;
}

class DisplaysState {
  const DisplaysState(this.displays, this.mode);
  factory DisplaysState.fromJson(Map<String, Object?> j) => DisplaysState(
    _list(j['displays']).map(DisplayInfo.fromJson).toList(),
    j['mode'] is String ? j['mode'] as String : null,
  );

  final List<DisplayInfo> displays;

  /// `extend` or `clone`, when the server can tell.
  final String? mode;

  List<DisplayInfo> get active => displays.where((d) => d.isActive).toList();
}

class BrightnessState {
  const BrightnessState(this.supported, this.value);
  factory BrightnessState.fromJson(Map<String, Object?> j) => BrightnessState(
    _bool(j['supported']),
    _int(j['value'], 100).clamp(0, 100),
  );

  final bool supported;
  final int value;
}

class NowPlaying {
  const NowPlaying({
    required this.active,
    this.title = '',
    this.artist = '',
    this.isPlaying = false,
    this.thumb,
  });
  factory NowPlaying.fromJson(Map<String, Object?> j) => _bool(j['playing'])
      ? NowPlaying(
          active: true,
          title: _str(j['title']),
          artist: _str(j['artist']),
          isPlaying: j['status'] == 'playing',
          thumb: j['thumb'] is String && (j['thumb'] as String).isNotEmpty
              ? j['thumb'] as String
              : null,
        )
      : none;

  static const none = NowPlaying(active: false);

  /// Something is loaded in a player (playing or paused).
  final bool active;
  final String title;
  final String artist;
  final bool isPlaying;

  /// Server-relative art path, e.g. `/thumbnail?ts=123`.
  final String? thumb;
}

/// A pending delayed power action.
class PowerTimer {
  const PowerTimer(this.action, this.deadline);

  /// Returns null when no timer runs (the server sends `remaining: -1`).
  static PowerTimer? fromJson(Map<String, Object?> j, DateTime now) {
    final remaining = _int(j['remaining'], -1);
    if (remaining < 0) return null;
    return PowerTimer(_str(j['action']), now.add(Duration(seconds: remaining)));
  }

  final String action;
  final DateTime deadline;
}

Iterable<Map<String, Object?>> _list(Object? v) => v is List
    ? v.whereType<Map>().map((m) => m.cast<String, Object?>())
    : const [];

List<AudioDevice> parseDevices(Map<String, Object?> j) =>
    _list(j['devices']).map(AudioDevice.fromJson).toList();

List<AudioSession> parseSessions(Map<String, Object?> j) =>
    _list(j['sessions']).map(AudioSession.fromJson).toList();

/// Human label for a power action id.
String powerActionLabel(String action) => switch (action) {
  'shutdown' => 'Shutdown',
  'restart' => 'Restart',
  'sleep' => 'Sleep',
  'hibernate' => 'Hibernate',
  'restart_bios' => 'Restart to BIOS',
  _ => action.isEmpty ? action : action[0].toUpperCase() + action.substring(1),
};
