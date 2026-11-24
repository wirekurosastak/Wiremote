import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../input.dart';
import '../theme.dart';
import '../widgets.dart';

/// Claims every pointer at once, so dragging on the touchpad never scrolls the page.
final _eager = <Type, GestureRecognizerFactory>{
  EagerGestureRecognizer:
      GestureRecognizerFactoryWithHandlers<EagerGestureRecognizer>(
        EagerGestureRecognizer.new,
        (_) {},
      ),
};

class ControlsSection extends StatefulWidget {
  const ControlsSection({super.key});

  @override
  State<ControlsSection> createState() => _ControlsSectionState();
}

class _ControlsSectionState extends State<ControlsSection> {
  bool _leftHeld = false;
  double? _sensitivityDrag;
  late final TouchpadGestures _pad = TouchpadGestures(
    _send,
    sensitivity: () =>
        _sensitivityDrag ?? AppScope.read(context).settings.sensitivity,
    isLeftHeld: () => _leftHeld,
  );
  final _kb = TextEditingController(text: keyboardSentinel);
  final _kbFocus = FocusNode(debugLabel: 'remote keyboard');

  void _send(String command, Map<String, Object?> args) =>
      AppScope.read(context).client.command(command, args);

  @override
  void dispose() {
    _kb.dispose();
    _kbFocus.dispose();
    super.dispose();
  }

  void _openKeyboard() {
    if (_kbFocus.hasFocus) {
      SystemChannels.textInput.invokeMethod<void>('TextInput.show');
    } else {
      _kbFocus.requestFocus();
    }
  }

  void _onTyped(String text) {
    final edit = diffKeyboardText(keyboardSentinel, text);
    for (var i = 0; i < edit.backspaces; i++) {
      _send('key_press', {'key': 'backspace'});
    }
    final lines = edit.inserted.split('\n');
    for (var i = 0; i < lines.length; i++) {
      if (i > 0) _send('key_press', {'key': 'enter'});
      if (lines[i].isNotEmpty) _send('type_text', {'text': lines[i]});
    }
    _kb.value = const TextEditingValue(
      text: keyboardSentinel,
      selection: TextSelection.collapsed(offset: keyboardSentinel.length),
    );
  }

