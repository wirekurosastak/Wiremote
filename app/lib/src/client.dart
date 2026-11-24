import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math' as math;

import 'package:flutter/foundation.dart';

import 'protocol.dart';
import 'settings.dart';

enum ConnectionStatus { offline, connecting, online }

/// Commands that carry a live value. They are never queued while offline:
/// on reconnect the phone must adopt the PC's real state, not replay a stale one.
const _neverQueued = {
  'set_volume',
  'set_brightness',
  'set_session_volume',
  'mouse_move',
  'mouse_scroll',
};

class _Queued {
  _Queued(this.payload) : at = DateTime.now();
  final Map<String, Object?> payload;
  final DateTime at;
}

/// One WebSocket connection to the Wiremote server, plus the PC state it reports.
class WiremoteClient {
  WiremoteClient(this.settings, {this.wsPortOverride, this.httpPortOverride});

  final Settings settings;

  /// Test hooks; the real server uses fixed ports.
  final int? wsPortOverride;
  final int? httpPortOverride;

  final status = ValueNotifier(ConnectionStatus.offline);

  /// Set when the server rejects our token; the UI asks for a new one.
  final authError = ValueNotifier<String?>(null);

  final volume = ValueNotifier<VolumeState?>(null);
  final devices = ValueNotifier<List<AudioDevice>?>(null);
  final sessions = ValueNotifier<List<AudioSession>>(const []);
  final displays = ValueNotifier<DisplaysState?>(null);
  final brightness = ValueNotifier<BrightnessState?>(null);
  final nowPlaying = ValueNotifier<NowPlaying>(NowPlaying.none);
  final timer = ValueNotifier<PowerTimer?>(null);

  WebSocket? _socket;
  bool _connecting = false;
  bool _authenticated = false;
  bool _active = false;
  int _generation = 0;
  int _failures = 0;
  Timer? _reconnectTimer;
  Timer? _pingTimer;
  Timer? _pongWatchdog;
  final List<_Queued> _queue = [];

  /// Base URL for server-relative paths such as album art.
  Uri? resolve(String path) {
    final host = settings.host;
    return host == null
        ? null
        : httpUri(host, path, port: httpPortOverride ?? httpPort);
  }

  /// Start (or keep) connecting. Called when the app comes to the foreground.
  void start() {
    _active = true;
    _connect();
  }

  /// Drop the connection and stop reconnecting. Called when the app is hidden.
  void stop() {
    _active = false;
    _reconnectTimer?.cancel();
    _teardown();
  }

  /// Reconnect now, e.g. after the host or token changed.
  void reconnect() {
    _reconnectTimer?.cancel();
    _teardown();
    _failures = 0;
    authError.value = null;
    if (_active) _connect();
  }

  void dispose() {
    stop();
  }

  /// Send a command. Momentary button presses made while offline are kept for
  /// two seconds and sent once the connection is back.
  void command(String name, [Map<String, Object?> args = const {}]) {
    final payload = <String, Object?>{'command': name, ...args};
    if (_socket != null && _sendRaw(payload)) return;
    if (!_neverQueued.contains(name)) {
      _queue.add(_Queued(payload));
      if (_queue.length > 5) _queue.removeAt(0);
    }
    if (_active && _socket == null && !_connecting) {
      _reconnectTimer?.cancel();
      _connect();
    }
  }

  bool _sendRaw(Map<String, Object?> payload) {
    try {
      _socket!.add(jsonEncode(payload));
      return true;
    } catch (_) {
      return false;
    }
  }

