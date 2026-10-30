import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../models/diagnostics.dart';
import '../providers/recent_problems_provider.dart';
import '../theme/app_theme.dart';
import '../theme/tokens.dart';
import '../utils/relative_time.dart';
import 'page_body.dart';
import 'status_views.dart';
import 'surface_card.dart';

/// The household admin's view of what went wrong lately: the server's own
/// warnings and errors and the ones the apps reported, newest first.
class RecentProblemsCard extends StatefulWidget {
  /// How many entries the card lists.
  static const int shown = 10;

  static const String allClear = 'Nothing went wrong in the last 24 hours.';
  static const String fullLog =
      'Kept in memory since the server last started. The full log is on the '
      'server: docker compose logs app.';

  const RecentProblemsCard({super.key});

  @override
  State<RecentProblemsCard> createState() => _RecentProblemsCardState();
}

class _RecentProblemsCardState extends State<RecentProblemsCard> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final provider = context.read<RecentProblemsProvider>();
      if (provider.problems == null && !provider.loading) provider.load();
    });
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<RecentProblemsProvider>();
    final theme = Theme.of(context);
    final problems = provider.problems;

    final Widget body;
    if (problems == null) {
      body = provider.error == null
          ? const LoadingView()
          : AttentionLine(provider.error ?? '');
    } else {
      final count = problems.last24Hours;
      body = Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            count == 0
                ? RecentProblemsCard.allClear
                : '$count in the last 24 hours.',
            style: theme.textTheme.bodyMedium,
          ),
          for (final entry in problems.items.take(RecentProblemsCard.shown))
            _ProblemTile(entry: entry),
          const SizedBox(height: Space.x3),
          Text(RecentProblemsCard.fullLog, style: theme.textTheme.bodySmall),
        ],
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionHeader(
          title: 'Recent problems',
          trailing: IconButton(
            tooltip: 'Refresh',
            onPressed: provider.loading ? null : provider.load,
            icon: const Icon(Icons.refresh),
          ),
        ),
        SurfaceCard(child: body),
      ],
    );
  }
}

class _ProblemTile extends StatelessWidget {
  final ProblemEntry entry;

  const _ProblemTile({required this.entry});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final detail = entry.detail;
    return ExpansionTile(
      tilePadding: EdgeInsets.zero,
      childrenPadding: const EdgeInsets.only(bottom: Space.x3),
      leading: Icon(
        entry.isError ? Icons.error_outline : Icons.warning_amber_outlined,
        size: 20,
        color: entry.isError ? colors.clinician : colors.attention,
      ),
      title: Text(
        entry.message,
        maxLines: 2,
        overflow: TextOverflow.ellipsis,
        style: theme.textTheme.bodyMedium,
      ),
      subtitle: Text(
        '${entry.source}, ${relativeTimeInSentence(entry.at)}',
        style: theme.textTheme.bodySmall,
      ),
      expandedCrossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SelectableText(entry.message, style: theme.textTheme.bodySmall),
        if (detail != null && detail.isNotEmpty) ...[
          const SizedBox(height: Space.x2),
          SelectableText(
            detail,
            style: AppTheme.numeric(context, base: theme.textTheme.bodySmall),
          ),
        ],
      ],
    );
  }
}
