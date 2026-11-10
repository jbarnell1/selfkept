import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../models/date_only.dart';
import '../../models/insight.dart';
import '../../models/lab.dart';
import '../../models/metric.dart';
import '../../models/trial.dart';
import '../../providers/labs_provider.dart';
import '../../providers/trials_provider.dart';
import '../../theme/tokens.dart';
import '../../widgets/dialogs.dart';
import '../../widgets/page_body.dart';
import '../../widgets/person_switcher.dart';
import '../../widgets/status_views.dart';
import '../../widgets/subpage_app_bar.dart';
import '../../widgets/surface_card.dart';
import 'systems_common.dart';

/// A reading's name for a trial: a lab marker from the catalogue, a
/// wearable metric by its own name, or the key itself.
String readingName(String key, List<LabMarker> markers) {
  for (final m in markers) {
    if (m.metricKey == key) return m.displayName;
  }
  return MetricKey.fromString(key)?.displayName ?? key;
}

/// Trying something: each change with a date to read the result, and the
/// server's word when one is ready. Your own trials can be started, edited,
/// ended and removed here.
class TrialsPage extends StatefulWidget {
  const TrialsPage({super.key});

  static const String title = 'Trying something';
  static const String subtitle =
      'One change at a time, with a date to read the result.';
  static const String startLabel = 'Start a trial';
  static const String emptyLine =
      'No trials yet. Start one when you change one thing and want to see what it moves.';
  static const String endedTitle = 'Ended';
  static const String loadErrorTitle = 'Trials did not load.';
  static const String reviewLabel = 'Open the lab review';
  static const String editLabel = 'Edit';
  static const String doneLabel = 'Mark done';
  static const String dropLabel = 'Drop it';
  static const String removeLabel = 'Remove';
  static const String reopenLabel = 'Run it again';
  static const String menuTooltip = 'Change this trial';
  static const String outcomeTitle = 'How did it go?';
  static const String outcomeLabel = 'In your own words (optional)';
  static const String endItLabel = 'End it';
  static const String removeTitle = 'Remove this trial?';
  static const String removeBody = 'Its dates and notes go too.';

  static String datesLine(Trial t) =>
      'Started ${spokenDay(t.startedOn)}. Read on ${spokenDay(t.readOn)}.';

  @override
  State<TrialsPage> createState() => _TrialsPageState();
}

class _TrialsPageState extends State<TrialsPage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      context.read<TrialsProvider>().ensureLoaded();
      final labs = context.read<LabsProvider>();
      if (!labs.hasLoaded && !labs.loading) labs.load();
    });
  }

  Future<void> _start() async {
    final provider = context.read<TrialsProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final saved = await Navigator.of(context).push<Trial>(
      MaterialPageRoute(
        builder: (_) => ChangeNotifierProvider.value(
          value: provider,
          child: const TrialEditorPage(),
        ),
      ),
    );
    if (saved != null) showMessage(messenger, 'Started.');
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<TrialsProvider>();
    final markers = context.watch<LabsProvider>().markers;
    return SubpageFrame(
      onRefresh: provider.load,
      children: [
        const PageHeading(
          title: TrialsPage.title,
          subtitle: TrialsPage.subtitle,
        ),
        const PersonSwitcher(),
        if (provider.canEdit && !provider.notShared)
          Padding(
            padding: const EdgeInsets.only(bottom: Space.x4),
            child: Align(
              alignment: Alignment.centerLeft,
              child: FilledButton.tonalIcon(
                onPressed: provider.isSaving ? null : _start,
                icon: const Icon(Icons.add, size: 18),
                label: const Text(TrialsPage.startLabel),
              ),
            ),
          ),
        ...personReadBody(
          context,
          provider,
          errorTitle: TrialsPage.loadErrorTitle,
          builder: (s) {
            if (s.trials.isEmpty) {
              return const [QuietLine(TrialsPage.emptyLine)];
            }
            return [
              for (final t in s.active)
                _TrialCard(
                  trial: t,
                  card: s.cards[t.id],
                  markers: markers,
                  canEdit: provider.canEdit,
                ),
              if (s.ended.isNotEmpty) ...[
                const SectionHeader(title: TrialsPage.endedTitle),
                for (final t in s.ended)
                  _TrialCard(
                    trial: t,
                    card: s.cards[t.id],
                    markers: markers,
                    canEdit: provider.canEdit,
                  ),
              ],
            ];
          },
        ),
      ],
    );
  }
}

