# Wiremote

A fast, pragmatic, and lightweight Windows PC remote-control web UI. Designed primarily for controlling media playback from a smartphone.

## Features

- **Media Control**: Play/Pause, Next/Previous Track, and seeking (+15s / -15s). Shows current playing media art.
- **Audio Control**: Change master volume, mute, and change active output device. Provides a per-app volume mixer.
- **Display Management**: Switch primary displays, adjust brightness, toggle display modes (Clone/Extend), and turn off displays.
- **Input & Keyboard**: Acts as a remote touchpad and keyboard. Includes left/right mouse clicks and full scrolling support.
- **Power**: Remotely shutdown, restart, sleep, or hibernate the PC. Supports delayed timers and cancellations.

## Architecture

This tool was designed with a "KISS" (Keep It Simple, Stupid) philosophy in mind:
- **Zero-Dependency Frontend**: The UI is built with vanilla JavaScript, HTML, and CSS. No Node.js, no Webpack, no React. Fast to load, easy to read.
- **Embedded Static Assets**: All frontend files are embedded into the C# binary. The application compiles to a single executable.
- **Direct OS Integration**: The backend relies on direct P/Invoke calls to `user32.dll` and simple static classes. No enterprise abstractions or over-engineered design patterns.

## Security & Token Authentication

Wiremote includes **Token-Based Authentication & Session Protection**:
- On startup, a secure persistent authentication token is generated (stored locally in `wiremote.token`).
- The terminal prints a direct link with your token: `http://<ip>:8765/?token=<TOKEN>`.
- When opened on your phone, the token is automatically saved into the browser's `localStorage` and stripped from the address bar.
- Any unauthenticated client on the network is blocked from sending mouse, keyboard, audio, or power commands.
- **Single Active Controller**: Reconnecting cleanly takes over the session and prunes stale zombie connections.

## Getting Started

1. **Prerequisites**: Ensure you have the .NET SDK installed (matches the target framework in the `.csproj`).
2. **First Launch (Important)**: You **MUST** run the application as **Administrator** the first time you launch it. 
   - The application automatically configures Windows Firewall (`netsh`) to allow incoming connections on ports 8765 and 8766.
   - If you do not run as Administrator, other devices on your network (like your phone) will not be able to connect.
3. **Usage**:
   - Run the application (`dotnet run` or launch the compiled `.exe`).
   - The console will display a URL with your token (e.g., `http://192.168.1.50:8765/?token=a8b3c4d5...`).
   - Open that URL in your smartphone's web browser once to pair it automatically.
   - Includes **Instant Reconnect**, an **Offline Adjustment Buffer** (adjust volume even while reconnecting without dropped commands), and **Keep Screen Awake** support.
   - For the best experience on iOS/Android, you can "Add to Home Screen" to run it in a full-screen, app-like mode.

## Development

If you'd like to tweak the UI, edit the files in the `wwwroot` directory. Since they are marked as embedded resources in the `.csproj`, any changes you make will be included the next time you compile the application.

## License

MIT License. See the [LICENSE](LICENSE) file for more information.
