import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'client.dart';
import 'protocol.dart';
import 'sections/audio_display.dart';
import 'sections/controls.dart';
import 'sections/media.dart';
import 'sections/misc.dart';
import 'sections/power.dart';
import 'theme.dart';
import 'widgets.dart';

SystemUiOverlayStyle _overlayStyle(Palette p) =>
    p.light ? SystemUiOverlayStyle.dark : SystemUiOverlayStyle.light;

class HomeScreen extends StatelessWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final scope = context.scope;
    return AnnotatedRegion(
      value: _overlayStyle(scope.palette),
      child: Scaffold(
        body: Stack(
          children: [
            SafeArea(
              child: SingleChildScrollView(
                padding: const EdgeInsets.fromLTRB(20, 24, 20, 60),
                child: Center(
                  child: ConstrainedBox(
                    constraints: const BoxConstraints(maxWidth: 430),
                    child: const Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      spacing: 20,
                      children: [
                        _Header(),
                        MediaSection(),
                        AudioDisplaySection(),
                        ControlsSection(),
                        PowerSection(),
                        MiscSection(),
                      ],
                    ),
                  ),
                ),
              ),
            ),
            ValueListenableBuilder(
              valueListenable: scope.client.authError,
              builder: (context, error, _) => error == null
                  ? const SizedBox.shrink()
                  : AuthOverlay(error: error),
            ),
          ],
        ),
      ),
    );
  }
}

class _Header extends StatelessWidget {
  const _Header();

