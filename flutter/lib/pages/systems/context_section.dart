import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/context.dart';
import '../../models/date_only.dart';
import '../../providers/context_provider.dart';
import '../../theme/tokens.dart';
import '../../widgets/dialogs.dart';
import '../../widgets/page_body.dart';
import '../../widgets/status_views.dart';
import '../../widgets/surface_card.dart';
import 'systems_common.dart';

/// "What is going on": illness, travel, a hard training block and the rest,
/// so readings are read with them in mind. Each change saves at once and
/// the snackbar offers Undo, as the day log does.
class ContextSection extends StatelessWidget {
  const ContextSection({super.key});

  static const String title = 'What is going on';
  static const String addLabel = 'Add';
  static const String emptyLine =
      'Nothing noted. Add an illness, travel or a hard training block and readings are read with it in mind.';
  static const String notSharedLine = 'Not shared with you.';
  static const String errorLine = 'What is going on did not load.';
  static const String endedTitle = 'Ended lately';
  static const String savedMessage = 'Saved.';
  static const String endedMessage = 'Ended.';
  static const String removedMessage = 'Removed.';
  static const String undoLabel = 'Undo';
  static const String undoneMessage = 'Put back as it was.';
  static const String editLabel = 'Edit';
  static const String endLabel = 'End today';
  static const String reopenLabel = 'Open again';
  static const String removeLabel = 'Remove';
  static const String menuTooltip = 'Change this';

  /// "Since Nov 8, 2026" or "Nov 8, 2026 to Nov 10, 2026".
  static String spanFor(DateTime since, DateTime? until) => until == null
      ? 'Since ${spokenDay(since)}'
      : '${spokenDay(since)} to ${spokenDay(until)}';

  static Future<void> add(BuildContext context) async {
    final provider = context.read<ContextProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final draft = await ContextEditorSheet.open(
      context,
      today: provider.data?.day ?? localToday(),
    );
    if (draft == null) return;
    final saved = await provider.add(draft);
    if (saved == null) {
      showMessage(messenger, provider.actionError ?? errorLine);
      return;
    }
    _offerUndo(messenger, provider, savedMessage, () async {
      return provider.remove(saved.id);
    });
  }

  static Future<void> edit(BuildContext context, ContextEvent event) async {
    final provider = context.read<ContextProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final draft = await ContextEditorSheet.open(
      context,
      today: provider.data?.day ?? localToday(),
      event: event,
    );
    if (draft == null) return;
    final saved = await provider.update(event.id, draft.toUpdateJson());
    if (saved == null) {
      showMessage(messenger, provider.actionError ?? errorLine);
      return;
    }
    _offerUndo(messenger, provider, savedMessage, () async {
      final back = await provider.update(
        event.id,
        event.toDraft().toUpdateJson(),
      );
      return back != null;
    });
  }

  static Future<void> end(BuildContext context, ContextEvent event) async {
    final provider = context.read<ContextProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final saved = await provider.end(event);
    if (saved == null) {
      showMessage(messenger, provider.actionError ?? errorLine);
      return;
    }
    _offerUndo(messenger, provider, endedMessage, () async {
      return await provider.reopen(event.id) != null;
    });
  }

  static Future<void> reopen(BuildContext context, ContextEvent event) async {
    final provider = context.read<ContextProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final saved = await provider.reopen(event.id);
    if (saved == null) {
      showMessage(messenger, provider.actionError ?? errorLine);
      return;
    }
    _offerUndo(messenger, provider, savedMessage, () async {
      return await provider.update(event.id, event.toDraft().toUpdateJson()) !=
          null;
    });
  }

  static Future<void> remove(BuildContext context, ContextEvent event) async {
    final provider = context.read<ContextProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final ok = await provider.remove(event.id);
    if (!ok) {
      showMessage(messenger, provider.actionError ?? errorLine);
      return;
    }
    _offerUndo(messenger, provider, removedMessage, () async {
      return await provider.add(event.toDraft()) != null;
    });
  }