  Future<void> _connect() async {
    _reconnectTimer?.cancel();
    final host = settings.host;
    if (!_active || host == null || _socket != null || _connecting) return;

    _connecting = true;
    final gen = ++_generation;
    status.value = ConnectionStatus.connecting;
    final client = HttpClient()..connectionTimeout = const Duration(seconds: 4);
    try {
      final ws = await WebSocket.connect(
        webSocketUri(host, port: wsPortOverride ?? wsPort).toString(),
        customClient: client,
      );
      if (gen != _generation || !_active) {
        ws.close().ignore();
        return;
      }
      _connecting = false;
      _socket = ws;
      ws.listen(
        (data) => _onMessage(gen, data),
        onDone: () => _onClosed(gen),
        onError: (Object _) => _onClosed(gen),
        cancelOnError: true,
      );
      _sendRaw({'command': 'auth', 'token': settings.token ?? ''});
    } catch (_) {
      if (gen == _generation) {
        _connecting = false;
        status.value = ConnectionStatus.offline;
        _scheduleReconnect();
      }
    } finally {
      client.close();
    }
  }

  void _onClosed(int gen) {
    if (gen != _generation) return;
    _teardown();
    _scheduleReconnect();
  }

  /// Forget the current socket. Late events from it are ignored via [_generation].
  void _teardown() {
    _generation++;
    _connecting = false;
    _authenticated = false;
    _pingTimer?.cancel();
    _pongWatchdog?.cancel();
    final s = _socket;
    _socket = null;
    s?.close().ignore();
    status.value = ConnectionStatus.offline;
  }

  void _scheduleReconnect() {
    _reconnectTimer?.cancel();
    if (!_active) return;
    // 250 ms first (instant reconnect after a blip), backing off to 2 s
    // so an offline PC doesn't keep the radio busy.
    final ms = math.min(2000, 250 * (1 << math.min(_failures, 3)));
    _failures++;
    _reconnectTimer = Timer(Duration(milliseconds: ms), _connect);
  }

  void _startPing(int gen) {
    _pingTimer?.cancel();
    _pingTimer = Timer.periodic(const Duration(seconds: 10), (_) {
      if (gen != _generation || _socket == null) return;
      _sendRaw({'command': 'ping'});
      _pongWatchdog?.cancel();
      _pongWatchdog = Timer(const Duration(seconds: 5), () {
        // Half-open socket (Wi-Fi dropped, PC asleep): reconnect.
        if (gen == _generation) _onClosed(gen);
      });
    });
  }

  void _onMessage(int gen, Object? data) {
    if (gen != _generation || data is! String) return;
    final Map<String, Object?> msg;
    try {
      final decoded = jsonDecode(data);
      if (decoded is! Map) return;
      msg = decoded.cast<String, Object?>();
    } catch (_) {
      return;
    }

    switch (msg['type']) {
      case 'pong':
        _pongWatchdog?.cancel();
      case 'auth_ok':
        final token = msg['token'];
        if (token is String && token.isNotEmpty && token != settings.token) {
          settings.token = token;
        }
        _authenticated = true;
        _failures = 0;
        authError.value = null;
        status.value = ConnectionStatus.online;
        _startPing(gen);
        _flushQueue();
      case 'auth_error' || 'auth_required':
        _authenticated = false;
        status.value = ConnectionStatus.offline;
        authError.value = msg['message'] is String
            ? msg['message'] as String
            : 'Invalid or missing token';
      case 'volume':
        volume.value = VolumeState.fromJson(msg);
      case 'devices':
        devices.value = parseDevices(msg);
      case 'sessions':
        sessions.value = parseSessions(msg);
      case 'displays':
        displays.value = DisplaysState.fromJson(msg);
      case 'brightness':
        brightness.value = BrightnessState.fromJson(msg);
      case 'nowplaying':
        nowPlaying.value = NowPlaying.fromJson(msg);
      case 'timer':
        timer.value = PowerTimer.fromJson(msg, DateTime.now());
    }
  }

  void _flushQueue() {
    if (_socket == null || !_authenticated) return;
    final now = DateTime.now();
    final recent = _queue
        .where((q) => now.difference(q.at) < const Duration(seconds: 2))
        .toList();
    _queue.clear();
    for (final q in recent) {
      _sendRaw(q.payload);
    }
  }
}
