import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../../models/enums.dart';
import '../../models/goal.dart';
import '../../models/target.dart';
import '../../providers/goals_provider.dart';
import '../../providers/journal_provider.dart';
import '../../providers/progress_provider.dart';
import '../../providers/viewed_person.dart';
import '../../theme/tokens.dart';
import '../../utils/goal_format.dart';
import '../../utils/target_format.dart';
import '../../widgets/dialogs.dart';
import '../../widgets/key_value_row.dart';
import '../../widgets/page_body.dart';
import '../../widgets/person_switcher.dart';
import '../../widgets/status_views.dart';
import '../../widgets/subpage_app_bar.dart';
import '../../widgets/surface_card.dart';
import '../../widgets/viewer_units.dart';
import '../progress_page.dart';
import 'goal_forms.dart';

/// Goals: a main and a second goal, and the targets that make them
/// something to do each day.
class GoalsPage extends StatefulWidget {
  const GoalsPage({super.key});

  static const String title = 'Goals';
  static const String subtitle =
      'One main goal and one second goal at a time, and the targets that go '
      'with them.';
  static const String loadErrorTitle = 'Your goals did not load.';
  static const String ideasTitle = 'Ideas to start from';
  static const String noTargetsLine =
      'No targets yet. A target is something to reach or stay under each day '
      'or each week.';
  static const String addTargetLabel = 'Add a target';
  static const String editLabel = 'Edit';
  static const String endLabel = 'End';
  static const String endTitle = 'End this goal?';
  static const String endBody =
      'Its targets stay, and you can keep, change or remove them.';
  static const String doneLabel = 'Mark as done';
  static const String dropLabel = 'Drop it';
  static const String keepLabel = 'Keep going';
  static const String removeTargetMessage = 'Remove this target?';
  static const String pausedText = 'Paused';
  static const String moveUpTooltip = 'Move up';
  static const String moveDownTooltip = 'Move down';
  static const String editTargetTooltip = 'Edit target';
  static const String removeTargetTooltip = 'Remove target';

  static String emptySlot(GoalSlot slot) => switch (slot) {
    GoalSlot.main => 'No main goal yet.',
    GoalSlot.secondary => 'No second goal. One is enough for most people.',
  };

  static String setLabel(GoalSlot slot) => switch (slot) {
    GoalSlot.main => 'Set a main goal',
    GoalSlot.secondary => 'Set a second goal',
  };

  @override
  State<GoalsPage> createState() => _GoalsPageState();
}

