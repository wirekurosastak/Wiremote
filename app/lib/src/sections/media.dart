import 'dart:async';

import 'package:flutter/material.dart';

import '../protocol.dart';
import '../theme.dart';
import '../widgets.dart';

class MediaSection extends StatelessWidget {
  const MediaSection({super.key});

  @override
  Widget build(BuildContext context) {
    final client = context.scope.client;
    return SectionCard(
      initiallyOpen: true,
      header: const SectionTitle('Media'),
      child: ValueListenableBuilder(
        valueListenable: client.nowPlaying,
        builder: (context, np, _) => Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            _NowPlaying(np),
            const CardDivider(),
            _Controls(isPlaying: np.active && np.isPlaying),
          ],
        ),
      ),
    );
  }
}

class _NowPlaying extends StatelessWidget {
  const _NowPlaying(this.np);
  final NowPlaying np;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    if (!np.active) {
      return SizedBox(
        height: 64,
        child: Center(
          child: Text(
            'Nothing there...',
            style: TextStyle(
              color: p.secondary,
              fontWeight: FontWeight.w600,
              fontSize: 15,
            ),
          ),
        ),
      );
    }
    final accent = context.accent;
    return Row(
      children: [
        _Art(np.thumb),
        const SizedBox(width: 14),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Marquee(
                np.title.isEmpty ? 'Unknown' : np.title,
                style: const TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.w700,
                ),
              ),
              if (np.artist.isNotEmpty)
                Marquee(
                  np.artist,
                  style: TextStyle(fontSize: 14, color: p.secondary),
                ),
              const SizedBox(height: 8),
              Row(
                spacing: 4,
                children: [
                  Icon(
                    np.isPlaying ? Icons.play_arrow : Icons.pause,
                    size: 14,
                    color: accent,
                  ),
                  Text(
                    np.isPlaying ? 'Playing' : 'Paused',
                    style: TextStyle(
                      fontSize: 12,
                      fontWeight: FontWeight.w600,
                      color: accent,
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _Art extends StatelessWidget {
  const _Art(this.thumb);
  final String? thumb;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final placeholder = Icon(Icons.music_note, size: 30, color: context.accent);
    final url = thumb == null ? null : context.scope.client.resolve(thumb!);
    return ClipRRect(
      borderRadius: BorderRadius.circular(12),
      child: Container(
        width: 64,
        height: 64,
        color: p.surface2,
        alignment: Alignment.center,
        child: url == null
            ? placeholder
            : Image.network(
                url.toString(),
                width: 64,
                height: 64,
                fit: BoxFit.cover,
                gaplessPlayback: true,
                errorBuilder: (_, _, _) => placeholder,
              ),
      ),
    );
  }
}

class _Controls extends StatelessWidget {
  const _Controls({required this.isPlaying});
  final bool isPlaying;

  @override
  Widget build(BuildContext context) {
    final client = context.scope.client;
    final p = context.palette;
    // Same proportions as the web grid: 1fr 1fr 1.2fr 1fr 1fr, square buttons.
    final buttons = <(int, Widget)>[
      (
        10,
        WmButton(
          height: null,
          onTap: () => client.command('media_previous'),
          child: const Icon(Icons.skip_previous, size: 24),
        ),
      ),
      (
        10,
        _HoldButton(
          label: '−15s',
          background: p.surface3,
          onFire: () => client.command('web_left'),
        ),
      ),
      (
        12,
        WmButton(
          height: null,
          accent: true,
          onTap: () => client.command('media_play_pause'),
          child: Icon(isPlaying ? Icons.pause : Icons.play_arrow, size: 30),
        ),
      ),
      (
        10,
        _HoldButton(
          label: '+15s',
          background: p.surface3,
          onFire: () => client.command('web_right'),
        ),
      ),
      (
        10,
        WmButton(
          height: null,
          onTap: () => client.command('media_next'),
          child: const Icon(Icons.skip_next, size: 24),
        ),
      ),
    ];
    return Row(
      spacing: 8,
      children: [
        for (final (flex, button) in buttons)
          Expanded(
            flex: flex,
            child: AspectRatio(aspectRatio: 1, child: button),
          ),
      ],
    );
  }
}

/// Seek button: fires once on press, then repeats every 150 ms after a 400 ms hold.
class _HoldButton extends StatefulWidget {
  const _HoldButton({
    required this.label,
    required this.onFire,
    required this.background,
  });
  final String label;
  final VoidCallback onFire;
  final Color background;

  @override
  State<_HoldButton> createState() => _HoldButtonState();
}

class _HoldButtonState extends State<_HoldButton> {
  Timer? _hold;
  Timer? _repeat;

  void _start() {
    widget.onFire();
    _hold = Timer(const Duration(milliseconds: 400), () {
      _repeat = Timer.periodic(
        const Duration(milliseconds: 150),
        (_) => widget.onFire(),
      );
    });
  }

  void _stop() {
    _hold?.cancel();
    _repeat?.cancel();
    _hold = _repeat = null;
  }

  @override
  void dispose() {
    _stop();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => WmButton(
    height: null,
    background: widget.background,
    fontSize: 15,
    onPressStart: _start,
    onPressEnd: _stop,
    child: Text(widget.label),
  );
}
