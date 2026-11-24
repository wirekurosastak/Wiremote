# Wiremote phone app

Flutter client for the Wiremote server in the repository root. See the root `README.md` for setup.

## Layout

- `lib/main.dart`: app entry, lifecycle (connect while visible), keep-screen-awake.
- `lib/src/protocol.dart`: message models for the server's JSON protocol, address parsing.
- `lib/src/client.dart`: WebSocket connection, auth, reconnect, offline button queue.
- `lib/src/settings.dart`: saved preferences (PC address, token, theme, accent, ...).
- `lib/src/theme.dart`: dark/light palettes, accent color, rainbow mode, border animation clock.
- `lib/src/widgets.dart`: shared widgets (cards, buttons, slider, select, marquee).
- `lib/src/input.dart`: touchpad gestures and keyboard diffing (plain Dart, unit-tested).
- `lib/src/sections/`: the five cards (media, audio/display, controls, power, miscellaneous).
- `lib/src/screens.dart`: home, first-run connect screen, token prompt, change-PC dialog.

## Commands

```sh
flutter test                   # unit, client (against a fake server) and widget tests
flutter build apk --release    # Android
flutter run                    # on a connected phone
```
