import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/scheduler.dart';

import 'client.dart';
import 'settings.dart';

const brandAccent = Color(0xFFB0914F);

/// Colors from the web UI's style.css, dark and light.
class Palette {
  const Palette({
    required this.light,
    required this.bg,
    required this.surface,
    required this.surface2,
    required this.surface3,
    required this.pressed,
    required this.text,
    required this.secondary,
    required this.success,
    required this.divider,
    required this.padBg,
    required this.padBorder,
    required this.padText,
    required this.padButton,
    required this.padButtonBorder,
    required this.padButtonText,
    required this.padButtonActive,
    required this.padButtonActiveText,
    required this.onAccent,
    required this.thumbBorder,
    required this.softBorder,
  });

  final bool light;
  final Color bg,
      surface,
      surface2,
      surface3,
      pressed,
      text,
      secondary,
      success;
  final Color divider;
  final Color padBg, padBorder, padText;
  final Color padButton,
      padButtonBorder,
      padButtonText,
      padButtonActive,
      padButtonActiveText;

  /// Text and icons drawn on an accent fill.
  final Color onAccent;
  final Color thumbBorder;
  final Color softBorder;

  static const danger = Color(0xFFD32F2F);
  static const subTitle = Color(0xFF555555);
  static const toggleOff = Color(0xFF333333);
  static const inputBorder = Color(0xFF333333);

  static const dark = Palette(
    light: false,
    bg: Color(0xFF000000),
    surface: Color(0xFF121212),
    surface2: Color(0xFF1E1E1E),
    surface3: Color(0xFF2A2A2A),
    pressed: Color(0xFF333333),
    text: Color(0xFFFFFFFF),
    secondary: Color(0xFF888888),
    success: Color(0xFF4CAF50),
    divider: Color(0xFF262626),
    padBg: Color(0xFF181818),
    padBorder: Color(0xFF333333),
    padText: Color(0xFF555555),
    padButton: Color(0x14FFFFFF),
    padButtonBorder: Color(0x1AFFFFFF),
    padButtonText: Color(0xFF888888),
    padButtonActive: Color(0x33FFFFFF),
    padButtonActiveText: Color(0xFFFFFFFF),
    onAccent: Color(0xFF000000),
    thumbBorder: Color(0xFFFFFFFF),
    softBorder: Color(0x26FFFFFF),
  );

  static const lightPalette = Palette(
    light: true,
    bg: Color(0xFFCBCFD8),
    surface: Color(0xFFFFFFFF),
    surface2: Color(0xFFE3E7EE),
    surface3: Color(0xFFD1D7E2),
    pressed: Color(0xFFB8C0CD),
    text: Color(0xFF111111),
    secondary: Color(0xFF555555),
    success: Color(0xFF388E3C),
    divider: Color(0xFFD1D7E2),
    padBg: Color(0xFFE3E7EE),
    padBorder: Color(0xFFD1D7E2),
    padText: Color(0xFF555555),
    padButton: Color(0xFFFFFFFF),
    padButtonBorder: Color(0xFFD1D7E2),
    padButtonText: Color(0xFF111111),
    padButtonActive: Color(0xFFB8C0CD),
    padButtonActiveText: Color(0xFF111111),
    onAccent: Color(0xFFFFFFFF),
    thumbBorder: Color(0xFF111111),
    softBorder: Color(0x40000000),
  );
}

ThemeData buildTheme(Palette p) {
  final scheme = ColorScheme.fromSeed(
    seedColor: brandAccent,
    brightness: p.light ? Brightness.light : Brightness.dark,
  ).copyWith(primary: brandAccent, surface: p.surface, onSurface: p.text);
  return ThemeData(
    colorScheme: scheme,
    scaffoldBackgroundColor: p.bg,
    canvasColor: p.surface3,
    splashFactory: NoSplash.splashFactory,
    highlightColor: Colors.transparent,
    textSelectionTheme: TextSelectionThemeData(
      cursorColor: brandAccent,
      selectionColor: brandAccent.withValues(alpha: 0.4),
      selectionHandleColor: brandAccent,
    ),
  );
}

class Pulse extends ChangeNotifier {
  void fire() => notifyListeners();
}

/// Accent color (custom HSL or cycling rainbow) and the border spotlight clock.
///
/// [accent] fires when the accent color changes (at most ~30 times a second
/// in rainbow mode). [frame] fires every animation frame and only drives
/// repaints of the card borders.
class Visuals {
  Visuals(this.settings) {
    _ticker = Ticker(_onTick);
    settings.addListener(_sync);
    _sync();
  }

  final Settings settings;
  late final Ticker _ticker;
  final accent = Pulse();
  final frame = Pulse();

  Color accentColor = brandAccent;
  double rainbowHue = 0;
  double borderPhase = 0;

  Duration _last = Duration.zero;
  Duration _lastAccent = Duration.zero;
  bool? _wasRainbow;

  bool get rainbow => settings.rainbow;

  static double _borderSeconds(BorderSpeed s) => switch (s) {
    BorderSpeed.slow => 12,
    BorderSpeed.normal => 6,
    BorderSpeed.fast => 3,
  };

  static double _rainbowDegPerSecond(BorderSpeed s) => switch (s) {
    BorderSpeed.slow => 6,
    BorderSpeed.normal => 15,
    BorderSpeed.fast => 36,
  };

  static Color hsl(num h, num s, num l) =>
      HSLColor.fromAHSL(1, h.toDouble() % 360, s / 100, l / 100).toColor();

