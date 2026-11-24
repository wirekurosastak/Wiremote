import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'settings.dart';
import 'theme.dart';

const _labelStyle = TextStyle(
  fontSize: 14,
  fontWeight: FontWeight.w600,
  letterSpacing: 1,
);

/// Card heading, e.g. MEDIA.
class SectionTitle extends StatelessWidget {
  const SectionTitle(this.text, {super.key});
  final String text;

  @override
  Widget build(BuildContext context) => Text(
    text.toUpperCase(),
    style: _labelStyle.copyWith(color: context.palette.secondary),
  );
}

/// Small grey label above a control, e.g. MASTER.
class SubTitle extends StatelessWidget {
  const SubTitle(
    this.text, {
    super.key,
    this.padding = const EdgeInsets.only(bottom: 6),
  });
  final String text;
  final EdgeInsets padding;

  @override
  Widget build(BuildContext context) => Padding(
    padding: padding,
    child: Text(
      text.toUpperCase(),
      style: const TextStyle(
        fontSize: 12,
        fontWeight: FontWeight.w600,
        letterSpacing: 1,
        color: Palette.subTitle,
      ),
    ),
  );
}

class CardDivider extends StatelessWidget {
  const CardDivider({super.key});

  @override
  Widget build(BuildContext context) => Container(
    height: 1,
    margin: const EdgeInsets.symmetric(vertical: 20),
    color: context.palette.divider,
  );
}

class Hint extends StatelessWidget {
  const Hint(this.text, {super.key});
  final String text;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 12),
    child: Center(
      child: Text(
        text,
        style: TextStyle(fontSize: 14, color: context.palette.secondary),
      ),
    ),
  );
}

class _Arrow extends StatelessWidget {
  const _Arrow({required this.open, required this.size, required this.color});
  final bool open;
  final double size;
  final Color color;

  @override
  Widget build(BuildContext context) => AnimatedRotation(
    turns: open ? 0.5 : 0,
    duration: const Duration(milliseconds: 200),
    child: Icon(Icons.arrow_drop_down, size: size, color: color),
  );
}

/// Shows or hides [child] with a height animation. The child stays mounted
/// while hidden, so text fields and timers keep their state.
class Collapsible extends StatefulWidget {
  const Collapsible({super.key, required this.open, required this.child});
  final bool open;
  final Widget child;

  @override
  State<Collapsible> createState() => _CollapsibleState();
}

class _CollapsibleState extends State<Collapsible>
    with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 200),
    value: widget.open ? 1 : 0,
  )..addStatusListener((_) => setState(() {}));
  late final Animation<double> _curve = CurvedAnimation(
    parent: _c,
    curve: Curves.easeOut,
  );

  @override
  void didUpdateWidget(Collapsible old) {
    super.didUpdateWidget(old);
    if (widget.open != old.open) widget.open ? _c.forward() : _c.reverse();
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => SizeTransition(
    sizeFactor: _curve,
    alignment: Alignment.topCenter,
    child: Visibility(
      visible: !_c.isDismissed,
      maintainState: true,
      child: widget.child,
    ),
  );
}

/// A top-level card with a collapsible body and the accent spotlight border.
class SectionCard extends StatefulWidget {
  const SectionCard({
    super.key,
    required this.header,
    required this.child,
    this.initiallyOpen = false,
  });

  final Widget header;
  final Widget child;
  final bool initiallyOpen;

  @override
  State<SectionCard> createState() => _SectionCardState();
}

class _SectionCardState extends State<SectionCard> {
  late bool _open = widget.initiallyOpen;

