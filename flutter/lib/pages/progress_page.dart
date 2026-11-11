import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../models/enums.dart';
import '../models/goal.dart';
import '../models/progress.dart';
import '../models/strength.dart';
import '../providers/progress_provider.dart';
import '../providers/viewed_person.dart';
import '../theme/app_theme.dart';
import '../theme/tokens.dart';
import '../utils/body_units.dart';
import '../utils/goal_format.dart';
import '../utils/target_format.dart';
import '../widgets/body_chart.dart';
import '../widgets/body_entry_sheets.dart';
import '../widgets/dialogs.dart';
import '../widgets/key_value_row.dart';
import '../widgets/page_body.dart';
import '../widgets/person_switcher.dart';
import '../widgets/status_views.dart';
import '../widgets/surface_card.dart';
import '../widgets/targets_card.dart';
import '../widgets/viewer_units.dart';

/// Progress: the goals and where they stand, the week's targets, the body
/// numbers that measure them and the strength log. One read from the server.
class ProgressPage extends StatefulWidget {
  const ProgressPage({super.key});

  static const String title = 'Progress';
  static const String subtitle =
      'Your goals and the numbers that measure them.';
  static const String loadErrorTitle = 'Progress did not load.';
  static const String noGoalLine =
      'No goal yet. Pick one of these to start, or set your own under You.';
  static const String noGoalOtherLine = 'No goal set.';
  static const String noBodyLine =
      'No body readings yet. Add a weight, body fat or waist reading, or '
      'connect a scale.';
  static const String noStrengthLine =
      'No sets logged yet. One top set per lift is enough to see whether '
      'strength is holding.';
  static const String manageLabel = 'Manage';
  static const String addReadingLabel = 'Add a reading';
  static const String logSetLabel = 'Log a set';
  static const String startLabel = 'Start this goal';
  static const String startingLabel = 'Starting';
  static const String setAsideNote =
      'Some weigh-ins sat far from their neighbours and were left out.';
  static const String deleteSetTooltip = 'Delete this set';

  /// Body series drawn on the page. Nutrition series are left out: the
  /// app does not count food.
  static const List<String> bodySeries = [
    'body_fat_pct',
    'waist_cm',
    'lean_mass_kg',
    'vo2max',
  ];

  @override
  State<ProgressPage> createState() => _ProgressPageState();
}