enum _TrialAction { edit, done, drop, reopen, remove }

class _TrialCard extends StatelessWidget {
  final Trial trial;
  final Insight? card;
  final List<LabMarker> markers;
  final bool canEdit;

  const _TrialCard({
    required this.trial,
    required this.card,
    required this.markers,
    required this.canEdit,
  });

  Future<void> _act(BuildContext context, _TrialAction action) async {
    final provider = context.read<TrialsProvider>();
    final messenger = ScaffoldMessenger.of(context);
    switch (action) {
      case _TrialAction.edit:
        final saved = await Navigator.of(context).push<Trial>(
          MaterialPageRoute(
            builder: (_) => ChangeNotifierProvider.value(
              value: provider,
              child: TrialEditorPage(trial: trial),
            ),
          ),
        );
        if (saved != null) showMessage(messenger, 'Saved.');
        return;
      case _TrialAction.done:
      case _TrialAction.drop:
        final outcome = await _askOutcome(context);
        if (outcome == null) return;
        final status = action == _TrialAction.done
            ? TrialStatus.done
            : TrialStatus.dropped;
        final saved = await provider.finish(trial, status, outcome: outcome);
        showMessage(
          messenger,
          saved == null ? (provider.actionError ?? '') : 'Ended.',
        );
        return;
      case _TrialAction.reopen:
        final saved = await provider.update(trial.id, {
          'status': TrialStatus.active.wireValue,
        });
        if (saved == null) showMessage(messenger, provider.actionError ?? '');
        return;
      case _TrialAction.remove:
        final ok = await confirmAction(
          context,
          title: TrialsPage.removeTitle,
          message: TrialsPage.removeBody,
          confirmLabel: TrialsPage.removeLabel,
        );
        if (!ok) return;
        final removed = await provider.remove(trial.id);
        showMessage(
          messenger,
          removed ? 'Removed.' : (provider.actionError ?? ''),
        );
        return;
    }
  }

  /// Null when the person backs out; an empty string to end without words.
  Future<String?> _askOutcome(BuildContext context) => showDialog<String>(
    context: context,
    builder: (_) => _OutcomeDialog(initial: trial.outcome ?? ''),
  );

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final change = trial.change;
    final outcome = trial.outcome;
    final card = this.card;
    final reportId = card?.evidence['reportId']?.toString();
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.x3),
      child: SurfaceCard(
        key: ValueKey('trial-${trial.id}'),
        padding: const EdgeInsets.all(Space.x4),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: Text(trial.title, style: theme.textTheme.titleMedium),
                ),
                if (!trial.isActive)
                  Padding(
                    padding: const EdgeInsets.only(top: Space.x1),
                    child: Text(
                      trial.status.displayName,
                      style: theme.textTheme.labelSmall,
                    ),
                  ),
                if (canEdit)
                  PopupMenuButton<_TrialAction>(
                    tooltip: TrialsPage.menuTooltip,
                    icon: const Icon(Icons.more_horiz),
                    onSelected: (a) => _act(context, a),
                    itemBuilder: (_) => [
                      const PopupMenuItem(
                        value: _TrialAction.edit,
                        child: Text(TrialsPage.editLabel),
                      ),
                      if (trial.isActive) ...const [
                        PopupMenuItem(
                          value: _TrialAction.done,
                          child: Text(TrialsPage.doneLabel),
                        ),
                        PopupMenuItem(
                          value: _TrialAction.drop,
                          child: Text(TrialsPage.dropLabel),
                        ),
                      ] else
                        const PopupMenuItem(
                          value: _TrialAction.reopen,
                          child: Text(TrialsPage.reopenLabel),
                        ),
                      const PopupMenuItem(
                        value: _TrialAction.remove,
                        child: Text(TrialsPage.removeLabel),
                      ),
                    ],
                  ),
              ],
            ),
            if (change != null && change.isNotEmpty) ...[
              const SizedBox(height: Space.x1),
              Text(change, style: theme.textTheme.bodyMedium),
            ],
            const SizedBox(height: Space.x2),
            Text(TrialsPage.datesLine(trial), style: theme.textTheme.bodySmall),
            if (trial.metricKeys.isNotEmpty)
              Text(
                'Should move: ${[for (final k in trial.metricKeys) readingName(k, markers)].join(', ')}',
                style: theme.textTheme.bodySmall,
              ),
            if (outcome != null && outcome.isNotEmpty) ...[
              const SizedBox(height: Space.x2),
              Text(outcome, style: theme.textTheme.bodyMedium),
            ],
            if (card != null) ...[
              const SizedBox(height: Space.x3),
              Text(card.text, style: theme.textTheme.bodyMedium),
              if (reportId != null && reportId.isNotEmpty)
                TextButton(
                  onPressed: () => context.push('/labs/report/$reportId'),
                  child: const Text(TrialsPage.reviewLabel),
                ),
            ],
          ],
        ),
      ),
    );
  }
}

