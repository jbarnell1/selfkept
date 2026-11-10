import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../../models/date_only.dart';
import '../../models/enums.dart';
import '../../models/goal.dart';
import '../../models/journal.dart';
import '../../models/target.dart';
import '../../providers/goals_provider.dart';
import '../../providers/journal_provider.dart';
import '../../theme/tokens.dart';
import '../../utils/body_units.dart';
import '../../utils/goal_format.dart';
import '../../widgets/status_views.dart';
import '../../widgets/subpage_app_bar.dart';
import '../../widgets/viewer_units.dart';

/// Reads a number field: null when empty, [invalid] when it is not a number.
const double _invalid = double.nan;
double? _readNumber(TextEditingController c) {
  if (c.text.trim().isEmpty) return null;
  return BodyUnits.parse(c.text) ?? _invalid;
}

/// Starts or edits a goal. Opened as a full page; pops true once saved.
class GoalEditor extends StatefulWidget {
  /// Null to start a new goal.
  final Goal? goal;

  /// The slot a new goal goes in.
  final GoalSlot slot;

  const GoalEditor({super.key, this.goal, this.slot = GoalSlot.main});

  static const String newTitle = 'New goal';
  static const String editTitle = 'Edit goal';
  static const String titleLabel = 'In your own words (optional)';
  static const String titleRequired = 'Your goal needs a title.';
  static const String measuredByLabel = 'Measured by';
  static const String usualMetric = 'The usual for this goal';
  static const String noMetric = 'Nothing: the targets show how it goes';
  static const String startLabel = 'Starting from';
  static const String targetLabel = 'Aiming for';
  static const String paceLabel = 'Planned change a week';
  static const String targetDateLabel = 'By (optional)';
  static const String startedLabel = 'Started';
  static const String notANumber = 'Numbers only, for example 82.5.';
  static const String slotNote =
      'Starting a goal in a slot that is taken ends the goal already there.';

  static Future<bool> open(
    BuildContext context, {
    Goal? goal,
    GoalSlot slot = GoalSlot.main,
  }) async {
    final goals = context.read<GoalsProvider>();
    goals.clearSaveError();
    final saved = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        fullscreenDialog: true,
        builder: (_) => ChangeNotifierProvider.value(
          value: goals,
          child: GoalEditor(goal: goal, slot: slot),
        ),
      ),
    );
    return saved ?? false;
  }

  @override
  State<GoalEditor> createState() => _GoalEditorState();
}

/// The metric choice: the kind's usual one, a named one, or none.
sealed class _MetricChoice {
  const _MetricChoice();
}

class _Usual extends _MetricChoice {
  const _Usual();
}

class _Named extends _MetricChoice {
  final GoalMetric metric;
  const _Named(this.metric);
  @override
  bool operator ==(Object other) =>
      other is _Named && other.metric.key == metric.key;
  @override
  int get hashCode => metric.key.hashCode;
}

class _GoalEditorState extends State<GoalEditor> {
  late GoalSlot _slot;
  late GoalKind _kind;
  late _MetricChoice _metric;
  final _title = TextEditingController();
  final _start = TextEditingController();
  final _target = TextEditingController();
  final _pace = TextEditingController();
  DateTime? _startedOn;
  DateTime? _targetDate;
  String _localError = '';
  bool _unitsReady = false;