class _GoalsPageState extends State<GoalsPage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final goals = context.read<GoalsProvider>();
      if (!goals.hasLoaded && !goals.isLoading) goals.load();
      // The suggestions come with Progress.
      final progress = context.read<ProgressProvider>();
      if (!progress.hasLoaded && !progress.isLoading) progress.load();
      final journal = context.read<JournalProvider>();
      if (journal.allTags.isEmpty) journal.loadTags();
    });
  }

  @override
  Widget build(BuildContext context) {
    final goals = context.watch<GoalsProvider>();
    final List<Widget> body;
    if (goals.notShared) {
      final viewing = context.read<ViewedPerson>();
      body = [
        NotSharedView(name: viewing.displayName, onBack: viewing.viewSelf),
      ];
    } else if (!goals.hasLoaded && goals.error == null) {
      body = const [LoadingView()];
    } else if (!goals.hasLoaded) {
      body = [
        ErrorView(
          title: GoalsPage.loadErrorTitle,
          detail: goals.error,
          onRetry: goals.load,
        ),
      ];
    } else {
      body = _sections(context, goals);
    }
    return Scaffold(
      appBar: const SubpageAppBar(fallback: '/you', title: Text('You')),
      body: SafeArea(
        child: PageBody(
          children: [
            const PageHeading(
              title: GoalsPage.title,
              subtitle: GoalsPage.subtitle,
            ),
            const PersonSwitcher(),
            ...body,
          ],
        ),
      ),
    );
  }

  List<Widget> _sections(BuildContext context, GoalsProvider goals) {
    final theme = Theme.of(context);
    final canEdit = goals.canEdit;
    final suggestions = canEdit
        ? (context.watch<ProgressProvider>().progress?.suggestions ?? const [])
        : const <GoalSuggestion>[];
    GoalSlot? freeSlot;
    for (final slot in GoalSlot.values) {
      if (goals.goalIn(slot) == null) {
        freeSlot = slot;
        break;
      }
    }
    return [
      for (final slot in GoalSlot.values) ...[
        SectionHeader(title: GoalFormat.slotName(slot)),
        if (goals.goalIn(slot) case final goal?)
          _GoalCard(goal: goal, canEdit: canEdit)
        else
          SurfaceCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  GoalsPage.emptySlot(slot),
                  style: theme.textTheme.bodyMedium,
                ),
                if (canEdit) ...[
                  const SizedBox(height: Space.x3),
                  OutlinedButton(
                    onPressed: () => GoalEditor.open(context, slot: slot),
                    child: Text(GoalsPage.setLabel(slot)),
                  ),
                ],
              ],
            ),
          ),
      ],
      if (freeSlot != null && suggestions.isNotEmpty) ...[
        const SectionHeader(title: GoalsPage.ideasTitle),
        for (final s in suggestions) ...[
          SuggestionCard(suggestion: s, slot: freeSlot),
          const SizedBox(height: Space.x3),
        ],
      ],
      SectionHeader(
        title: 'Targets',
        trailing: canEdit
            ? TextButton(
                onPressed: () => TargetEditor.open(
                  context,
                  goalId: goals.goalIn(GoalSlot.main)?.id,
                ),
                child: const Text(GoalsPage.addTargetLabel),
              )
            : null,
      ),
      SurfaceCard(
        child: goals.targets.isEmpty
            ? Text(GoalsPage.noTargetsLine, style: theme.textTheme.bodyMedium)
            : _TargetList(goals: goals, canEdit: canEdit),
      ),
      AttentionLine(goals.saveError ?? ''),
    ];
  }
}

class _GoalCard extends StatelessWidget {
  final Goal goal;
  final bool canEdit;

  const _GoalCard({required this.goal, required this.canEdit});

  Future<void> _end(BuildContext context) async {
    final goals = context.read<GoalsProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final status = await showDialog<GoalStatus>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text(GoalsPage.endTitle),
        content: const Text(GoalsPage.endBody),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(),
            child: const Text(GoalsPage.keepLabel),
          ),
          TextButton(
            onPressed: () => Navigator.of(context).pop(GoalStatus.dropped),
            child: const Text(GoalsPage.dropLabel),
          ),
          TextButton(
            onPressed: () => Navigator.of(context).pop(GoalStatus.done),
            child: const Text(GoalsPage.doneLabel),
          ),
        ],
      ),
    );
    if (status == null) return;
    final ok = await goals.endGoal(goal.id, status);
    if (ok) {
      showMessage(
        messenger,
        status == GoalStatus.done ? 'Marked as done.' : 'Goal dropped.',
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final units = viewerUnitsOf(context);
    final metric = GoalFormat.metricOf(goal);
    String shown(double v) =>
        metric == null ? v.toString() : GoalFormat.value(v, metric.unit, units);
    final dates = DateFormat.yMMMd();
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(goal.title, style: theme.textTheme.titleLarge),
          if (goal.title != goal.kind.displayName) ...[
            const SizedBox(height: Space.x1),
            Text(goal.kind.displayName, style: theme.textTheme.bodySmall),
          ],
          const SizedBox(height: Space.x3),
          KeyValueRow('Measured by', metric?.displayName ?? 'Its targets'),
          if (goal.startValue != null)
            KeyValueRow(
              'Starting from',
              shown(goal.startValue!),
              numeric: true,
            ),
          if (goal.targetValue != null)
            KeyValueRow('Aiming for', shown(goal.targetValue!), numeric: true),
          if (goal.pacePerWeek != null && metric != null)
            KeyValueRow(
              'Planned pace',
              GoalFormat.pace(goal.pacePerWeek!, metric.unit, units),
              numeric: true,
            ),
          if (goal.startedOn != null)
            KeyValueRow(
              'Started',
              dates.format(goal.startedOn!),
              numeric: true,
            ),
          if (goal.targetDate != null)
            KeyValueRow('By', dates.format(goal.targetDate!), numeric: true),
          if (canEdit) ...[
            const SizedBox(height: Space.x3),
            Wrap(
              spacing: Space.x2,
              children: [
                OutlinedButton(
                  onPressed: () => GoalEditor.open(context, goal: goal),
                  child: const Text(GoalsPage.editLabel),
                ),
                TextButton(
                  onPressed: () => _end(context),
                  child: const Text(GoalsPage.endLabel),
                ),
              ],
            ),
          ],
        ],
      ),
    );
  }
}

