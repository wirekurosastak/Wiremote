# Wiremote

A fast, pragmatic, and lightweight PC remote-control web UI for Windows and Linux. Designed primarily for controlling media playback and PC functions from a smartphone.

## Features

- **Media Control**: Play/Pause, Next/Previous Track, and seeking (+15s / -15s). Shows current playing media art (via Windows GSMTC or Linux MPRIS D-Bus).
- **Audio Control**: Change master volume, mute, and change active output device. Provides a per-app volume mixer (via Windows Wasapi or Linux PipeWire/PulseAudio).
- **Display Management**: Switch primary displays, adjust brightness, toggle display modes (Clone/Extend), and turn off displays (via Windows User32 or Linux kscreen-doctor / xrandr).
- **Input & Keyboard**: Acts as a remote touchpad and keyboard. Includes left/right mouse clicks and full scrolling support (via Windows SendInput or Linux XTest / XWayland).
- **Power**: Remotely shutdown, restart, sleep, or hibernate the PC. Supports delayed timers and cancellations (via systemctl on Linux or shutdown.exe on Windows).

## Phone App (Flutter)

The `app/` folder holds a native Flutter app for Android and iOS. It has the same features and look as the web UI, and talks to the same server over the same WebSocket protocol, so the server needs no changes.

- **Pairing**: on first launch, paste the link the server prints (`http://<ip>:8765/?token=<TOKEN>`), or type the PC's IP address and token.
- **Connection**: connects while the app is on screen, drops the socket when it goes to the background, and reconnects instantly when it comes back.
- **Build for Android**: `cd app && flutter build apk --release`, then install `app/build/app/outputs/flutter-apk/app-release.apk`. Add `--split-per-abi` for smaller per-device APKs.
- **iOS**: build from macOS with Xcode (`flutter build ios`).
- **Tests**: `cd app && flutter test`.

The web UI is still served on port 8765, so a browser keeps working without the app.

## Architecture

This tool was designed with a "KISS" (Keep It Simple, Stupid) philosophy in mind:
- **Zero-Dependency Web Frontend**: The web UI is built with vanilla JavaScript, HTML, and CSS. No Node.js, no Webpack, no React. Fast to load, easy to read.
- **Native Phone App**: The Flutter app in `app/` uses only two plugins (settings storage and keep-screen-awake).
- **Embedded Static Assets**: All frontend files are embedded into the C# binary. The application compiles to a single executable.
- **Direct OS Integration**: Pragmatic platform implementations for Windows and Linux without heavy third-party framework overhead.

## Security & Token Authentication

Wiremote includes **Token-Based Authentication & Session Protection**:
- On startup, a secure persistent authentication token is generated (stored locally in `wiremote.token`).
- The terminal prints a direct link with your token: `http://<ip>:8765/?token=<TOKEN>`.
- When opened on your phone, the token is automatically saved into the browser's `localStorage` and stripped from the address bar.
- Any unauthenticated client on the network is blocked from sending mouse, keyboard, audio, or power commands.
- **Single Active Controller**: Reconnecting cleanly takes over the session and prunes stale zombie connections.

## Getting Started

1. **Prerequisites**: Ensure you have the .NET SDK (10.0+) installed.
2. **First Launch**:
   - **Windows**: Run the application as **Administrator** the first time you launch it to automatically configure Windows Firewall (`netsh`) for incoming connections on ports 8765 and 8766.
   - **Linux**: Run normally (`dotnet run` or launch binary). If you have an active firewall (such as `firewalld` or `ufw`), ensure ports 8765 and 8766 are allowed:
     - `sudo firewall-cmd --add-port=8765/tcp --add-port=8766/tcp --permanent && sudo firewall-cmd --reload`
     - or `sudo ufw allow 8765/tcp && sudo ufw allow 8766/tcp`
   - **Linux (Automatic Build & System Startup / Autostart)**:
     - Simply run `./setup.sh` in the repository root. It compiles a Release publish build, configures a systemd user service with autostart on system boot (linger), creates a `~/.local/bin/wiremote` command link, and starts the service.
3. **Usage**:
   - Run the application (`dotnet run` or launch the compiled executable).
   - The console will display a URL with your token (e.g., `http://192.168.1.50:8765/?token=a8b3c4d5...`).
   - Open that URL in your smartphone's web browser once to pair it automatically.
   - Includes **Instant Reconnect**, an **Offline Adjustment Buffer** (adjust volume even while reconnecting without dropped commands), and **Keep Screen Awake** support.
   - For the best experience on iOS/Android, you can "Add to Home Screen" to run it in a full-screen, app-like mode.

## Development

If you'd like to tweak the UI, edit the files in the `wwwroot` directory. Since they are marked as embedded resources in the `.csproj`, any changes you make will be included the next time you compile the application.

## License

MIT License. See the [LICENSE](LICENSE) file for more information.
