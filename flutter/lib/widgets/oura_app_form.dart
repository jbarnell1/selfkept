import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../providers/oura_app_provider.dart';
import '../providers/server_settings.dart';
import '../theme/app_theme.dart';
import '../theme/tokens.dart';
import '../utils/open_url.dart';
import 'copyable_value.dart';
import 'status_views.dart';
import 'dialogs.dart';

/// The household admin's one-time Oura setup: create an application on
/// Oura's developer page, then paste its client id and secret here. Shows
/// what is set once it is.
class OuraAppForm extends StatefulWidget {
  /// Called after a save or a removal, so the caller can refresh Oura's status.
  final VoidCallback? onChanged;

  const OuraAppForm({super.key, this.onChanged});

  @override
  State<OuraAppForm> createState() => _OuraAppFormState();
}

class _OuraAppFormState extends State<OuraAppForm> {
  final _clientId = TextEditingController();
  final _clientSecret = TextEditingController();
  bool _replacing = false;
  bool _obscure = true;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final provider = context.read<OuraAppProvider>();
      if (provider.app == null && !provider.loading) provider.load();
    });
  }

  @override
  void dispose() {
    _clientId.dispose();
    _clientSecret.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final id = _clientId.text.trim();
    final secret = _clientSecret.text.trim();
    if (id.isEmpty || secret.isEmpty) return;
    final ok = await context.read<OuraAppProvider>().save(
      clientId: id,
      clientSecret: secret,
    );
    if (!mounted || !ok) return;
    _clientId.clear();
    _clientSecret.clear();
    setState(() => _replacing = false);
    widget.onChanged?.call();
  }

  Future<void> _remove() async {
    final provider = context.read<OuraAppProvider>();
    final yes = await confirmAction(
      context,
      message:
          'Remove the Oura application? Everyone connected to Oura stops '
          'receiving new readings until it is added again.',
      confirmLabel: 'Remove',
    );
    if (!yes) return;
    if (await provider.clear()) widget.onChanged?.call();
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<OuraAppProvider>();
    final app = provider.app;

    if (app == null) {
      if (provider.error == null) return const LoadingView();
      return Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          AttentionLine(provider.error ?? ''),
          TextButton(
            onPressed: provider.load,
            child: const Text('Check again'),
          ),
        ],
      );
    }

    if (app.configured && !_replacing) {
      return _Configured(
        clientId: app.clientId,
        editable: app.editable,
        busy: provider.saving,
        error: provider.error,
        onReplace: () => setState(() => _replacing = true),
        onRemove: _remove,
      );
    }

    final theme = Theme.of(context);
    final origin = context.read<ServerSettings>().currentUrl;
    final redirect = app.redirectUri;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        _Numbered(
          n: 1,
          children: [
            const Text(
              "Create an application on Oura's developer page. One serves up "
              'to ten people, so the household needs only one.',
            ),
            const SizedBox(height: Space.x2),
            OutlinedButton.icon(
              onPressed: app.developerUrl.isEmpty
                  ? null
                  : () => openLink(context, app.developerUrl),
              icon: const Icon(Icons.open_in_new, size: 18),
              label: const Text("Open Oura's developer page"),
            ),
          ],
        ),
        _Numbered(
          n: 2,
          children: [
            const Text(
              'Any name and description will do. For the website, privacy '
              "policy and terms, use this server's address:",
            ),
            const SizedBox(height: Space.x2),
            CopyableValue(origin),
          ],
        ),
        _Numbered(
          n: 3,
          children: [
            const Text('Under redirect URIs, add exactly this address:'),
            const SizedBox(height: Space.x2),
            if (redirect == null)
              const AttentionLine(
                'This server does not know its own address yet. Set the '
                'origin in its settings file (.env) and restart it.',
              )
            else
              CopyableValue(redirect),
          ],
        ),
        _Numbered(
          n: 4,
          children: [
            const Text(
              'Tick the daily, heart rate and SpO2 scopes and save. Then '
              'paste the client ID and secret Oura shows you.',
            ),
            const SizedBox(height: Space.x3),
            TextField(
              controller: _clientId,
              autocorrect: false,
              enableSuggestions: false,
              textInputAction: TextInputAction.next,
              decoration: const InputDecoration(labelText: 'Client ID'),
            ),
            const SizedBox(height: Space.x3),
            TextField(
              controller: _clientSecret,
              obscureText: _obscure,
              autocorrect: false,
              enableSuggestions: false,
              onSubmitted: (_) => _save(),
              decoration: InputDecoration(
                labelText: 'Client secret',
                suffixIcon: IconButton(
                  tooltip: _obscure ? 'Show secret' : 'Hide secret',
                  onPressed: () => setState(() => _obscure = !_obscure),
                  icon: Icon(
                    _obscure
                        ? Icons.visibility_outlined
                        : Icons.visibility_off_outlined,
                  ),
                ),
              ),
            ),
            AttentionLine(provider.error ?? ''),
            const SizedBox(height: Space.x3),
            Wrap(
              spacing: Space.x2,
              children: [
                FilledButton(
                  onPressed: provider.saving || redirect == null ? null : _save,
                  child: Text(provider.saving ? 'Saving' : 'Save'),
                ),
                if (_replacing)
                  TextButton(
                    onPressed: () => setState(() => _replacing = false),
                    child: const Text('Cancel'),
                  ),
              ],
            ),
            const SizedBox(height: Space.x2),
            Text(
              'The secret is stored encrypted on this server and never shown again.',
              style: theme.textTheme.bodySmall,
            ),
          ],
        ),
      ],
    );
  }
}

/// What is set, with Replace and Remove when the app may change it.
class _Configured extends StatelessWidget {
  final String? clientId;
  final bool editable;
  final bool busy;
  final String? error;
  final VoidCallback onReplace;
  final VoidCallback onRemove;

  const _Configured({
    required this.clientId,
    required this.editable,
    required this.busy,
    required this.error,
    required this.onReplace,
    required this.onRemove,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          editable
              ? 'Oura is set up for this server.'
              : "Oura is set up in the server's settings file (.env).",
          style: theme.textTheme.bodyMedium,
        ),
        if (clientId != null)
          Text(
            'Application $clientId',
            style: AppTheme.numeric(context, base: theme.textTheme.bodySmall),
          ),
        if (editable) ...[
          const SizedBox(height: Space.x2),
          Wrap(
            spacing: Space.x2,
            children: [
              TextButton(onPressed: onReplace, child: const Text('Replace')),
              TextButton(
                onPressed: busy ? null : onRemove,
                child: const Text('Remove'),
              ),
            ],
          ),
        ],
        AttentionLine(error ?? ''),
      ],
    );
  }
}

/// A step number beside its instructions.
class _Numbered extends StatelessWidget {
  final int n;
  final List<Widget> children;

  const _Numbered({required this.n, required this.children});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.x4),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 28,
            child: Text(
              '$n.',
              style: AppTheme.numeric(context, base: theme.textTheme.bodyLarge),
            ),
          ),
          Expanded(
            child: DefaultTextStyle.merge(
              style: theme.textTheme.bodyMedium,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: children,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
