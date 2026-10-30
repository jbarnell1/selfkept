import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../models/insight.dart';
import '../providers/insights_provider.dart';
import '../theme/tokens.dart';
import '../widgets/clinician_summary_button.dart';
import '../widgets/insight_actions.dart';
import '../widgets/insight_card.dart';
import '../widgets/page_body.dart';
import '../widgets/status_views.dart';

/// Insights: the archive, grouped by day, newest first.
class InsightsPage extends StatefulWidget {
  const InsightsPage({super.key});

  static const String emptySentence =
      'Nothing to point out yet. When something shifts against your own usual, it is noted here.';
  static const String emptyFilteredSentence =
      'Nothing matches this filter in the last 90 days.';

  @override
  State<InsightsPage> createState() => _InsightsPageState();
}

class _InsightsPageState extends State<InsightsPage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final insights = context.read<InsightsProvider>();
      if (!insights.hasLoaded && !insights.isLoading) insights.load();
    });
  }

  @override
  Widget build(BuildContext context) {
    final insights = context.watch<InsightsProvider>();
    final theme = Theme.of(context);

    final List<Widget> body;
    if (!insights.hasLoaded && insights.isLoading) {
      body = const [
        Padding(
          padding: EdgeInsets.symmetric(vertical: Space.x8),
          child: LoadingView(),
        ),
      ];
    } else if (!insights.hasLoaded) {
      body = [
        ErrorView(
          title: 'Insights did not load.',
          detail: insights.error,
          onRetry: insights.load,
        ),
      ];
    } else {
      final days = insights.byDay;
      final filtered = insights.severity != null || insights.includeDismissed;
      body = [
        const _Filters(),
        const SizedBox(height: Space.x6),
        if (days.isEmpty)
          Padding(
            padding: const EdgeInsets.only(top: Space.x4),
            child: Text(
              filtered && insights.insights.isNotEmpty
                  ? InsightsPage.emptyFilteredSentence
                  : InsightsPage.emptySentence,
              style: theme.textTheme.headlineSmall,
            ),
          )
        else
          for (final group in days) ...[
            Padding(
              padding: const EdgeInsets.only(bottom: Space.x3, top: Space.x2),
              child: Text(
                _dayLabel(group.day),
                style: theme.textTheme.labelMedium?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
            ),
            for (final insight in group.insights) ...[
              InsightCard(
                key: ValueKey('archive-${insight.id}'),
                insight: insight,
                animateIn: false,
                onReact: (r) => reactToInsight(context, insight, r),
                onDismiss: insight.isDismissed
                    ? null
                    : () => dismissInsight(context, insight),
              ),
              const SizedBox(height: Space.x3),
            ],
            const SizedBox(height: Space.x3),
          ],
      ];
    }

    return SafeArea(
      child: RefreshIndicator(
        onRefresh: insights.load,
        color: theme.colorScheme.primary,
        child: PageBody(
          physics: const AlwaysScrollableScrollPhysics(),
          children: [
            const PageHeading(
              title: 'Insights',
              subtitle: 'What changed, against your own baseline.',
            ),
            ...body,
            const SizedBox(height: Space.x6),
            const ClinicianSummaryButton(),
          ],
        ),
      ),
    );
  }

  static String _dayLabel(DateTime day) {
    final now = DateTime.now();
    final today = DateTime(now.year, now.month, now.day);
    final diff = today.difference(day).inDays;
    if (diff == 0) return 'Today';
    if (diff == 1) return 'Yesterday';
    if (diff < 7) return DateFormat.EEEE().format(day);
    return DateFormat.MMMMEEEEd().format(day);
  }
}

/// Severity chips and the dismissed toggle.
class _Filters extends StatelessWidget {
  const _Filters();

  @override
  Widget build(BuildContext context) {
    final insights = context.watch<InsightsProvider>();
    return Wrap(
      spacing: Space.x2,
      runSpacing: Space.x2,
      crossAxisAlignment: WrapCrossAlignment.center,
      children: [
        ChoiceChip(
          label: const Text('All'),
          selected: insights.severity == null,
          showCheckmark: false,
          onSelected: (_) => insights.setSeverity(null),
        ),
        for (final s in InsightSeverity.values)
          ChoiceChip(
            label: Text(s.displayName),
            selected: insights.severity == s,
            showCheckmark: false,
            onSelected: (_) => insights.setSeverity(s),
          ),
        FilterChip(
          label: const Text('Include dismissed'),
          selected: insights.includeDismissed,
          onSelected: insights.setIncludeDismissed,
        ),
      ],
    );
  }
}
