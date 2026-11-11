import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/household_overview.dart';
import '../../providers/systems_provider.dart';
import '../../theme/tokens.dart';
import '../../utils/body_units.dart';
import '../../utils/metric_format.dart';
import '../../utils/relative_time.dart';
import '../../widgets/page_body.dart';
import '../../widgets/status_views.dart';
import '../../widgets/surface_card.dart';
import '../../widgets/viewer_units.dart';
import '../systems/systems_common.dart';

/// Everyone in the household at a glance: you first, then each member with
/// only the summaries they share with you. A part they do not share is not
/// on the page at all.
class HouseholdOverviewPage extends StatefulWidget {
  const HouseholdOverviewPage({super.key});

  static const String title = 'Everyone at a glance';
  static const String subtitle =
      'Each person with what they share with you, nothing more.';
  static const String loadErrorTitle = 'The household did not load.';
  static const String noHousehold = 'You are not in a household yet.';
  static const String nothingShared = 'Shares nothing with you yet.';
  static const String youLabel = 'You';

  /// "Shares: activity, sleep".
  static String sharesLine(MemberOverview m) =>
      'Shares: ${m.sharedCategories.map((c) => c.displayName.toLowerCase()).join(', ')}';

  @override
  State<HouseholdOverviewPage> createState() => _HouseholdOverviewPageState();
}

class _HouseholdOverviewPageState extends State<HouseholdOverviewPage> {
  @override
  void initState() {
    super.initState();
    loadAfterFirstFrame(this, () => context.read<HouseholdOverviewProvider>());
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<HouseholdOverviewProvider>();
    final data = provider.data;
    final List<Widget> body;
    if (provider.notShared) {
      body = const [QuietLine(HouseholdOverviewPage.noHousehold)];
    } else if (data == null && provider.error == null) {
      body = const [LoadingView()];
    } else if (data == null) {
      body = [
        ErrorView(
          title: HouseholdOverviewPage.loadErrorTitle,
          detail: provider.error,
          onRetry: provider.load,
        ),
      ];
    } else {
      body = [for (final m in data.members) _MemberCard(member: m)];
    }
    return SubpageFrame(
      fallback: '/you',
      backTitle: 'You',
      onRefresh: provider.load,
      children: [
        const PageHeading(
          title: HouseholdOverviewPage.title,
          subtitle: HouseholdOverviewPage.subtitle,
        ),
        ...body,
      ],
    );
  }
}

class _MemberCard extends StatelessWidget {
  final MemberOverview member;

  const _MemberCard({required this.member});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final units = viewerUnitsOf(context);
    final lines = <String>[];
    final labs = member.labs;
    if (labs != null) {
      lines.add(
        labs.latestCollectedOn == null
            ? 'Labs: no panels yet'
            : 'Labs: latest panel ${spokenDay(labs.latestCollectedOn!)}, '
                  '${labs.outOfRange} flagged',
      );
    }
    final body = member.body;
    if (body != null) {
      final parts = <String>[
        if (body.weightKg != null)
          'weight ${BodyUnits.formatMass(body.weightKg!, units)}'
              '${body.weighedOn == null ? '' : ' on ${shortDay(body.weighedOn!)}'}',
        if (body.mainGoal != null) 'goal: ${body.mainGoal}',
        if (body.activeTrials > 0)
          body.activeTrials == 1
              ? '1 trial running'
              : '${body.activeTrials} trials running',
      ];
      lines.add(
        parts.isEmpty ? 'Body: nothing recent' : 'Body: ${parts.join(', ')}',
      );
    }
    String value(String label, OverviewValue? v) {
      if (v?.value == null) return '$label: nothing recent';
      final text = formatMetricWithUnit(v!.metric, v.value!, unit: v.unit);
      return v.day == null
          ? '$label: $text'
          : '$label: $text on ${shortDay(v.day!)}';
    }

    if (member.sleep != null) lines.add(value('Sleep', member.sleep));
    if (member.activity != null) lines.add(value('Steps', member.activity));
    if (member.vitals != null) {
      lines.add(value('Resting heart rate', member.vitals));
    }
    final insights = member.insights;
    if (insights != null) {
      final top = insights.topSeverity;
      lines.add(
        insights.cards == 0
            ? 'Cards: none lately'
            : 'Cards: ${insights.cards} lately'
                  '${top == null ? '' : ', the highest ${top.displayName.toLowerCase()}'}',
      );
    }
    final checkins = member.checkins;
    if (checkins != null) {
      lines.add(
        checkins.lastAt == null
            ? 'Check-ins: none lately'
            : 'Last check-in ${relativeTimeInSentence(checkins.lastAt)}',
      );
    }

    return Padding(
      padding: const EdgeInsets.only(bottom: Space.x3),
      child: SurfaceCard(
        key: ValueKey('member-${member.personId}'),
        padding: const EdgeInsets.all(Space.x4),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              member.isSelf
                  ? '${member.displayName} (${HouseholdOverviewPage.youLabel})'
                  : member.displayName,
              style: theme.textTheme.titleMedium,
            ),
            if (!member.isSelf) ...[
              const SizedBox(height: Space.x1),
              Text(
                member.sharedCategories.isEmpty
                    ? HouseholdOverviewPage.nothingShared
                    : HouseholdOverviewPage.sharesLine(member),
                style: theme.textTheme.bodySmall,
              ),
            ],
            for (final line in lines)
              Padding(
                padding: const EdgeInsets.only(top: Space.x2),
                child: Text(line, style: theme.textTheme.bodyMedium),
              ),
          ],
        ),
      ),
    );
  }
}
