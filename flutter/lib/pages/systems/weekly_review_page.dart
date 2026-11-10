import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/date_only.dart';
import '../../models/weekly_review.dart';
import '../../providers/systems_provider.dart';
import '../../providers/systems_scope.dart';
import '../../theme/tokens.dart';
import '../../widgets/insight_card.dart';
import '../../widgets/page_body.dart';
import '../../widgets/person_switcher.dart';
import '../../widgets/surface_card.dart';
import 'systems_common.dart';

/// The week in review, Monday to Sunday: targets kept and missed, goals,
/// sleep, what stood out, the week's cards and patterns. Every line is the
/// server's; a section it leaves out is not shared.
class WeeklyReviewPage extends StatefulWidget {
  /// Today on this device, replaceable for tests.
  final DateTime Function() today;

  const WeeklyReviewPage({super.key, this.today = localToday});

  static const String title = 'Week in review';
  static const String loadErrorTitle = 'The review did not load.';
  static const String notSharedLine = 'Not shared with you.';
  static const String notOverLine = 'This week is not over yet.';
  static const String previousTooltip = 'The week before';
  static const String nextTooltip = 'The week after';
  static const String targetsTitle = 'Targets';
  static const String goalsTitle = 'Goals';
  static const String sleepTitle = 'Sleep';
  static const String standoutsTitle = 'What stood out';
  static const String cardsTitle = 'Cards';
  static const String patternsTitle = 'Patterns';
  static const String keptLabel = 'Kept';
  static const String missedLabel = 'Missed';
  static const String unknownLabel = 'Not known';
  static const String noTargets = 'No targets this week.';
  static const String noGoals = 'No goals set.';
  static const String noStandouts = 'Nothing stood out this week.';
  static const String noCards = 'No cards this week.';
  static const String noPatterns = 'No patterns yet.';

  /// "Nov 2 to Nov 8, 2026".
  static String rangeFor(WeeklyReview r) =>
      '${shortDay(r.weekOf)} to ${spokenDay(r.weekEnd)}';

  /// "6 of 7 days".
  static String targetLine(ReviewTarget t) => t.knownDays == 0
      ? 'No days known'
      : '${t.metDays} of ${t.knownDays} days';

  @override
  State<WeeklyReviewPage> createState() => _WeeklyReviewPageState();
}

class _WeeklyReviewPageState extends State<WeeklyReviewPage> {
  @override
  void initState() {
    super.initState();
    loadAfterFirstFrame(this, () => context.read<WeeklyReviewProvider>());
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<WeeklyReviewProvider>();
    final review = provider.data;
    final theme = Theme.of(context);
    return SubpageFrame(
      onRefresh: provider.load,
      children: [
        const PageHeading(title: WeeklyReviewPage.title),
        const PersonSwitcher(),
        if (review != null)
          Padding(
            padding: const EdgeInsets.only(bottom: Space.x4),
            child: Row(
              children: [
                IconButton(
                  tooltip: WeeklyReviewPage.previousTooltip,
                  onPressed: provider.isLoading ? null : provider.previousWeek,
                  icon: const Icon(Icons.chevron_left),
                ),
                Expanded(
                  child: Text(
                    WeeklyReviewPage.rangeFor(review),
                    textAlign: TextAlign.center,
                    style: theme.textTheme.titleMedium,
                  ),
                ),
                IconButton(
                  tooltip: WeeklyReviewPage.nextTooltip,
                  onPressed:
                      provider.isLoading ||
                          !provider.canGoForward(widget.today())
                      ? null
                      : provider.nextWeek,
                  icon: const Icon(Icons.chevron_right),
                ),
              ],
            ),
          ),
        ...personReadBody(
          context,
          provider,
          errorTitle: WeeklyReviewPage.loadErrorTitle,
          builder: (r) => _sections(context, r),
        ),
      ],
    );
  }

  List<Widget> _sections(BuildContext context, WeeklyReview r) {
    final theme = Theme.of(context);
    Widget card(List<Widget> children) => SurfaceCard(
      padding: const EdgeInsets.all(Space.x4),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: children,
      ),
    );
    Widget paragraph(String text) => Padding(
      padding: const EdgeInsets.only(bottom: Space.x2),
      child: Text(text, style: theme.textTheme.bodyLarge),
    );
    const notShared = QuietLine(WeeklyReviewPage.notSharedLine);

