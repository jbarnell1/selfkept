import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../../models/medication.dart';
import '../../providers/medications_provider.dart';
import '../../providers/viewed_person.dart';
import '../../theme/tokens.dart';
import '../../utils/body_units.dart';
import '../../widgets/dialogs.dart';
import '../../widgets/page_body.dart';
import '../../widgets/person_switcher.dart';
import '../../widgets/status_views.dart';
import '../../widgets/subpage_app_bar.dart';
import '../../widgets/surface_card.dart';

/// Medicines and supplements: what you take and what you took. A record that
/// helps explain readings and blood work, never advice.
class MedicinesPage extends StatefulWidget {
  const MedicinesPage({super.key});

  static const String title = 'Medicines and supplements';
  static const String subtitle =
      'A record of what you take. It helps explain readings and blood work. '
      'Nothing here is advice to start, stop or change anything.';
  static const String loadErrorTitle = 'The list did not load.';
  static const String emptyLine =
      'Nothing listed yet. Add a medicine or a supplement, with its '
      'ingredients if you like.';
  static const String emptyOtherLine = 'Nothing listed.';
  static const String addLabel = 'Add';
  static const String currentTitle = 'Taking now';
  static const String stoppedTitle = 'Stopped';
  static const String substancesLabel = 'Noted for your readings';

  /// "Daily", "Weekly, Sunday mornings", "As needed. For the odd headache".
  /// The note is kept as typed: one that starts with a small letter carries
  /// on the schedule's sentence, one with a capital starts its own.
  static String scheduleLine(Medication m) {
    final note = m.scheduleNote?.trim() ?? '';
    if (note.isEmpty) return m.schedule.displayName;
    final first = note[0];
    final carriesOn = first != first.toUpperCase();
    return carriesOn
        ? '${m.schedule.displayName}, $note'
        : '${m.schedule.displayName}. $note';
  }

  /// "Medicine. Generic. As needed. For the odd headache.": each part a
  /// sentence, with a full stop at the end unless the note already has one.
  static String kindLine(Medication m) {
    final line = [
      m.kind.displayName,
      if (m.generic == true) 'Generic',
      if (m.generic == false) 'Name brand',
      scheduleLine(m),
    ].join('. ');
    return RegExp(r'[.!?]$').hasMatch(line) ? line : '$line.';
  }

  /// "Magnesium 200 mg".
  static String ingredientLine(MedicationIngredient i) {
    final amount = i.amount == null
        ? ''
        : ' ${BodyUnits.forField(i.amount!, decimals: 3)}';
    final unit = (i.unit ?? '').trim();
    return '${i.name}$amount${unit.isEmpty ? '' : ' $unit'}';
  }

  @override
  State<MedicinesPage> createState() => _MedicinesPageState();
}

class _MedicinesPageState extends State<MedicinesPage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final meds = context.read<MedicationsProvider>();
      if (!meds.hasLoaded && !meds.isLoading) meds.load();
    });
  }

  @override
  Widget build(BuildContext context) {
    final meds = context.watch<MedicationsProvider>();
    final theme = Theme.of(context);
    final List<Widget> body;
    if (meds.notShared) {
      final viewing = context.read<ViewedPerson>();
      body = [
        NotSharedView(name: viewing.displayName, onBack: viewing.viewSelf),
      ];
    } else if (!meds.hasLoaded && meds.error == null) {
      body = const [LoadingView()];
    } else if (!meds.hasLoaded) {
      body = [
        ErrorView(
          title: MedicinesPage.loadErrorTitle,
          detail: meds.error,
          onRetry: meds.load,
        ),
      ];
    } else if (meds.items.isEmpty) {
      body = [
        SurfaceCard(
          child: Text(
            meds.canEdit
                ? MedicinesPage.emptyLine
                : MedicinesPage.emptyOtherLine,
            style: theme.textTheme.bodyMedium,
          ),
        ),
      ];
    } else {
      body = [
        if (meds.current.isNotEmpty) ...[
          const SectionHeader(title: MedicinesPage.currentTitle),
          for (final m in meds.current) ...[
            _MedicationCard(item: m, canEdit: meds.canEdit),
            const SizedBox(height: Space.x3),
          ],
        ],
        if (meds.stopped.isNotEmpty) ...[
          const SectionHeader(title: MedicinesPage.stoppedTitle),
          for (final m in meds.stopped) ...[
            _MedicationCard(item: m, canEdit: meds.canEdit),
            const SizedBox(height: Space.x3),
          ],
        ],
      ];
    }
    return Scaffold(
      appBar: const SubpageAppBar(fallback: '/you', title: Text('You')),
      floatingActionButton: meds.canEdit && meds.hasLoaded
          ? FloatingActionButton.extended(
              onPressed: () => MedicationEditor.open(context),
              icon: const Icon(Icons.add),
              label: const Text(MedicinesPage.addLabel),
            )
          : null,
      body: SafeArea(
        child: PageBody(
          children: [
            const PageHeading(
              title: MedicinesPage.title,
              subtitle: MedicinesPage.subtitle,
            ),
            const PersonSwitcher(),
            ...body,
          ],
        ),
      ),
    );
  }
}

