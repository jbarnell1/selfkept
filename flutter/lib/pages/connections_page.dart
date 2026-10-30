import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../models/connect.dart';
import '../providers/connect_provider.dart';
import '../theme/app_theme.dart';
import '../theme/tokens.dart';
import '../utils/relative_time.dart';
import '../widgets/copyable_value.dart';
import '../widgets/open_webui_steps.dart';
import '../widgets/page_body.dart';
import '../widgets/status_views.dart';
import '../widgets/surface_card.dart';
import '../widgets/subpage_app_bar.dart';
import '../widgets/dialogs.dart';

/// Assistants: how to connect one, who is connected, access tokens, and what
/// assistants have been asked to remember.
class ConnectionsPage extends StatefulWidget {
  const ConnectionsPage({super.key});

  static const String intro =
      'Assistants such as Open WebUI or Claude can read your health data here and answer questions about it. Nothing leaves this server unless an assistant you connect asks for it.';
  static const String noAssistants = 'No assistants connected yet.';
  static const String tokensLine =
      'For an assistant that takes a token instead of signing in.';
  static const String noMemory =
      'Nothing yet. When you ask an assistant to remember something, it shows here.';
  static const String secretNote =
      'This is the only time it is shown. Paste it into your assistant now.';

  @override
  State<ConnectionsPage> createState() => _ConnectionsPageState();
}

class _ConnectionsPageState extends State<ConnectionsPage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final connect = context.read<ConnectProvider>();
      if (!connect.loading) connect.load();
    });
  }

  @override
  Widget build(BuildContext context) {
    final connect = context.watch<ConnectProvider>();
    final theme = Theme.of(context);

    final List<Widget> body;
    if (!connect.hasLoaded && connect.error == null) {
      body = const [
        Padding(
          padding: EdgeInsets.symmetric(vertical: Space.x8),
          child: LoadingView(),
        ),
      ];
    } else if (!connect.hasLoaded) {
      body = [
        ErrorView(
          title: 'Assistants did not load.',
          detail: connect.error,
          onRetry: connect.load,
        ),
      ];
    } else {
      final data = connect.connections!;
      body = [
        _ConnectCard(mcpUrl: data.mcpUrl),
        _AssistantsSection(assistants: data.assistants),
        _TokensSection(tokens: data.tokens),
        _MemorySection(items: connect.memory),
        AttentionLine(connect.error ?? ''),
      ];
    }

    return Scaffold(
      appBar: const SubpageAppBar(fallback: '/you'),
      body: SafeArea(
        child: RefreshIndicator(
          onRefresh: connect.load,
          color: theme.colorScheme.primary,
          child: PageBody(
            physics: const AlwaysScrollableScrollPhysics(),
            children: [
              const PageHeading(title: 'Assistants'),
              ...body,
            ],
          ),
        ),
      ),
    );
  }
}

String _date(DateTime? at) =>
    at == null ? '' : DateFormat.yMMMd().format(at.toLocal());

String _lastUsed(DateTime? at) =>
    at == null ? 'Not used yet' : 'Last used ${relativeTimeInSentence(at)}';

// ---------------------------------------------------------------------------
// Connect an assistant
// ---------------------------------------------------------------------------

class _ConnectCard extends StatelessWidget {
  final String mcpUrl;

  const _ConnectCard({required this.mcpUrl});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader(title: 'Connect an assistant'),
        SurfaceCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(ConnectionsPage.intro, style: theme.textTheme.bodyMedium),
              const SizedBox(height: Space.x4),
              OpenWebUiSteps(mcpUrl: mcpUrl),
            ],
          ),
        ),
      ],
    );
  }
}

// ---------------------------------------------------------------------------
// Connected assistants
// ---------------------------------------------------------------------------

class _AssistantsSection extends StatelessWidget {
  final List<ConnectedAssistant> assistants;

  const _AssistantsSection({required this.assistants});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader(title: 'Connected assistants'),
        SurfaceCard(
          child: assistants.isEmpty
              ? Text(
                  ConnectionsPage.noAssistants,
                  style: theme.textTheme.bodyMedium,
                )
              : Column(
                  children: [
                    for (final a in assistants) _AssistantRow(assistant: a),
                  ],
                ),
        ),
      ],
    );
  }
}

class _AssistantRow extends StatelessWidget {
  final ConnectedAssistant assistant;

  const _AssistantRow({required this.assistant});

  Future<void> _disconnect(BuildContext context) async {
    final connect = context.read<ConnectProvider>();
    final yes = await confirmAction(
      context,
      message:
          'Disconnect ${assistant.clientName}? It will need your permission again to read your data.',
      confirmLabel: 'Disconnect',
    );
    if (yes) await connect.revokeAssistant(assistant.clientId);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: Space.x2),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(assistant.clientName, style: theme.textTheme.titleMedium),
                if (assistant.connectedAt != null)
                  Text(
                    'Connected ${_date(assistant.connectedAt)}',
                    style: theme.textTheme.bodySmall,
                  ),
                Text(
                  _lastUsed(assistant.lastUsedAt),
                  style: theme.textTheme.bodySmall,
                ),
              ],
            ),
          ),
          const SizedBox(width: Space.x3),
          TextButton(
            onPressed: () => _disconnect(context),
            child: const Text('Disconnect'),
          ),
        ],
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Access tokens
// ---------------------------------------------------------------------------

class _TokensSection extends StatelessWidget {
  final List<PersonalToken> tokens;

  const _TokensSection({required this.tokens});

