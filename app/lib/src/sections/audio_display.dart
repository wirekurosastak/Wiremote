import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

import '../protocol.dart';
import '../theme.dart';
import '../throttle.dart';
import '../widgets.dart';

class AudioDisplaySection extends StatefulWidget {
  const AudioDisplaySection({super.key});

  @override
  State<AudioDisplaySection> createState() => _AudioDisplaySectionState();
}

class _AudioDisplaySectionState extends State<AudioDisplaySection> {
  bool _audio = true;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    Widget tab(String label, bool selected, bool toAudio, EdgeInsets padding) =>
        GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: () => setState(() => _audio = toAudio),
          child: Padding(
            padding: padding,
            child: Opacity(
              opacity: selected ? 1 : 0.6,
              child: Container(
                padding: const EdgeInsets.only(bottom: 2),
                decoration: BoxDecoration(
                  border: Border(
                    bottom: BorderSide(
                      color: selected ? p.secondary : Colors.transparent,
                      width: 1.5,
                    ),
                  ),
                ),
                child: SectionTitle(label),
              ),
            ),
          ),
        );

    return SectionCard(
      initiallyOpen: true,
      header: Row(
        children: [
          tab('Audio', _audio, true, const EdgeInsets.fromLTRB(0, 4, 8, 4)),
          Text(
            '//',
            style: TextStyle(
              color: p.secondary.withValues(alpha: 0.4),
              fontWeight: FontWeight.w300,
            ),
          ),
          tab(
            'Display',
            !_audio,
            false,
            const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
          ),
        ],
      ),
      // Both tabs stay mounted so their open/closed panels survive switching.
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Visibility(
            visible: _audio,
            maintainState: true,
            child: const _AudioTab(),
          ),
          Visibility(
            visible: !_audio,
            maintainState: true,
            child: const _DisplayTab(),
          ),
        ],
      ),
    );
  }
}

class _AudioTab extends StatelessWidget {
  const _AudioTab();

  @override
  Widget build(BuildContext context) {
    final client = context.scope.client;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SubTitle('Master'),
        ValueListenableBuilder(
          valueListenable: client.volume,
          builder: (context, vol, _) => LiveSlider(
            serverValue: vol?.value,
            muted: vol?.muted ?? false,
            throttle: const Duration(milliseconds: 50),
            onSend: (v) => client.command('set_volume', {'value': v}),
            onLabelTap: () => client.command('mute'),
          ),
        ),
        const CardDivider(),
        SubSection(
          title: 'Output',
          child: ValueListenableBuilder(
            valueListenable: client.devices,
            builder: (context, devices, _) {
              if (devices == null) return const Hint('…');
              if (devices.isEmpty) return const Hint('No devices');
              return Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                spacing: 8,
                children: [
                  for (final d in devices)
                    DeviceButton(
                      key: ValueKey(d.id),
                      label: d.name,
                      active: d.isDefault,
                      onTap: () {
                        if (!d.isDefault) {
                          client.command('set_device', {'id': d.id});
                        }
                      },
                    ),
                ],
              );
            },
          ),
        ),
        SubSection(
          title: 'Mixer',
          margin: EdgeInsets.zero,
          child: _Mixer(client.sessions),
        ),
      ],
    );
  }
}

/// Per-app volumes. While a slider is being dragged, incoming lists are held
/// back so rows don't jump under the finger.
class _Mixer extends StatefulWidget {
  const _Mixer(this.source);
  final ValueListenable<List<AudioSession>> source;

  @override
  State<_Mixer> createState() => _MixerState();
}

class _MixerState extends State<_Mixer> {
  late List<AudioSession> _shown = widget.source.value;
  int _dragging = 0;

  @override
  void initState() {
    super.initState();
    widget.source.addListener(_onSessions);
  }

  @override
  void didUpdateWidget(_Mixer old) {
    super.didUpdateWidget(old);
    if (old.source != widget.source) {
      old.source.removeListener(_onSessions);
      widget.source.addListener(_onSessions);
      _onSessions();
    }
  }

  @override
  void dispose() {
    widget.source.removeListener(_onSessions);
    super.dispose();
  }

  void _onSessions() {
    if (_dragging == 0 && mounted) setState(() => _shown = widget.source.value);
  }

  void _onDragging(bool dragging) {
    _dragging += dragging ? 1 : -1;
    if (_dragging < 0) _dragging = 0;
    // May be called from a row's dispose, when setState isn't allowed yet.
    if (_dragging == 0) scheduleMicrotask(_onSessions);
  }

  @override
  Widget build(BuildContext context) {
    final client = context.scope.client;
    final p = context.palette;
    if (_shown.isEmpty) return const Hint('No active apps');
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      spacing: 16,
      children: [
        for (final s in _shown)
          Column(
            key: ValueKey(s.id),
            crossAxisAlignment: CrossAxisAlignment.stretch,
            spacing: 4,
            children: [
              Marquee(
                s.name,
                style: TextStyle(
                  fontSize: 14,
                  fontWeight: FontWeight.w600,
                  color: s.muted ? p.secondary : p.text,
                  decoration: s.muted ? TextDecoration.lineThrough : null,
                ),
              ),
              LiveSlider(
                serverValue: s.volume,
                muted: s.muted,
                small: true,
                onSend: (v) => client.command('set_session_volume', {
                  'id': s.id,
                  'value': v,
                }),
                onLabelTap: () => client.command('session_mute', {'id': s.id}),
                onDragging: _onDragging,
              ),
            ],
          ),
      ],
    );
  }
}