/// How a trial went, in the person's own words. Owns its controller so the
/// closing animation never sees it disposed.
class _OutcomeDialog extends StatefulWidget {
  final String initial;

  const _OutcomeDialog({required this.initial});

  @override
  State<_OutcomeDialog> createState() => _OutcomeDialogState();
}

class _OutcomeDialogState extends State<_OutcomeDialog> {
  late final TextEditingController _controller = TextEditingController(
    text: widget.initial,
  );

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text(TrialsPage.outcomeTitle),
    content: TextField(
      controller: _controller,
      maxLength: 500,
      maxLines: 3,
      minLines: 1,
      textCapitalization: TextCapitalization.sentences,
      decoration: const InputDecoration(labelText: TrialsPage.outcomeLabel),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.of(context).pop(),
        child: const Text('Cancel'),
      ),
      FilledButton(
        onPressed: () => Navigator.of(context).pop(_controller.text),
        child: const Text(TrialsPage.endItLabel),
      ),
    ],
  );
}

/// Start or change a trial: what is being tried, what it should move, the
/// start and the date to read the result. Pops with the saved trial.
class TrialEditorPage extends StatefulWidget {
  final Trial? trial;

  /// Today on this device, replaceable for tests.
  final DateTime Function() today;

  const TrialEditorPage({super.key, this.trial, this.today = localToday});

  static const String startTitle = 'Start a trial';
  static const String editTitle = 'Change this trial';
  static const String titleLabel = 'What are you trying?';
  static const String changeLabel = 'What changes, in a few words (optional)';
  static const String startedLabel = 'Started';
  static const String readLabel = 'Read the result on';
  static const String movesTitle = 'What it should move';
  static const String movesLine = 'Up to eight readings.';
  static const String saveLabel = 'Save';
  static const String needTitle = 'Say what the trial is.';
  static const int maxReadings = 8;

  /// Wearable and body readings offered beside the lab markers.
  static const List<MetricKey> wearables = [
    MetricKey.restingHrBpm,
    MetricKey.hrvSdnnMs,
    MetricKey.sleepStage,
    MetricKey.steps,
    MetricKey.weightKg,
    MetricKey.bodyFatPct,
    MetricKey.vo2max,
  ];

  @override
  State<TrialEditorPage> createState() => _TrialEditorPageState();
}