class _MedicationCard extends StatelessWidget {
  final Medication item;
  final bool canEdit;

  const _MedicationCard({required this.item, required this.canEdit});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final dates = DateFormat.yMMMd();
    final kindLine = MedicinesPage.kindLine(item);
    final dose = item.dose?.trim() ?? '';
    final started = item.startedOn;
    final stopped = item.stoppedOn;
    final when = [
      if (started != null) 'Since ${dates.format(started)}',
      if (stopped != null) 'Stopped ${dates.format(stopped)}',
    ].join('. ');
    return SurfaceCard(
      onTap: canEdit ? () => MedicationEditor.open(context, item: item) : null,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(item.fullName, style: theme.textTheme.titleMedium),
              ),
              if (canEdit) const Icon(Icons.chevron_right),
            ],
          ),
          const SizedBox(height: Space.x1),
          Text(kindLine, style: theme.textTheme.bodySmall),
          if (dose.isNotEmpty) ...[
            const SizedBox(height: Space.x1),
            Text(dose, style: theme.textTheme.bodyMedium),
          ],
          if (when.isNotEmpty) ...[
            const SizedBox(height: Space.x1),
            Text(when, style: theme.textTheme.bodySmall),
          ],
          if (item.ingredients.isNotEmpty) ...[
            const SizedBox(height: Space.x3),
            Text(
              item.ingredients.map(MedicinesPage.ingredientLine).join(', '),
              style: theme.textTheme.bodySmall,
            ),
          ],
          if (item.substances.isNotEmpty) ...[
            const SizedBox(height: Space.x3),
            Text(
              MedicinesPage.substancesLabel,
              style: theme.textTheme.labelMedium,
            ),
            const SizedBox(height: Space.x1),
            Wrap(
              spacing: Space.x2,
              runSpacing: Space.x2,
              children: [
                for (final s in item.substances)
                  Chip(
                    label: Text(Medication.substanceLabel(s)),
                    visualDensity: VisualDensity.compact,
                  ),
              ],
            ),
          ],
        ],
      ),
    );
  }
}

/// One editable ingredient row's text.
class _IngredientRow {
  final TextEditingController name;
  final TextEditingController amount;
  final TextEditingController unit;

  _IngredientRow({String name = '', String amount = '', String unit = ''})
    : name = TextEditingController(text: name),
      amount = TextEditingController(text: amount),
      unit = TextEditingController(text: unit);

  void dispose() {
    name.dispose();
    amount.dispose();
    unit.dispose();
  }
}

/// Adds or edits one item, as a full page. Pops true once saved or removed.
class MedicationEditor extends StatefulWidget {
  final Medication? item;

  /// Starting values for a new item, such as a label a photo read. Nothing
  /// is saved until the person saves the form.
  final MedicationDraft? prefill;

  const MedicationEditor({super.key, this.item, this.prefill});

  static const String newTitle = 'Add to the list';
  static const String editTitle = 'Edit';
  static const String brandLabel = 'Brand (optional)';
  static const String productLabel = 'Product';
  static const String doseLabel = 'Dose (optional)';
  static const String doseHint = 'For example 1 tablet, or 2 capsules';
  static const String scheduleNoteLabel = 'When (optional)';
  static const String ingredientsTitle = 'Ingredients';
  static const String ingredientsNote =
      'One row per ingredient, as the label lists it.';
  static const String addIngredientLabel = 'Add an ingredient';
  static const String ingredientName = 'Ingredient';
  static const String ingredientAmount = 'Amount';
  static const String ingredientUnit = 'Unit';
  static const String removeIngredientTooltip = 'Remove this ingredient';
  static const String needProduct = 'Say what the product is.';
  static const String needIngredientName = 'Every ingredient needs a name.';
  static const String badAmount = 'An amount is a number, for example 200.';
  static const String deleteLabel = 'Remove from the list';
  static const String deleteMessage =
      'Remove this item? To keep a record that you took it, set a stop date '
      'instead.';
  static const String stoppedLabel = 'Stopped';
  static const String startedLabel = 'Started';
  static const String stillTaking = 'Still taking';
  static const String notSure = 'Not sure';
  static const int maxIngredients = 40;

