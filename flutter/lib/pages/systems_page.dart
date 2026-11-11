import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../models/insight.dart';
import '../models/systems.dart';
import '../providers/context_provider.dart';
import '../providers/systems_provider.dart';
import '../providers/systems_scope.dart';
import '../providers/viewed_person.dart';
import '../theme/tokens.dart';
import '../widgets/insight_card.dart';
import '../widgets/page_body.dart';
import '../widgets/person_switcher.dart';
import '../widgets/subpage_app_bar.dart';
import '../widgets/surface_card.dart';
import 'systems/context_section.dart';
import 'systems/systems_common.dart';
import 'trends_page.dart';

/// Systems: the way into Labs, what is going on, each body system with a
/// sentence on where it stands, and the pages behind them. On a server
/// without these sections it stays the Labs entry and the trends list.
class SystemsPage extends StatelessWidget {
  const SystemsPage({super.key});

  static const String subtitle =
      'Your results and your readings, each against your own usual.';
  static const String labsLine =
      'Blood work and other results, marker by marker.';
  static const String updateNote =
      'Body systems, patterns and the weekly review come with a newer server. Update your server to see them here.';
  static const String systemsTitle = 'Your systems';
  static const String emptyLine =
      'Nothing to show yet. Systems fill in as readings and results arrive.';
  static const String loadErrorTitle = 'Systems did not load.';
  static const String moreTitle = 'Looking closer';
  static const String reviewTitle = 'Week in review';
  static const String reviewLine =
      'Targets, sleep and what stood out, Monday to Sunday.';
  static const String patternsTitle = 'Your patterns';
  static const String patternsLine =
      'Nights after a tag in the day log, against the rest.';
  static const String sleepTitle = 'Sleep, night by night';
  static const String sleepLine = 'Times, stages and minutes asleep.';
  static const String workoutsTitle = 'Workouts';
  static const String workoutsLine = 'Each session and the weekly totals.';
  static const String trendsTitle = 'Every trend';
  static const String trendsLine =
      'Each reading against your own usual, one row each.';
  static const String trialsTitle = 'Trying something';
  static const String trialsLine =
      'One change at a time, with a date to read the result.';
  static const String timelineTitle = 'Lab timeline';
  static const String timelineLine =
      'Every panel with how it was drawn and what was taken.';

  /// The Labs card's line from the server's counts.
  static String labsSummary(LabsEntry entry) {
    if (entry.panels == 0 || entry.latestPanelOn == null) {
      return 'No panels yet. $labsLine';
    }
    final panels = entry.panels == 1 ? 'One panel' : '${entry.panels} panels';
    final flagged = entry.outsideRange == 0
        ? 'none flagged high or low'
        : '${entry.outsideRange} of ${entry.markers} flagged high or low';
    return '$panels. The latest, ${spokenDay(entry.latestPanelOn!)}: $flagged.';
  }

  @override
  Widget build(BuildContext context) {
    if (!SystemsFeatures.has(context, SystemsFeatures.contextPatterns)) {
      return const TrendsPage(
        title: 'Systems',
        subtitle: subtitle,
        leading: [
          SystemsLabsEntry(),
          _UpdateNote(),
          SectionHeader(title: 'Trends'),
        ],
      );
    }
    return const _SystemsHome();
  }
}

class _UpdateNote extends StatelessWidget {
  const _UpdateNote();

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(top: Space.x2),
    child: QuietLine(SystemsPage.updateNote),
  );
}

class _SystemsHome extends StatefulWidget {
  const _SystemsHome();

  @override
  State<_SystemsHome> createState() => _SystemsHomeState();
}