class _TargetList extends StatelessWidget {
  final GoalsProvider goals;
  final bool canEdit;

  const _TargetList({required this.goals, required this.canEdit});

  Future<void> _remove(BuildContext context, Target target) async {
    final ok = await confirmAction(
      context,
      message: GoalsPage.removeTargetMessage,
      confirmLabel: 'Remove',
    );
    if (!ok || !context.mounted) return;
    final messenger = ScaffoldMessenger.of(context);
    if (await goals.deleteTarget(target.id)) {
      showMessage(messenger, 'Target removed.');
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final units = viewerUnitsOf(context);
    final targets = goals.targets;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (var i = 0; i < targets.length; i++) ...[
          if (i > 0) Divider(height: Space.x6, color: colors.hairline),
          _TargetRow(
            key: ValueKey('target-${targets[i].id}'),
            target: targets[i],
            goalTitle: goals.goalById(targets[i].goalId)?.title,
            units: units,
            canEdit: canEdit,
            first: i == 0,
            last: i == targets.length - 1,
            onUp: () => goals.moveTarget(targets[i].id, -1),
            onDown: () => goals.moveTarget(targets[i].id, 1),
            onEdit: () => TargetEditor.open(context, target: targets[i]),
            onRemove: () => _remove(context, targets[i]),
            soft: theme.colorScheme.onSurfaceVariant,
          ),
        ],
      ],
    );
  }
}

class _TargetRow extends StatelessWidget {
  final Target target;
  final String? goalTitle;
  final Units units;
  final bool canEdit;
  final bool first;
  final bool last;
  final VoidCallback onUp;
  final VoidCallback onDown;
  final VoidCallback onEdit;
  final VoidCallback onRemove;
  final Color soft;

  const _TargetRow({
    super.key,
    required this.target,
    required this.goalTitle,
    required this.units,
    required this.canEdit,
    required this.first,
    required this.last,
    required this.onUp,
    required this.onDown,
    required this.onEdit,
    required this.onRemove,
    required this.soft,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final detail = [
      TargetFormat.rule(target, units),
      ?goalTitle,
      if (!target.active) GoalsPage.pausedText,
    ].join('. ');
    return Row(
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                target.label,
                style: theme.textTheme.titleSmall?.copyWith(
                  color: target.active ? null : soft,
                ),
              ),
              const SizedBox(height: Space.x1),
              Text(detail, style: theme.textTheme.bodySmall),
            ],
          ),
        ),
        if (canEdit) ...[
          IconButton(
            onPressed: first ? null : onUp,
            icon: const Icon(Icons.arrow_upward, size: 18),
            tooltip: GoalsPage.moveUpTooltip,
            visualDensity: VisualDensity.compact,
          ),
          IconButton(
            onPressed: last ? null : onDown,
            icon: const Icon(Icons.arrow_downward, size: 18),
            tooltip: GoalsPage.moveDownTooltip,
            visualDensity: VisualDensity.compact,
          ),
          IconButton(
            onPressed: onEdit,
            icon: const Icon(Icons.edit_outlined, size: 18),
            tooltip: GoalsPage.editTargetTooltip,
            visualDensity: VisualDensity.compact,
          ),
          IconButton(
            onPressed: onRemove,
            icon: const Icon(Icons.close, size: 18),
            tooltip: GoalsPage.removeTargetTooltip,
            visualDensity: VisualDensity.compact,
          ),
        ],
      ],
    );
  }
}