  @override
  Widget build(BuildContext context) {
    final scope = context.scope;
    final p = scope.palette;
    return CustomPaint(
      painter: _BorderPainter(scope.visuals),
      child: Padding(
        padding: const EdgeInsets.all(2),
        child: DecoratedBox(
          decoration: BoxDecoration(
            color: p.surface,
            borderRadius: BorderRadius.circular(18),
          ),
          child: Padding(
            padding: const EdgeInsets.all(18),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                GestureDetector(
                  behavior: HitTestBehavior.opaque,
                  onTap: () => setState(() => _open = !_open),
                  child: Row(
                    children: [
                      Expanded(child: widget.header),
                      const SizedBox(width: 10),
                      _Arrow(open: _open, size: 26, color: p.secondary),
                    ],
                  ),
                ),
                Collapsible(
                  open: _open,
                  child: Padding(
                    padding: const EdgeInsets.only(top: 20),
                    child: widget.child,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// Paints the 2 px card border: off, a full accent ring, or a spotlight that
/// travels around the card corners (as in the web UI).
class _BorderPainter extends CustomPainter {
  _BorderPainter(this.visuals) : super(repaint: visuals.frame);

  final Visuals visuals;

  static Offset spotPosition(double t, Size s) {
    // Corners in order: top-left, top-right, bottom-right, bottom-left.
    final seg = (t * 4).floor() % 4;
    final f = t * 4 - (t * 4).floor();
    return switch (seg) {
      0 => Offset(f * s.width, 0),
      1 => Offset(s.width, f * s.height),
      2 => Offset((1 - f) * s.width, s.height),
      _ => Offset(0, (1 - f) * s.height),
    };
  }

  @override
  void paint(Canvas canvas, Size size) {
    final visibility = visuals.settings.borderVisibility;
    if (visibility == BorderVisibility.off) return;
    final bounds = Offset.zero & size;
    final rrect = RRect.fromRectAndRadius(bounds, const Radius.circular(20));
    final rainbow = visuals.rainbow;

    final fill = Paint();
    if (rainbow) {
      fill.shader = LinearGradient(colors: visuals.rainbowColors())
          .createShader(bounds);
    } else {
      fill.color = visuals.accentColor;
    }

    if (visibility == BorderVisibility.full) {
      canvas.drawRRect(rrect, fill);
      return;
    }

    final center = spotPosition(visuals.borderPhase, size);
    final radius = (size.height * 0.4).roundToDouble().clamp(35.0, 80.0);
    final spot = Rect.fromCircle(center: center, radius: radius);

    if (!rainbow) {
      final c = visuals.accentColor;
      final paint = Paint()
        ..shader = RadialGradient(
          colors: [c, c, c.withValues(alpha: 0)],
          stops: const [0, 0.2, 1],
        ).createShader(spot);
      canvas.save();
      canvas.clipRRect(rrect);
      canvas.drawRect(spot, paint);
      canvas.restore();
      return;
    }

    // Rainbow: mask the gradient ring with the radial spotlight.
    canvas.saveLayer(bounds, Paint());
    canvas.drawRRect(rrect, fill);
    canvas.drawRect(
      bounds,
      Paint()
        ..blendMode = BlendMode.dstIn
        ..shader = const RadialGradient(
          colors: [Color(0xFF000000), Color(0xFF000000), Color(0x00000000)],
          stops: [0, 0.2, 1],
        ).createShader(spot),
    );
    canvas.restore();
  }

  @override
  bool shouldRepaint(_BorderPainter old) => old.visuals != visuals;
}

/// A rounded inner panel that expands, e.g. OUTPUT or MIXER.
class SubSection extends StatefulWidget {
  const SubSection({
    super.key,
    required this.title,
    required this.child,
    this.margin = const EdgeInsets.only(bottom: 12),
  });

  final String title;
  final Widget child;
  final EdgeInsets margin;

  @override
  State<SubSection> createState() => _SubSectionState();
}

class _SubSectionState extends State<SubSection> {
  bool _open = false;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final color = _open ? p.text : p.secondary;
    return Container(
      margin: widget.margin,
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: p.surface2,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          GestureDetector(
            behavior: HitTestBehavior.opaque,
            onTap: () => setState(() => _open = !_open),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    widget.title.toUpperCase(),
                    style: _labelStyle.copyWith(fontSize: 13, color: color),
                  ),
                ),
                _Arrow(open: _open, size: 24, color: color),
              ],
            ),
          ),
          Collapsible(
            open: _open,
            child: Padding(
              padding: const EdgeInsets.only(top: 16),
              child: widget.child,
            ),
          ),
        ],
      ),
    );
  }
}

/// Press feedback shared by all buttons: shrink to 95 % while held.
class Pressable extends StatefulWidget {
  const Pressable({
    super.key,
    required this.builder,
    this.onTap,
    this.onPressStart,
    this.onPressEnd,
    this.enabled = true,
  });

  final Widget Function(BuildContext context, bool pressed) builder;
  final VoidCallback? onTap;

  /// Called when a press is recognised (not when the user starts scrolling).
  final VoidCallback? onPressStart;
  final VoidCallback? onPressEnd;
  final bool enabled;

  @override
  State<Pressable> createState() => _PressableState();
}

class _PressableState extends State<Pressable> {
  bool _pressed = false;

  void _set(bool v) {
    if (_pressed == v) return;
    setState(() => _pressed = v);
    v ? widget.onPressStart?.call() : widget.onPressEnd?.call();
  }

