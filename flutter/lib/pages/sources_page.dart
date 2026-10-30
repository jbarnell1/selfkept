import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../models/connect.dart';
import '../models/source.dart';
import '../offline/sync_engine.dart';
import '../providers/auth_provider.dart';
import '../providers/collector_provider.dart';
import '../providers/oura_provider.dart';
import '../providers/sources_provider.dart';
import '../theme/app_theme.dart';
import '../theme/tokens.dart';
import '../utils/relative_time.dart';
import '../widgets/key_value_row.dart';
import '../widgets/oura_app_form.dart';
import '../widgets/page_body.dart';
import '../widgets/status_views.dart';
import '../widgets/surface_card.dart';
import '../widgets/subpage_app_bar.dart';
import '../widgets/dialogs.dart';

/// Sources: where readings come from, this phone's collector, and the state
/// of the outbox.
class SourcesPage extends StatefulWidget {
  /// The `oura` query value Oura's round trip leaves behind: `connected` or
  /// `error`. Shown once as a message.
  final String? ouraResult;

  const SourcesPage({super.key, this.ouraResult});

  static const String webNotice =
      'Collection happens in the phone apps. Connect Apple Health or Health Connect there and the readings gather here.';

  static const String ouraNotConfigured =
      'Oura needs a one-time setup by the household admin. Once it is done, connect your ring here.';

  @override
  State<SourcesPage> createState() => _SourcesPageState();
}

class _SourcesPageState extends State<SourcesPage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final sources = context.read<SourcesProvider>();
      if (!sources.isLoading) sources.load();
      final collector = context.read<CollectorProvider>();
      if (collector.status == CollectorStatus.unknown) collector.init();
      context.read<SyncEngine>().refreshPending();
      final oura = context.read<OuraProvider>();
      if (!oura.loading) oura.load();
      final message = switch (widget.ouraResult) {
        'connected' => 'Oura connected. The first pull is under way.',
        'error' => 'Oura did not connect. Try again.',
        _ => null,
      };
      if (message != null) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(message)));
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: const SubpageAppBar(fallback: '/you'),
      body: const SafeArea(
        child: PageBody(
          children: [
            PageHeading(
              title: 'Sources',
              subtitle:
                  'Everything that feeds your record, and when it last spoke to your server.',
            ),
            _CollectorCard(),
            SizedBox(height: Space.x3),
            _OuraCard(),
            _SourceList(),
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// This phone's collector
// ---------------------------------------------------------------------------

class _CollectorCard extends StatelessWidget {
  const _CollectorCard();

  @override
  Widget build(BuildContext context) {
    final collector = context.watch<CollectorProvider>();
    final sync = context.watch<SyncEngine>();
    final theme = Theme.of(context);

    switch (collector.status) {
      case CollectorStatus.unknown:
        return const SurfaceCard(child: LoadingView());
      case CollectorStatus.unavailable:
        return SurfaceCard(
          child: Text(SourcesPage.webNotice, style: theme.textTheme.bodyMedium),
        );
      case CollectorStatus.disconnected:
      case CollectorStatus.connecting:
      case CollectorStatus.error when !collector.isConnected:
        return _ConnectCard(collector: collector);
      case CollectorStatus.connected:
      case CollectorStatus.collecting:
      case CollectorStatus.error:
        return _SyncCard(collector: collector, sync: sync);
    }
  }
}

class _ConnectCard extends StatelessWidget {
  final CollectorProvider collector;

  const _ConnectCard({required this.collector});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final name = collector.kind.displayName;
    final connecting = collector.status == CollectorStatus.connecting;
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('This phone', style: theme.textTheme.titleMedium),
          const SizedBox(height: Space.x2),
          Text(
            'Read steps, sleep, heart rate and the rest from $name and keep them on your own server. Nothing goes anywhere else.',
            style: theme.textTheme.bodyMedium,
          ),
          const SizedBox(height: Space.x4),
          FilledButton.icon(
            onPressed: connecting ? null : collector.connect,
            icon: const Icon(Icons.favorite_outline, size: 18),
            label: Text(connecting ? 'Connecting' : 'Connect $name'),
          ),
          _ProgressLine(collector.progress),
          AttentionLine(collector.error ?? ''),
        ],
      ),
    );
  }
}

class _SyncCard extends StatelessWidget {
  final CollectorProvider collector;
  final SyncEngine sync;