    final targets = r.targets;
    final goals = r.goals;
    final sleep = r.sleep;
    final standouts = r.standouts;
    final insights = r.insights;
    final patterns = r.patterns;

    return [
      if (!r.complete) const QuietLine(WeeklyReviewPage.notOverLine),
      const SectionHeader(title: WeeklyReviewPage.targetsTitle),
      if (targets == null)
        notShared
      else if (targets.isEmpty)
        const QuietLine(WeeklyReviewPage.noTargets)
      else
        card([
          ..._targetGroup(context, WeeklyReviewPage.keptLabel, targets.kept),
          ..._targetGroup(
            context,
            WeeklyReviewPage.missedLabel,
            targets.missed,
          ),
          ..._targetGroup(
            context,
            WeeklyReviewPage.unknownLabel,
            targets.unknown,
          ),
        ]),
      const SectionHeader(title: WeeklyReviewPage.goalsTitle),
      if (goals == null)
        notShared
      else if (goals.isEmpty)
        const QuietLine(WeeklyReviewPage.noGoals)
      else
        card([
          for (final g in goals) ...[
            Text(g.title, style: theme.textTheme.titleSmall),
            const SizedBox(height: Space.x1),
            paragraph(g.summary),
          ],
        ]),
      const SectionHeader(title: WeeklyReviewPage.sleepTitle),
      if (sleep == null) notShared else card([paragraph(sleep.summary)]),
      const SectionHeader(title: WeeklyReviewPage.standoutsTitle),
      if (standouts == null)
        notShared
      else if (standouts.isEmpty)
        const QuietLine(WeeklyReviewPage.noStandouts)
      else
        card([for (final s in standouts) paragraph(s.summary)]),
      const SectionHeader(title: WeeklyReviewPage.cardsTitle),
      if (insights == null)
        notShared
      else if (insights.isEmpty)
        const QuietLine(WeeklyReviewPage.noCards)
      else
        for (final i in insights) ...[
          InsightCard(insight: i, animateIn: false),
          const SizedBox(height: Space.x3),
        ],
      const SectionHeader(title: WeeklyReviewPage.patternsTitle),
      if (patterns == null)
        notShared
      else if (patterns.isEmpty)
        const QuietLine(WeeklyReviewPage.noPatterns)
      else
        card([for (final p in patterns) paragraph(p.sentence)]),
    ];
  }

  List<Widget> _targetGroup(
    BuildContext context,
    String label,
    List<ReviewTarget> targets,
  ) {
    if (targets.isEmpty) return const [];
    final theme = Theme.of(context);
    return [
      Padding(
        padding: const EdgeInsets.only(top: Space.x2, bottom: Space.x1),
        child: Text(
          label,
          style: theme.textTheme.labelLarge?.copyWith(
            color: theme.colorScheme.secondary,
          ),
        ),
      ),
      for (final t in targets)
        Padding(
          padding: const EdgeInsets.symmetric(vertical: Space.x1),
          child: Row(
            children: [
              Expanded(child: Text(t.label, style: theme.textTheme.bodyLarge)),
              Text(
                WeeklyReviewPage.targetLine(t),
                style: theme.textTheme.bodySmall,
              ),
            ],
          ),
        ),
    ];
  }
}

/// On Today, on the day a week's review is ready (the Sunday it ends and
/// the Monday after): one small card that opens it.
class WeeklyReviewTodayCard extends StatelessWidget {
  /// Today on this device, replaceable for tests.
  final DateTime Function() today;

  const WeeklyReviewTodayCard({super.key, this.today = localToday});

  static const String title = 'Your week in review is ready';
  static const String line =
      'Targets, sleep and what stood out, Monday to Sunday.';

  static bool readyOn(DateTime day) =>
      day.weekday == DateTime.sunday || day.weekday == DateTime.monday;

  @override
  Widget build(BuildContext context) {
    if (!SystemsFeatures.has(context, SystemsFeatures.contextPatterns) ||
        !readyOn(today())) {
      return const SizedBox.shrink();
    }
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.x6),
      child: LinkCard(
        icon: Icons.event_note_outlined,
        title: title,
        line: line,
        onTap: () => Navigator.of(context).push(
          MaterialPageRoute<void>(builder: (_) => const WeeklyReviewPage()),
        ),
      ),
    );
  }
}
