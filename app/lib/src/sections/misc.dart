import 'package:flutter/material.dart';

import '../screens.dart';
import '../settings.dart';
import '../theme.dart';
import '../widgets.dart';

class MiscSection extends StatelessWidget {
  const MiscSection({super.key});

  @override
  Widget build(BuildContext context) {
    final settings = context.scope.settings;
    return ListenableBuilder(
      listenable: settings,
      builder: (context, _) {
        final p = context.palette;
        final rainbow = settings.rainbow;
        final speedEnabled =
            settings.borderVisibility == BorderVisibility.animated || rainbow;
        return SectionCard(
          header: const SectionTitle('Miscellaneous'),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              SubSection(
                title: 'PC',
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  spacing: 12,
                  children: [
                    Text(
                      settings.host ?? '',
                      textAlign: TextAlign.center,
                      style: const TextStyle(
                        fontSize: 15,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    WmButton(
                      height: 44,
                      fontSize: 14,
                      fontWeight: FontWeight.w600,
                      background: p.surface,
                      onTap: () => showServerDialog(context),
                      child: const Text('Change PC'),
                    ),
                  ],
                ),
              ),
              SubSection(
                title: 'Theme',
                child: WmSelect<bool>(
                  value: settings.lightTheme,
                  options: const [(false, 'Dark'), (true, 'Light')],
                  onChanged: (v) => settings.lightTheme = v,
                ),
              ),
              _MiscRow(
                label: 'Keep Screen Awake',
                padding: const EdgeInsets.fromLTRB(4, 10, 4, 12),
                background: Colors.transparent,
                child: WmToggle(
                  value: settings.keepScreenAwake,
                  onChanged: (v) => settings.keepScreenAwake = v,
                ),
              ),
              SubSection(
                title: 'Border',
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  spacing: 10,
                  children: [
                    _MiscRow(
                      label: 'Visibility',
                      child: WmSelect<BorderVisibility>(
                        compact: true,
                        value: settings.borderVisibility,
                        options: const [
                          (BorderVisibility.off, 'Off'),
                          (BorderVisibility.animated, 'Animated'),
                          (BorderVisibility.full, 'Full'),
                        ],
                        onChanged: (v) => settings.borderVisibility = v,
                      ),
                    ),
                    Opacity(
                      opacity: speedEnabled ? 1 : 0.35,
                      child: _MiscRow(
                        label: 'Animation Speed',
                        child: WmSelect<BorderSpeed>(
                          compact: true,
                          enabled: speedEnabled,
                          value: settings.borderSpeed,
                          options: const [
                            (BorderSpeed.slow, 'Slow'),
                            (BorderSpeed.normal, 'Normal'),
                            (BorderSpeed.fast, 'Fast'),
                          ],
                          onChanged: (v) => settings.borderSpeed = v,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
              SubSection(
                title: 'Accent',
                margin: EdgeInsets.zero,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  spacing: 10,
                  children: [
                    _MiscRow(
                      label: 'Rainbow Accent',
                      child: WmToggle(
                        value: rainbow,
                        onChanged: (v) => settings.rainbow = v,
                      ),
                    ),
                    IgnorePointer(
                      ignoring: rainbow,
                      child: Opacity(
                        opacity: rainbow ? 0.35 : 1,
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          spacing: 10,
                          children: [
                            _ColorSlider(
                              label: 'Hue',
                              value: settings.hue,
                              suffix: '°',
                              max: 360,
                              spectrum: true,
                              onChanged: (v) => settings.hue = v,
                            ),
                            _ColorSlider(
                              label: 'Saturation',
                              value: settings.saturation,
                              suffix: '%',
                              max: 100,
                              onChanged: (v) => settings.saturation = v,
                            ),
                            _ColorSlider(
                              label: 'Lightness',
                              value: settings.lightness,
                              suffix: '%',
                              min: 10,
                              max: 90,
                              onChanged: (v) => settings.lightness = v,
                            ),
                            Padding(
                              padding: const EdgeInsets.only(top: 4),
                              child: WmButton(
                                height: 40,
                                fontSize: 13,
                                fontWeight: FontWeight.w600,
                                onTap: settings.resetAccent,
                                child: const Text('Default Accent (#b0914f)'),
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        );
      },
    );
  }
}

class _MiscRow extends StatelessWidget {
  const _MiscRow({
    required this.label,
    required this.child,
    this.padding = const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
    this.background,
  });

  final String label;
  final Widget child;
  final EdgeInsets padding;
  final Color? background;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return Container(
      padding: padding,
      decoration: BoxDecoration(
        color: background ?? p.surface2,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Row(
        children: [
          Expanded(
            child: Text(
              label,
              style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600),
            ),
          ),
          child,
        ],
      ),
    );
  }
}

class _ColorSlider extends StatelessWidget {
  const _ColorSlider({
    required this.label,
    required this.value,
    required this.suffix,
    required this.max,
    required this.onChanged,
    this.min = 0,
    this.spectrum = false,
  });

  final String label;
  final int value;
  final String suffix;
  final int min;
  final int max;
  final bool spectrum;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return Container(
      padding: const EdgeInsets.fromLTRB(14, 12, 14, 4),
      decoration: BoxDecoration(
        color: p.surface2,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  label,
                  style: const TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
              Text(
                '$value$suffix',
                style: TextStyle(
                  fontSize: 13,
                  fontWeight: FontWeight.w500,
                  color: p.secondary,
                ),
              ),
            ],
          ),
          WmSlider(
            value: value.toDouble(),
            min: min.toDouble(),
            max: max.toDouble(),
            divisions: max - min,
            spectrum: spectrum,
            onChanged: (v) => onChanged(v.round()),
          ),
        ],
      ),
    );
  }
}