  @override
  Widget build(BuildContext context) {
    if (!widget.enabled) return widget.builder(context, false);
    return GestureDetector(
      onTapDown: (_) => _set(true),
      onTapUp: (_) => _set(false),
      onTapCancel: () => _set(false),
      onTap: widget.onTap,
      child: AnimatedScale(
        scale: _pressed ? 0.95 : 1,
        duration: const Duration(milliseconds: 100),
        child: widget.builder(context, _pressed),
      ),
    );
  }
}

/// The web UI's `button`: rounded, surface-colored, or accent-filled.
class WmButton extends StatelessWidget {
  const WmButton({
    super.key,
    required this.child,
    this.onTap,
    this.onPressStart,
    this.onPressEnd,
    this.accent = false,
    this.background,
    this.foreground,
    this.height = 60,
    this.fontSize = 20,
    this.fontWeight = FontWeight.w500,
    this.enabled = true,
  });

  final Widget child;
  final VoidCallback? onTap;
  final VoidCallback? onPressStart;
  final VoidCallback? onPressEnd;
  final bool accent;
  final Color? background;
  final Color? foreground;
  final double? height;
  final double fontSize;
  final FontWeight fontWeight;
  final bool enabled;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final rainbow = accent && AccentScope.of(context).rainbow;
    const radius = BorderRadius.all(Radius.circular(12));
    return Pressable(
      enabled: enabled,
      onTap: onTap,
      onPressStart: onPressStart,
      onPressEnd: onPressEnd,
      builder: (context, pressed) {
        final fg = pressed
            ? p.text
            : foreground ??
                  (accent ? (rainbow ? Colors.white : p.onAccent) : p.text);
        final content = Center(
          child: IconTheme.merge(
            data: IconThemeData(color: fg),
            child: DefaultTextStyle.merge(
              style: TextStyle(
                color: fg,
                fontSize: fontSize,
                fontWeight: fontWeight,
              ),
              textAlign: TextAlign.center,
              child: child,
            ),
          ),
        );
        final Widget box = accent && !pressed
            ? AccentBox(borderRadius: radius, child: content)
            : DecoratedBox(
                decoration: BoxDecoration(
                  color: pressed ? p.pressed : background ?? p.surface2,
                  borderRadius: radius,
                ),
                child: content,
              );
        return Opacity(
          opacity: enabled ? 1 : 0.4,
          child: SizedBox(height: height, child: box),
        );
      },
    );
  }
}

/// A row in a device list (audio output, monitor); filled with accent when active.
class DeviceButton extends StatelessWidget {
  const DeviceButton({
    super.key,
    required this.label,
    required this.active,
    required this.onTap,
  });

  final String label;
  final bool active;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final rainbow = AccentScope.of(context).rainbow;
    const radius = BorderRadius.all(Radius.circular(12));
    return Pressable(
      onTap: onTap,
      builder: (context, pressed) {
        final fg = active && !pressed
            ? (rainbow ? Colors.white : p.onAccent)
            : p.text;
        final content = ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 52),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
            child: Row(
              children: [
                SizedBox(
                  width: 20,
                  child: active ? Icon(Icons.check, size: 20, color: fg) : null,
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Marquee(
                    label,
                    style: TextStyle(
                      fontSize: 15,
                      color: fg,
                      fontWeight: active ? FontWeight.w600 : FontWeight.w500,
                    ),
                  ),
                ),
              ],
            ),
          ),
        );
        return active && !pressed
            ? AccentBox(borderRadius: radius, child: content)
            : DecoratedBox(
                decoration: BoxDecoration(
                  color: pressed ? p.pressed : p.surface,
                  borderRadius: radius,
                ),
                child: content,
              );
      },
    );
  }
}

class _Thumb extends SliderComponentShape {
  const _Thumb(this.border);
  final Color border;

  @override
  Size getPreferredSize(bool isEnabled, bool isDiscrete) => const Size(18, 18);

  @override
  void paint(
    PaintingContext context,
    Offset center, {
    required Animation<double> activationAnimation,
    required Animation<double> enableAnimation,
    required bool isDiscrete,
    required TextPainter labelPainter,
    required RenderBox parentBox,
    required SliderThemeData sliderTheme,
    required TextDirection textDirection,
    required double value,
    required double textScaleFactor,
    required Size sizeWithOverflow,
  }) {
    final canvas = context.canvas;
    canvas.drawCircle(
      center.translate(0, 2),
      9,
      Paint()
        ..color = const Color(0x66000000)
        ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 3),
    );
    canvas.drawCircle(center, 9, Paint()..color = sliderTheme.thumbColor!);
    canvas.drawCircle(
      center,
      8,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 2
        ..color = border,
    );
  }
}