class _ProgressPageState extends State<ProgressPage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final progress = context.read<ProgressProvider>();
      if (!progress.hasLoaded && !progress.isLoading) progress.load();
    });
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<ProgressProvider>();
    final theme = Theme.of(context);
    final progress = provider.progress;

    final List<Widget> body;
    if (provider.notShared) {
      final viewing = context.read<ViewedPerson>();
      body = [
        NotSharedView(name: viewing.displayName, onBack: viewing.viewSelf),
      ];
    } else if (progress == null && provider.error == null) {
      body = const [LoadingView()];
    } else if (progress == null) {
      body = [
        ErrorView(
          title: ProgressPage.loadErrorTitle,
          detail: provider.error,
          onRetry: provider.load,
        ),
      ];
    } else {
      body = _sections(context, provider, progress);
    }

    return SafeArea(
      child: RefreshIndicator(
        onRefresh: provider.load,
        color: theme.colorScheme.primary,
        child: PageBody(
          physics: const AlwaysScrollableScrollPhysics(),
          children: [
            const PageHeading(
              title: ProgressPage.title,
              subtitle: ProgressPage.subtitle,
            ),
            const PersonSwitcher(),
            ...body,
          ],
        ),
      ),
    );
  }

  List<Widget> _sections(
    BuildContext context,
    ProgressProvider provider,
    Progress progress,
  ) {
    final theme = Theme.of(context);
    final canEdit = provider.canEdit;
    final units = viewerUnitsOf(context);
    final weight = _series(progress, 'weight_kg');
    final others = [
      for (final key in ProgressPage.bodySeries) ?_series(progress, key),
    ];
    final hasBody =
        weight != null || others.isNotEmpty || progress.body.hasAnything;
    return [
      SectionHeader(
        title: 'Goals',
        trailing: canEdit
            ? TextButton(
                onPressed: () => context.push('/you/goals'),
                child: const Text(ProgressPage.manageLabel),
              )
            : null,
      ),
      if (progress.goals.isNotEmpty)
        for (final g in progress.goals) ...[
          GoalProgressCard(progress: g, units: units),
          const SizedBox(height: Space.x3),
        ]
      else if (canEdit && progress.suggestions.isNotEmpty) ...[
        Text(
          ProgressPage.noGoalLine,
          style: theme.textTheme.bodyMedium?.copyWith(
            color: theme.colorScheme.onSurfaceVariant,
          ),
        ),
        const SizedBox(height: Space.x3),
        for (final s in progress.suggestions) ...[
          SuggestionCard(suggestion: s),
          const SizedBox(height: Space.x3),
        ],
      ] else
        SurfaceCard(
          child: Text(
            ProgressPage.noGoalOtherLine,
            style: theme.textTheme.bodyMedium,
          ),
        ),
      AttentionLine(provider.actionError ?? ''),
      if (progress.targets.isNotEmpty) ...[
        const SectionHeader(title: 'This week\'s targets'),
        SurfaceCard(child: TargetTallyList(targets: progress.targets)),
      ],
      SectionHeader(
        title: 'Body',
        trailing: canEdit
            ? TextButton(
                onPressed: () => MeasurementSheet.open(context),
                child: const Text(ProgressPage.addReadingLabel),
              )
            : null,
      ),
      if (!hasBody)
        SurfaceCard(
          child: Text(
            ProgressPage.noBodyLine,
            style: theme.textTheme.bodyMedium,
          ),
        )
      else ...[
        if (weight != null) ...[
          SurfaceCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text('Weight, smoothed', style: theme.textTheme.titleMedium),
                const SizedBox(height: Space.x3),
                BodyChart(series: weight, units: units),
              ],
            ),
          ),
          const SizedBox(height: Space.x3),
        ],
        _SnapshotCard(body: progress.body, units: units),
        for (final s in others) ...[
          const SizedBox(height: Space.x3),
          SurfaceCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        s.displayName,
                        style: theme.textTheme.titleMedium,
                      ),
                    ),
                    if (s.latest != null)
                      Text(
                        BodyUnits.formatStored(s.latest!, s.unit, units),
                        style: AppTheme.numeric(
                          context,
                          base: theme.textTheme.titleSmall,
                        ),
                      ),
                  ],
                ),
                const SizedBox(height: Space.x3),
                BodyChart(series: s, units: units, height: 120),
              ],
            ),
          ),
        ],
      ],
      SectionHeader(
        title: 'Strength',
        trailing: canEdit
            ? TextButton(
                onPressed: () => StrengthSetSheet.open(context),
                child: const Text(ProgressPage.logSetLabel),
              )
            : null,
      ),
      if (progress.strength.isEmpty)
        SurfaceCard(
          child: Text(
            ProgressPage.noStrengthLine,
            style: theme.textTheme.bodyMedium,
          ),
        )
      else
        for (final lift in progress.strength) ...[
          LiftCard(lift: lift, units: units, canEdit: canEdit),
          const SizedBox(height: Space.x3),
        ],
    ];
  }

  static BodySeries? _series(Progress progress, String key) {
    for (final s in progress.series) {
      if (s.key == key && s.points.isNotEmpty) return s;
    }
    return null;
  }
}

/// One goal with where it stands: its name, the status in plain words, the
/// server's sentence, how far along it is and the pace.
class GoalProgressCard extends StatelessWidget {
  final GoalProgress progress;
  final Units units;

  const GoalProgressCard({
    super.key,
    required this.progress,
    required this.units,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final goal = progress.goal;
    final unit = progress.unit ?? GoalFormat.metricOf(goal)?.unit ?? '';
    final pace = progress.actualPacePerWeek;
    final fraction = progress.fraction;
    final projected = progress.projectedDate;
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            GoalFormat.slotName(goal.slot),
            style: theme.textTheme.labelMedium?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: Space.x1),
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Text(goal.title, style: theme.textTheme.titleLarge),
              ),
              const SizedBox(width: Space.x3),
              PaceStatusLabel(status: progress.paceStatus),
            ],
          ),
          if (goal.startedOn != null) ...[
            const SizedBox(height: Space.x1),
            Text(
              GoalFormat.since(goal.startedOn!),
              style: theme.textTheme.bodySmall,
            ),
          ],
          if (progress.summary.isNotEmpty) ...[
            const SizedBox(height: Space.x3),
            Text(progress.summary, style: theme.textTheme.bodyMedium),
          ],
          if (fraction != null) ...[
            const SizedBox(height: Space.x4),
            Semantics(
              label:
                  '${(fraction.clamp(0, 1) * 100).round()} percent of the way',
              child: ClipRRect(
                borderRadius: Radii.controlRadius,
                child: LinearProgressIndicator(
                  value: fraction.clamp(0, 1).toDouble(),
                  minHeight: 6,
                ),
              ),
            ),
          ],
          if (pace != null || projected != null) ...[
            const SizedBox(height: Space.x3),
            if (pace != null && unit.isNotEmpty)
              KeyValueRow(
                'Pace',
                GoalFormat.pace(pace, unit, units),
                numeric: true,
              ),
            if (projected != null)
              KeyValueRow('Getting there', GoalFormat.projected(projected)),
          ],
        ],
      ),
    );
  }
}

