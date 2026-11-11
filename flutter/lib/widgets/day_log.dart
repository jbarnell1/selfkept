import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../models/journal.dart';
import '../providers/journal_provider.dart';
import '../theme/app_theme.dart';
import '../theme/tokens.dart';
import 'dialogs.dart';
import 'status_views.dart';
import 'surface_card.dart';

/// Icons for the six kinds of entry.
IconData journalKindIcon(JournalKind kind) => switch (kind) {
  JournalKind.food => Icons.restaurant_outlined,
  JournalKind.drink => Icons.local_cafe_outlined,
  JournalKind.symptom => Icons.healing_outlined,
  JournalKind.training => Icons.fitness_center_outlined,
  JournalKind.sleep => Icons.bedtime_outlined,
  JournalKind.note => Icons.edit_note_outlined,
};

/// "Add to today": one chip per kind of entry, then what today already
/// holds. An entry saves at once and the snackbar offers Undo.
class AddToToday extends StatelessWidget {
  const AddToToday({super.key});

  static const String title = 'Add to today';
  static const String subtitle =
      'A few words or a tag. No counting, no weighing.';
  static const String savedMessage = 'Added to today.';
  static const String undoLabel = 'Undo';
  static const String undoneMessage = 'Taken back out.';
  static const String removedMessage = 'Removed.';
  static const String emptyLine = 'Nothing added today yet.';
  static const String errorLine = 'Today\'s entries did not load.';
  static const String removeTooltip = 'Remove this entry';

  static Future<void> open(BuildContext context, JournalKind kind) async {
    final journal = context.read<JournalProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final entry = await showModalBottomSheet<JournalEntry>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (_) => ChangeNotifierProvider.value(
        value: journal,
        child: DayLogSheet(kind: kind),
      ),
    );
    if (entry == null) return;
    messenger
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          content: const Text(savedMessage),
          action: SnackBarAction(
            label: undoLabel,
            onPressed: () async {
              final ok = await journal.remove(entry.id);
              showMessage(
                messenger,
                ok ? undoneMessage : (journal.saveError ?? undoneMessage),
              );
            },
          ),
        ),
      );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(title, style: theme.textTheme.titleMedium),
          const SizedBox(height: Space.x1),
          Text(subtitle, style: theme.textTheme.bodySmall),
          const SizedBox(height: Space.x4),
          Wrap(
            spacing: Space.x2,
            runSpacing: Space.x2,
            children: [
              for (final kind in JournalKind.values)
                ActionChip(
                  avatar: Icon(journalKindIcon(kind), size: 18),
                  label: Text(kind.displayName),
                  onPressed: () => open(context, kind),
                ),
            ],
          ),
          const SizedBox(height: Space.x4),
          const _TodayEntries(),
        ],
      ),
    );
  }
}

class _TodayEntries extends StatelessWidget {
  const _TodayEntries();

  Future<void> _remove(BuildContext context, JournalEntry entry) async {
    final journal = context.read<JournalProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final ok = await journal.remove(entry.id);
    showMessage(
      messenger,
      ok ? AddToToday.removedMessage : (journal.saveError ?? ''),
    );
  }

  @override
  Widget build(BuildContext context) {
    final journal = context.watch<JournalProvider>();
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    if (!journal.hasLoaded) {
      if (journal.error == null) {
        return Text(
          'Checking today\'s entries',
          style: theme.textTheme.bodySmall,
        );
      }
      return Row(
        children: [
          Expanded(
            child: Text(AddToToday.errorLine, style: theme.textTheme.bodySmall),
          ),
          TextButton(onPressed: journal.load, child: const Text('Try again')),
        ],
      );
    }
    final entries = journal.entries;
    if (entries.isEmpty) {
      return Text(AddToToday.emptyLine, style: theme.textTheme.bodySmall);
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (var i = 0; i < entries.length; i++) ...[
          if (i > 0) Divider(height: Space.x4, color: colors.hairline),
          _EntryRow(
            key: ValueKey('entry-${entries[i].id}'),
            entry: entries[i],
            labelFor: journal.labelFor,
            onRemove: () => _remove(context, entries[i]),
          ),
        ],
      ],
    );
  }
}

class _EntryRow extends StatelessWidget {
  final JournalEntry entry;
  final String Function(String tag) labelFor;
  final VoidCallback onRemove;

  const _EntryRow({
    super.key,
    required this.entry,
    required this.labelFor,
    required this.onRemove,
  });

  /// "Food, usual portion: Oats and berries. High protein, Fruit."
  static String describe(JournalEntry entry, String Function(String) label) {
    final head = entry.portion == null
        ? entry.kind.displayName
        : '${entry.kind.displayName}, '
              '${entry.portion!.displayName.toLowerCase()} portion';
    final parts = <String>[head];
    final text = entry.text?.trim() ?? '';
    if (text.isNotEmpty) parts.add(text);
    if (entry.tags.isNotEmpty) parts.add(entry.tags.map(label).join(', '));
    return parts.join('. ');
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final at = entry.at;
    final text = entry.text?.trim() ?? '';
    final tags = entry.tags.map(labelFor).join(', ');
    return Semantics(
      container: true,
      label: describe(entry, labelFor),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.only(top: Space.x1),
            child: Icon(
              journalKindIcon(entry.kind),
              size: 18,
              color: theme.colorScheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(width: Space.x3),
          Expanded(
            child: ExcludeSemantics(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Text(
                        entry.kind.displayName,
                        style: theme.textTheme.titleSmall,
                      ),
                      if (entry.portion != null) ...[
                        const SizedBox(width: Space.x2),
                        Text(
                          entry.portion!.displayName,
                          style: theme.textTheme.bodySmall,
                        ),
                      ],
                      const Spacer(),
                      if (at != null)
                        Text(
                          DateFormat.Hm().format(at.toLocal()),
                          style: AppTheme.numeric(
                            context,
                            base: theme.textTheme.bodySmall,
                          ),
                        ),
                    ],
                  ),
                  if (text.isNotEmpty) ...[
                    const SizedBox(height: Space.x1),
                    Text(text, style: theme.textTheme.bodyMedium),
                  ],
                  if (tags.isNotEmpty) ...[
                    const SizedBox(height: Space.x1),
                    Text(tags, style: theme.textTheme.bodySmall),
                  ],
                ],
              ),
            ),
          ),
          IconButton(
            onPressed: onRemove,
            icon: const Icon(Icons.close, size: 18),
            tooltip: AddToToday.removeTooltip,
            visualDensity: VisualDensity.compact,
          ),
        ],
      ),
    );
  }
}