  static Future<bool> open(
    BuildContext context, {
    Medication? item,
    MedicationDraft? prefill,
  }) async {
    final meds = context.read<MedicationsProvider>();
    meds.clearSaveError();
    final saved = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        fullscreenDialog: true,
        builder: (_) => ChangeNotifierProvider.value(
          value: meds,
          child: MedicationEditor(item: item, prefill: prefill),
        ),
      ),
    );
    return saved ?? false;
  }

  @override
  State<MedicationEditor> createState() => _MedicationEditorState();
}

class _MedicationEditorState extends State<MedicationEditor> {
  late MedicationKind _kind;
  late MedicationSchedule _schedule;

  /// True generic, false name brand, null not said.
  bool? _generic;
  DateTime? _startedOn;
  DateTime? _stoppedOn;
  final _brand = TextEditingController();
  final _product = TextEditingController();
  final _dose = TextEditingController();
  final _scheduleNote = TextEditingController();
  final List<_IngredientRow> _ingredients = [];
  String _localError = '';

  @override
  void initState() {
    super.initState();
    final item = widget.item;
    final prefill = item == null ? widget.prefill : null;
    _kind = item?.kind ?? prefill?.kind ?? MedicationKind.supplement;
    _schedule = item?.schedule ?? prefill?.schedule ?? MedicationSchedule.daily;
    _generic = item?.generic ?? prefill?.generic;
    _startedOn = item?.startedOn ?? prefill?.startedOn;
    _stoppedOn = item?.stoppedOn ?? prefill?.stoppedOn;
    _brand.text = item?.brand ?? prefill?.brand ?? '';
    _product.text = item?.product ?? prefill?.product ?? '';
    _dose.text = item?.dose ?? prefill?.dose ?? '';
    _scheduleNote.text = item?.scheduleNote ?? prefill?.scheduleNote ?? '';
    for (final i
        in item?.ingredients ??
            prefill?.ingredients ??
            const <MedicationIngredient>[]) {
      _ingredients.add(
        _IngredientRow(
          name: i.name,
          amount: i.amount == null
              ? ''
              : BodyUnits.forField(i.amount!, decimals: 3),
          unit: i.unit ?? '',
        ),
      );
    }
  }

  @override
  void dispose() {
    _brand.dispose();
    _product.dispose();
    _dose.dispose();
    _scheduleNote.dispose();
    for (final row in _ingredients) {
      row.dispose();
    }
    super.dispose();
  }

  void _addIngredient() {
    if (_ingredients.length >= MedicationEditor.maxIngredients) return;
    setState(() => _ingredients.add(_IngredientRow()));
  }

  void _removeIngredient(int index) {
    final row = _ingredients.removeAt(index);
    setState(() {});
    WidgetsBinding.instance.addPostFrameCallback((_) => row.dispose());
  }

  Future<void> _pickDate({required bool stopped}) async {
    final now = DateTime.now();
    final picked = await showDatePicker(
      context: context,
      initialDate: (stopped ? _stoppedOn : _startedOn) ?? now,
      firstDate: DateTime(now.year - 50),
      lastDate: DateTime(now.year + 1),
      helpText: stopped
          ? MedicationEditor.stoppedLabel
          : MedicationEditor.startedLabel,
    );
    if (picked == null) return;
    setState(() => stopped ? _stoppedOn = picked : _startedOn = picked);
  }

  String? _trimmed(TextEditingController c) {
    final text = c.text.trim();
    return text.isEmpty ? null : text;
  }