  const _SyncCard({required this.collector, required this.sync});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final busy = collector.isBusy || sync.isFlushing;
    final pending = sync.pendingCount;
    final error = collector.error ?? sync.lastError;
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  'This phone, ${collector.kind.displayName}',
                  style: theme.textTheme.titleMedium,
                ),
              ),
              Text(
                collector.status.displayName,
                style: theme.textTheme.labelMedium,
              ),
            ],
          ),
          const SizedBox(height: Space.x3),
          KeyValueRow(
            'Last successful sync',
            relativeTime(sync.lastFlushAt),
            labelWidth: 150,
            numeric: true,
          ),
          KeyValueRow(
            'Waiting to send',
            pending == 0 ? 'Nothing' : '$pending ${_rows(pending)}',
            labelWidth: 150,
            numeric: true,
          ),
          if (sync.lastRejected > 0)
            KeyValueRow(
              'Not accepted',
              '${sync.lastRejected} ${_rows(sync.lastRejected)} in the last batch',
              labelWidth: 150,
              numeric: true,
            ),
          const SizedBox(height: Space.x4),
          Row(
            children: [
              OutlinedButton.icon(
                onPressed: busy ? null : collector.collectAndFlush,
                icon: const Icon(Icons.sync, size: 18),
                label: Text(busy ? 'Syncing' : 'Sync now'),
              ),
            ],
          ),
          _ProgressLine(collector.progress),
          AttentionLine(error ?? ''),
        ],
      ),
    );
  }

  static String _rows(int n) => n == 1 ? 'reading' : 'readings';
}

class _ProgressLine extends StatelessWidget {
  final String? text;

  const _ProgressLine(this.text);

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return AnimatedSwitcher(
      duration: Motion.short,
      child: text == null
          ? const SizedBox.shrink()
          : Padding(
              key: ValueKey(text),
              padding: const EdgeInsets.only(top: Space.x3),
              child: Row(
                children: [
                  const SizedBox(
                    width: 14,
                    height: 14,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                  const SizedBox(width: Space.x2),
                  Expanded(
                    child: Text(text!, style: theme.textTheme.bodySmall),
                  ),
                ],
              ),
            ),
    );
  }
}

// ---------------------------------------------------------------------------
// Oura ring
// ---------------------------------------------------------------------------

class _OuraCard extends StatelessWidget {
  const _OuraCard();

  static const String pitch =
      'Bring in readiness, sleep stages, HRV and temperature from Oura\'s cloud.';

  @override
  Widget build(BuildContext context) {
    final oura = context.watch<OuraProvider>();
    final isAdmin = context.select<AuthProvider, bool>((a) => a.isAdmin);
    final theme = Theme.of(context);
    final status = oura.status;

    final List<Widget> body;
    if (status == null && oura.error == null) {
      body = const [LoadingView()];
    } else if (status == null) {
      body = [
        AttentionLine(oura.error ?? ''),
        const SizedBox(height: Space.x2),
        TextButton(onPressed: oura.load, child: const Text('Check again')),
      ];
    } else if (!status.configured) {
      body = [
        if (isAdmin)
          OuraAppForm(onChanged: oura.load)
        else
          Text(
            SourcesPage.ouraNotConfigured,
            style: theme.textTheme.bodyMedium,
          ),
      ];
    } else if (!status.connected) {
      body = [
        Text(pitch, style: theme.textTheme.bodyMedium),
        const SizedBox(height: Space.x4),
        Align(
          alignment: Alignment.centerLeft,
          child: FilledButton.tonal(
            onPressed: oura.connect,
            child: const Text('Connect Oura'),
          ),
        ),
        AttentionLine(oura.error ?? ''),
      ];
    } else {
      body = [_OuraConnected(status: status, oura: oura)];
    }

    return Padding(
      padding: const EdgeInsets.only(bottom: Space.x3),
      child: SurfaceCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Oura ring', style: theme.textTheme.titleMedium),
            const SizedBox(height: Space.x2),
            ...body,
          ],
        ),
      ),
    );
  }
}

class _OuraConnected extends StatelessWidget {
  final OuraStatus status;
  final OuraProvider oura;

  const _OuraConnected({required this.status, required this.oura});