  @override
  Widget build(BuildContext context) {
    final scope = context.scope;
    final p = scope.palette;
    return Padding(
      padding: const EdgeInsets.only(bottom: 5),
      child: Row(
        children: [
          const Expanded(
            child: Text(
              'Wiremote',
              style: TextStyle(
                fontSize: 24,
                fontWeight: FontWeight.w600,
                letterSpacing: -0.5,
              ),
            ),
          ),
          GestureDetector(
            onTap: () => showServerDialog(context),
            child: ValueListenableBuilder(
              valueListenable: scope.client.status,
              builder: (context, status, _) {
                final online = status == ConnectionStatus.online;
                return Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 12,
                    vertical: 6,
                  ),
                  decoration: BoxDecoration(
                    color: p.surface2,
                    borderRadius: BorderRadius.circular(20),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    spacing: 6,
                    children: [
                      Container(
                        width: 8,
                        height: 8,
                        decoration: BoxDecoration(
                          shape: BoxShape.circle,
                          color: online ? p.success : Palette.danger,
                        ),
                      ),
                      Text(
                        online ? 'Online' : 'Offline',
                        style: const TextStyle(
                          fontSize: 13,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ],
                  ),
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}

/// Rounded card used by the connect screen, the dialog and the auth overlay.
class _Panel extends StatelessWidget {
  const _Panel({required this.child});
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return ConstrainedBox(
      constraints: const BoxConstraints(maxWidth: 380),
      child: Container(
        padding: const EdgeInsets.fromLTRB(24, 28, 24, 24),
        decoration: BoxDecoration(
          color: p.surface,
          borderRadius: BorderRadius.circular(20),
          border: Border.all(color: p.surface3),
          boxShadow: [
            BoxShadow(
              color: Color(p.light ? 0x26000000 : 0x80000000),
              blurRadius: 36,
              offset: const Offset(0, 16),
            ),
          ],
        ),
        child: child,
      ),
    );
  }
}

InputDecoration _fieldDecoration(BuildContext context, String hint) {
  final p = context.palette;
  OutlineInputBorder border(Color c) => OutlineInputBorder(
    borderRadius: BorderRadius.circular(12),
    borderSide: BorderSide(color: c),
  );
  return InputDecoration(
    hintText: hint,
    hintStyle: TextStyle(color: p.secondary),
    filled: true,
    fillColor: p.surface2,
    contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
    enabledBorder: border(p.surface3),
    focusedBorder: border(context.accent),
  );
}

class _ErrorBox extends StatelessWidget {
  const _ErrorBox(this.text);
  final String text;

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
    decoration: BoxDecoration(
      color: Palette.danger.withValues(alpha: 0.12),
      borderRadius: BorderRadius.circular(8),
    ),
    child: Text(
      text,
      textAlign: TextAlign.center,
      style: const TextStyle(fontSize: 13.5, color: Palette.danger),
    ),
  );
}

/// PC address + token form. Pasting the link the PC prints fills both.
class ServerForm extends StatefulWidget {
  const ServerForm({super.key, this.onDone});
  final VoidCallback? onDone;

  @override
  State<ServerForm> createState() => _ServerFormState();
}

class _ServerFormState extends State<ServerForm> {
  late final TextEditingController _address;
  late final TextEditingController _token;
  String? _error;

  @override
  void initState() {
    super.initState();
    final settings = AppScope.read(context).settings;
    _address = TextEditingController(text: settings.host ?? '');
    _token = TextEditingController(text: settings.token ?? '');
  }

  @override
  void dispose() {
    _address.dispose();
    _token.dispose();
    super.dispose();
  }

  void _onAddressChanged(String text) {
    final parsed = ServerAddress.parse(text);
    if (parsed?.token != null) {
      _token.text = parsed!.token!;
      _address.value = TextEditingValue(
        text: parsed.host,
        selection: TextSelection.collapsed(offset: parsed.host.length),
      );
    }
    if (_error != null) setState(() => _error = null);
  }

  void _submit() {
    final parsed = ServerAddress.parse(_address.text);
    if (parsed == null) {
      setState(() => _error = "Enter the link or the PC's IP address.");
      return;
    }
    final token = _token.text.trim();
    final scope = AppScope.read(context);
    scope.settings.setServer(
      parsed.host,
      parsed.token ?? (token.isEmpty ? null : token),
    );
    scope.client.reconnect();
    widget.onDone?.call();
  }

  @override
  Widget build(BuildContext context) => Column(
    mainAxisSize: MainAxisSize.min,
    crossAxisAlignment: CrossAxisAlignment.stretch,
    spacing: 12,
    children: [
      TextField(
        controller: _address,
        keyboardType: TextInputType.url,
        autocorrect: false,
        textInputAction: TextInputAction.next,
        decoration: _fieldDecoration(context, 'Link or PC address'),
        onChanged: _onAddressChanged,
      ),
      TextField(
        controller: _token,
        obscureText: true,
        autocorrect: false,
        enableSuggestions: false,
        textAlign: TextAlign.center,
        decoration: _fieldDecoration(context, 'Token'),
        onSubmitted: (_) => _submit(),
      ),
      WmButton(
        accent: true,
        height: 48,
        fontSize: 16,
        fontWeight: FontWeight.w600,
        onTap: _submit,
        child: const Text('Connect'),
      ),
      if (_error != null) _ErrorBox(_error!),
    ],
  );
}

Future<void> showServerDialog(BuildContext context) => showDialog<void>(
  context: context,
  builder: (dialogContext) => Dialog(
    backgroundColor: Colors.transparent,
    elevation: 0,
    insetPadding: const EdgeInsets.all(20),
    child: _Panel(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Text(
            'Change PC',
            textAlign: TextAlign.center,
            style: TextStyle(fontSize: 21, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 20),
          ServerForm(onDone: () => Navigator.of(dialogContext).pop()),
        ],
      ),
    ),
  ),
);

/// First run: no PC saved yet.
class ConnectScreen extends StatelessWidget {
  const ConnectScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return AnnotatedRegion(
      value: _overlayStyle(p),
      child: Scaffold(
        body: SafeArea(
          child: Center(
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(20),
              child: _Panel(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const Text(
                      'Wiremote',
                      textAlign: TextAlign.center,
                      style: TextStyle(
                        fontSize: 24,
                        fontWeight: FontWeight.w600,
                        letterSpacing: -0.5,
                      ),
                    ),
                    const SizedBox(height: 10),
                    Text(
                      'Start Wiremote on your PC. It prints a link like '
                      'http://192.168.1.50:8765/?token=… — paste it here, '
                      "or type the PC's address and token.",
                      textAlign: TextAlign.center,
                      style: TextStyle(
                        fontSize: 14,
                        height: 1.45,
                        color: p.secondary,
                      ),
                    ),
                    const SizedBox(height: 20),
                    const ServerForm(),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// Shown when the PC rejects the saved token.
class AuthOverlay extends StatefulWidget {
  const AuthOverlay({super.key, required this.error});
  final String error;

  @override
  State<AuthOverlay> createState() => _AuthOverlayState();
}

class _AuthOverlayState extends State<AuthOverlay> {
  final _token = TextEditingController();

  @override
  void dispose() {
    _token.dispose();
    super.dispose();
  }

  void _submit() {
    final value = _token.text.trim();
    if (value.isEmpty) return;
    final scope = AppScope.read(context);
    final link = value.contains('token=') ? ServerAddress.parse(value) : null;
    if (link?.token != null) {
      scope.settings.setServer(link!.host, link.token);
    } else {
      scope.settings.token = value;
    }
    scope.client.reconnect();
  }

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return Positioned.fill(
      child: BackdropFilter(
        filter: ImageFilter.blur(sigmaX: 8, sigmaY: 8),
        child: ColoredBox(
          color: p.light ? const Color(0xBFC8CDD7) : const Color(0xD1000000),
          child: SafeArea(
            child: Center(
              child: SingleChildScrollView(
                padding: const EdgeInsets.all(20),
                child: _Panel(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Icon(Icons.lock_outline, size: 40, color: context.accent),
                      const SizedBox(height: 12),
                      const Text(
                        'Wiremote Auth',
                        textAlign: TextAlign.center,
                        style: TextStyle(
                          fontSize: 21,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      const SizedBox(height: 10),
                      Text(
                        'To prevent hijacking, enter the auth token shown in the PC console, '
                        'or paste the link that contains it.',
                        textAlign: TextAlign.center,
                        style: TextStyle(
                          fontSize: 14,
                          height: 1.45,
                          color: p.secondary,
                        ),
                      ),
                      const SizedBox(height: 20),
                      TextField(
                        controller: _token,
                        obscureText: true,
                        autocorrect: false,
                        enableSuggestions: false,
                        textAlign: TextAlign.center,
                        style: const TextStyle(letterSpacing: 1),
                        decoration: _fieldDecoration(context, 'Enter Token'),
                        onSubmitted: (_) => _submit(),
                      ),
                      const SizedBox(height: 12),
                      WmButton(
                        accent: true,
                        height: 48,
                        fontSize: 16,
                        fontWeight: FontWeight.w600,
                        onTap: _submit,
                        child: const Text('Authorize'),
                      ),
                      const SizedBox(height: 14),
                      _ErrorBox(widget.error),
                      const SizedBox(height: 6),
                      TextButton(
                        onPressed: () => showServerDialog(context),
                        child: Text(
                          'Change PC',
                          style: TextStyle(color: p.secondary),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