  static void _offerUndo(
    ScaffoldMessengerState messenger,
    ContextProvider provider,
    String message,
    Future<bool> Function() undo,
  ) {
    messenger
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          content: Text(message),
          action: SnackBarAction(
            label: undoLabel,
            onPressed: () async {
              final ok = await undo();
              showMessage(
                messenger,
                ok ? undoneMessage : (provider.actionError ?? undoneMessage),
              );
            },
          ),
        ),
      );
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<ContextProvider>();
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final data = provider.data;
    final canEdit = provider.canEdit;

    final Widget content;
    if (provider.notShared) {
      content = const QuietLine(notSharedLine);
    } else if (data == null && provider.error == null) {
      content = const LoadingView();
    } else if (data == null) {
      content = Row(
        children: [
          const Expanded(child: QuietLine(errorLine)),
          TextButton(onPressed: provider.load, child: const Text('Try again')),
        ],
      );
    } else {
      final activeIds = {
        for (final a in data.active)
          if (a.eventId != null) a.eventId,
      };
      final ended = [
        for (final e in data.events)
          if (!e.active && !activeIds.contains(e.id)) e,
      ];
      content = Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (data.active.isEmpty) const QuietLine(emptyLine),
          for (var i = 0; i < data.active.length; i++) ...[
            if (i > 0) Divider(height: Space.x6, color: colors.hairline),
            _ActiveRow(
              item: data.active[i],
              event: canEdit ? data.eventById(data.active[i].eventId) : null,
            ),
          ],
          if (ended.isNotEmpty) ...[
            const SizedBox(height: Space.x6),
            Text(endedTitle, style: theme.textTheme.labelLarge),
            for (final e in ended) _EndedRow(event: e, canEdit: canEdit),
          ],
        ],
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionHeader(
          title: title,
          trailing: canEdit && !provider.notShared
              ? TextButton.icon(
                  onPressed: provider.isSaving ? null : () => add(context),
                  icon: const Icon(Icons.add, size: 18),
                  label: const Text(addLabel),
                )
              : null,
        ),
        SurfaceCard(padding: const EdgeInsets.all(Space.x4), child: content),
      ],
    );
  }
}

class _ActiveRow extends StatelessWidget {
  final ActiveContext item;

  /// The stored event behind it, when you may change it.
  final ContextEvent? event;

  const _ActiveRow({required this.item, this.event});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final event = this.event;
    final note = event?.note;
    return Row(
      key: ValueKey('context-${item.kind.wireValue}-${item.eventId ?? ''}'),
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(item.label, style: theme.textTheme.titleSmall),
              const SizedBox(height: Space.x1),
              Text(
                ContextSection.spanFor(item.since, item.until),
                style: theme.textTheme.bodySmall,
              ),
              if (item.reason.isNotEmpty) ...[
                const SizedBox(height: Space.x1),
                Text(item.reason, style: theme.textTheme.bodyMedium),
              ],
              if (note != null && note.isNotEmpty) ...[
                const SizedBox(height: Space.x1),
                Text(note, style: theme.textTheme.bodySmall),
              ],
            ],
          ),
        ),
        if (event != null)
          _EventMenu(event: event, open: item.until == null)
        else
          Padding(
            padding: const EdgeInsets.only(top: Space.x1),
            child: Text(
              item.source.displayName,
              style: theme.textTheme.labelSmall,
            ),
          ),
      ],
    );
  }
}

class _EndedRow extends StatelessWidget {
  final ContextEvent event;
  final bool canEdit;

  const _EndedRow({required this.event, required this.canEdit});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.only(top: Space.x2),
      child: Row(
        children: [
          Expanded(
            child: Text(
              '${event.label}, '
              '${ContextSection.spanFor(event.startedOn, event.endedOn)}',
              style: theme.textTheme.bodySmall,
            ),
          ),
          if (canEdit) _EventMenu(event: event, open: event.endedOn == null),
        ],
      ),
    );
  }
}

enum _Action { edit, end, reopen, remove }

class _EventMenu extends StatelessWidget {
  final ContextEvent event;
  final bool open;

  const _EventMenu({required this.event, required this.open});

  @override
  Widget build(BuildContext context) {
    return PopupMenuButton<_Action>(
      tooltip: ContextSection.menuTooltip,
      icon: const Icon(Icons.more_horiz),
      onSelected: (action) => switch (action) {
        _Action.edit => ContextSection.edit(context, event),
        _Action.end => ContextSection.end(context, event),
        _Action.reopen => ContextSection.reopen(context, event),
        _Action.remove => ContextSection.remove(context, event),
      },
      itemBuilder: (_) => [
        const PopupMenuItem(
          value: _Action.edit,
          child: Text(ContextSection.editLabel),
        ),
        if (open)
          const PopupMenuItem(
            value: _Action.end,
            child: Text(ContextSection.endLabel),
          )
        else
          const PopupMenuItem(
            value: _Action.reopen,
            child: Text(ContextSection.reopenLabel),
          ),
        const PopupMenuItem(
          value: _Action.remove,
          child: Text(ContextSection.removeLabel),
        ),
      ],
    );
  }
}

/// The sheet behind Add and Edit: the kind, the start, an optional end and
/// a note in your own words. Pops with the draft; the caller saves it.
class ContextEditorSheet extends StatefulWidget {
  final DateTime today;
  final ContextEvent? event;

