import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/assistant_lookup.dart';
import '../../providers/assistant_lookup_providers.dart';
import '../../theme/tokens.dart';
import '../../utils/relative_time.dart';
import '../../widgets/assistant/assistant_feature.dart';
import '../../widgets/dialogs.dart';
import '../../widgets/page_body.dart';
import '../../widgets/status_views.dart';
import '../../widgets/subpage_app_bar.dart';
import '../../widgets/surface_card.dart';
import 'cloud_lookup_page.dart';

/// "Sent to the cloud": every lookup the person sent, newest first, with the
/// exact text that went. Only the person sees this list.
class SentToCloudPage extends StatelessWidget {
  const SentToCloudPage({super.key});

  static const String title = 'Sent to the cloud';
  static const String subtitle =
      'Every question you sent to a cloud model, newest first, with the '
      'exact text that went. Only you see this list.';
  static const String emptyLine = 'Nothing has been sent to a cloud model.';
  static const String loadErrorTitle = 'This list did not load.';
  static const String deleteQuestion =
      'Delete this record? The answer stays in its conversation.';
  static const String deleteLabel = 'Delete';
  static const String keepLabel = 'Keep';
  static const String deletedMessage = 'Deleted.';
  static const String deleteTooltip = 'Delete this record';

  /// "Anonymized, Anthropic claude-opus-5-5, 2 hours ago, Answered".
  static String meta(LookupRecord r) => [
    r.mode.displayName,
    [r.provider, r.model].where((s) => s.trim().isNotEmpty).join(' '),
    if (r.sentAt != null) relativeTime(r.sentAt),
    r.status.displayName,
  ].where((s) => s.isNotEmpty).join(', ');

  @override
  Widget build(BuildContext context) {
    return const Scaffold(
      appBar: SubpageAppBar(fallback: '/you', title: Text('You')),
      body: _SentBody(),
    );
  }
}

class _SentBody extends StatefulWidget {
  const _SentBody();

  @override
  State<_SentBody> createState() => _SentBodyState();
}

class _SentBodyState extends State<_SentBody> {
  bool _asked = false;
  String? _confirming;

  void _loadOnce() {
    if (_asked) return;
    _asked = true;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) context.read<AssistantLookupProvider>().loadHistory();
    });
  }

  Future<void> _delete(String id) async {
    final lookups = context.read<AssistantLookupProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final ok = await lookups.delete(id);
    if (!mounted) return;
    setState(() => _confirming = null);
    showMessage(
      messenger,
      ok ? SentToCloudPage.deletedMessage : (lookups.deleteError ?? ''),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final Widget content;
    if (!LookupFeature.watch(context)) {
      content = SurfaceCard(
        child: Text(
          CloudLookupPage.unavailableLine,
          style: theme.textTheme.bodyMedium,
        ),
      );
    } else {
      _loadOnce();
      final lookups = context.watch<AssistantLookupProvider>();
      if (!lookups.historyLoaded) {
        content = lookups.historyError != null && !lookups.isLoadingHistory
            ? ErrorView(
                title: SentToCloudPage.loadErrorTitle,
                detail: lookups.historyError,
                onRetry: lookups.loadHistory,
              )
            : const LoadingView();
      } else if (lookups.history.isEmpty) {
        content = SurfaceCard(
          child: Text(
            SentToCloudPage.emptyLine,
            style: theme.textTheme.bodyMedium,
          ),
        );
      } else {
        content = Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            for (final r in lookups.history) ...[
              _SentCard(
                key: ValueKey(r.id),
                record: r,
                confirming: _confirming == r.id,
                onAsk: () => setState(() => _confirming = r.id),
                onKeep: () => setState(() => _confirming = null),
                onDelete: () => _delete(r.id),
              ),
              const SizedBox(height: Space.x3),
            ],
          ],
        );
      }
    }
    return PageBody(
      children: [
        const PageHeading(
          title: SentToCloudPage.title,
          subtitle: SentToCloudPage.subtitle,
        ),
        content,
      ],
    );
  }
}

class _SentCard extends StatelessWidget {
  final LookupRecord record;
  final bool confirming;
  final VoidCallback onAsk;
  final VoidCallback onKeep;
  final VoidCallback onDelete;

  const _SentCard({
    super.key,
    required this.record,
    required this.confirming,
    required this.onAsk,
    required this.onKeep,
    required this.onDelete,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Text(
                  SentToCloudPage.meta(record),
                  style: theme.textTheme.bodySmall,
                ),
              ),
              if (!confirming)
                IconButton(
                  tooltip: SentToCloudPage.deleteTooltip,
                  icon: const Icon(Icons.delete_outline),
                  onPressed: onAsk,
                ),
            ],
          ),
          const SizedBox(height: Space.x1),
          SelectableText(record.text, style: theme.textTheme.bodyMedium),
          if (confirming) ...[
            const SizedBox(height: Space.x3),
            Text(
              SentToCloudPage.deleteQuestion,
              style: theme.textTheme.bodyMedium,
            ),
            const SizedBox(height: Space.x2),
            Row(
              mainAxisAlignment: MainAxisAlignment.end,
              children: [
                TextButton(
                  onPressed: onKeep,
                  child: const Text(SentToCloudPage.keepLabel),
                ),
                const SizedBox(width: Space.x2),
                FilledButton.tonal(
                  onPressed: onDelete,
                  child: const Text(SentToCloudPage.deleteLabel),
                ),
              ],
            ),
          ],
        ],
      ),
    );
  }
}
