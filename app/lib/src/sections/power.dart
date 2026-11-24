import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../protocol.dart';
import '../theme.dart';
import '../widgets.dart';

class PowerSection extends StatefulWidget {
  const PowerSection({super.key});

  @override
  State<PowerSection> createState() => _PowerSectionState();
}

class _PowerSectionState extends State<PowerSection> {
  final _hours = TextEditingController(text: '0');
  final _minutes = TextEditingController(text: '0');
  final _seconds = TextEditingController(text: '0');
  Timer? _countdown;

  @override
  void dispose() {
    _countdown?.cancel();
    _hours.dispose();
    _minutes.dispose();
    _seconds.dispose();
    super.dispose();
  }

  int get _delaySeconds {
    int read(TextEditingController c) => int.tryParse(c.text) ?? 0;
    return read(_hours) * 3600 + read(_minutes) * 60 + read(_seconds);
  }

  void _power(String action) => AppScope.read(context).client.command('power', {
    'action': action,
    'seconds': action == 'cancel' ? 0 : _delaySeconds,
  });

  /// Ticks the header countdown once a second while a timer runs.
  void _syncCountdown(PowerTimer? timer) {
    if (timer != null && _countdown == null) {
      _countdown = Timer.periodic(const Duration(seconds: 1), (_) {
        if (mounted) setState(() {});
      });
    } else if (timer == null && _countdown != null) {
      _countdown!.cancel();
      _countdown = null;
    }
  }

  static String _badge(PowerTimer t) {
    final left = t.deadline.difference(DateTime.now()).inSeconds;
    if (left <= 0) return 'Executing...';
    String two(int n) => n.toString().padLeft(2, '0');
    final hms =
        '${two(left ~/ 3600)}:${two(left % 3600 ~/ 60)}:${two(left % 60)}';
    return '${powerActionLabel(t.action)} in $hms';
  }

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final client = context.scope.client;
    return ValueListenableBuilder(
      valueListenable: client.timer,
      builder: (context, timer, _) {
        _syncCountdown(timer);
        final idle = timer == null;
        return SectionCard(
          header: Row(
            children: [
              const SectionTitle('Power'),
              if (timer != null)
                Expanded(
                  child: Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    spacing: 4,
                    children: [
                      Icon(
                        Icons.hourglass_bottom,
                        size: 13,
                        color: context.accent,
                      ),
                      Flexible(
                        child: Text(
                          _badge(timer),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                            fontSize: 12,
                            fontWeight: FontWeight.w600,
                            color: context.accent,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
            ],
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                spacing: 8,
                children: [
                  for (final (label, c) in [
                    ('Hours', _hours),
                    ('Minutes', _minutes),
                    ('Seconds', _seconds),
                  ])
                    Expanded(
                      child: _NumberField(
                        label: label,
                        controller: c,
                        enabled: idle,
                      ),
                    ),
                ],
              ),
              const SizedBox(height: 20),
              _grid([
                _PowerButton(
                  'Shutdown',
                  Icons.power_settings_new,
                  accent: true,
                  enabled: idle,
                  onTap: () => _power('shutdown'),
                ),
                _PowerButton(
                  'Sleep',
                  Icons.bedtime_outlined,
                  enabled: idle,
                  onTap: () => _power('sleep'),
                ),
                _PowerButton(
                  'Restart',
                  Icons.restart_alt,
                  enabled: idle,
                  onTap: () => _power('restart'),
                ),
                _PowerButton(
                  'Hibernate',
                  Icons.save_outlined,
                  enabled: idle,
                  onTap: () => _power('hibernate'),
                ),
                _PowerButton(
                  'Restart to BIOS',
                  Icons.memory,
                  enabled: idle,
                  onTap: () => _power('restart_bios'),
                ),
                _PowerButton(
                  'Cancel',
                  Icons.close,
                  foreground: Palette.danger,
                  background: p.surface2,
                  onTap: () => _power('cancel'),
                ),
              ]),
            ],
          ),
        );
      },
    );
  }

  Widget _grid(List<Widget> buttons) => Column(
    spacing: 10,
    children: [
      for (var i = 0; i < buttons.length; i += 2)
        Row(
          spacing: 10,
          children: [
            Expanded(child: buttons[i]),
            Expanded(
              child: i + 1 < buttons.length ? buttons[i + 1] : const SizedBox(),
            ),
          ],
        ),
    ],
  );
}

class _PowerButton extends StatelessWidget {
  const _PowerButton(
    this.label,
    this.icon, {
    required this.onTap,
    this.accent = false,
    this.enabled = true,
    this.foreground,
    this.background,
  });

  final String label;
  final IconData icon;
  final VoidCallback onTap;
  final bool accent;
  final bool enabled;
  final Color? foreground;
  final Color? background;

  @override
  Widget build(BuildContext context) => WmButton(
    height: 65,
    fontSize: 15,
    fontWeight: accent ? FontWeight.w700 : FontWeight.w500,
    accent: accent,
    enabled: enabled,
    foreground: foreground,
    background: background,
    onTap: onTap,
    child: Column(
      mainAxisSize: MainAxisSize.min,
      spacing: 5,
      children: [
        Icon(icon, size: 20),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 6),
          child: FittedBox(
            fit: BoxFit.scaleDown,
            child: Text(label, maxLines: 1),
          ),
        ),
      ],
    ),
  );
}

class _NumberField extends StatelessWidget {
  const _NumberField({
    required this.label,
    required this.controller,
    required this.enabled,
  });

  final String label;
  final TextEditingController controller;
  final bool enabled;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    OutlineInputBorder border(Color c) => OutlineInputBorder(
      borderRadius: BorderRadius.circular(8),
      borderSide: BorderSide(color: c),
    );
    return Opacity(
      opacity: enabled ? 1 : 0.4,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        spacing: 6,
        children: [
          Padding(
            padding: const EdgeInsets.only(left: 2),
            child: Text(
              label,
              style: TextStyle(
                fontSize: 13,
                fontWeight: FontWeight.w500,
                color: p.text,
              ),
            ),
          ),
          TextField(
            controller: controller,
            enabled: enabled,
            keyboardType: TextInputType.number,
            inputFormatters: [FilteringTextInputFormatter.digitsOnly],
            textAlign: TextAlign.center,
            style: TextStyle(fontSize: 16, color: p.text),
            onTap: () => controller.selection = TextSelection(
              baseOffset: 0,
              extentOffset: controller.text.length,
            ),
            decoration: InputDecoration(
              isDense: true,
              filled: true,
              fillColor: p.surface2,
              contentPadding: const EdgeInsets.symmetric(vertical: 10),
              enabledBorder: border(Palette.inputBorder),
              disabledBorder: border(Palette.inputBorder),
              focusedBorder: border(context.accent),
            ),
          ),
        ],
      ),
    );
  }
}
