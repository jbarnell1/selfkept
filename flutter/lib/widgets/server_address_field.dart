import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../providers/server_settings.dart';
import '../theme/tokens.dart';

/// The in-app server address. A phone on a tailnet reaches its server by a
/// Tailscale hostname, so the person types it here once; it is kept in secure
/// storage and overrides the build-time default.
class ServerAddressField extends StatefulWidget {
  /// Called after a new address is saved, so the caller can re-run the handshake.
  final Future<void> Function()? onSaved;
  final bool compact;

  const ServerAddressField({super.key, this.onSaved, this.compact = false});

  static const String example = 'https://box.tailnet.ts.net';

  @override
  State<ServerAddressField> createState() => _ServerAddressFieldState();
}

class _ServerAddressFieldState extends State<ServerAddressField> {
  late final TextEditingController _controller;
  bool _saving = false;

  @override
  void initState() {
    super.initState();
    final settings = context.read<ServerSettings>();
    _controller = TextEditingController(text: settings.overrideUrl ?? '');
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (_saving) return;
    setState(() => _saving = true);
    try {
      await context.read<ServerSettings>().setOverride(_controller.text);
      await widget.onSaved?.call();
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _useDefault() async {
    _controller.clear();
    await _save();
  }

  @override
  Widget build(BuildContext context) {
    final settings = context.watch<ServerSettings>();
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        TextField(
          controller: _controller,
          keyboardType: TextInputType.url,
          autocorrect: false,
          enableSuggestions: false,
          textInputAction: TextInputAction.done,
          onSubmitted: (_) => _save(),
          decoration: InputDecoration(
            labelText: 'Server address',
            hintText: settings.defaultUrl.isEmpty
                ? ServerAddressField.example
                : settings.defaultUrl,
            helperText: widget.compact
                ? null
                : settings.defaultUrl.isEmpty
                ? 'A Tailscale name looks like ${ServerAddressField.example}'
                : 'Leave empty to use ${settings.defaultUrl}. '
                      'A Tailscale name looks like ${ServerAddressField.example}',
            helperMaxLines: 3,
          ),
        ),
        const SizedBox(height: Space.x3),
        Row(
          children: [
            if (settings.isOverridden && settings.defaultUrl.isNotEmpty)
              TextButton(
                onPressed: _saving ? null : _useDefault,
                child: const Text('Use default'),
              ),
            const Spacer(),
            FilledButton(
              onPressed: _saving ? null : _save,
              child: _saving
                  ? SizedBox(
                      width: 18,
                      height: 18,
                      child: CircularProgressIndicator(
                        strokeWidth: 2,
                        color: theme.colorScheme.onPrimary,
                      ),
                    )
                  : const Text('Save address'),
            ),
          ],
        ),
      ],
    );
  }
}