class _SystemsHomeState extends State<_SystemsHome> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      context.read<SystemsProvider>().ensureLoaded();
      context.read<ContextProvider>().ensureLoaded();
    });
  }

  Future<void> _refresh() => Future.wait([
    context.read<SystemsProvider>().load(),
    context.read<ContextProvider>().load(),
  ]);

  @override
  Widget build(BuildContext context) {
    final systems = context.watch<SystemsProvider>();
    final self = context.watch<ViewedPerson>().isSelf;
    final labsReview = SystemsFeatures.has(context, SystemsFeatures.labsReview);
    final theme = Theme.of(context);
    final data = systems.data;

    final List<Widget> body;
    if (systems.notShared) {
      body = personReadBody(
        context,
        systems,
        errorTitle: SystemsPage.loadErrorTitle,
        builder: (_) => const [],
      );
    } else {
      body = [
        if (self || data?.labs != null) SystemsLabsEntry(entry: data?.labs),
        const ContextSection(),
        const SectionHeader(title: SystemsPage.systemsTitle),
        ...personReadBody(
          context,
          systems,
          errorTitle: SystemsPage.loadErrorTitle,
          builder: (d) => d.systems.isEmpty
              ? const [QuietLine(SystemsPage.emptyLine)]
              : [for (final s in d.systems) _SystemCard(system: s)],
        ),
        const SectionHeader(title: SystemsPage.moreTitle),
        LinkCard(
          icon: Icons.event_note_outlined,
          title: SystemsPage.reviewTitle,
          line: SystemsPage.reviewLine,
          onTap: () => context.push('/systems/review'),
        ),
        LinkCard(
          icon: Icons.insights_outlined,
          title: SystemsPage.patternsTitle,
          line: SystemsPage.patternsLine,
          onTap: () => context.push('/systems/patterns'),
        ),
        LinkCard(
          icon: Icons.bedtime_outlined,
          title: SystemsPage.sleepTitle,
          line: SystemsPage.sleepLine,
          onTap: () => context.push('/systems/sleep'),
        ),
        LinkCard(
          icon: Icons.directions_run_outlined,
          title: SystemsPage.workoutsTitle,
          line: SystemsPage.workoutsLine,
          onTap: () => context.push('/systems/workouts'),
        ),
        LinkCard(
          icon: Icons.show_chart,
          title: SystemsPage.trendsTitle,
          line: SystemsPage.trendsLine,
          onTap: () => Navigator.of(context).push(
            MaterialPageRoute<void>(
              builder: (_) => const Scaffold(
                appBar: SubpageAppBar(
                  fallback: '/systems',
                  title: Text('Systems'),
                ),
                body: TrendsPage(),
              ),
            ),
          ),
        ),
        if (labsReview) ...[
          LinkCard(
            icon: Icons.science_outlined,
            title: SystemsPage.trialsTitle,
            line: SystemsPage.trialsLine,
            onTap: () => context.push('/systems/trials'),
          ),
          LinkCard(
            icon: Icons.timeline_outlined,
            title: SystemsPage.timelineTitle,
            line: SystemsPage.timelineLine,
            onTap: () => context.push('/labs/timeline'),
          ),
        ],
      ];
    }

    return SafeArea(
      child: RefreshIndicator(
        onRefresh: _refresh,
        color: theme.colorScheme.primary,
        child: PageBody(
          physics: const AlwaysScrollableScrollPhysics(),
          children: [
            const PageHeading(title: 'Systems', subtitle: SystemsPage.subtitle),
            const PersonSwitcher(),
            ...body,
          ],
        ),
      ),
    );
  }
}

/// The way into Labs. Your own record opens the Labs page; someone else's
/// opens their lab timeline, since the Labs page shows only your own.
class SystemsLabsEntry extends StatelessWidget {
  final LabsEntry? entry;

  const SystemsLabsEntry({super.key, this.entry});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final self = context.watch<ViewedPerson>().isSelf;
    final summary = entry;
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.x3),
      child: Semantics(
        button: true,
        child: SurfaceCard(
          onTap: () => context.push(self ? '/labs' : '/labs/timeline'),
          child: Row(
            children: [
              Icon(Icons.science_outlined, color: theme.colorScheme.primary),
              const SizedBox(width: Space.x4),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('Labs', style: theme.textTheme.titleMedium),
                    const SizedBox(height: Space.x1),
                    Text(
                      summary == null
                          ? SystemsPage.labsLine
                          : SystemsPage.labsSummary(summary),
                      style: theme.textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
              const Icon(Icons.chevron_right),
            ],
          ),
        ),
      ),
    );
  }
}

/// One system: its name, the server's sentence on it, and a dot in the
/// colour of its most pressing card.
class _SystemCard extends StatelessWidget {
  final BodySystem system;

  const _SystemCard({required this.system});

  static int _rank(InsightSeverity severity) => switch (severity) {
    InsightSeverity.notice => 0,
    InsightSeverity.watch => 1,
    InsightSeverity.clinician => 2,
  };

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    InsightSeverity? top;
    for (final card in system.insights) {
      if (top == null || _rank(card.severity) > _rank(top)) {
        top = card.severity;
      }
    }
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.x3),
      child: Semantics(
        button: true,
        child: SurfaceCard(
          key: ValueKey('system-${system.key}'),
          padding: const EdgeInsets.all(Space.x4),
          onTap: () => context.push('/systems/${system.key}'),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        if (top != null) ...[
                          Container(
                            width: 8,
                            height: 8,
                            decoration: BoxDecoration(
                              color: InsightCard.colorFor(top, colors),
                              shape: BoxShape.circle,
                            ),
                          ),
                          const SizedBox(width: Space.x2),
                        ],
                        Flexible(
                          child: Text(
                            system.name,
                            style: theme.textTheme.titleMedium,
                          ),
                        ),
                      ],
                    ),
                    if (system.state.isNotEmpty) ...[
                      const SizedBox(height: Space.x1),
                      Text(system.state, style: theme.textTheme.bodyMedium),
                    ],
                  ],
                ),
              ),
              const Icon(Icons.chevron_right),
            ],
          ),
        ),
      ),
    );
  }
}