  @override
  Widget build(BuildContext context) {
    final settings = context.scope.settings;
    final sensitivity = _sensitivityDrag ?? settings.sensitivity;
    return SectionCard(
      header: const SectionTitle('Controls'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          AspectRatio(aspectRatio: 1, child: _touchpad(context)),
          const SubTitle(
            'Sensitivity',
            padding: EdgeInsets.only(top: 30, bottom: 5),
          ),
          Row(
            children: [
              Expanded(
                child: WmSlider(
                  value: sensitivity,
                  min: 0,
                  max: 10,
                  divisions: 100,
                  onChanged: (v) => setState(() => _sensitivityDrag = v),
                  onChangeEnd: (v) {
                    settings.sensitivity = v;
                    setState(() => _sensitivityDrag = null);
                  },
                ),
              ),
              const SizedBox(width: 12),
              ConstrainedBox(
                constraints: const BoxConstraints(minWidth: 48),
                child: Text(
                  '${sensitivity.toStringAsFixed(1)}x',
                  textAlign: TextAlign.right,
                  style: const TextStyle(
                    fontSize: 20,
                    fontWeight: FontWeight.w700,
                    fontFeatures: [FontFeature.tabularFigures()],
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 20),
          Stack(
            children: [
              WmButton(
                accent: true,
                fontWeight: FontWeight.bold,
                onTap: _openKeyboard,
                child: const Text('Keyboard'),
              ),
              Positioned(left: 0, top: 0, child: _hiddenField()),
            ],
          ),
        ],
      ),
    );
  }

  /// Invisible text field that receives the phone keyboard's input.
  Widget _hiddenField() => IgnorePointer(
    child: Opacity(
      opacity: 0,
      child: SizedBox(
        width: 1,
        height: 1,
        child: OverflowBox(
          alignment: Alignment.topLeft,
          maxWidth: 200,
          maxHeight: 60,
          child: TextField(
            controller: _kb,
            focusNode: _kbFocus,
            autocorrect: false,
            enableSuggestions: false,
            enableIMEPersonalizedLearning: false,
            showCursor: false,
            keyboardType: TextInputType.text,
            textInputAction: TextInputAction.send,
            decoration: const InputDecoration.collapsed(hintText: ''),
            onChanged: _onTyped,
            // Enter key: send it and keep the keyboard open.
            onEditingComplete: () => _send('key_press', {'key': 'enter'}),
            // Keep focus while using the touchpad alongside the keyboard.
            onTapOutside: (_) {},
          ),
        ),
      ),
    ),
  );

  Widget _touchpad(BuildContext context) {
    final p = context.palette;
    DateTime now() => DateTime.now();
    return Container(
      decoration: BoxDecoration(
        color: p.padBg,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: p.padBorder),
      ),
      child: Stack(
        children: [
          Positioned.fill(
            child: RawGestureDetector(
              gestures: _eager,
              child: Listener(
                behavior: HitTestBehavior.opaque,
                onPointerDown: (e) =>
                    _pad.down(e.pointer, e.localPosition, now()),
                onPointerMove: (e) => _pad.move(e.pointer, e.localPosition),
                onPointerUp: (e) => _pad.up(e.pointer, now()),
                onPointerCancel: (e) => _pad.cancel(e.pointer, now()),
                child: Center(
                  child: Text(
                    'TOUCHPAD',
                    style: TextStyle(
                      color: p.padText,
                      fontWeight: FontWeight.w600,
                      fontSize: 14,
                      letterSpacing: 2,
                    ),
                  ),
                ),
              ),
            ),
          ),
          Positioned(
            left: 15,
            right: 15,
            bottom: 15,
            child: Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                _PadButton(
                  label: 'L',
                  onDown: (_) {
                    _leftHeld = true;
                    HapticFeedback.mediumImpact();
                    _send('mouse_down', {'button': 'left'});
                  },
                  onUp: (_, _) => _releaseLeft(),
                  onCancel: _releaseLeft,
                ),
                _MiddleButton(send: _send),
                _PadButton(
                  label: 'R',
                  onUp: (_, moved) {
                    if (!moved) _send('mouse_click', {'button': 'right'});
                  },
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  void _releaseLeft() {
    if (!_leftHeld) return;
    _leftHeld = false;
    _send('mouse_up', {'button': 'left'});
  }
}

/// M: tap for a middle click, or drag up/down on it to scroll.
class _MiddleButton extends StatefulWidget {
  const _MiddleButton({required this.send});
  final SendCommand send;

  @override
  State<_MiddleButton> createState() => _MiddleButtonState();
}

class _MiddleButtonState extends State<_MiddleButton> {
  double? _startY;
  double? _lastY;
  bool _scrolled = false;

  @override
  Widget build(BuildContext context) => _PadButton(
    label: 'M',
    onDown: (pos) {
      _startY = _lastY = pos.dy;
      _scrolled = false;
      HapticFeedback.lightImpact();
    },
    onMove: (pos) {
      final start = _startY;
      final last = _lastY;
      if (start == null || last == null) return;
      if ((pos.dy - start).abs() > 5) _scrolled = true;
      final dy = pos.dy - last;
      if (dy.abs() >= 1) {
        widget.send('mouse_scroll', {'dy': dy * 3.0});
        _lastY = pos.dy;
      }
    },
    onUp: (_, _) {
      if (!_scrolled) widget.send('mouse_click', {'button': 'middle'});
      _startY = _lastY = null;
    },
    onCancel: () => _startY = _lastY = null,
  );
}

/// Round L/M/R button on the touchpad. Tracks one finger with raw pointer events.
class _PadButton extends StatefulWidget {
  const _PadButton({
    required this.label,
    this.onDown,
    this.onMove,
    this.onUp,
    this.onCancel,
  });

  final String label;
  final void Function(Offset pos)? onDown;
  final void Function(Offset pos)? onMove;

  /// [moved] is true when the finger travelled more than 8 px.
  final void Function(Offset pos, bool moved)? onUp;
  final VoidCallback? onCancel;

  @override
  State<_PadButton> createState() => _PadButtonState();
}

class _PadButtonState extends State<_PadButton> {
  int? _pointer;
  Offset _start = Offset.zero;
  bool _moved = false;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final active = _pointer != null;
    return RawGestureDetector(
      gestures: _eager,
      child: Listener(
        onPointerDown: (e) {
          if (_pointer != null) return;
          setState(() => _pointer = e.pointer);
          _start = e.localPosition;
          _moved = false;
          widget.onDown?.call(e.localPosition);
        },
        onPointerMove: (e) {
          if (e.pointer != _pointer) return;
          if ((e.localPosition - _start).distance > 8) _moved = true;
          widget.onMove?.call(e.localPosition);
        },
        onPointerUp: (e) {
          if (e.pointer != _pointer) return;
          setState(() => _pointer = null);
          widget.onUp?.call(e.localPosition, _moved);
        },
        onPointerCancel: (e) {
          if (e.pointer != _pointer) return;
          setState(() => _pointer = null);
          widget.onCancel?.call();
        },
        child: AnimatedScale(
          scale: active ? 0.95 : 1,
          duration: const Duration(milliseconds: 100),
          child: Container(
            width: 48,
            height: 48,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: active ? p.padButtonActive : p.padButton,
              border: Border.all(color: p.padButtonBorder),
              boxShadow: p.light
                  ? const [
                      BoxShadow(
                        color: Color(0x14000000),
                        blurRadius: 6,
                        offset: Offset(0, 2),
                      ),
                    ]
                  : null,
            ),
            child: Text(
              widget.label,
              style: TextStyle(
                fontSize: 16,
                fontWeight: FontWeight.w700,
                color: active ? p.padButtonActiveText : p.padButtonText,
              ),
            ),
          ),
        ),
      ),
    );
  }
}
