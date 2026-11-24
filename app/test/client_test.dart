import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:wiremote/src/client.dart';
import 'package:wiremote/src/settings.dart';

/// Minimal stand-in for Server.cs: same auth rules and message shapes.
class FakeServer {
  FakeServer._(this._http);

  static Future<FakeServer> start({String token = 'srvtoken'}) async {
    final http = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    final s = FakeServer._(http)..token = token;
    http.transform(WebSocketTransformer()).listen(s._onSocket);
    return s;
  }

  final HttpServer _http;
  late String token;
  final sockets = <WebSocket>[];
  final authed = <WebSocket>{};
  final received = <Map<String, Object?>>[];
  int connections = 0;

  int get port => _http.port;

  void _onSocket(WebSocket ws) {
    connections++;
    sockets.add(ws);
    ws.listen(
      (data) {
        final msg = (jsonDecode(data as String) as Map).cast<String, Object?>();
        final command = msg['command'];
        if (command == 'ping') return ws.add('{"type":"pong"}');
        if (command == 'auth') {
          if (msg['token'] == token || authed.isEmpty) {
            authed.add(ws);
            // Single active controller: kick everyone else.
            for (final other in [...sockets]) {
              if (other != ws) {
                other.close();
                sockets.remove(other);
                authed.remove(other);
              }
            }
            ws.add('{"type":"auth_ok","token":"$token"}');
            ws.add('{"type":"volume","value":42,"muted":false}');
            ws.add(
              '{"type":"devices","devices":[{"id":"sink1","name":"Speakers","isDefault":true}]}',
            );
            ws.add('{"type":"brightness","supported":true,"value":70}');
            ws.add(
              '{"type":"nowplaying","playing":true,"title":"Song","artist":"Band",'
              '"status":"playing","thumb":"/thumbnail?ts=5"}',
            );
          } else {
            ws.add(
              '{"type":"auth_error","message":"Invalid or missing token"}',
            );
          }
          return;
        }
        if (!authed.contains(ws)) return ws.add('{"type":"auth_required"}');
        received.add(msg);
      },
      onDone: () {
        sockets.remove(ws);
        authed.remove(ws);
      },
    );
  }

  Future<void> dropAll() async {
    for (final ws in [...sockets]) {
      await ws.close();
    }
  }

  Future<void> close() async {
    await dropAll();
    await _http.close(force: true);
  }
}

Future<void> until(
  bool Function() cond, [
  Duration timeout = const Duration(seconds: 3),
]) async {
  final end = DateTime.now().add(timeout);
  while (!cond()) {
    if (DateTime.now().isAfter(end)) {
      throw TimeoutException('condition not met');
    }
    await Future<void>.delayed(const Duration(milliseconds: 10));
  }
}

void main() {
  late FakeServer server;
  late Settings settings;
  late WiremoteClient client;

  setUp(() async {
    server = await FakeServer.start();
    settings = Settings.memory({'host': '127.0.0.1'});
    client = WiremoteClient(
      settings,
      wsPortOverride: server.port,
      httpPortOverride: 1234,
    );
  });

  tearDown(() async {
    client.dispose();
    await server.close();
  });

  test(
    'first device pairs without a token and stores the one it gets back',
    () async {
      client.start();
      await until(() => client.status.value == ConnectionStatus.online);
      expect(settings.token, 'srvtoken');
      await until(() => client.nowPlaying.value.active);
      expect(client.volume.value!.value, 42);
      expect(client.devices.value!.single.name, 'Speakers');
      expect(client.brightness.value!.value, 70);
      expect(
        client.resolve(client.nowPlaying.value.thumb!).toString(),
        'http://127.0.0.1:1234/thumbnail?ts=5',
      );
    },
  );

  test('commands reach the server', () async {
    client.start();
    await until(() => client.status.value == ConnectionStatus.online);
    client.command('media_next');
    client.command('set_volume', {'value': 12});
    await until(() => server.received.length == 2);
    expect(server.received, [
      {'command': 'media_next'},
      {'command': 'set_volume', 'value': 12},
    ]);
  });

  test(
    'wrong token while another phone is in control shows the auth prompt',
    () async {
      final other = await WebSocket.connect('ws://127.0.0.1:${server.port}');
      other.add('{"command":"auth","token":"srvtoken"}');
      await until(() => server.authed.length == 1);

      settings.token = 'wrong';
      client.start();
      await until(() => client.authError.value != null);
      expect(client.authError.value, 'Invalid or missing token');
      expect(client.status.value, ConnectionStatus.offline);

      settings.token = 'srvtoken';
      client.reconnect();
      await until(() => client.status.value == ConnectionStatus.online);
      expect(client.authError.value, isNull);
      await other.close();
    },
  );

  test('reconnects after the PC drops the socket, replaying only recent button presses', () async {
    client.start();
    await until(() => client.status.value == ConnectionStatus.online);
    expect(server.connections, 1);

    await server.dropAll();
    await until(() => client.status.value != ConnectionStatus.online);
    // Pressed while offline: the button press is kept, the slider value is not.
    client.command('mute');
    client.command('set_volume', {'value': 99});

    await until(() => client.status.value == ConnectionStatus.online);
    expect(server.connections, 2);
    await until(() => server.received.isNotEmpty);
    await Future<void>.delayed(const Duration(milliseconds: 100));
    expect(server.received, [
      {'command': 'mute'},
    ]);
  });

  test('stop drops the socket and does not reconnect', () async {
    client.start();
    await until(() => client.status.value == ConnectionStatus.online);
    client.stop();
    await until(() => server.sockets.isEmpty);
    await Future<void>.delayed(const Duration(milliseconds: 600));
    expect(server.connections, 1);
    expect(client.status.value, ConnectionStatus.offline);

    client.start();
    await until(() => client.status.value == ConnectionStatus.online);
    expect(server.connections, 2);
  });

  test('keeps retrying while the PC is unreachable', () async {
    final port = server.port;
    await server.close();
    client.start();
    await Future<void>.delayed(const Duration(milliseconds: 400));
    expect(client.status.value, isNot(ConnectionStatus.online));

    server = await _bindOn(port);
    await until(
      () => client.status.value == ConnectionStatus.online,
      const Duration(seconds: 5),
    );
  });
}

Future<FakeServer> _bindOn(int port) async {
  // Re-bind the same port so the client's fixed address works again.
  final http = await HttpServer.bind(InternetAddress.loopbackIPv4, port);
  final s = FakeServer._(http)..token = 'srvtoken';
  http.transform(WebSocketTransformer()).listen(s._onSocket);
  return s;
}