  @override
  void initState() {
    super.initState();
    final goal = widget.goal;
    _slot = goal?.slot ?? widget.slot;
    _kind = goal?.kind ?? GoalKind.loseFat;
    final named = GoalFormat.metricFor(goal?.metricKey);
    _metric = named == null ? const _Usual() : _Named(named);
    // The server writes the kind's name as the title when none was given.
    final title = goal?.title ?? '';
    _title.text = title == goal?.kind.displayName ? '' : title;
    _startedOn = goal?.startedOn;
    _targetDate = goal?.targetDate;
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_unitsReady) return;
    _unitsReady = true;
    final goal = widget.goal;
    final unit = _unit;
    if (goal == null || unit == null) return;
    final units = readViewerUnits(context);
    String field(double? v) =>
        v == null ? '' : BodyUnits.forField(GoalFormat.toField(v, unit, units));
    _start.text = field(goal.startValue);
    _target.text = field(goal.targetValue);
    _pace.text = field(goal.pacePerWeek?.abs());
  }

  @override
  void dispose() {
    _title.dispose();
    _start.dispose();
    _target.dispose();
    _pace.dispose();
    super.dispose();
  }

  GoalMetric? get _resolvedMetric => switch (_metric) {
    _Usual() => GoalFormat.metricFor(GoalFormat.defaultMetricKey(_kind)),
    _Named(:final metric) => metric,
  };

  String? get _unit => _resolvedMetric?.unit;

  Future<void> _pickDate({required bool target}) async {
    final now = localToday();
    final initial = target
        ? (_targetDate ?? now.add(const Duration(days: 84)))
        : (_startedOn ?? now);
    final picked = await showDatePicker(
      context: context,
      initialDate: initial,
      firstDate: DateTime(now.year - 5),
      lastDate: target ? DateTime(now.year + 5) : now,
      helpText: target ? GoalEditor.targetDateLabel : GoalEditor.startedLabel,
    );
    if (picked == null) return;
    setState(() => target ? _targetDate = picked : _startedOn = picked);
  }

  Future<void> _save() async {
    final title = _title.text.trim();
    if (_kind == GoalKind.custom && title.isEmpty) {
      setState(() => _localError = GoalEditor.titleRequired);
      return;
    }
    final unit = _unit;
    final units = readViewerUnits(context);
    double? stored(double? typed) => typed == null || unit == null
        ? null
        : GoalFormat.fromField(typed, unit, units);
    final start = _readNumber(_start);
    final target = _readNumber(_target);
    final pace = _readNumber(_pace);
    if ([start, target, pace].any((v) => v != null && v.isNaN)) {
      setState(() => _localError = GoalEditor.notANumber);
      return;
    }
    // The pace is typed as an amount; its direction follows the target.
    double? signedPace = pace?.abs();
    if (signedPace != null) {
      final down = start != null && target != null
          ? target < start
          : _kind == GoalKind.loseFat;
      if (down) signedPace = -signedPace;
    }
    setState(() => _localError = '');
    final draft = GoalDraft(
      slot: _slot,
      kind: _kind,
      title: title.isEmpty ? null : title,
      metricKey: switch (_metric) {
        _Usual() => null,
        _Named(:final metric) => metric.key,
      },
      startValue: stored(start),
      targetValue: stored(target),
      pacePerWeek: stored(signedPace),
      startedOn: _startedOn,
      targetDate: _targetDate,
    );
    final goals = context.read<GoalsProvider>();
    final ok = await goals.saveGoal(draft, id: widget.goal?.id);
    if (ok && mounted) Navigator.of(context).pop(true);
  }

  @override
  Widget build(BuildContext context) {
    final goals = context.watch<GoalsProvider>();
    final theme = Theme.of(context);
    final units = viewerUnitsOf(context);
    final unit = _unit;
    final fieldUnit = unit == null ? null : GoalFormat.fieldUnit(unit, units);
    const number = TextInputType.numberWithOptions(decimal: true);
    final isNew = widget.goal == null;
    final dates = DateFormat.yMMMd();
    return Scaffold(
      appBar: SubpageAppBar(
        fallback: '/you/goals',
        title: Text(isNew ? GoalEditor.newTitle : GoalEditor.editTitle),
      ),
      body: SafeArea(
        child: Align(
          alignment: Alignment.topCenter,
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: Space.maxContentWidth),
            child: ListView(
              padding: const EdgeInsets.all(Space.gutter),
              children: [
                if (isNew) ...[
                  Text('Slot', style: theme.textTheme.labelMedium),
                  const SizedBox(height: Space.x2),
                  SegmentedButton<GoalSlot>(
                    segments: [
                      for (final s in GoalSlot.values)
                        ButtonSegment(
                          value: s,
                          label: Text(GoalFormat.slotName(s)),
                        ),
                    ],
                    selected: {_slot},
                    showSelectedIcon: false,
                    onSelectionChanged: (v) => setState(() => _slot = v.first),
                  ),
                  const SizedBox(height: Space.x2),
                  Text(GoalEditor.slotNote, style: theme.textTheme.bodySmall),
                  const SizedBox(height: Space.x6),
                ],
                DropdownButtonFormField<GoalKind>(
                  isExpanded: true,
                  initialValue: _kind,
                  decoration: const InputDecoration(labelText: 'Goal'),
                  items: [
                    for (final k in GoalKind.values)
                      DropdownMenuItem(value: k, child: Text(k.displayName)),
                  ],
                  onChanged: (k) => setState(() => _kind = k ?? _kind),
                ),
                const SizedBox(height: Space.x4),
                TextField(
                  controller: _title,
                  maxLength: 80,
                  textCapitalization: TextCapitalization.sentences,
                  decoration: InputDecoration(
                    labelText: _kind == GoalKind.custom
                        ? 'Title'
                        : GoalEditor.titleLabel,
                  ),
                ),
                const SizedBox(height: Space.x2),
                DropdownButtonFormField<_MetricChoice>(
                  isExpanded: true,
                  initialValue: _metric,
                  decoration: const InputDecoration(
                    labelText: GoalEditor.measuredByLabel,
                  ),
                  items: [
                    DropdownMenuItem(
                      value: const _Usual(),
                      child: Text(
                        GoalFormat.defaultMetricKey(_kind) == null
                            ? GoalEditor.noMetric
                            : '${GoalEditor.usualMetric}: '
                                  '${GoalFormat.metricFor(GoalFormat.defaultMetricKey(_kind))!.displayName.toLowerCase()}',
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                    for (final m in GoalFormat.metrics)
                      DropdownMenuItem(
                        value: _Named(m),
                        child: Text(m.displayName),
                      ),
                  ],
                  onChanged: (m) => setState(() => _metric = m ?? _metric),
                ),
                if (fieldUnit != null) ...[
                  const SizedBox(height: Space.x4),
                  Row(
                    children: [
                      Expanded(
                        child: TextField(
                          controller: _start,
                          keyboardType: number,
                          decoration: InputDecoration(
                            labelText: GoalEditor.startLabel,
                            suffixText: fieldUnit,
                          ),
                        ),
                      ),
                      const SizedBox(width: Space.x4),
                      Expanded(
                        child: TextField(
                          controller: _target,
                          keyboardType: number,
                          decoration: InputDecoration(
                            labelText: GoalEditor.targetLabel,
                            suffixText: fieldUnit,
                          ),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: Space.x4),
                  TextField(
                    controller: _pace,
                    keyboardType: number,
                    decoration: InputDecoration(
                      labelText: GoalEditor.paceLabel,
                      suffixText: fieldUnit,
                      helperText:
                          'Optional. Leave it empty to just follow the trend.',
                    ),
                  ),
                ],
                const SizedBox(height: Space.x6),
                Wrap(
                  spacing: Space.x3,
                  runSpacing: Space.x3,
                  children: [
                    OutlinedButton.icon(
                      onPressed: () => _pickDate(target: false),
                      icon: const Icon(Icons.calendar_today_outlined, size: 18),
                      label: Text(
                        _startedOn == null
                            ? '${GoalEditor.startedLabel}: today'
                            : '${GoalEditor.startedLabel}: ${dates.format(_startedOn!)}',
                      ),
                    ),
                    OutlinedButton.icon(
                      onPressed: () => _pickDate(target: true),
                      icon: const Icon(Icons.event_outlined, size: 18),
                      label: Text(
                        _targetDate == null
                            ? GoalEditor.targetDateLabel
                            : 'By ${dates.format(_targetDate!)}',
                      ),
                    ),
                    if (_targetDate != null)
                      IconButton(
                        onPressed: () => setState(() => _targetDate = null),
                        icon: const Icon(Icons.close),
                        tooltip: 'Clear the date',
                      ),
                  ],
                ),
                AttentionLine(
                  _localError.isNotEmpty
                      ? _localError
                      : (goals.saveError ?? ''),
                ),
                const SizedBox(height: Space.x8),
                FilledButton(
                  onPressed: goals.isSaving ? null : _save,
                  child: Text(goals.isSaving ? 'Saving' : 'Save'),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// A metric a target can count, with its stored unit.
class TargetMetric {
  final String key;
  final String displayName;
  final String unit;

  const TargetMetric(this.key, this.displayName, this.unit);

  /// Sleep is typed in hours and stored in minutes.
  bool get inHours => key == 'sleep_stage';

  static const List<TargetMetric> all = [
    TargetMetric('steps', 'Steps', 'count'),
    TargetMetric('exercise_min', 'Exercise minutes', 'min'),
    TargetMetric('sleep_stage', 'Sleep', 'min'),
    TargetMetric('protein_g', 'Protein', 'g'),
    TargetMetric('weight_kg', 'Weight', 'kg'),
    TargetMetric('body_fat_pct', 'Body fat', '%'),
    TargetMetric('waist_cm', 'Waist', 'cm'),
    TargetMetric('resting_hr_bpm', 'Resting heart rate', 'bpm'),
    TargetMetric('hrv_rmssd_ms', 'HRV', 'ms'),
    TargetMetric('vo2max', 'VO2 max', 'mL/kg/min'),
  ];

  static TargetMetric? find(String? key) {
    for (final m in all) {
      if (m.key == key) return m;
    }
    return null;
  }

  double toField(double stored, Units units) =>
      inHours ? stored / 60 : BodyUnits.fromStored(stored, unit, units);

  double fromField(double typed, Units units) => inHours
      ? typed * 60
      : switch (unit) {
          'kg' => BodyUnits.massToKg(typed, units),
          'cm' => BodyUnits.lengthToCm(typed, units),
          _ => typed,
        };

  String fieldUnit(Units units) {
    if (inHours) return 'hours';
    if (unit == 'count') return '';
    return BodyUnits.displayUnit(unit, units);
  }
}

/// Adds or edits a target, in a bottom sheet. Pops true once saved.
class TargetEditor extends StatefulWidget {
  final Target? target;

  /// The goal a new target belongs to.
  final String? goalId;

  const TargetEditor({super.key, this.target, this.goalId});

  static const String newTitle = 'New target';
  static const String editTitle = 'Edit target';
  static const String labelLabel = 'Name';
  static const String labelHint = 'For example Protein, or No late caffeine';
  static const String needLabel = 'Give the target a name.';
  static const String needValue = 'Type the number to reach or stay under.';
  static const String needMetric = 'Choose what to count.';
  static const String needTag = 'Choose a tag from the day log.';
  static const String activeLabel = 'Count this target';
  static const String goalLabel = 'Belongs to';
  static const String noGoal = 'No goal';

  static Future<bool> open(
    BuildContext context, {
    Target? target,
    String? goalId,
  }) async {
    final goals = context.read<GoalsProvider>();
    final journal = context.read<JournalProvider>();
    goals.clearSaveError();
    final saved = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (_) => MultiProvider(
        providers: [
          ChangeNotifierProvider.value(value: goals),
          ChangeNotifierProvider.value(value: journal),
        ],
        child: TargetEditor(target: target, goalId: goalId),
      ),
    );
    return saved ?? false;
  }

  @override
  State<TargetEditor> createState() => _TargetEditorState();
}

class _TargetEditorState extends State<TargetEditor> {
  final _label = TextEditingController();
  final _value = TextEditingController();
  late TargetKind _kind;
  late TargetComparator _comparator;
  late TargetPeriod _period;
  String? _metricKey;
  String? _tag;
  String? _goalId;
  late bool _active;
  String _localError = '';
  bool _unitsReady = false;

  @override
  void initState() {
    super.initState();
    final t = widget.target;
    _label.text = t?.label ?? '';
    _kind = t?.kind ?? TargetKind.metric;
    _comparator = t?.comparator ?? TargetComparator.atLeast;
    _period = t?.period ?? TargetPeriod.day;
    _metricKey = t?.metricKey ?? TargetMetric.all.first.key;
    _tag = t?.tag;
    _goalId = t?.goalId ?? widget.goalId;
    _active = t?.active ?? true;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final journal = context.read<JournalProvider>();
      if (journal.allTags.isEmpty) journal.loadTags();
    });
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_unitsReady) return;
    _unitsReady = true;
    final t = widget.target;
    if (t == null) return;
    final units = readViewerUnits(context);
    final metric = TargetMetric.find(t.metricKey);
    final shown = t.kind == TargetKind.metric && metric != null
        ? metric.toField(t.value, units)
        : t.value;
    _value.text = BodyUnits.forField(shown);
  }

  @override
  void dispose() {
    _label.dispose();
    _value.dispose();
    super.dispose();
  }

  String _unitFor(Units units) => switch (_kind) {
    TargetKind.metric => TargetMetric.find(_metricKey)?.fieldUnit(units) ?? '',
    TargetKind.journal => 'entries',
    TargetKind.strength => _period == TargetPeriod.day ? 'lifts' : 'days',
  };

  Future<void> _save() async {
    final label = _label.text.trim();
    final typed = BodyUnits.parse(_value.text);
    final metric = TargetMetric.find(_metricKey);
    final String error;
    if (label.isEmpty) {
      error = TargetEditor.needLabel;
    } else if (typed == null || typed < 0) {
      error = TargetEditor.needValue;
    } else if (_kind == TargetKind.metric && metric == null) {
      error = TargetEditor.needMetric;
    } else if (_kind == TargetKind.journal && _tag == null) {
      error = TargetEditor.needTag;
    } else {
      error = '';
    }
    setState(() => _localError = error);
    if (error.isNotEmpty) return;
    final units = readViewerUnits(context);
    final value = _kind == TargetKind.metric
        ? metric!.fromField(typed!, units)
        : typed!;
    final draft = TargetDraft(
      goalId: _goalId,
      label: label,
      kind: _kind,
      metricKey: _kind == TargetKind.metric ? _metricKey : null,
      tag: _kind == TargetKind.journal ? _tag : null,
      comparator: _comparator,
      value: value,
      period: _period,
      active: _active,
      sortOrder: widget.target?.sortOrder,
    );
    final goals = context.read<GoalsProvider>();
    final ok = await goals.saveTarget(draft, id: widget.target?.id);
    if (ok && mounted) Navigator.of(context).pop(true);
  }

  @override
  Widget build(BuildContext context) {
    final goals = context.watch<GoalsProvider>();
    final journal = context.watch<JournalProvider>();
    final theme = Theme.of(context);
    final units = viewerUnitsOf(context);
    final bottom = MediaQuery.viewInsetsOf(context).bottom;
    final tags = journal.allTags;
    final unit = _unitFor(units);
    final linkable = goals.goals;
    return Padding(
      padding: EdgeInsets.fromLTRB(
        Space.gutter,
        0,
        Space.gutter,
        Space.x6 + bottom,
      ),
      child: ListView(
        shrinkWrap: true,
        children: [
          Text(
            widget.target == null
                ? TargetEditor.newTitle
                : TargetEditor.editTitle,
            style: theme.textTheme.headlineSmall,
          ),
          const SizedBox(height: Space.x4),
          TextField(
            controller: _label,
            maxLength: 48,
            textCapitalization: TextCapitalization.sentences,
            decoration: const InputDecoration(
              labelText: TargetEditor.labelLabel,
              hintText: TargetEditor.labelHint,
            ),
          ),
          const SizedBox(height: Space.x2),
          Text('What it counts', style: theme.textTheme.labelMedium),
          const SizedBox(height: Space.x2),
          SegmentedButton<TargetKind>(
            segments: [
              for (final k in TargetKind.values)
                ButtonSegment(value: k, label: Text(k.displayName)),
            ],
            selected: {_kind},
            showSelectedIcon: false,
            onSelectionChanged: (v) => setState(() => _kind = v.first),
          ),
          const SizedBox(height: Space.x4),
          if (_kind == TargetKind.metric)
            DropdownButtonFormField<String>(
              isExpanded: true,
              initialValue: TargetMetric.find(_metricKey)?.key,
              decoration: const InputDecoration(labelText: 'Reading'),
              items: [
                for (final m in TargetMetric.all)
                  DropdownMenuItem(value: m.key, child: Text(m.displayName)),
              ],
              onChanged: (k) => setState(() => _metricKey = k),
            ),
          if (_kind == TargetKind.journal)
            DropdownButtonFormField<String>(
              isExpanded: true,
              initialValue: tags.any((t) => t.tag == _tag) ? _tag : null,
              decoration: const InputDecoration(labelText: 'Day log tag'),
              items: [
                for (final t in tags)
                  DropdownMenuItem(value: t.tag, child: Text(t.label)),
                if (_tag != null && !tags.any((t) => t.tag == _tag))
                  DropdownMenuItem(
                    value: _tag,
                    child: Text(JournalTag.labelFor(_tag!)),
                  ),
              ],
              onChanged: (t) => setState(() => _tag = t),
            ),
          if (_kind == TargetKind.strength)
            Text(
              'Counts the sets in the strength log.',
              style: theme.textTheme.bodySmall,
            ),
          const SizedBox(height: Space.x4),
          SegmentedButton<TargetComparator>(
            segments: [
              for (final c in TargetComparator.values)
                ButtonSegment(value: c, label: Text(c.displayName)),
            ],
            selected: {_comparator},
            showSelectedIcon: false,
            onSelectionChanged: (v) => setState(() => _comparator = v.first),
          ),
          const SizedBox(height: Space.x4),
          TextField(
            controller: _value,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            decoration: InputDecoration(
              labelText: 'Number',
              suffixText: unit.isEmpty ? null : unit,
            ),
          ),
          const SizedBox(height: Space.x4),
          SegmentedButton<TargetPeriod>(
            segments: [
              for (final p in TargetPeriod.values)
                ButtonSegment(value: p, label: Text(p.displayName)),
            ],
            selected: {_period},
            showSelectedIcon: false,
            onSelectionChanged: (v) => setState(() => _period = v.first),
          ),
          if (_period == TargetPeriod.week && _kind == TargetKind.metric) ...[
            const SizedBox(height: Space.x2),
            Text(
              'A weekly reading target uses the average of the days that '
              'have a reading.',
              style: theme.textTheme.bodySmall,
            ),
          ],
          if (linkable.isNotEmpty) ...[
            const SizedBox(height: Space.x4),
            DropdownButtonFormField<String?>(
              isExpanded: true,
              initialValue: linkable.any((g) => g.id == _goalId)
                  ? _goalId
                  : null,
              decoration: const InputDecoration(
                labelText: TargetEditor.goalLabel,
              ),
              items: [
                const DropdownMenuItem<String?>(
                  value: null,
                  child: Text(TargetEditor.noGoal),
                ),
                for (final g in linkable)
                  DropdownMenuItem<String?>(value: g.id, child: Text(g.title)),
              ],
              onChanged: (id) => setState(() => _goalId = id),
            ),
          ],
          if (widget.target != null) ...[
            const SizedBox(height: Space.x2),
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text(TargetEditor.activeLabel),
              subtitle: const Text(
                'Paused targets stay here but are not counted.',
              ),
              value: _active,
              onChanged: (v) => setState(() => _active = v),
            ),
          ],
          AttentionLine(
            _localError.isNotEmpty ? _localError : (goals.saveError ?? ''),
          ),
          const SizedBox(height: Space.x6),
          FilledButton(
            onPressed: goals.isSaving ? null : _save,
            child: Text(goals.isSaving ? 'Saving' : 'Save'),
          ),
        ],
      ),
    );
  }
}