  /// The rainbow gradient stops: six hues 60° apart, wrapping back to the first.
  List<Color> rainbowColors() => [
    for (var i = 0; i <= 6; i++) hsl(rainbowHue + 60 * (i % 6), 85, 60),
  ];

  void _sync() {
    final rainbow = settings.rainbow;
    if (!rainbow) {
      final c = hsl(settings.hue, settings.saturation, settings.lightness);
      if (c != accentColor || _wasRainbow != false) {
        accentColor = c;
        accent.fire();
      }
    } else if (_wasRainbow != true) {
      accentColor = hsl(rainbowHue, 85, 60);
      accent.fire();
    }
    _wasRainbow = rainbow;

    final needsTicker =
        rainbow || settings.borderVisibility == BorderVisibility.animated;
    if (needsTicker && !_ticker.isActive) {
      _last = Duration.zero;
      _lastAccent = Duration.zero;
      _ticker.start();
    } else if (!needsTicker && _ticker.isActive) {
      _ticker.stop();
    }
    frame.fire();
  }

  void _onTick(Duration elapsed) {
    final dt = (elapsed - _last).inMicroseconds / 1e6;
    _last = elapsed;
    final speed = settings.borderSpeed;
    borderPhase = (borderPhase + dt / _borderSeconds(speed)) % 1.0;
    if (settings.rainbow) {
      rainbowHue = (rainbowHue - _rainbowDegPerSecond(speed) * dt) % 360;
      if (elapsed - _lastAccent >= const Duration(milliseconds: 33)) {
        _lastAccent = elapsed;
        accentColor = hsl(rainbowHue, 85, 60);
        accent.fire();
      }
    }
    frame.fire();
  }

  void dispose() {
    settings.removeListener(_sync);
    _ticker.dispose();
    accent.dispose();
    frame.dispose();
  }
}

/// Services and palette for the widget tree.
class AppScope extends InheritedWidget {
  const AppScope({
    super.key,
    required this.settings,
    required this.client,
    required this.visuals,
    required this.palette,
    required super.child,
  });

  final Settings settings;
  final WiremoteClient client;
  final Visuals visuals;
  final Palette palette;

  static AppScope of(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<AppScope>()!;

  /// For event handlers: looks the scope up without rebuilding on changes.
  static AppScope read(BuildContext context) =>
      context.getInheritedWidgetOfExactType<AppScope>()!;

  @override
  bool updateShouldNotify(AppScope old) =>
      palette != old.palette ||
      client != old.client ||
      settings != old.settings ||
      visuals != old.visuals;
}

/// Rebuilds its dependents when the accent color changes.
class AccentScope extends InheritedNotifier<Pulse> {
  AccentScope({super.key, required this.visuals, required super.child})
    : super(notifier: visuals.accent);

  final Visuals visuals;

  static Visuals of(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<AccentScope>()!.visuals;
}

extension ScopeContext on BuildContext {
  Palette get palette => AppScope.of(this).palette;
  AppScope get scope => AppScope.of(this);

  /// Current accent color; the calling widget rebuilds when it changes.
  Color get accent => AccentScope.of(this).accentColor;
}

/// An accent-filled box. In rainbow mode it shows the rainbow gradient pinned to
/// the screen, so every accent surface shows its slice of one shared gradient.
class AccentBox extends StatelessWidget {
  const AccentBox({super.key, required this.borderRadius, this.child});

  final BorderRadius borderRadius;
  final Widget? child;

  @override
  Widget build(BuildContext context) {
    final v = AccentScope.of(context);
    if (!v.rainbow) {
      return DecoratedBox(
        decoration: BoxDecoration(
          color: v.accentColor,
          borderRadius: borderRadius,
        ),
        child: child,
      );
    }
    return _ScreenGradientFill(
      colors: v.rainbowColors(),
      borderRadius: borderRadius,
      screenWidth: MediaQuery.sizeOf(context).width,
      child: child,
    );
  }
}

class _ScreenGradientFill extends SingleChildRenderObjectWidget {
  const _ScreenGradientFill({
    required this.colors,
    required this.borderRadius,
    required this.screenWidth,
    super.child,
  });

  final List<Color> colors;
  final BorderRadius borderRadius;
  final double screenWidth;

  @override
  RenderObject createRenderObject(BuildContext context) =>
      _RenderScreenGradientFill(colors, borderRadius, screenWidth);

  @override
  void updateRenderObject(BuildContext context, _RenderScreenGradientFill r) {
    r
      ..colors = colors
      ..borderRadius = borderRadius
      ..screenWidth = screenWidth
      ..markNeedsPaint();
  }
}

class _RenderScreenGradientFill extends RenderProxyBox {
  _RenderScreenGradientFill(this.colors, this.borderRadius, this.screenWidth);

  List<Color> colors;
  BorderRadius borderRadius;
  double screenWidth;

  @override
  void paint(PaintingContext context, Offset offset) {
    final rect = offset & size;
    final globalX = localToGlobal(Offset.zero).dx;
    final shaderRect = Rect.fromLTWH(
      offset.dx - globalX,
      offset.dy,
      screenWidth,
      size.height,
    );
    final paint = Paint()
      ..shader = LinearGradient(colors: colors).createShader(shaderRect);
    context.canvas.drawRRect(borderRadius.toRRect(rect), paint);
    super.paint(context, offset);
  }
}