class _Track extends SliderTrackShape with BaseSliderTrackShape {
  const _Track({this.spectrum = false});
  final bool spectrum;

  static const _spectrum = [
    Color(0xFFFF0000),
    Color(0xFFFFFF00),
    Color(0xFF00FF00),
    Color(0xFF00FFFF),
    Color(0xFF0000FF),
    Color(0xFFFF00FF),
    Color(0xFFFF0000),
  ];

  @override
  void paint(
    PaintingContext context,
    Offset offset, {
    required RenderBox parentBox,
    required SliderThemeData sliderTheme,
    required Animation<double> enableAnimation,
    required TextDirection textDirection,
    required Offset thumbCenter,
    Offset? secondaryOffset,
    bool isDiscrete = false,
    bool isEnabled = false,
  }) {
    final rect = getPreferredRect(
      parentBox: parentBox,
      offset: offset,
      sliderTheme: sliderTheme,
      isEnabled: isEnabled,
      isDiscrete: isDiscrete,
    );
    const r = Radius.circular(3);
    final canvas = context.canvas;
    if (spectrum) {
      canvas.drawRRect(
        RRect.fromRectAndRadius(rect, r),
        Paint()
          ..shader = const LinearGradient(colors: _spectrum).createShader(rect),
      );
      return;
    }
    canvas.drawRRect(
      RRect.fromRectAndRadius(rect, r),
      Paint()..color = sliderTheme.inactiveTrackColor!,
    );
    final active = Rect.fromLTRB(
      rect.left,
      rect.top,
      math.max(rect.left, thumbCenter.dx),
      rect.bottom,
    );
    canvas.drawRRect(
      RRect.fromRectAndRadius(active, r),
      Paint()..color = sliderTheme.activeTrackColor!,
    );
  }
}

/// The web UI's range input: 6 px track, accent fill, round bordered thumb.
class WmSlider extends StatelessWidget {
  const WmSlider({
    super.key,
    required this.value,
    required this.onChanged,
    this.min = 0,
    this.max = 100,
    this.divisions,
    this.onChangeStart,
    this.onChangeEnd,
    this.enabled = true,
    this.dimmed = false,
    this.height = 36,
    this.spectrum = false,
  });

  final double value;
  final double min;
  final double max;
  final int? divisions;
  final ValueChanged<double> onChanged;
  final ValueChanged<double>? onChangeStart;
  final ValueChanged<double>? onChangeEnd;
  final bool enabled;

  /// Muted look (30 % opacity) while staying usable.
  final bool dimmed;
  final double height;

  /// Hue picker: draws the color spectrum as the track.
  final bool spectrum;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final accent = context.accent;
    return Opacity(
      opacity: enabled && !dimmed ? 1 : 0.3,
      child: SliderTheme(
        data: SliderThemeData(
          trackHeight: 6,
          activeTrackColor: accent,
          inactiveTrackColor: p.surface3,
          disabledActiveTrackColor: accent,
          disabledInactiveTrackColor: p.surface3,
          thumbColor: accent,
          disabledThumbColor: accent,
          overlayShape: SliderComponentShape.noOverlay,
          thumbShape: _Thumb(p.thumbBorder),
          trackShape: _Track(spectrum: spectrum),
          tickMarkShape: SliderTickMarkShape.noTickMark,
          showValueIndicator: ShowValueIndicator.never,
          padding: const EdgeInsets.symmetric(horizontal: 9),
        ),
        child: SizedBox(
          height: height,
          child: Slider(
            value: value.clamp(min, max).toDouble(),
            min: min,
            max: max,
            divisions: divisions,
            onChanged: enabled ? onChanged : null,
            onChangeStart: onChangeStart,
            onChangeEnd: onChangeEnd,
          ),
        ),
      ),
    );
  }
}

class WmToggle extends StatelessWidget {
  const WmToggle({super.key, required this.value, required this.onChanged});
  final bool value;
  final ValueChanged<bool> onChanged;

