import 'package:flutter/material.dart';
import 'package:wakelock_plus/wakelock_plus.dart';

import 'src/client.dart';
import 'src/screens.dart';
import 'src/settings.dart';
import 'src/theme.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  final settings = await Settings.load();
  runApp(WiremoteApp(settings: settings));
}

class WiremoteApp extends StatefulWidget {
  const WiremoteApp({
    super.key,
    required this.settings,
    this.client,
    this.home,
  });

  final Settings settings;

  /// Test hooks.
  final WiremoteClient? client;
  final Widget? home;

  @override
  State<WiremoteApp> createState() => _WiremoteAppState();
}

class _WiremoteAppState extends State<WiremoteApp> {
  Settings get _settings => widget.settings;
  late final WiremoteClient _client =
      widget.client ?? WiremoteClient(_settings);
  late final Visuals _visuals = Visuals(_settings);
  late final AppLifecycleListener _lifecycle;
  late bool _light = _settings.lightTheme;
  late bool _hasHost = _settings.host != null;
  bool? _keepAwake;

  @override
  void initState() {
    super.initState();
    // Like the web page: connect while visible, drop the socket when hidden.
    _lifecycle = AppLifecycleListener(
      onShow: _client.start,
      onHide: _client.stop,
    );
    _settings.addListener(_onSettings);
    _client.start();
    _syncWakelock();
  }

  @override
  void dispose() {
    _lifecycle.dispose();
    _settings.removeListener(_onSettings);
    _client.dispose();
    _visuals.dispose();
    super.dispose();
  }

  void _onSettings() {
    _syncWakelock();
    final light = _settings.lightTheme;
    final hasHost = _settings.host != null;
    if (light != _light || hasHost != _hasHost) {
      setState(() {
        _light = light;
        _hasHost = hasHost;
      });
    }
  }

  void _syncWakelock() {
    final on = _settings.keepScreenAwake;
    if (on == _keepAwake) return;
    _keepAwake = on;
    // Keeps the screen on only while the app is in front.
    WakelockPlus.toggle(enable: on).catchError((Object _) {});
  }

  @override
  Widget build(BuildContext context) {
    final palette = _light ? Palette.lightPalette : Palette.dark;
    return AppScope(
      settings: _settings,
      client: _client,
      visuals: _visuals,
      palette: palette,
      child: AccentScope(
        visuals: _visuals,
        child: MaterialApp(
          title: 'Wiremote',
          debugShowCheckedModeBanner: false,
          theme: buildTheme(palette),
          home:
              widget.home ??
              (_hasHost ? const HomeScreen() : const ConnectScreen()),
        ),
      ),
    );
  }
}