  Future<void> _save() async {
    final product = _product.text.trim();
    if (product.isEmpty) {
      setState(() => _localError = MedicationEditor.needProduct);
      return;
    }
    final ingredients = <MedicationIngredient>[];
    for (final row in _ingredients) {
      final name = row.name.text.trim();
      final amountText = row.amount.text.trim();
      final unit = row.unit.text.trim();
      if (name.isEmpty && amountText.isEmpty && unit.isEmpty) continue;
      if (name.isEmpty) {
        setState(() => _localError = MedicationEditor.needIngredientName);
        return;
      }
      final amount = amountText.isEmpty ? null : BodyUnits.parse(amountText);
      if (amountText.isNotEmpty && (amount == null || amount < 0)) {
        setState(() => _localError = MedicationEditor.badAmount);
        return;
      }
      ingredients.add(
        MedicationIngredient(
          name: name,
          amount: amount,
          unit: unit.isEmpty ? null : unit,
        ),
      );
    }
    setState(() => _localError = '');
    final draft = MedicationDraft(
      kind: _kind,
      brand: _trimmed(_brand),
      product: product,
      dose: _trimmed(_dose),
      scheduleNote: _trimmed(_scheduleNote),
      generic: _generic,
      schedule: _schedule,
      ingredients: ingredients,
      startedOn: _startedOn,
      stoppedOn: _stoppedOn,
    );
    final meds = context.read<MedicationsProvider>();
    final ok = await meds.save(draft, id: widget.item?.id);
    if (ok && mounted) Navigator.of(context).pop(true);
  }

  Future<void> _delete() async {
    final item = widget.item;
    if (item == null) return;
    final sure = await confirmAction(
      context,
      message: MedicationEditor.deleteMessage,
      confirmLabel: 'Remove',
    );
    if (!sure || !mounted) return;
    final meds = context.read<MedicationsProvider>();
    final navigator = Navigator.of(context);
    if (await meds.remove(item.id)) navigator.pop(true);
  }