/// The pace status as a small word with a mark beside it. Good news in the
/// primary colour, behind or the wrong way in amber, unknown in soft text.
class PaceStatusLabel extends StatelessWidget {
  final PaceStatus status;

  const PaceStatusLabel({super.key, required this.status});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final color = status.isGood
        ? theme.colorScheme.primary
        : status.needsAttention
        ? colors.attention
        : theme.colorScheme.onSurfaceVariant;
    final icon = switch (status) {
      PaceStatus.reached => Icons.check_circle_outline,
      PaceStatus.onTrack || PaceStatus.ahead => Icons.trending_flat,
      PaceStatus.behind => Icons.schedule,
      PaceStatus.wrongWay => Icons.swap_vert,
      PaceStatus.unknown => Icons.help_outline,
    };
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 16, color: color),
        const SizedBox(width: Space.x1),
        Text(
          status.displayName,
          style: theme.textTheme.labelMedium?.copyWith(color: color),
        ),
      ],
    );
  }
}

/// A goal the server offers, with why and the targets that come with it.
class SuggestionCard extends StatelessWidget {
  final GoalSuggestion suggestion;
  final GoalSlot? slot;

  const SuggestionCard({super.key, required this.suggestion, this.slot});

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<ProgressProvider>();
    final theme = Theme.of(context);
    final units = viewerUnitsOf(context);
    final starting = provider.startingKind == suggestion.kind;
    final metric = GoalFormat.metricFor(suggestion.metricKey);
    final start = suggestion.startValue;
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(suggestion.title, style: theme.textTheme.titleMedium),
          if (start != null && metric != null) ...[
            const SizedBox(height: Space.x1),
            Text(
              'Starting from ${GoalFormat.value(start, metric.unit, units)}',
              style: theme.textTheme.bodySmall,
            ),
          ],
          const SizedBox(height: Space.x2),
          Text(suggestion.why, style: theme.textTheme.bodyMedium),
          if (suggestion.targets.isNotEmpty) ...[
            const SizedBox(height: Space.x3),
            Text('Comes with', style: theme.textTheme.labelMedium),
            const SizedBox(height: Space.x1),
            for (final t in suggestion.targets)
              Text(
                '${t.label}: '
                '${TargetFormat.rule(t.asTarget(), units).toLowerCase()}',
                style: theme.textTheme.bodySmall,
              ),
          ],
          const SizedBox(height: Space.x4),
          Align(
            alignment: Alignment.centerLeft,
            child: OutlinedButton(
              onPressed: provider.isSaving
                  ? null
                  : () async {
                      final messenger = ScaffoldMessenger.of(context);
                      final ok = await provider.startGoal(
                        suggestion,
                        slot: slot,
                      );
                      if (ok) showMessage(messenger, 'Goal started.');
                    },
              child: Text(
                starting ? ProgressPage.startingLabel : ProgressPage.startLabel,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _SnapshotCard extends StatelessWidget {
  final BodySnapshot body;
  final Units units;

  const _SnapshotCard({required this.body, required this.units});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    String mass(double kg) => BodyUnits.formatMass(kg, units);
    String length(double cm) => BodyUnits.formatLength(cm, units);
    final rows = <Widget>[
      if (body.trendWeightKg != null)
        KeyValueRow('Trend weight', mass(body.trendWeightKg!), numeric: true),
      if (body.weightKg != null)
        KeyValueRow(
          'Last weigh-in',
          body.lastWeighedOn == null
              ? mass(body.weightKg!)
              : '${mass(body.weightKg!)}, '
                    '${DateFormat.MMMd().format(body.lastWeighedOn!)}',
          numeric: true,
        ),
      if (body.weeklyRateKg != null)
        KeyValueRow(
          'Pace',
          body.weeklyRatePct == null
              ? GoalFormat.pace(body.weeklyRateKg!, 'kg', units)
              : '${GoalFormat.pace(body.weeklyRateKg!, 'kg', units)} '
                    '(${body.weeklyRatePct!.abs().toStringAsFixed(1)}%)',
          numeric: true,
        ),
      if (body.bodyFatPct != null)
        KeyValueRow(
          'Body fat',
          '${body.bodyFatPct!.toStringAsFixed(1)}%',
          numeric: true,
        ),
      if (body.fatMassKg != null)
        KeyValueRow(
          'Fat mass',
          'about ${mass(body.fatMassKg!)}',
          numeric: true,
        ),
      if (body.leanMassKg != null)
        KeyValueRow(
          'Lean mass',
          'about ${mass(body.leanMassKg!)}',
          numeric: true,
        ),
      if (body.leanShareOfLoss != null)
        KeyValueRow(
          'Lean share of loss',
          '${(body.leanShareOfLoss! * 100).round()}%',
          numeric: true,
        ),
      if (body.waistCm != null)
        KeyValueRow('Waist', length(body.waistCm!), numeric: true),
      if (body.waistToHeight != null)
        KeyValueRow(
          'Waist to height',
          body.waistToHeight!.toStringAsFixed(2),
          numeric: true,
        ),
      if (body.bmi != null)
        KeyValueRow('BMI', body.bmi!.toStringAsFixed(1), numeric: true),
      if (body.leanMassIndex != null)
        KeyValueRow(
          'Lean mass index',
          body.leanMassIndex!.toStringAsFixed(1),
          numeric: true,
        ),
    ];
    if (rows.isEmpty) return const SizedBox.shrink();
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('Where things stand', style: theme.textTheme.titleMedium),
          const SizedBox(height: Space.x3),
          ...rows,
          if (body.bodyFatPct != null) ...[
            const SizedBox(height: Space.x2),
            Text(
              'Scales estimate body fat, so read the direction more than the '
              'number.',
              style: theme.textTheme.bodySmall,
            ),
          ],
          if (body.readingsSetAside > 0) ...[
            const SizedBox(height: Space.x2),
            Text(ProgressPage.setAsideNote, style: theme.textTheme.bodySmall),
          ],
        ],
      ),
    );
  }
}

