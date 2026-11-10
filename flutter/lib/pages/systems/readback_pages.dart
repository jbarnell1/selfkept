import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../../models/enums.dart';
import '../../models/systems.dart';
import '../../providers/systems_provider.dart';
import '../../theme/app_theme.dart';
import '../../theme/tokens.dart';
import '../../utils/metric_format.dart';
import '../../widgets/page_body.dart';
import '../../widgets/person_switcher.dart';
import '../../widgets/surface_card.dart';
import '../../widgets/viewer_units.dart';
import 'systems_common.dart';

/// "23:00" in this device's clock.
String _clock(DateTime? at) =>
    at == null ? '' : DateFormat.Hm().format(at.toLocal());

/// "45 min" or "1 h 05".
String _duration(num minutes) =>
    minutes < 60 ? '${minutes.round()} min' : formatSleepMinutes(minutes);

/// Sleep read back night by night, newest first: when, how long, and the
/// stages where the source recorded them.
class SleepNightsPage extends StatefulWidget {
  const SleepNightsPage({super.key});

  static const String title = 'Sleep, night by night';
  static const String loadErrorTitle = 'Sleep did not load.';
  static const String noStagesText = 'No stages recorded';

  static String emptyLine(SleepNights s) => s.from == null || s.to == null
      ? 'No nights recorded yet.'
      : 'No nights recorded from ${spokenDay(s.from!)} to ${spokenDay(s.to!)}.';

  /// "Deep 2 h · REM 3 h 15 · Light 2 h · Awake 15 min".
  static String stagesLine(SleepNight n) {
    if (!n.hasStages) return noStagesText;
    final parts = <String>[
      if (n.deepMinutes != null) 'Deep ${_duration(n.deepMinutes!)}',
      if (n.remMinutes != null) 'REM ${_duration(n.remMinutes!)}',
      if (n.lightMinutes != null) 'Light ${_duration(n.lightMinutes!)}',
      if (n.awakeMinutes != null) 'Awake ${_duration(n.awakeMinutes!)}',
      if (n.unspecifiedMinutes != null)
        'Without a stage ${_duration(n.unspecifiedMinutes!)}',
    ];
    return parts.join(' · ');
  }

  @override
  State<SleepNightsPage> createState() => _SleepNightsPageState();
}

