import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:wiremote/src/protocol.dart';

Map<String, Object?> j(String s) =>
    (jsonDecode(s) as Map).cast<String, Object?>();

void main() {
  group('ServerAddress.parse', () {
    test('full link printed by the server', () {
      final a = ServerAddress.parse(
        'http://192.168.1.50:8765/?token=a8b3c4d5e6f70812',
      )!;
      expect(a.host, '192.168.1.50');
      expect(a.token, 'a8b3c4d5e6f70812');
    });

    test('bare host, host with port, hostname', () {
      expect(ServerAddress.parse('  10.0.0.7 ')!.host, '10.0.0.7');
      expect(ServerAddress.parse('10.0.0.7:8765')!.host, '10.0.0.7');
      expect(ServerAddress.parse('10.0.0.7')!.token, isNull);
      expect(ServerAddress.parse('mypc.local')!.host, 'mypc.local');
    });

    test('rejects empty and garbage', () {
      expect(ServerAddress.parse(''), isNull);
      expect(ServerAddress.parse('   '), isNull);
      expect(ServerAddress.parse('http://'), isNull);
      expect(ServerAddress.parse('my pc'), isNull);
    });

    test('URIs built from a host', () {
      expect(webSocketUri('192.168.1.50').toString(), 'ws://192.168.1.50:8766');
      expect(webSocketUri('fe80::1').toString(), 'ws://[fe80::1]:8766');
      expect(
        httpUri('192.168.1.50', '/thumbnail?ts=42').toString(),
        'http://192.168.1.50:8765/thumbnail?ts=42',
      );
    });
  });

  // Payloads below are copied from what the C# services serialize.
  group('server messages', () {
    test('volume', () {
      final v = VolumeState.fromJson(
        j('{"type":"volume","value":37,"muted":true}'),
      );
      expect(v.value, 37);
      expect(v.muted, isTrue);
    });

    test('devices and sessions', () {
      final d = parseDevices(
        j(
          '{"type":"devices","devices":[{"id":"alsa_output.pci","name":"Speakers","isDefault":true}]}',
        ),
      );
      expect(d.single.id, 'alsa_output.pci');
      expect(d.single.isDefault, isTrue);

      final s = parseSessions(
        j(
          '{"type":"sessions","sessions":[{"id":4242,"name":"Firefox","volume":80,"muted":false}]}',
        ),
      );
      expect(s.single.id, 4242);
      expect(s.single.volume, 80);
    });

    test('displays (camelCase from DisplayInfo)', () {
      final ds = DisplaysState.fromJson(
        j(
          '{"type":"displays","displays":['
          '{"id":"DP-1","name":"Dell","isActive":true,"isPrimary":true,"x":0,"y":0},'
          '{"id":"HDMI-A-1","name":"","isActive":false,"isPrimary":false,"x":0,"y":0}'
          '],"mode":"extend"}',
        ),
      );
      expect(ds.displays, hasLength(2));
      expect(ds.active.single.id, 'DP-1');
      expect(ds.displays[1].label, 'HDMI-A-1');
      expect(ds.mode, 'extend');
    });

    test('brightness', () {
      final b = BrightnessState.fromJson(
        j('{"type":"brightness","supported":false,"value":0}'),
      );
      expect(b.supported, isFalse);
    });

    test('now playing', () {
      final np = NowPlaying.fromJson(
        j(
          '{"type":"nowplaying","playing":true,"title":"Song",'
          '"artist":"Band","status":"paused","thumb":"/thumbnail?ts=1"}',
        ),
      );
      expect(np.active, isTrue);
      expect(np.isPlaying, isFalse);
      expect(np.thumb, '/thumbnail?ts=1');
      expect(
        NowPlaying.fromJson(j('{"type":"nowplaying","playing":false}')).active,
        isFalse,
      );
      expect(
        NowPlaying.fromJson(
          j('{"type":"nowplaying","playing":true,"title":"x","thumb":null}'),
        ).thumb,
        isNull,
      );
    });

    test('power timer', () {
      final now = DateTime(2026, 1, 1, 12);
      final t = PowerTimer.fromJson(
        j(
          '{"type":"timer","targetMs":1,"remaining":90,"action":"restart_bios"}',
        ),
        now,
      )!;
      expect(t.deadline, now.add(const Duration(seconds: 90)));
      expect(powerActionLabel(t.action), 'Restart to BIOS');
      expect(
        PowerTimer.fromJson(
          j(
            '{"type":"timer","targetMs":-1,"remaining":-1,"action":"cancelled"}',
          ),
          now,
        ),
        isNull,
      );
    });
  });
}