/// One lift: the best estimated max in the window, the change, and the
/// latest sets with a way to delete them.
class LiftCard extends StatelessWidget {
  final StrengthLift lift;
  final Units units;
  final bool canEdit;

  /// How many of the newest sets are listed.
  static const int shown = 3;

  const LiftCard({
    super.key,
    required this.lift,
    required this.units,
    required this.canEdit,
  });

  static String setLine(StrengthSet set, Units units) {
    final weight = set.weightKg == 0
        ? 'Bodyweight'
        : BodyUnits.formatMass(set.weightKg, units);
    final rpe = set.rpe == null
        ? ''
        : ', effort ${BodyUnits.forField(set.rpe!)}';
    return '$weight x ${set.reps}$rpe';
  }

  Future<void> _delete(BuildContext context, StrengthSet set) async {
    final provider = context.read<ProgressProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final ok = await provider.deleteStrengthSet(set.id);
    showMessage(messenger, ok ? 'Set deleted.' : (provider.actionError ?? ''));
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final best = lift.bestEstimatedMaxKg;
    final change = lift.changePct;
    final recent = lift.sets.reversed.take(shown).toList();
    final numeric = AppTheme.numeric(context, base: theme.textTheme.bodyMedium);
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(lift.liftName, style: theme.textTheme.titleMedium),
              ),
              if (best != null)
                Text(
                  BodyUnits.formatMass(best, units),
                  style: AppTheme.numeric(
                    context,
                    base: theme.textTheme.titleSmall,
                  ),
                ),
            ],
          ),
          const SizedBox(height: Space.x1),
          Text(
            best == null
                ? 'No estimate yet: it needs a weighted set of 12 reps or fewer.'
                : change == null
                ? 'Best estimated one-rep max in the last 120 days.'
                : 'Best estimated one-rep max in the last 120 days. '
                      '${change >= 0 ? 'Up' : 'Down'} '
                      '${change.abs().toStringAsFixed(1)}% from the first set.',
            style: theme.textTheme.bodySmall,
          ),
          const SizedBox(height: Space.x3),
          for (final set in recent)
            Row(
              children: [
                SizedBox(
                  width: 72,
                  child: Text(
                    set.day == null ? '' : DateFormat.MMMd().format(set.day!),
                    style: theme.textTheme.bodySmall,
                  ),
                ),
                Expanded(child: Text(setLine(set, units), style: numeric)),
                if (canEdit)
                  IconButton(
                    onPressed: () => _delete(context, set),
                    icon: const Icon(Icons.close, size: 18),
                    tooltip: ProgressPage.deleteSetTooltip,
                    visualDensity: VisualDensity.compact,
                  ),
              ],
            ),
        ],
      ),
    );
  }
}