  const ContextEditorSheet({super.key, required this.today, this.event});

  static const String addTitle = 'What is going on';
  static const String editTitle = 'Change this';
  static const String startLabel = 'Started';
  static const String endLabel = 'Ended (optional)';
  static const String stillGoing = 'Still going';
  static const String noteLabel = 'In your own words (optional)';
  static const String saveLabel = 'Save';
  static const String clearEndTooltip = 'Still going';
  static const int maxNote = 500;

  static Future<ContextDraft?> open(
    BuildContext context, {
    required DateTime today,
    ContextEvent? event,
  }) => showModalBottomSheet<ContextDraft>(
    context: context,
    isScrollControlled: true,
    useSafeArea: true,
    builder: (_) => ContextEditorSheet(today: today, event: event),
  );

  @override
  State<ContextEditorSheet> createState() => _ContextEditorSheetState();
}

class _ContextEditorSheetState extends State<ContextEditorSheet> {
  late ContextKind _kind = widget.event?.kind ?? ContextKind.illness;
  late DateTime _start = widget.event?.startedOn ?? widget.today;
  late DateTime? _end = widget.event?.endedOn;
  late final TextEditingController _note = TextEditingController(
    text: widget.event?.note ?? '',
  );

  @override
  void dispose() {
    _note.dispose();
    super.dispose();
  }

  Future<DateTime?> _pick(DateTime initial, {DateTime? first}) {
    final today = widget.today;
    return showDatePicker(
      context: context,
      initialDate: initial,
      firstDate: first ?? DateTime(today.year - 5, today.month, today.day),
      lastDate: DateTime(today.year, today.month, today.day + 60),
    );
  }

  Future<void> _pickStart() async {
    final picked = await _pick(_start);
    if (picked == null || !mounted) return;
    setState(() {
      _start = picked;
      if (_end != null && _end!.isBefore(picked)) _end = picked;
    });
  }

  Future<void> _pickEnd() async {
    final initial =
        _end ?? (widget.today.isBefore(_start) ? _start : widget.today);
    final picked = await _pick(initial, first: _start);
    if (picked == null || !mounted) return;
    setState(() => _end = picked);
  }

  void _save() {
    Navigator.of(context).pop(
      ContextDraft(
        kind: _kind,
        startedOn: _start,
        endedOn: _end,
        note: _note.text,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final bottom = MediaQuery.viewInsetsOf(context).bottom;
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
            widget.event == null
                ? ContextEditorSheet.addTitle
                : ContextEditorSheet.editTitle,
            style: theme.textTheme.headlineSmall,
          ),
          const SizedBox(height: Space.x4),
          Wrap(
            spacing: Space.x2,
            runSpacing: Space.x2,
            children: [
              for (final kind in ContextKind.values)
                ChoiceChip(
                  label: Text(kind.displayName),
                  selected: _kind == kind,
                  onSelected: (_) => setState(() => _kind = kind),
                ),
            ],
          ),
          const SizedBox(height: Space.x6),
          InkWell(
            onTap: _pickStart,
            borderRadius: Radii.controlRadius,
            child: InputDecorator(
              decoration: const InputDecoration(
                labelText: ContextEditorSheet.startLabel,
                suffixIcon: Icon(Icons.calendar_today_outlined, size: 18),
              ),
              child: Text(spokenDay(_start), style: theme.textTheme.bodyLarge),
            ),
          ),
          const SizedBox(height: Space.x4),
          InkWell(
            onTap: _pickEnd,
            borderRadius: Radii.controlRadius,
            child: InputDecorator(
              decoration: InputDecoration(
                labelText: ContextEditorSheet.endLabel,
                suffixIcon: _end == null
                    ? const Icon(Icons.calendar_today_outlined, size: 18)
                    : IconButton(
                        tooltip: ContextEditorSheet.clearEndTooltip,
                        icon: const Icon(Icons.close, size: 18),
                        onPressed: () => setState(() => _end = null),
                      ),
              ),
              child: Text(
                _end == null ? ContextEditorSheet.stillGoing : spokenDay(_end!),
                style: theme.textTheme.bodyLarge,
              ),
            ),
          ),
          const SizedBox(height: Space.x4),
          TextField(
            controller: _note,
            maxLength: ContextEditorSheet.maxNote,
            minLines: 1,
            maxLines: 4,
            textCapitalization: TextCapitalization.sentences,
            decoration: const InputDecoration(
              labelText: ContextEditorSheet.noteLabel,
            ),
          ),
          const SizedBox(height: Space.x4),
          FilledButton(
            onPressed: _save,
            child: const Text(ContextEditorSheet.saveLabel),
          ),
        ],
      ),
    );
  }
}