class _SleepNightsPageState extends State<SleepNightsPage> {
  @override
  void initState() {
    super.initState();
    loadAfterFirstFrame(this, () => context.read<SleepNightsProvider>());
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<SleepNightsProvider>();
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    return SubpageFrame(
      onRefresh: provider.load,
      children: [
        const PageHeading(title: SleepNightsPage.title),
        const PersonSwitcher(),
        ...personReadBody(
          context,
          provider,
          errorTitle: SleepNightsPage.loadErrorTitle,
          builder: (s) {
            if (s.nights.isEmpty) {
              return [QuietLine(SleepNightsPage.emptyLine(s))];
            }
            final nights = s.nights.reversed.toList();
            return [
              SurfaceCard(
                padding: const EdgeInsets.symmetric(
                  horizontal: Space.x4,
                  vertical: Space.x2,
                ),
                child: Column(
                  children: [
                    for (var i = 0; i < nights.length; i++) ...[
                      if (i > 0) Divider(height: 1, color: colors.hairline),
                      Padding(
                        key: ValueKey('night-${nights[i].day}'),
                        padding: const EdgeInsets.symmetric(vertical: Space.x3),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Row(
                              children: [
                                Expanded(
                                  child: Text(
                                    DateFormat.MMMEd().format(nights[i].day),
                                    style: theme.textTheme.titleSmall,
                                  ),
                                ),
                                Text(
                                  formatSleepMinutes(nights[i].asleepMinutes),
                                  style: AppTheme.numeric(
                                    context,
                                    base: theme.textTheme.titleSmall,
                                  ),
                                ),
                              ],
                            ),
                            const SizedBox(height: Space.x1),
                            Text(
                              'In bed ${_clock(nights[i].bedtime)} to '
                              '${_clock(nights[i].wakeTime)}, '
                              '${formatSleepMinutes(nights[i].inBedMinutes)}',
                              style: theme.textTheme.bodySmall,
                            ),
                            Text(
                              SleepNightsPage.stagesLine(nights[i]),
                              style: theme.textTheme.bodySmall,
                            ),
                          ],
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ];
          },
        ),
      ],
    );
  }
}

/// Workouts read back: the weekly totals, then each session newest first.
class WorkoutsPage extends StatefulWidget {
  const WorkoutsPage({super.key});

  static const String title = 'Workouts';
  static const String weeksTitle = 'By week';
  static const String sessionsTitle = 'Sessions';
  static const String loadErrorTitle = 'Workouts did not load.';

  static String emptyLine(Workouts w) => w.from == null || w.to == null
      ? 'No workouts recorded yet.'
      : 'No workouts recorded from ${spokenDay(w.from!)} to ${spokenDay(w.to!)}.';

  /// "2 workouts, 1 h 45. 1 strength, 1 cardio."
  static String weekLine(WorkoutWeek w) {
    final count = w.count == 1 ? '1 workout' : '${w.count} workouts';
    final kinds = <String>[
      if (w.strength > 0) '${w.strength} strength',
      if (w.cardio > 0) '${w.cardio} cardio',
      if (w.mixed > 0) '${w.mixed} mixed',
    ];
    final tail = kinds.isEmpty ? '' : ' ${kinds.join(', ')}.';
    return '$count, ${_duration(w.minutes)}.$tail';
  }

  /// "45 min · 7.2 km · 480 kcal · average 150 bpm, top 171 bpm".
  static String detailLine(WorkoutRead w, Units units) {
    final parts = <String>[_duration(w.durationMinutes)];
    final distance = w.distanceM;
    if (distance != null && distance > 0) {
      parts.add(
        units == Units.imperial
            ? '${(distance / 1609.344).toStringAsFixed(1)} mi'
            : '${(distance / 1000).toStringAsFixed(1)} km',
      );
    }
    if (w.energyKcal != null) parts.add('${w.energyKcal!.round()} kcal');
    final avg = w.avgHr;
    final max = w.maxHr;
    if (avg != null && max != null) {
      parts.add('average ${avg.round()} bpm, top ${max.round()} bpm');
    } else if (avg != null) {
      parts.add('average ${avg.round()} bpm');
    }
    return parts.join(' · ');
  }

  @override
  State<WorkoutsPage> createState() => _WorkoutsPageState();
}

class _WorkoutsPageState extends State<WorkoutsPage> {
  @override
  void initState() {
    super.initState();
    loadAfterFirstFrame(this, () => context.read<WorkoutsProvider>());
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<WorkoutsProvider>();
    final units = viewerUnitsOf(context);
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    return SubpageFrame(
      onRefresh: provider.load,
      children: [
        const PageHeading(title: WorkoutsPage.title),
        const PersonSwitcher(),
        ...personReadBody(
          context,
          provider,
          errorTitle: WorkoutsPage.loadErrorTitle,
          builder: (w) {
            if (w.workouts.isEmpty) {
              return [QuietLine(WorkoutsPage.emptyLine(w))];
            }
            final weeks = w.weeks.reversed.toList();
            final sessions = w.workouts.reversed.toList();
            return [
              if (weeks.isNotEmpty) ...[
                const SectionHeader(title: WorkoutsPage.weeksTitle),
                SurfaceCard(
                  padding: const EdgeInsets.symmetric(
                    horizontal: Space.x4,
                    vertical: Space.x2,
                  ),
                  child: Column(
                    children: [
                      for (var i = 0; i < weeks.length; i++) ...[
                        if (i > 0) Divider(height: 1, color: colors.hairline),
                        Padding(
                          padding: const EdgeInsets.symmetric(
                            vertical: Space.x3,
                          ),
                          child: Row(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              SizedBox(
                                width: 96,
                                child: Text(
                                  'Week of ${shortDay(weeks[i].weekOf)}',
                                  style: theme.textTheme.titleSmall,
                                ),
                              ),
                              const SizedBox(width: Space.x3),
                              Expanded(
                                child: Text(
                                  WorkoutsPage.weekLine(weeks[i]),
                                  style: theme.textTheme.bodyMedium,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ],
                  ),
                ),
              ],
              const SectionHeader(title: WorkoutsPage.sessionsTitle),
              SurfaceCard(
                padding: const EdgeInsets.symmetric(
                  horizontal: Space.x4,
                  vertical: Space.x2,
                ),
                child: Column(
                  children: [
                    for (var i = 0; i < sessions.length; i++) ...[
                      if (i > 0) Divider(height: 1, color: colors.hairline),
                      Padding(
                        key: ValueKey('workout-${sessions[i].id}'),
                        padding: const EdgeInsets.symmetric(vertical: Space.x3),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Row(
                              children: [
                                Expanded(
                                  child: Text(
                                    sessions[i].typeLabel,
                                    style: theme.textTheme.titleSmall,
                                  ),
                                ),
                                Text(
                                  DateFormat.MMMEd().format(sessions[i].day),
                                  style: theme.textTheme.bodySmall,
                                ),
                              ],
                            ),
                            const SizedBox(height: Space.x1),
                            Text(
                              WorkoutsPage.detailLine(sessions[i], units),
                              style: theme.textTheme.bodySmall,
                            ),
                          ],
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ];
          },
        ),
      ],
    );
  }
}