  @override
  Widget build(BuildContext context) {
    const radius = BorderRadius.all(Radius.circular(12));
    return Semantics(
      toggled: value,
      child: GestureDetector(
        onTap: () => onChanged(!value),
        child: SizedBox(
          width: 44,
          height: 24,
          child: Stack(
            children: [
              Positioned.fill(
                child: value
                    ? const AccentBox(borderRadius: radius)
                    : const DecoratedBox(
                        decoration: BoxDecoration(
                          color: Palette.toggleOff,
                          borderRadius: radius,
                        ),
                      ),
              ),
              AnimatedAlign(
                alignment: value ? Alignment.centerRight : Alignment.centerLeft,
                duration: const Duration(milliseconds: 200),
                child: Container(
                  width: 18,
                  height: 18,
                  margin: const EdgeInsets.all(3),
                  decoration: const BoxDecoration(
                    color: Colors.white,
                    shape: BoxShape.circle,
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// A select box. [compact] is the small inline style used in MISCELLANEOUS.
class WmSelect<T> extends StatelessWidget {
  const WmSelect({
    super.key,
    required this.value,
    required this.options,
    required this.onChanged,
    this.enabled = true,
    this.compact = false,
    this.placeholder = '',
  });

  final T? value;
  final List<(T, String)> options;
  final ValueChanged<T> onChanged;
  final bool enabled;
  final bool compact;
  final String placeholder;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final style = Theme.of(context).textTheme.titleMedium!.copyWith(
      fontSize: compact ? 14 : 15,
      fontWeight: compact ? FontWeight.w600 : FontWeight.w500,
      color: p.text,
    );
    final align = compact
        ? AlignmentDirectional.centerStart
        : AlignmentDirectional.center;
    final hasValue = options.any((o) => o.$1 == value);
    return Opacity(
      opacity: enabled ? 1 : 0.5,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 12),
        decoration: BoxDecoration(
          color: p.surface3,
          borderRadius: BorderRadius.circular(8),
          border: compact ? Border.all(color: p.softBorder) : null,
        ),
        child: DropdownButtonHideUnderline(
          child: DropdownButton<T>(
            value: hasValue ? value : null,
            isExpanded: !compact,
            isDense: compact,
            alignment: align,
            icon: compact
                ? Icon(Icons.arrow_drop_down, size: 20, color: p.text)
                : const SizedBox.shrink(),
            dropdownColor: p.surface3,
            borderRadius: BorderRadius.circular(8),
            style: style,
            padding: compact ? const EdgeInsets.symmetric(vertical: 6) : null,
            hint: Text(placeholder, style: style),
            disabledHint: Text(
              hasValue
                  ? options.firstWhere((o) => o.$1 == value).$2
                  : placeholder,
              style: style,
            ),
            items: [
              for (final (v, label) in options)
                DropdownMenuItem(
                  value: v,
                  alignment: align,
                  child: Text(label),
                ),
            ],
            onChanged: enabled
                ? (v) {
                    if (v != null) onChanged(v);
                  }
                : null,
          ),
        ),
      ),
    );
  }
}

/// Single-line text that scrolls slowly (40 px/s) when it doesn't fit.
class Marquee extends StatefulWidget {
  const Marquee(this.text, {super.key, this.style});
  final String text;
  final TextStyle? style;

  @override
  State<Marquee> createState() => _MarqueeState();
}

class _MarqueeState extends State<Marquee> with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(vsync: this);
  double? _extent;

  void _setExtent(double? extent) {
    if (extent == _extent) return;
    _extent = extent;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted || _extent != extent) return;
      if (extent == null) {
        _c.stop();
        _c.value = 0;
      } else {
        _c.duration = Duration(milliseconds: (extent / 40 * 1000).round());
        _c.repeat();
      }
    });
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final style = DefaultTextStyle.of(context).style.merge(widget.style);
    return LayoutBuilder(
      builder: (context, constraints) {
        final tp = TextPainter(
          text: TextSpan(text: widget.text, style: style),
          maxLines: 1,
          textDirection: Directionality.of(context),
          textScaler: MediaQuery.textScalerOf(context),
        )..layout();
        final width = tp.width;
        final height = tp.height;
        tp.dispose();

        final text = Text(
          widget.text,
          style: style,
          maxLines: 1,
          softWrap: false,
        );
        if (width <= constraints.maxWidth) {
          _setExtent(null);
          return text;
        }
        final gap = (style.fontSize ?? 14) * 2;
        final extent = width + gap;
        _setExtent(extent);
        return SizedBox(
          height: height,
          width: constraints.maxWidth,
          child: ClipRect(
            child: AnimatedBuilder(
              animation: _c,
              builder: (context, child) => Transform.translate(
                offset: Offset(-extent * _c.value, 0),
                child: child,
              ),
              child: OverflowBox(
                alignment: AlignmentDirectional.centerStart,
                maxWidth: double.infinity,
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    text,
                    SizedBox(width: gap),
                    text,
                  ],
                ),
              ),
            ),
          ),
        );
      },
    );
  }
}