/// The sheet behind one chip: the kind's tags, a portion for food, and a
/// line in the person's own words. Pops with the saved entry.
class DayLogSheet extends StatefulWidget {
  final JournalKind kind;

  const DayLogSheet({super.key, required this.kind});

  static const String textLabel = 'In your own words (optional)';
  static const String portionLabel = 'Portion (optional)';
  static const String saveLabel = 'Add';
  static const String savingLabel = 'Adding';
  static const String emptyHint = 'Choose a tag or write a few words.';
  static const int maxTags = 8;
  static const int maxLength = 500;

  static String titleFor(JournalKind kind) => switch (kind) {
    JournalKind.food => 'Something you ate',
    JournalKind.drink => 'Something you drank',
    JournalKind.symptom => 'How you feel',
    JournalKind.training => 'Training',
    JournalKind.sleep => 'Sleep',
    JournalKind.note => 'A note',
  };

  @override
  State<DayLogSheet> createState() => _DayLogSheetState();
}

class _DayLogSheetState extends State<DayLogSheet> {
  final _text = TextEditingController();
  final Set<String> _tags = {};
  JournalPortion? _portion;

  @override
  void initState() {
    super.initState();
    _text.addListener(() => setState(() {}));
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final journal = context.read<JournalProvider>();
      journal.clearSaveError();
      if (journal.tagsFor(widget.kind).isEmpty &&
          widget.kind != JournalKind.note) {
        journal.loadTags();
      }
    });
  }

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  JournalDraft get _draft => JournalDraft(
    kind: widget.kind,
    text: _text.text,
    tags: [..._tags],
    portion: widget.kind == JournalKind.food ? _portion : null,
  );

  Future<void> _save() async {
    final draft = _draft;
    if (draft.isEmpty) return;
    final journal = context.read<JournalProvider>();
    final entry = await journal.add(draft);
    if (entry != null && mounted) Navigator.of(context).pop(entry);
  }

  void _toggle(String tag, bool on) {
    setState(() {
      if (on && _tags.length < DayLogSheet.maxTags) {
        _tags.add(tag);
      } else {
        _tags.remove(tag);
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final journal = context.watch<JournalProvider>();
    final theme = Theme.of(context);
    final tags = journal.tagsFor(widget.kind);
    final bottom = MediaQuery.viewInsetsOf(context).bottom;
    final empty = _draft.isEmpty;
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
          Row(
            children: [
              Icon(journalKindIcon(widget.kind)),
              const SizedBox(width: Space.x3),
              Expanded(
                child: Text(
                  DayLogSheet.titleFor(widget.kind),
                  style: theme.textTheme.headlineSmall,
                ),
              ),
            ],
          ),
          if (tags.isNotEmpty) ...[
            const SizedBox(height: Space.x4),
            Wrap(
              spacing: Space.x2,
              runSpacing: Space.x2,
              children: [
                for (final t in tags)
                  FilterChip(
                    label: Text(t.label),
                    selected: _tags.contains(t.tag),
                    onSelected: (on) => _toggle(t.tag, on),
                  ),
              ],
            ),
          ],
          if (widget.kind == JournalKind.food) ...[
            const SizedBox(height: Space.x6),
            Text(DayLogSheet.portionLabel, style: theme.textTheme.labelMedium),
            const SizedBox(height: Space.x2),
            Wrap(
              spacing: Space.x2,
              children: [
                for (final p in JournalPortion.values)
                  ChoiceChip(
                    label: Text(p.displayName),
                    selected: _portion == p,
                    onSelected: (on) =>
                        setState(() => _portion = on ? p : null),
                  ),
              ],
            ),
          ],
          const SizedBox(height: Space.x6),
          TextField(
            controller: _text,
            maxLength: DayLogSheet.maxLength,
            minLines: 1,
            maxLines: 4,
            textCapitalization: TextCapitalization.sentences,
            // Keeps the Add button above the keyboard while typing.
            scrollPadding: const EdgeInsets.only(bottom: Space.x8 * 5),
            decoration: const InputDecoration(labelText: DayLogSheet.textLabel),
          ),
          AttentionLine(journal.saveError ?? ''),
          const SizedBox(height: Space.x4),
          if (empty)
            Padding(
              padding: const EdgeInsets.only(bottom: Space.x3),
              child: Text(
                DayLogSheet.emptyHint,
                style: theme.textTheme.bodySmall,
              ),
            ),
          FilledButton(
            onPressed: empty || journal.isSaving ? null : _save,
            child: Text(
              journal.isSaving
                  ? DayLogSheet.savingLabel
                  : DayLogSheet.saveLabel,
            ),
          ),
        ],
      ),
    );
  }
}
