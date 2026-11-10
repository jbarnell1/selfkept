import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../models/enums.dart';
import '../models/target.dart';
import '../providers/tally_provider.dart';
import '../theme/app_theme.dart';
import '../theme/tokens.dart';
import '../utils/target_format.dart';
import 'surface_card.dart';
import 'viewer_units.dart';

/// Today's targets: each one against today, with the week behind it as
/// seven small dots. A day that is still open is never shown as a miss.
class TargetsCard extends StatelessWidget {
  const TargetsCard({super.key});

  static const String title = 'Today\'s targets';
  static const String loadingLine = 'Checking your targets';
  static const String errorLine = 'Your targets did not load.';
  static const String retryTooltip = 'Load targets again';
  static const String emptyLine =
      'No goal yet. Set one and its targets appear here each day.';
  static const String noTargetsLine =
      'Your goal has no targets yet. Add one to see it here each day.';
  static const String setGoalLabel = 'Set a goal';
  static const String addTargetLabel = 'Add a target';

  static const String metText = 'Met';
  static const String notYetText = 'Not yet';
  static const String notMetText = 'Not met';
  static const String openText = 'Still open';
  static const String noReadingText = 'No reading yet';
  static const String nothingLoggedText = 'Nothing logged yet';

  /// The word beside a target for where it stands today. Unknown is said as
  /// unknown, never as a miss.
  static String statusFor(TargetTally tally) {
    final target = tally.target;
    return switch (tally.met) {
      true => metText,
      false =>
        target.period == TargetPeriod.day &&
                target.comparator == TargetComparator.atLeast
            ? notYetText
            : notMetText,
      null =>
        tally.current != null
            ? openText
            : (target.kind == TargetKind.journal
                  ? nothingLoggedText
                  : noReadingText),
    };
  }

  /// The week in words, for the line beside the dots and for a reader. A day
  /// target's dots are days it was met; a week target's dots are the days
  /// that added to it, so "met" would be the wrong word there.
  static String weekFor(TargetTally tally) {
    if (tally.knownDays == 0) return 'No days counted yet';
    final days = tally.knownDays == 1 ? 'day' : 'days';
    return tally.target.period == TargetPeriod.week
        ? 'Logged on ${tally.metDays} of ${tally.knownDays} $days'
        : '${tally.metDays} of ${tally.knownDays} $days met';
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<TallyProvider>();
    final theme = Theme.of(context);
    final tally = provider.tally;

    final Widget body;
    if (tally == null && (provider.isLoading || provider.error == null)) {
      // Quiet on purpose: Today already shows one spinner for the day.
      body = Text(loadingLine, style: theme.textTheme.bodySmall);
    } else if (tally == null) {
      body = Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(errorLine, style: theme.textTheme.bodyMedium),
                if (provider.error != null) ...[
                  const SizedBox(height: Space.x1),
                  Text(provider.error!, style: theme.textTheme.bodySmall),
                ],
              ],
            ),
          ),
          IconButton(
            onPressed: provider.load,
            icon: const Icon(Icons.refresh),
            tooltip: retryTooltip,
          ),
        ],
      );
    } else if (tally.targets.isEmpty) {
      final noGoals = tally.goals.isEmpty;
      body = Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            noGoals ? emptyLine : noTargetsLine,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: Space.x3),
          OutlinedButton(
            onPressed: () => context.go('/you/goals'),
            child: Text(noGoals ? setGoalLabel : addTargetLabel),
          ),
        ],
      );
    } else {
      body = TargetTallyList(targets: tally.targets);
    }

    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(title, style: theme.textTheme.titleMedium),
          const SizedBox(height: Space.x3),
          body,
        ],
      ),
    );
  }
}

/// Targets with where each stands, one under the other. Shared by Today
/// and Progress.
class TargetTallyList extends StatelessWidget {
  final List<TargetTally> targets;

  const TargetTallyList({super.key, required this.targets});

  @override
  Widget build(BuildContext context) {
    final units = viewerUnitsOf(context);
    final colors = AppColors.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (var i = 0; i < targets.length; i++) ...[
          if (i > 0) Divider(height: Space.x6, color: colors.hairline),
          _TargetRow(tally: targets[i], units: units),
        ],
      ],
    );
  }
}

class _TargetRow extends StatelessWidget {
  final TargetTally tally;
  final Units units;

  const _TargetRow({required this.tally, required this.units});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final target = tally.target;
    final met = tally.met == true;
    final soft = theme.colorScheme.onSurfaceVariant;
    final status = TargetsCard.statusFor(tally);
    final standing = TargetFormat.standing(target, tally.current, units);
    final rule = TargetFormat.rule(target, units);
    final week = TargetsCard.weekFor(tally);
    return Semantics(
      container: true,
      label: '${target.label}, $standing, $status. $rule. $week.',
      excludeSemantics: true,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Text(target.label, style: theme.textTheme.titleSmall),
              ),
              const SizedBox(width: Space.x3),
              Text(
                standing,
                style: AppTheme.numeric(
                  context,
                  base: theme.textTheme.titleSmall,
                ),
              ),
            ],
          ),
          const SizedBox(height: Space.x1),
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(child: Text(rule, style: theme.textTheme.bodySmall)),
              const SizedBox(width: Space.x3),
              if (met) ...[
                Icon(Icons.check, size: 16, color: theme.colorScheme.primary),
                const SizedBox(width: Space.x1),
              ],
              Text(
                status,
                style: theme.textTheme.bodySmall?.copyWith(
                  color: met ? theme.colorScheme.primary : soft,
                ),
              ),
            ],
          ),
          const SizedBox(height: Space.x2),
          Row(
            children: [
              WeekDots(week: tally.week),
              const SizedBox(width: Space.x3),
              Expanded(child: Text(week, style: theme.textTheme.bodySmall)),
            ],
          ),
        ],
      ),
    );
  }
}

/// Seven days, oldest first: a filled dot for a day met, a ring for a day
/// not met, a faint dot for a day nothing is known about.
class WeekDots extends StatelessWidget {
  final List<bool?> week;

  const WeekDots({super.key, required this.week});

  static const int days = 7;
  static const double dot = 10;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    // Short lists are padded at the old end so today stays on the right.
    final padded = <bool?>[
      for (var i = week.length; i < days; i++) null,
      ...week.length > days ? week.sublist(week.length - days) : week,
    ];
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        for (var i = 0; i < padded.length; i++) ...[
          if (i > 0) const SizedBox(width: Space.x1),
          _Dot(
            state: padded[i],
            filled: theme.colorScheme.primary,
            ring: theme.colorScheme.onSurfaceVariant,
            faint: colors.hairline,
          ),
        ],
      ],
    );
  }
}

class _Dot extends StatelessWidget {
  final bool? state;
  final Color filled;
  final Color ring;
  final Color faint;

  const _Dot({
    required this.state,
    required this.filled,
    required this.ring,
    required this.faint,
  });

  @override
  Widget build(BuildContext context) {
    final BoxDecoration decoration = switch (state) {
      true => BoxDecoration(color: filled, shape: BoxShape.circle),
      false => BoxDecoration(
        shape: BoxShape.circle,
        border: Border.all(color: ring, width: 1.5),
      ),
      null => BoxDecoration(color: faint, shape: BoxShape.circle),
    };
    return Container(
      width: WeekDots.dot,
      height: WeekDots.dot,
      decoration: decoration,
    );
  }
}
