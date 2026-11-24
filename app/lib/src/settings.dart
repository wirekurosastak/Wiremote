import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';

typedef _Persist = Future<void> Function(String key, Object? value);

enum BorderVisibility { off, animated, full }

enum BorderSpeed { slow, normal, fast }

/// User preferences, persisted on the device.
class Settings extends ChangeNotifier {
  Settings._(this._values, this._persist);

  /// In-memory settings, for tests.
  factory Settings.memory([Map<String, Object?> initial = const {}]) =>
      Settings._({...initial}, (_, _) async {});

  static Future<Settings> load() async {
    final prefs = await SharedPreferencesWithCache.create(
      cacheOptions: const SharedPreferencesWithCacheOptions(),
    );
    final values = {for (final k in prefs.keys) k: prefs.get(k)};
    return Settings._(values, (key, value) async {
      switch (value) {
        case null:
          await prefs.remove(key);
        case final String v:
          await prefs.setString(key, v);
        case final bool v:
          await prefs.setBool(key, v);
        case final int v:
          await prefs.setInt(key, v);
        case final double v:
          await prefs.setDouble(key, v);
      }
    });
  }

  /// The brand accent, #b0914f, as HSL.
  static const defaultHue = 41;
  static const defaultSat = 38;
  static const defaultLight = 50;

  final Map<String, Object?> _values;
  final _Persist _persist;

  T? _get<T>(String key) {
    final v = _values[key];
    return v is T ? v : null;
  }

  void _set(String key, Object? value) {
    if (_values[key] == value) return;
    if (value == null) {
      _values.remove(key);
    } else {
      _values[key] = value;
    }
    _persist(key, value);
    notifyListeners();
  }

  // Connection
  String? get host => _get<String>('host');
  String? get token => _get<String>('token');

  void setServer(String host, String? token) {
    _values['host'] = host;
    _persist('host', host);
    _values['token'] = token;
    _persist('token', token);
    notifyListeners();
  }

  set token(String? v) => _set('token', v);

  // Appearance
  bool get lightTheme => _get<bool>('lightTheme') ?? false;
  set lightTheme(bool v) => _set('lightTheme', v);

  BorderVisibility get borderVisibility =>
      BorderVisibility.values.asNameMap()[_get<String>('borderVisibility')] ??
      BorderVisibility.animated;
  set borderVisibility(BorderVisibility v) => _set('borderVisibility', v.name);

  BorderSpeed get borderSpeed =>
      BorderSpeed.values.asNameMap()[_get<String>('borderSpeed')] ??
      BorderSpeed.slow;
  set borderSpeed(BorderSpeed v) => _set('borderSpeed', v.name);

  bool get rainbow => _get<bool>('rainbow') ?? false;
  set rainbow(bool v) => _set('rainbow', v);

  int get hue => _get<int>('hue') ?? defaultHue;
  set hue(int v) => _set('hue', v);

  int get saturation => _get<int>('saturation') ?? defaultSat;
  set saturation(int v) => _set('saturation', v);

  int get lightness => _get<int>('lightness') ?? defaultLight;
  set lightness(int v) => _set('lightness', v);

  void resetAccent() {
    for (final k in const ['hue', 'saturation', 'lightness']) {
      _values.remove(k);
      _persist(k, null);
    }
    notifyListeners();
  }

  // Behaviour
  bool get keepScreenAwake => _get<bool>('keepScreenAwake') ?? true;
  set keepScreenAwake(bool v) => _set('keepScreenAwake', v);

  double get sensitivity => _get<double>('sensitivity') ?? 5.0;
  set sensitivity(double v) => _set('sensitivity', v);
}
