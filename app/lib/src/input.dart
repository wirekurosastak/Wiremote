import 'dart:ui' show Offset;

typedef SendCommand = void Function(String command, Map<String, Object?> args);

/// Touchpad gestures, ported from the web UI:
/// - 1 finger: move the cursor (scaled by sensitivity); quick tap = left click
/// - 2 fingers: scroll; quick tap = right click
/// - 3 fingers: quick tap = middle click
class TouchpadGestures {
  TouchpadGestures(
    this.send, {
    required this.sensitivity,
    required this.isLeftHeld,
  });

  final SendCommand send;
  final double Function() sensitivity;

  /// True while the L button is held (a drag); suppresses the tap click.
  final bool Function() isLeftHeld;

  static const tapMaxDuration = Duration(milliseconds: 400);
  static const tapSlop = 5.0;
  static const scrollFactor = 3.0;

  /// Active pointers in the order they went down.
  final Map<int, Offset> _pointers = {};
  int _mode = 0;
  int _tapFingers = 0;
  bool _dragged = false;
  DateTime _startTime = DateTime.fromMillisecondsSinceEpoch(0);
  Offset? _start;
  Offset? _last;
  double? _lastScrollY;

  Offset get _primary => _pointers.values.first;

  void down(int pointer, Offset pos, DateTime now) {
    _pointers[pointer] = pos;
    final fingers = _pointers.length;
    if (fingers > _tapFingers) {
      _tapFingers = fingers;
      _startTime = now;
    }
    if (fingers == 1) {
      _mode = 1;
      _start = _last = pos;
      _dragged = false;
    } else if (fingers == 2) {
      _mode = 2;
      _start = _primary;
      _lastScrollY = _primary.dy;
    } else {
      _mode = 3;
      _start = _primary;
    }
  }

  void move(int pointer, Offset pos) {
    if (!_pointers.containsKey(pointer)) return;
    _pointers[pointer] = pos;
    // Like the web version, only the first finger's movement counts.
    if (pointer != _pointers.keys.first) return;
    final start = _start;
    if (start != null &&
        ((pos.dx - start.dx).abs() > tapSlop ||
            (pos.dy - start.dy).abs() > tapSlop)) {
      _dragged = true;
    }
    if (_mode == 1) {
      final last = _last;
      if (last != null) {
        final s = sensitivity();
        send('mouse_move', {
          'dx': (pos.dx - last.dx) * s,
          'dy': (pos.dy - last.dy) * s,
        });
      }
      _last = pos;
    } else if (_mode == 2 && _pointers.length >= 2) {
      final lastY = _lastScrollY;
      if (lastY != null) {
        final dy = (pos.dy - lastY) * scrollFactor;
        if (dy.abs() > 1) {
          send('mouse_scroll', {'dy': dy});
          _lastScrollY = pos.dy;
        }
      }
    }
  }

  void up(int pointer, DateTime now) =>
      _release(pointer, now, cancelled: false);

  void cancel(int pointer, DateTime now) =>
      _release(pointer, now, cancelled: true);

  void _release(int pointer, DateTime now, {required bool cancelled}) {
    if (_pointers.remove(pointer) == null) return;
    if (_pointers.isEmpty) {
      if (!cancelled &&
          !_dragged &&
          now.difference(_startTime) < tapMaxDuration) {
        if (_tapFingers == 1 && !isLeftHeld()) {
          send('mouse_click', {'button': 'left'});
        } else if (_tapFingers == 2) {
          send('mouse_click', {'button': 'right'});
        } else if (_tapFingers == 3) {
          send('mouse_click', {'button': 'middle'});
        }
      }
      _mode = 0;
      _tapFingers = 0;
      _start = _last = null;
      _lastScrollY = null;
    } else if (_dragged && _pointers.length == 1) {
      // Lifted the second finger mid-gesture: carry on moving with the first.
      _mode = 1;
      _last = _primary;
      _lastScrollY = null;
    }
  }
}

/// What the user typed into the hidden keyboard field since the last reset.
class KeyboardEdit {
  const KeyboardEdit(this.backspaces, this.inserted);
  final int backspaces;
  final String inserted;

  bool get isEmpty => backspaces == 0 && inserted.isEmpty;
}

/// The hidden field holds a single space so that backspace is still a change
/// the keyboard reports. After each edit the field is reset to it.
const keyboardSentinel = ' ';

KeyboardEdit diffKeyboardText(String before, String after) {
  var prefix = 0;
  final maxPrefix = before.length < after.length ? before.length : after.length;
  while (prefix < maxPrefix &&
      before.codeUnitAt(prefix) == after.codeUnitAt(prefix)) {
    prefix++;
  }
  var suffix = 0;
  while (suffix < before.length - prefix &&
      suffix < after.length - prefix &&
      before.codeUnitAt(before.length - 1 - suffix) ==
          after.codeUnitAt(after.length - 1 - suffix)) {
    suffix++;
  }
  return KeyboardEdit(
    before.length - prefix - suffix,
    after.substring(prefix, after.length - suffix),
  );
}