  Future<void> _create(BuildContext context) async {
    final created = await showDialog<CreatedToken>(
      context: context,
      builder: (context) => const _CreateTokenDialog(),
    );
    if (created == null || !context.mounted) return;
    await showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (context) => _SecretDialog(secret: created.secret),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader(title: 'Access tokens'),
        SurfaceCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                ConnectionsPage.tokensLine,
                style: theme.textTheme.bodyMedium,
              ),
              const SizedBox(height: Space.x2),
              for (final t in tokens) _TokenRow(token: t),
              const SizedBox(height: Space.x3),
              OutlinedButton(
                onPressed: () => _create(context),
                child: const Text('Create token'),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _TokenRow extends StatelessWidget {
  final PersonalToken token;

  const _TokenRow({required this.token});

  Future<void> _revoke(BuildContext context) async {
    final connect = context.read<ConnectProvider>();
    final yes = await confirmAction(
      context,
      message: 'Revoke ${token.name}? Anything using it stops working.',
      confirmLabel: 'Revoke',
    );
    if (yes) await connect.revokeToken(token.id);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final created = _date(token.createdAt);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: Space.x2),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(token.name, style: theme.textTheme.titleMedium),
                Text(
                  token.prefix,
                  style: AppTheme.numeric(
                    context,
                    base: theme.textTheme.bodySmall,
                  ),
                ),
                Text(
                  token.canRemember ? 'Can remember notes' : 'Read only',
                  style: theme.textTheme.bodySmall,
                ),
                Text(
                  [
                    if (created.isNotEmpty) 'Created $created',
                    _lastUsed(token.lastUsedAt),
                  ].join('. '),
                  style: theme.textTheme.bodySmall,
                ),
              ],
            ),
          ),
          const SizedBox(width: Space.x3),
          TextButton(
            onPressed: () => _revoke(context),
            child: const Text('Revoke'),
          ),
        ],
      ),
    );
  }
}

/// Asks for a name and whether the token may remember notes, then makes it.
/// Pops the [CreatedToken], or null when cancelled.
class _CreateTokenDialog extends StatefulWidget {
  const _CreateTokenDialog();

  @override
  State<_CreateTokenDialog> createState() => _CreateTokenDialogState();
}

class _CreateTokenDialogState extends State<_CreateTokenDialog> {
  final _form = GlobalKey<FormState>();
  final _name = TextEditingController();
  bool _remember = true;
  bool _busy = false;
  String _problem = '';

  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  Future<void> _create() async {
    if (!(_form.currentState?.validate() ?? false)) return;
    final connect = context.read<ConnectProvider>();
    setState(() {
      _busy = true;
      _problem = '';
    });
    final created = await connect.createToken(
      _name.text.trim(),
      allowMemoryWrite: _remember,
    );
    if (!mounted) return;
    if (created == null) {
      setState(() {
        _busy = false;
        _problem = connect.error ?? 'That did not work.';
      });
      return;
    }
    Navigator.of(context).pop(created);
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('Create token'),
      content: Form(
        key: _form,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            TextFormField(
              controller: _name,
              autofocus: true,
              maxLength: 80,
              enabled: !_busy,
              textInputAction: TextInputAction.done,
              decoration: const InputDecoration(labelText: 'Name'),
              onFieldSubmitted: (_) => _create(),
              validator: (v) =>
                  (v == null || v.trim().isEmpty) ? 'Give it a name.' : null,
            ),
            SwitchListTile.adaptive(
              contentPadding: EdgeInsets.zero,
              title: const Text('Let it remember notes'),
              value: _remember,
              onChanged: _busy ? null : (on) => setState(() => _remember = on),
            ),
            AttentionLine(_problem),
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: _busy ? null : () => Navigator.of(context).pop(),
          child: const Text('Cancel'),
        ),
        FilledButton(
          onPressed: _busy ? null : _create,
          child: Text(_busy ? 'Creating' : 'Create'),
        ),
      ],
    );
  }
}

/// The secret of a new token. Shown once.
class _SecretDialog extends StatelessWidget {
  final String secret;

  const _SecretDialog({required this.secret});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return AlertDialog(
      title: const Text('Your new token'),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          CopyableValue(secret),
          const SizedBox(height: Space.x3),
          Text(ConnectionsPage.secretNote, style: theme.textTheme.bodySmall),
        ],
      ),
      actions: [
        FilledButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('Done'),
        ),
      ],
    );
  }
}

// ---------------------------------------------------------------------------
// What assistants remember
// ---------------------------------------------------------------------------

class _MemorySection extends StatelessWidget {
  final List<AiMemoryItem> items;

  const _MemorySection({required this.items});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader(title: 'What assistants remember'),
        SurfaceCard(
          child: items.isEmpty
              ? Text(
                  ConnectionsPage.noMemory,
                  style: theme.textTheme.bodyMedium,
                )
              : Column(children: [for (final m in items) _MemoryRow(item: m)]),
        ),
      ],
    );
  }
}

class _MemoryRow extends StatelessWidget {
  final AiMemoryItem item;

  const _MemoryRow({required this.item});

  Future<void> _forget(BuildContext context) async {
    final connect = context.read<ConnectProvider>();
    final yes = await confirmAction(
      context,
      message: 'Forget this?',
      confirmLabel: 'Forget',
    );
    if (yes) await connect.deleteMemory(item.id);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final source = item.source;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: Space.x2),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  item.type.displayName,
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: theme.colorScheme.secondary,
                  ),
                ),
                Text(item.text, style: theme.textTheme.bodyMedium),
                if (source != null)
                  Text(
                    'Remembered by $source',
                    style: theme.textTheme.bodySmall,
                  ),
              ],
            ),
          ),
          IconButton(
            tooltip: 'Forget',
            onPressed: () => _forget(context),
            icon: const Icon(Icons.delete_outline),
          ),
        ],
      ),
    );
  }
}