class _DisplayTab extends StatelessWidget {
  const _DisplayTab();

  @override
  Widget build(BuildContext context) {
    final client = context.scope.client;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SubTitle('Brightness'),
        ValueListenableBuilder(
          valueListenable: client.brightness,
          builder: (context, b, _) {
            final unsupported = b != null && !b.supported;
            return LiveSlider(
              serverValue: b == null ? null : (unsupported ? 100 : b.value),
              enabled: !unsupported,
              muted: unsupported,
              onSend: (v) => client.command('set_brightness', {'value': v}),
            );
          },
        ),
        const CardDivider(),
        ValueListenableBuilder(
          valueListenable: client.displays,
          builder: (context, ds, _) {
            final displays = ds?.displays;
            final active = ds?.active ?? const <DisplayInfo>[];
            final multiple = active.length > 1;
            final primary = active.where((d) => d.isPrimary).firstOrNull;
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                SubSection(
                  title: 'Output',
                  margin: const EdgeInsets.only(top: 8, bottom: 12),
                  child: displays == null
                      ? const Hint('…')
                      : displays.isEmpty
                      ? const Hint('No monitors found')
                      : Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          spacing: 8,
                          children: [
                            for (final d in displays)
                              DeviceButton(
                                key: ValueKey(d.id),
                                label: d.isPrimary
                                    ? '${d.label} (Primary)'
                                    : d.label,
                                active: d.isActive,
                                onTap: () => client.command('set_display', {
                                  'id': d.id,
                                  'active': !d.isActive,
                                }),
                              ),
                          ],
                        ),
                ),
                SubSection(
                  title: 'Primary',
                  child: WmSelect<String>(
                    value: primary?.id,
                    placeholder: 'None',
                    enabled: multiple,
                    options: [for (final d in active) (d.id, d.label)],
                    onChanged: (id) =>
                        client.command('set_primary_display', {'id': id}),
                  ),
                ),
                SubSection(
                  title: 'Mode',
                  margin: EdgeInsets.zero,
                  child: WmSelect<String>(
                    value: ds?.mode ?? 'extend',
                    enabled: multiple,
                    options: const [('extend', 'Extend'), ('clone', 'Clone')],
                    onChanged: (mode) =>
                        client.command('display_switch', {'mode': mode}),
                  ),
                ),
              ],
            );
          },
        ),
      ],
    );
  }
}

/// A 0–100 slider with a percent label that follows the PC's value, except
/// while the user drags it (and briefly after, until the PC confirms).
class LiveSlider extends StatefulWidget {
  const LiveSlider({
    super.key,
    required this.serverValue,
    required this.onSend,
    this.muted = false,
    this.enabled = true,
    this.small = false,
    this.throttle = const Duration(milliseconds: 60),
    this.onLabelTap,
    this.onDragging,
  });

  /// Null until the PC has reported a value.
  final int? serverValue;
  final ValueChanged<int> onSend;
  final bool muted;
  final bool enabled;

  /// Mixer row style: shorter slider, smaller label.
  final bool small;
  final Duration throttle;
  final VoidCallback? onLabelTap;
  final ValueChanged<bool>? onDragging;

  @override
  State<LiveSlider> createState() => _LiveSliderState();
}

class _LiveSliderState extends State<LiveSlider> {
  late final _send = Throttle<int>(widget.throttle, (v) => widget.onSend(v));
  double? _local;
  bool _dragging = false;
  Timer? _release;

  @override
  void didUpdateWidget(LiveSlider old) {
    super.didUpdateWidget(old);
    if (widget.serverValue != old.serverValue && !_dragging) {
      _release?.cancel();
      _local = null;
    }
  }

  @override
  void dispose() {
    _send.flush();
    _send.dispose();
    _release?.cancel();
    if (_dragging) widget.onDragging?.call(false);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final local = _local;
    final value = local ?? widget.serverValue?.toDouble();
    final muted = local != null ? local.round() == 0 : widget.muted;
    final label = value == null ? '--%' : '${value.round()}%';
    return Row(
      children: [
        Expanded(
          child: WmSlider(
            value: value ?? 50,
            height: widget.small ? 30 : 36,
            enabled: widget.enabled,
            dimmed: muted,
            onChangeStart: (v) {
              _release?.cancel();
              _dragging = true;
              widget.onDragging?.call(true);
            },
            onChanged: (v) {
              setState(() => _local = v);
              _send(v.round());
            },
            onChangeEnd: (v) {
              _send.flush();
              _dragging = false;
              widget.onDragging?.call(false);
              // Keep showing the dragged value until the PC echoes it back.
              _release = Timer(const Duration(milliseconds: 1500), () {
                if (mounted) setState(() => _local = null);
              });
            },
          ),
        ),
        const SizedBox(width: 12),
        GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: widget.onLabelTap,
          child: ConstrainedBox(
            constraints: BoxConstraints(minWidth: widget.small ? 42 : 48),
            child: Text(
              label,
              textAlign: TextAlign.right,
              style: TextStyle(
                fontSize: widget.small ? 14 : 20,
                fontWeight: widget.small ? FontWeight.w600 : FontWeight.w700,
                color: muted ? p.secondary : p.text,
                fontFeatures: const [FontFeature.tabularFigures()],
              ),
            ),
          ),
        ),
      ],
    );
  }
}
