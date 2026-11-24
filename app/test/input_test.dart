import 'package:flutter_test/flutter_test.dart';
import 'package:wiremote/src/input.dart';
import 'package:wiremote/src/throttle.dart';

void main() {
  group('TouchpadGestures', () {
    late List<List<Object?>> sent;
    late bool leftHeld;
    late TouchpadGestures pad;
    final t0 = DateTime(2026);
    DateTime at(int ms) => t0.add(Duration(milliseconds: ms));

    setUp(() {
      sent = [];
      leftHeld = false;
      pad = TouchpadGestures(
        (c, a) => sent.add([c, a]),
        sensitivity: () => 2.0,
        isLeftHeld: () => leftHeld,
      );
    });

    test('one-finger tap is a left click', () {
      pad.down(1, const Offset(10, 10), at(0));
      pad.up(1, at(100));
      expect(sent, [
        [
          'mouse_click',
          {'button': 'left'},
        ],
      ]);
    });

    test('slow tap is not a click', () {
      pad.down(1, const Offset(10, 10), at(0));
      pad.up(1, at(450));
      expect(sent, isEmpty);
    });

    test('tap while L is held does not click', () {
      leftHeld = true;
      pad.down(1, const Offset(10, 10), at(0));
      pad.up(1, at(50));
      expect(sent, isEmpty);
    });

    test('one-finger drag moves the cursor, scaled by sensitivity', () {
      pad.down(1, const Offset(10, 10), at(0));
      pad.move(1, const Offset(13, 8));
      pad.move(1, const Offset(20, 8));
      pad.up(1, at(100));
      expect(sent, [
        [
          'mouse_move',
          {'dx': 6.0, 'dy': -4.0},
        ],
        [
          'mouse_move',
          {'dx': 14.0, 'dy': 0.0},
        ],
      ]);
    });

    test('two-finger tap is a right click, three is middle', () {
      pad.down(1, const Offset(10, 10), at(0));
      pad.down(2, const Offset(40, 10), at(20));
      pad.up(1, at(100));
      pad.up(2, at(110));
      pad.down(1, const Offset(10, 10), at(1000));
      pad.down(2, const Offset(40, 10), at(1010));
      pad.down(3, const Offset(70, 10), at(1020));
      pad.up(3, at(1100));
      pad.up(2, at(1100));
      pad.up(1, at(1100));
      expect(sent, [
        [
          'mouse_click',
          {'button': 'right'},
        ],
        [
          'mouse_click',
          {'button': 'middle'},
        ],
      ]);
    });

    test('two-finger drag scrolls; lifting one finger resumes moving', () {
      pad.down(1, const Offset(10, 100), at(0));
      pad.down(2, const Offset(40, 100), at(10));
      pad.move(1, const Offset(10, 110)); // dy 10 * 3
      pad.move(2, const Offset(40, 110)); // second finger is ignored
      pad.up(2, at(200));
      pad.move(1, const Offset(15, 110));
      expect(sent, [
        [
          'mouse_scroll',
          {'dy': 30.0},
        ],
        [
          'mouse_move',
          {'dx': 10.0, 'dy': 0.0},
        ],
      ]);
    });

    test('cancelled pointer does not click', () {
      pad.down(1, const Offset(10, 10), at(0));
      pad.cancel(1, at(50));
      expect(sent, isEmpty);
    });
  });

  group('diffKeyboardText', () {
    test('typed characters after the sentinel', () {
      final e = diffKeyboardText(' ', ' hé');
      expect(e.backspaces, 0);
      expect(e.inserted, 'hé');
    });

    test('backspace deletes the sentinel', () {
      final e = diffKeyboardText(' ', '');
      expect(e.backspaces, 1);
      expect(e.inserted, '');
    });

    test('typing a space', () {
      expect(diffKeyboardText(' ', '  ').inserted, ' ');
    });

    test('text inserted before the sentinel', () {
      final e = diffKeyboardText(' ', 'a ');
      expect(e.backspaces, 0);
      expect(e.inserted, 'a');
    });

    test('autocorrect replacing the content', () {
      final e = diffKeyboardText(' ', 'Hi');
      expect(e.backspaces, 1);
      expect(e.inserted, 'Hi');
    });

    test('emoji', () {
      expect(diffKeyboardText(' ', ' 😀').inserted, '😀');
    });
  });

  group('Throttle', () {
    test('first call at once, burst collapses to the last value', () async {
      final got = <int>[];
      final t = Throttle<int>(const Duration(milliseconds: 80), got.add);
      t(1);
      t(2);
      t(3);
      expect(got, [1]);
      await Future<void>.delayed(const Duration(milliseconds: 160));
      expect(got, [1, 3]);
      t.dispose();
    });

    test('flush sends the pending value now', () {
      final got = <int>[];
      final t = Throttle<int>(const Duration(seconds: 10), got.add);
      t(1);
      t(2);
      t.flush();
      expect(got, [1, 2]);
      t.dispose();
    });

    test('dispose drops the pending value', () async {
      final got = <int>[];
      final t = Throttle<int>(const Duration(milliseconds: 30), got.add);
      t(1);
      t(2);
      t.dispose();
      await Future<void>.delayed(const Duration(milliseconds: 60));
      expect(got, [1]);
    });
  });
}