class _TrialEditorPageState extends State<TrialEditorPage> {
  late final TextEditingController _title = TextEditingController(
    text: widget.trial?.title ?? '',
  );
  late final TextEditingController _change = TextEditingController(
    text: widget.trial?.change ?? '',
  );
  late DateTime _start = widget.trial?.startedOn ?? widget.today();
  late DateTime _read =
      widget.trial?.readOn ??
      DateTime(_start.year, _start.month + 3, _start.day);
  late final List<String> _keys = [...?widget.trial?.metricKeys];
  String? _error;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      context.read<TrialsProvider>().clearActionError();
    });
  }

  @override
  void dispose() {
    _title.dispose();
    _change.dispose();
    super.dispose();
  }

  Future<void> _pickStart() async {
    final today = widget.today();
    final picked = await showDatePicker(
      context: context,
      initialDate: _start,
      firstDate: DateTime(today.year - 5),
      lastDate: DateTime(today.year, today.month + 1, today.day),
    );
    if (picked == null || !mounted) return;
    setState(() {
      _start = picked;
      if (_read.isBefore(picked)) _read = picked;
    });
  }

  Future<void> _pickRead() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: _read.isBefore(_start) ? _start : _read,
      firstDate: _start,
      lastDate: DateTime(_start.year + 2, _start.month, _start.day),
    );
    if (picked == null || !mounted) return;
    setState(() => _read = picked);
  }

  void _toggle(String key, bool on) {
    setState(() {
      if (on && _keys.length < TrialEditorPage.maxReadings) {
        _keys.add(key);
      } else {
        _keys.remove(key);
      }
    });
  }

  Future<void> _save() async {
    if (_title.text.trim().isEmpty) {
      setState(() => _error = TrialEditorPage.needTitle);
      return;
    }
    setState(() => _error = null);
    final provider = context.read<TrialsProvider>();
    final draft = TrialDraft(
      title: _title.text,
      change: _change.text,
      startedOn: _start,
      readOn: _read,
      metricKeys: _keys,
    );
    final original = widget.trial;
    final saved = original == null
        ? await provider.create(draft)
        : await provider.update(original.id, draft.toUpdateJson(original));
    if (saved != null && mounted) Navigator.of(context).pop(saved);
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<TrialsProvider>();
    final markers = [
      for (final m in context.watch<LabsProvider>().markers)
        if (m.applicable && !m.isRatio) m,
    ];
    final theme = Theme.of(context);
    Widget chip(String key, String label) => FilterChip(
      label: Text(label),
      selected: _keys.contains(key),
      onSelected: (on) => _toggle(key, on),
    );
    return Scaffold(
      appBar: SubpageAppBar(
        fallback: '/systems/trials',
        title: Text(
          widget.trial == null
              ? TrialEditorPage.startTitle
              : TrialEditorPage.editTitle,
        ),
      ),
      body: SafeArea(
        child: PageBody(
          children: [
            TextField(
              controller: _title,
              maxLength: 80,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(
                labelText: TrialEditorPage.titleLabel,
              ),
            ),
            const SizedBox(height: Space.x2),
            TextField(
              controller: _change,
              maxLength: 200,
              minLines: 1,
              maxLines: 3,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(
                labelText: TrialEditorPage.changeLabel,
              ),
            ),
            const SizedBox(height: Space.x4),
            _DateField(
              label: TrialEditorPage.startedLabel,
              day: _start,
              onTap: _pickStart,
            ),
            const SizedBox(height: Space.x4),
            _DateField(
              label: TrialEditorPage.readLabel,
              day: _read,
              onTap: _pickRead,
            ),
            const SectionHeader(title: TrialEditorPage.movesTitle),
            Text(TrialEditorPage.movesLine, style: theme.textTheme.bodySmall),
            const SizedBox(height: Space.x3),
            Wrap(
              spacing: Space.x2,
              runSpacing: Space.x2,
              children: [
                for (final m in TrialEditorPage.wearables)
                  chip(m.wireValue, m.displayName),
                for (final m in markers) chip(m.metricKey, m.displayName),
              ],
            ),
            const SizedBox(height: Space.x6),
            FilledButton(
              onPressed: provider.isSaving ? null : _save,
              child: const Text(TrialEditorPage.saveLabel),
            ),
            AttentionLine(_error ?? provider.actionError ?? ''),
          ],
        ),
      ),
    );
  }
}

class _DateField extends StatelessWidget {
  final String label;
  final DateTime day;
  final VoidCallback onTap;

  const _DateField({
    required this.label,
    required this.day,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return InkWell(
      onTap: onTap,
      borderRadius: Radii.controlRadius,
      child: InputDecorator(
        decoration: InputDecoration(
          labelText: label,
          suffixIcon: const Icon(Icons.calendar_today_outlined, size: 18),
        ),
        child: Text(spokenDay(day), style: theme.textTheme.bodyLarge),
      ),
    );
  }
}
