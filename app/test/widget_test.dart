import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wiremote/main.dart';
import 'package:wiremote/src/client.dart';
import 'package:wiremote/src/protocol.dart';
import 'package:wiremote/src/settings.dart';

/// Records commands instead of opening a socket.
class FakeClient extends WiremoteClient {
  FakeClient(super.settings);

  final sent = <List<Object?>>[];
  int reconnects = 0;

  @override
  void start() {}

  @override
  void stop() {}

  @override
  void reconnect() => reconnects++;

  @override
  void command(String name, [Map<String, Object?> args = const {}]) =>
      sent.add([name, args]);
}

void main() {
  late Settings settings;
  late FakeClient client;

  Future<void> pumpApp(
    WidgetTester tester, {
    Map<String, Object?> prefs = const {},
  }) async {
    tester.view.physicalSize = const Size(430 * 3, 932 * 3);
    tester.view.devicePixelRatio = 3;
    addTearDown(tester.view.reset);
    // No animated border: keeps the frame clock idle so pumpAndSettle can finish.
    settings = Settings.memory({'borderVisibility': 'off', ...prefs});
    client = FakeClient(settings);
    await tester.pumpWidget(WiremoteApp(settings: settings, client: client));
    await tester.pump();
  }

  testWidgets(
    'first run asks for the PC; pasting the link fills host and token',
    (tester) async {
      await pumpApp(tester);
      expect(find.text('Connect'), findsOneWidget);

      await tester.enterText(
        find.byType(TextField).first,
        'http://192.168.1.50:8765/?token=abc123',
      );
      await tester.tap(find.text('Connect'));
      await tester.pumpAndSettle();

      expect(settings.host, '192.168.1.50');
      expect(settings.token, 'abc123');
      expect(client.reconnects, 1);
      expect(find.text('MEDIA'), findsOneWidget);
    },
  );

  testWidgets('bad address shows an error', (tester) async {
    await pumpApp(tester);
    await tester.enterText(find.byType(TextField).first, 'my pc');
    await tester.tap(find.text('Connect'));
    await tester.pump();
    expect(find.text("Enter the link or the PC's IP address."), findsOneWidget);
    expect(settings.host, isNull);
  });

  testWidgets('shows PC state and sends media and volume commands', (
    tester,
  ) async {
    await pumpApp(tester, prefs: {'host': '10.0.0.2'});
    expect(find.text('Offline'), findsOneWidget);
    expect(find.text('Nothing there...'), findsOneWidget);

    client.status.value = ConnectionStatus.online;
    client.volume.value = const VolumeState(42, false);
    client.nowPlaying.value = const NowPlaying(
      active: true,
      title: 'Song',
      artist: 'Band',
      isPlaying: true,
    );
    client.devices.value = const [
      AudioDevice('a', 'Speakers', true),
      AudioDevice('b', 'Headset', false),
    ];
    await tester.pump();

    expect(find.text('Online'), findsOneWidget);
    expect(find.text('42%'), findsOneWidget);
    expect(find.text('Song'), findsOneWidget);
    expect(find.text('Playing'), findsOneWidget);

    await tester.tap(find.byIcon(Icons.pause));
    await tester.tap(find.byIcon(Icons.skip_next));
    await tester.tap(find.text('42%'));
    await tester.pump(const Duration(milliseconds: 200));
    expect(client.sent, [
      ['media_play_pause', <String, Object?>{}],
      ['media_next', <String, Object?>{}],
      ['mute', <String, Object?>{}],
    ]);

    // Output list: tapping the inactive device switches to it.
    client.sent.clear();
    await tester.tap(find.text('OUTPUT').first);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Headset'));
    await tester.tap(find.text('Speakers'));
    await tester.pump(const Duration(milliseconds: 200));
    expect(client.sent, [
      [
        'set_device',
        {'id': 'b'},
      ],
    ]);
  });

  testWidgets('dragging the master slider sends throttled set_volume', (
    tester,
  ) async {
    await pumpApp(tester, prefs: {'host': '10.0.0.2'});
    client.volume.value = const VolumeState(50, false);
    await tester.pump();

    final slider = find.byType(Slider).first;
    await tester.drag(slider, const Offset(-400, 0));
    await tester.pump(const Duration(milliseconds: 200));
    final volumes = client.sent.where((c) => c[0] == 'set_volume').toList();
    expect(volumes, isNotEmpty);
    expect(volumes.last, [
      'set_volume',
      {'value': 0},
    ]);
    expect(find.text('0%'), findsOneWidget);

    // The PC echoes the new value back; the label follows it.
    client.volume.value = const VolumeState(0, false);
    await tester.pump(const Duration(seconds: 2));
    expect(find.text('0%'), findsOneWidget);
  });

  testWidgets('touchpad tap clicks and drag moves the cursor', (tester) async {
    await pumpApp(tester, prefs: {'host': '10.0.0.2'});
    await tester.tap(find.text('CONTROLS'));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.text('TOUCHPAD'));
    await tester.pumpAndSettle();

    final pad = tester.getCenter(find.text('TOUCHPAD'));
    await tester.tapAt(pad);
    await tester.pump();
    expect(client.sent.last, [
      'mouse_click',
      {'button': 'left'},
    ]);

    client.sent.clear();
    final gesture = await tester.startGesture(pad);
    await gesture.moveBy(const Offset(10, 0));
    await gesture.moveBy(const Offset(10, 4));
    await gesture.up();
    await tester.pump();
    // Default sensitivity is 5x. No click after a drag.
    expect(client.sent, [
      [
        'mouse_move',
        {'dx': 50.0, 'dy': 0.0},
      ],
      [
        'mouse_move',
        {'dx': 50.0, 'dy': 20.0},
      ],
    ]);

    client.sent.clear();
    await tester.tap(find.text('R'));
    await tester.pump();
    expect(client.sent, [
      [
        'mouse_click',
        {'button': 'right'},
      ],
    ]);
  });

  testWidgets('power buttons send the delay, and lock while a timer runs', (
    tester,
  ) async {
    await pumpApp(tester, prefs: {'host': '10.0.0.2'});
    await tester.tap(find.text('POWER'));
    await tester.pumpAndSettle();

    final fields = find.byType(TextField);
    await tester.enterText(fields.at(1), '2'); // minutes
    await tester.ensureVisible(find.text('Restart'));
    await tester.tap(find.text('Restart'));
    await tester.pump(const Duration(milliseconds: 200));
    expect(client.sent.last, [
      'power',
      {'action': 'restart', 'seconds': 120},
    ]);

    client.timer.value = PowerTimer(
      'restart',
      DateTime.now().add(const Duration(seconds: 120)),
    );
    await tester.pump();
    expect(find.textContaining('Restart in 00:0'), findsOneWidget);

    client.sent.clear();
    await tester.tap(find.text('Sleep'));
    await tester.tap(find.text('Cancel'));
    await tester.pump(const Duration(milliseconds: 200));
    expect(client.sent, [
      [
        'power',
        {'action': 'cancel', 'seconds': 0},
      ],
    ]);

    client.timer.value = null;
    await tester.pump();
  });

  testWidgets('rejected token shows the auth prompt and saves the new token', (
    tester,
  ) async {
    await pumpApp(tester, prefs: {'host': '10.0.0.2', 'token': 'old'});
    client.authError.value = 'Invalid or missing token';
    await tester.pump();
    expect(find.text('Wiremote Auth'), findsOneWidget);

    await tester.enterText(find.byType(TextField).last, 'newtoken');
    await tester.tap(find.text('Authorize'));
    await tester.pump();
    expect(settings.token, 'newtoken');
    expect(client.reconnects, 1);
  });

  testWidgets('theme and accent settings apply and persist', (tester) async {
    await pumpApp(tester, prefs: {'host': '10.0.0.2'});
    await tester.ensureVisible(find.text('MISCELLANEOUS'));
    await tester.tap(find.text('MISCELLANEOUS'));
    await tester.pumpAndSettle();

    await tester.ensureVisible(find.text('THEME'));
    await tester.tap(find.text('THEME'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Dark'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Light').last);
    await tester.pumpAndSettle();
    expect(settings.lightTheme, isTrue);
    expect(
      tester.widget<Scaffold>(find.byType(Scaffold)).backgroundColor ??
          Theme.of(tester.element(find.byType(Scaffold)))
              .scaffoldBackgroundColor,
      const Color(0xFFCBCFD8),
    );
  });
}