  Future<void> _disconnect(BuildContext context) async {
    final yes = await confirmAction(
      context,
      message: 'Disconnect Oura? Readings already brought in stay.',
      confirmLabel: 'Disconnect',
    );
    if (yes) await oura.disconnect();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final connectedAt = status.connectedAt;
    final pulled = status.lastPullAt == null
        ? 'Not pulled yet'
        : 'Last pulled ${relativeTimeInSentence(status.lastPullAt)}';
    final n = status.sampleCount;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (connectedAt != null)
          Text(
            'Connected ${DateFormat.yMMMd().format(connectedAt.toLocal())}',
            style: theme.textTheme.bodyMedium,
          ),
        Text(pulled, style: theme.textTheme.bodyMedium),
        Text(
          '$n ${n == 1 ? 'reading' : 'readings'}',
          style: AppTheme.numeric(context, base: theme.textTheme.bodySmall),
        ),
        AttentionLine(status.lastError ?? oura.error ?? ''),
        const SizedBox(height: Space.x4),
        Wrap(
          spacing: Space.x2,
          runSpacing: Space.x2,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            OutlinedButton(
              onPressed: oura.syncing ? null : oura.sync,
              child: Text(oura.syncing ? 'Pulling' : 'Pull now'),
            ),
            TextButton(
              onPressed: () => _disconnect(context),
              child: const Text('Disconnect'),
            ),
          ],
        ),
      ],
    );
  }
}

// ---------------------------------------------------------------------------
// All sources
// ---------------------------------------------------------------------------

class _SourceList extends StatelessWidget {
  const _SourceList();

  @override
  Widget build(BuildContext context) {
    final sources = context.watch<SourcesProvider>();
    final theme = Theme.of(context);
    final list = sources.sources;

    Widget body;
    if (!sources.hasLoaded && sources.isLoading) {
      body = const SurfaceCard(child: LoadingView());
    } else if (!sources.hasLoaded && sources.error != null) {
      body = SurfaceCard(
        child: ErrorView(
          title: 'Your sources did not load.',
          detail: sources.error,
          onRetry: sources.load,
        ),
      );
    } else if (list.isEmpty) {
      body = SurfaceCard(
        child: Text(
          'No sources yet. Once a phone is connected it appears here.',
          style: theme.textTheme.bodyMedium,
        ),
      );
    } else {
      body = Column(
        children: [
          for (final s in list) ...[
            _SourceTile(source: s),
            const SizedBox(height: Space.x3),
          ],
          if (sources.hasLoaded) AttentionLine(sources.error ?? ''),
        ],
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionHeader(
          title: 'All sources',
          trailing: sources.hasLoaded
              ? IconButton(
                  tooltip: 'Refresh',
                  onPressed: sources.isLoading ? null : sources.load,
                  icon: const Icon(Icons.refresh),
                )
              : null,
        ),
        body,
      ],
    );
  }
}

/// Icon per source kind. Nothing branded: a shape that says what it is.
IconData sourceKindIcon(SourceKind kind) => switch (kind) {
  SourceKind.healthkit => Icons.phone_iphone,
  SourceKind.healthConnect => Icons.phone_android,
  SourceKind.oura => Icons.circle_outlined,
  SourceKind.googleHealth => Icons.cloud_outlined,
  SourceKind.manual => Icons.edit_outlined,
  SourceKind.labScan => Icons.document_scanner_outlined,
};

class _SourceTile extends StatelessWidget {
  final Source source;

  const _SourceTile({required this.source});

  @override
  Widget build(BuildContext context) {
    final sources = context.read<SourcesProvider>();
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final name = source.deviceName.isEmpty
        ? source.kind.displayName
        : source.deviceName;
    return SurfaceCard(
      padding: const EdgeInsets.fromLTRB(
        Space.x4,
        Space.x3,
        Space.x3,
        Space.x3,
      ),
      child: Row(
        children: [
          CircleAvatar(
            radius: 20,
            backgroundColor: colors.band.withValues(alpha: 0.35),
            child: Icon(
              sourceKindIcon(source.kind),
              size: 20,
              color: theme.colorScheme.onSurface,
            ),
          ),
          const SizedBox(width: Space.x3),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(name, style: theme.textTheme.titleMedium),
                const SizedBox(height: 2),
                Text(
                  '${source.kind.displayName}. Last sync ${relativeTimeInSentence(source.lastSyncAt)}.',
                  style: theme.textTheme.bodySmall,
                ),
                Text(
                  '${source.sampleCount} ${source.sampleCount == 1 ? 'reading' : 'readings'}',
                  style: AppTheme.numeric(
                    context,
                    base: theme.textTheme.bodySmall,
                  ),
                ),
              ],
            ),
          ),
          // The built-in sources (check-ins, lab results) are written by this
          // app itself; switching one off would only make keeping a check-in
          // or a panel fail, so they have no switch.
          if (source.kind.canPause)
            Semantics(
              label: 'Enabled, $name',
              child: Switch.adaptive(
                value: source.enabled,
                onChanged: (on) => sources.setEnabled(source.id, on),
              ),
            ),
        ],
      ),
    );
  }
}