  @override
  Widget build(BuildContext context) {
    final meds = context.watch<MedicationsProvider>();
    final theme = Theme.of(context);
    final dates = DateFormat.yMMMd();
    final isNew = widget.item == null;
    const number = TextInputType.numberWithOptions(decimal: true);
    return Scaffold(
      appBar: SubpageAppBar(
        fallback: '/you/medicines',
        title: Text(
          isNew ? MedicationEditor.newTitle : MedicationEditor.editTitle,
        ),
      ),
      body: SafeArea(
        child: Align(
          alignment: Alignment.topCenter,
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: Space.maxContentWidth),
            child: ListView(
              padding: const EdgeInsets.all(Space.gutter),
              children: [
                SegmentedButton<MedicationKind>(
                  segments: [
                    for (final k in MedicationKind.values)
                      ButtonSegment(value: k, label: Text(k.displayName)),
                  ],
                  selected: {_kind},
                  showSelectedIcon: false,
                  onSelectionChanged: (v) => setState(() => _kind = v.first),
                ),
                const SizedBox(height: Space.x6),
                TextField(
                  controller: _brand,
                  maxLength: 80,
                  textCapitalization: TextCapitalization.words,
                  decoration: const InputDecoration(
                    labelText: MedicationEditor.brandLabel,
                  ),
                ),
                const SizedBox(height: Space.x2),
                TextField(
                  controller: _product,
                  maxLength: 80,
                  textCapitalization: TextCapitalization.words,
                  decoration: const InputDecoration(
                    labelText: MedicationEditor.productLabel,
                  ),
                ),
                const SizedBox(height: Space.x2),
                Text(
                  'Generic or name brand',
                  style: theme.textTheme.labelMedium,
                ),
                const SizedBox(height: Space.x2),
                Wrap(
                  spacing: Space.x2,
                  children: [
                    ChoiceChip(
                      label: const Text('Generic'),
                      selected: _generic == true,
                      onSelected: (_) => setState(() => _generic = true),
                    ),
                    ChoiceChip(
                      label: const Text('Name brand'),
                      selected: _generic == false,
                      onSelected: (_) => setState(() => _generic = false),
                    ),
                    ChoiceChip(
                      label: const Text(MedicationEditor.notSure),
                      selected: _generic == null,
                      onSelected: (_) => setState(() => _generic = null),
                    ),
                  ],
                ),
                const SizedBox(height: Space.x6),
                TextField(
                  controller: _dose,
                  maxLength: 80,
                  decoration: const InputDecoration(
                    labelText: MedicationEditor.doseLabel,
                    hintText: MedicationEditor.doseHint,
                  ),
                ),
                const SizedBox(height: Space.x2),
                Text('How often', style: theme.textTheme.labelMedium),
                const SizedBox(height: Space.x2),
                SegmentedButton<MedicationSchedule>(
                  segments: [
                    for (final s in MedicationSchedule.values)
                      ButtonSegment(value: s, label: Text(s.displayName)),
                  ],
                  selected: {_schedule},
                  showSelectedIcon: false,
                  onSelectionChanged: (v) =>
                      setState(() => _schedule = v.first),
                ),
                const SizedBox(height: Space.x4),
                TextField(
                  controller: _scheduleNote,
                  maxLength: 120,
                  decoration: const InputDecoration(
                    labelText: MedicationEditor.scheduleNoteLabel,
                    hintText: 'For example with breakfast',
                  ),
                ),
                const SizedBox(height: Space.x4),
                Wrap(
                  spacing: Space.x3,
                  runSpacing: Space.x3,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  children: [
                    OutlinedButton.icon(
                      onPressed: () => _pickDate(stopped: false),
                      icon: const Icon(Icons.calendar_today_outlined, size: 18),
                      label: Text(
                        _startedOn == null
                            ? '${MedicationEditor.startedLabel}: not set'
                            : '${MedicationEditor.startedLabel}: ${dates.format(_startedOn!)}',
                      ),
                    ),
                    OutlinedButton.icon(
                      onPressed: () => _pickDate(stopped: true),
                      icon: const Icon(Icons.event_busy_outlined, size: 18),
                      label: Text(
                        _stoppedOn == null
                            ? MedicationEditor.stillTaking
                            : '${MedicationEditor.stoppedLabel}: ${dates.format(_stoppedOn!)}',
                      ),
                    ),
                    if (_stoppedOn != null)
                      IconButton(
                        onPressed: () => setState(() => _stoppedOn = null),
                        icon: const Icon(Icons.close),
                        tooltip: 'Clear the stop date',
                      ),
                  ],
                ),
                const SizedBox(height: Space.x8),
                Text(
                  MedicationEditor.ingredientsTitle,
                  style: theme.textTheme.titleMedium,
                ),
                const SizedBox(height: Space.x1),
                Text(
                  MedicationEditor.ingredientsNote,
                  style: theme.textTheme.bodySmall,
                ),
                const SizedBox(height: Space.x3),
                for (var i = 0; i < _ingredients.length; i++)
                  Padding(
                    key: ObjectKey(_ingredients[i]),
                    padding: const EdgeInsets.only(bottom: Space.x3),
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Expanded(
                          flex: 5,
                          child: TextField(
                            controller: _ingredients[i].name,
                            textCapitalization: TextCapitalization.sentences,
                            decoration: const InputDecoration(
                              labelText: MedicationEditor.ingredientName,
                            ),
                          ),
                        ),
                        const SizedBox(width: Space.x2),
                        Expanded(
                          flex: 3,
                          child: TextField(
                            controller: _ingredients[i].amount,
                            keyboardType: number,
                            decoration: const InputDecoration(
                              labelText: MedicationEditor.ingredientAmount,
                            ),
                          ),
                        ),
                        const SizedBox(width: Space.x2),
                        Expanded(
                          flex: 2,
                          child: TextField(
                            controller: _ingredients[i].unit,
                            maxLength: 12,
                            decoration: const InputDecoration(
                              labelText: MedicationEditor.ingredientUnit,
                              counterText: '',
                            ),
                          ),
                        ),
                        IconButton(
                          onPressed: () => _removeIngredient(i),
                          icon: const Icon(Icons.close, size: 18),
                          tooltip: MedicationEditor.removeIngredientTooltip,
                        ),
                      ],
                    ),
                  ),
                Align(
                  alignment: Alignment.centerLeft,
                  child: TextButton.icon(
                    onPressed:
                        _ingredients.length >= MedicationEditor.maxIngredients
                        ? null
                        : _addIngredient,
                    icon: const Icon(Icons.add),
                    label: const Text(MedicationEditor.addIngredientLabel),
                  ),
                ),
                AttentionLine(
                  _localError.isNotEmpty ? _localError : (meds.saveError ?? ''),
                ),
                const SizedBox(height: Space.x8),
                FilledButton(
                  onPressed: meds.isSaving ? null : _save,
                  child: Text(meds.isSaving ? 'Saving' : 'Save'),
                ),
                if (!isNew) ...[
                  const SizedBox(height: Space.x3),
                  TextButton(
                    onPressed: meds.isSaving ? null : _delete,
                    child: const Text(MedicationEditor.deleteLabel),
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}
