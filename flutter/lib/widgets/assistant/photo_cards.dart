import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/enums.dart';
import '../../models/journal.dart';
import '../../models/medication.dart';
import '../../models/photo.dart';
import '../../pages/lab_entry_page.dart';
import '../../pages/you/medicines_page.dart';
import '../../providers/assistant_chat_provider.dart';
import '../../providers/assistant_chat_session.dart';
import '../../providers/journal_provider.dart';
import '../../providers/progress_provider.dart';
import '../../services/photo_picker.dart';
import '../../theme/app_theme.dart';
import '../../theme/tokens.dart';
import '../../utils/body_units.dart';
import '../day_log.dart' show journalKindIcon;
import '../status_views.dart';
import '../viewer_units.dart';

// Photos in the assistant (docs/photo-design.md): the button in the message
// box, the sheet that picks a photo, and the card that shows what the home
// model read. Nothing is saved until the person presses save.

/// The photo button in the message box. Shown only when the server says
/// photos are on.
class PhotoButton extends StatelessWidget {
  final PhotoPicker picker;
  final bool enabled;

  /// Whether the sheet offers the camera. Not on the web.
  final bool camera;

  const PhotoButton({
    super.key,
    required this.picker,
    this.enabled = true,
    this.camera = !kIsWeb,
  });

  static const String tooltip = 'Add a photo';

  Future<void> _pick(BuildContext context) async {
    final chat = context.read<AssistantChatProvider>();
    final source = await showModalBottomSheet<PhotoSource>(
      context: context,
      useSafeArea: true,
      builder: (_) => PhotoSourceSheet(camera: camera),
    );
    if (source == null) return;
    final bytes = await picker.pick(source);
    if (bytes == null || bytes.isEmpty) return;
    await chat.readPhoto(bytes);
  }

  @override
  Widget build(BuildContext context) {
    return IconButton(
      tooltip: tooltip,
      onPressed: enabled ? () => _pick(context) : null,
      icon: const Icon(Icons.photo_camera_outlined),
    );
  }
}

/// Take a photo (not on the web) or choose one, with the privacy line.
class PhotoSourceSheet extends StatelessWidget {
  final bool camera;

  const PhotoSourceSheet({super.key, this.camera = !kIsWeb});

  static const String takeLabel = 'Take a photo';
  static const String chooseLabel = 'Choose a photo';
  static const String privacyLine =
      'The photo goes to your home server and its model, is read once and is '
      'not kept.';

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: Space.x3),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (camera)
              ListTile(
                leading: const Icon(Icons.photo_camera_outlined),
                title: const Text(takeLabel),
                onTap: () => Navigator.of(context).pop(PhotoSource.camera),
              ),
            ListTile(
              leading: const Icon(Icons.photo_library_outlined),
              title: const Text(chooseLabel),
              onTap: () => Navigator.of(context).pop(PhotoSource.gallery),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(
                Space.x4,
                Space.x2,
                Space.x4,
                Space.x2,
              ),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Icon(Icons.lock_outline, size: 18, color: colors.textSoft),
                  const SizedBox(width: Space.x2),
                  Expanded(
                    child: Text(privacyLine, style: theme.textTheme.bodySmall),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// How a reading from a display is shown and typed: in the person's units
/// for weight and temperature, in the metric's own unit otherwise.
abstract final class ReadingUnits {
  static String name(PhotoReading r) => switch (r.metricKey) {
    'weight_kg' => 'Weight',
    'body_fat_pct' => 'Body fat',
    'blood_pressure_sys' => 'Systolic pressure',
    'blood_pressure_dia' => 'Diastolic pressure',
    'heart_rate_bpm' => 'Pulse',
    'body_temp_c' => 'Temperature',
    'blood_glucose_mmol' => 'Blood glucose',
    'spo2_pct' => 'Oxygen',
    _ => r.label.isEmpty ? r.metricKey : r.label,
  };

  static String unit(String metricKey, String stored, Units units) =>
      switch (metricKey) {
        'weight_kg' => BodyUnits.massUnit(units),
        'body_temp_c' => units == Units.imperial ? 'F' : 'C',
        'body_fat_pct' || 'spo2_pct' => '%',
        'blood_pressure_sys' || 'blood_pressure_dia' => 'mmHg',
        'heart_rate_bpm' => 'bpm',
        'blood_glucose_mmol' => 'mmol/L',
        _ => stored,
      };

  /// A stored value in the person's units.
  static double shown(String metricKey, double value, Units units) =>
      switch (metricKey) {
        'weight_kg' => BodyUnits.massFromKg(value, units),
        'body_temp_c' when units == Units.imperial => value * 9 / 5 + 32,
        _ => value,
      };

  /// What the person typed, back in the metric's own unit.
  static double stored(String metricKey, double value, Units units) =>
      switch (metricKey) {
        'weight_kg' => BodyUnits.massToKg(value, units),
        'body_temp_c' when units == Units.imperial => (value - 32) * 5 / 9,
        _ => value,
      };

  static int decimals(String metricKey) => switch (metricKey) {
    'blood_pressure_sys' || 'blood_pressure_dia' || 'heart_rate_bpm' => 0,
    'spo2_pct' => 0,
    _ => 1,
  };
}

/// Servings eaten times the numbers a label prints per serving, to one
/// decimal. Null stays null.
double? labelTotal(double? perServing, double servings) {
  if (perServing == null) return null;
  return (perServing * servings * 10).roundToDouble() / 10;
}

/// One photo in the thread: the picture while it is read, then the card for
/// its kind, then the saved line.
class PhotoCard<T extends ChatSession> extends StatelessWidget {
  final ChatItem item;

  const PhotoCard({super.key, required this.item});

  static const String lookingLine = 'Looking at the photo';
  static const String tryAgainLabel = 'Try again';
  static const String discardLabel = 'Discard';
  static const String saveLabel = 'Save';
  static const String notRightLabel = 'Not right?';
  static const String notRightTooltip = 'Read it as something else';
  static const String imageLabel = 'Your photo';
  static const String guessLine =
      'A guess from the picture. It can be well off, so change anything that '
      'looks wrong.';
  static const String servingsLabel = 'Servings eaten';
  static const String addToMedicinesLabel = 'Add to medicines';
  static const String reviewLabel = 'Review the panel';
  static const String sendLabel = 'Send';
  static const String undoLabel = 'Undo';
  static const String undoneLine = 'Taken back out.';

  @override
  Widget build(BuildContext context) {
    final photo = item.photo;
    final session = context.read<T>();
    if (photo == null || session is! AssistantChatProvider) {
      return const SizedBox.shrink();
    }
    if (photo.stage == PhotoStage.saved) {
      return _SavedLine(item: item, chat: session);
    }
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final width = MediaQuery.sizeOf(context).width;
    final maxWidth = (width * 0.9).clamp(260.0, 560.0);
    final bytes = photo.bytes;
    final result = photo.result;

    final Widget body = switch (photo.stage) {
      PhotoStage.reading => Row(
        children: [
          const SizedBox(
            width: 14,
            height: 14,
            child: CircularProgressIndicator(strokeWidth: 2),
          ),
          const SizedBox(width: Space.x2),
          Expanded(child: Text(lookingLine, style: theme.textTheme.bodySmall)),
        ],
      ),
      PhotoStage.failed => _Failed(item: item, chat: session),
      _ when result == null => const SizedBox.shrink(),
      _ => switch (result.shownKind) {
        PhotoKind.meal || PhotoKind.nutritionLabel => _FoodForm(
          key: ValueKey('food-${item.key}-${photo.attempt}'),
          item: item,
          chat: session,
          result: result,
        ),
        PhotoKind.medicineLabel => _MedicineBody(
          item: item,
          chat: session,
          medicine: result.medicine!,
        ),
        PhotoKind.deviceDisplay => _ReadingsForm(
          key: ValueKey('readings-${item.key}-${photo.attempt}'),
          item: item,
          chat: session,
          readings: result.readings,
        ),
        PhotoKind.labReport => _LabBody(
          item: item,
          chat: session,
          result: result,
        ),
        PhotoKind.other => _OtherBody(
          item: item,
          chat: session,
          description: result.description,
        ),
      },
    };

    return Align(
      alignment: Alignment.centerLeft,
      child: ConstrainedBox(
        constraints: BoxConstraints(maxWidth: maxWidth),
        child: DecoratedBox(
          key: ValueKey('photo-card-${item.key}'),
          decoration: BoxDecoration(
            color: colors.surfaceRaised,
            borderRadius: Radii.cardRadius,
            border: Border.all(color: colors.hairline),
          ),
          child: Padding(
            padding: const EdgeInsets.all(Space.x4),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (bytes != null)
                  ClipRRect(
                    borderRadius: Radii.controlRadius,
                    child: Image.memory(
                      bytes,
                      height: 180,
                      fit: BoxFit.cover,
                      gaplessPlayback: true,
                      semanticLabel: imageLabel,
                      errorBuilder: (context, _, _) => SizedBox(
                        height: 72,
                        child: Center(
                          child: Icon(
                            Icons.image_outlined,
                            color: colors.textSoft,
                            semanticLabel: imageLabel,
                          ),
                        ),
                      ),
                    ),
                  ),
                if (bytes != null) const SizedBox(height: Space.x3),
                if (photo.stage == PhotoStage.ready && result != null)
                  _CardHeader(
                    item: item,
                    chat: session,
                    kind: result.shownKind,
                  ),
                body,
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// The kind the card reads as, and "Not right?" with the other kinds.
class _CardHeader extends StatelessWidget {
  final ChatItem item;
  final AssistantChatProvider chat;
  final PhotoKind kind;

  const _CardHeader({
    required this.item,
    required this.chat,
    required this.kind,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final busy = item.photo?.saving ?? false;
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.x2),
      child: Wrap(
        alignment: WrapAlignment.spaceBetween,
        crossAxisAlignment: WrapCrossAlignment.center,
        spacing: Space.x2,
        children: [
          Text(kind.displayName, style: theme.textTheme.titleSmall),
          PopupMenuButton<PhotoKind>(
            tooltip: PhotoCard.notRightTooltip,
            enabled: !busy,
            onSelected: (k) => chat.rereadPhoto(item, k),
            itemBuilder: (_) => [
              for (final k in PhotoKind.values)
                if (k != kind)
                  PopupMenuItem(value: k, child: Text(k.displayName)),
            ],
            child: ConstrainedBox(
              constraints: const BoxConstraints(minHeight: 48),
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: Space.x2),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(
                      PhotoCard.notRightLabel,
                      style: theme.textTheme.labelLarge?.copyWith(
                        color: colors.textSoft,
                      ),
                    ),
                    Icon(Icons.arrow_drop_down, color: colors.textSoft),
                  ],
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _Failed extends StatelessWidget {
  final ChatItem item;
  final AssistantChatProvider chat;

  const _Failed({required this.item, required this.chat});

  @override
  Widget build(BuildContext context) {
    final photo = item.photo!;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        AttentionLine(photo.error ?? ''),
        const SizedBox(height: Space.x2),
        _Actions(
          children: [
            FilledButton(
              onPressed: photo.bytes == null
                  ? null
                  : () => chat.retryPhoto(item),
              child: const Text(PhotoCard.tryAgainLabel),
            ),
            TextButton(
              onPressed: () => chat.discardPhoto(item),
              child: const Text(PhotoCard.discardLabel),
            ),
          ],
        ),
      ],
    );
  }
}

/// The buttons at the foot of a card.
class _Actions extends StatelessWidget {
  final List<Widget> children;

  const _Actions({required this.children});

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(top: Space.x2),
    child: Wrap(
      spacing: Space.x2,
      runSpacing: Space.x2,
      crossAxisAlignment: WrapCrossAlignment.center,
      children: children,
    ),
  );
}

/// A number box for the food and readings forms.
class _NumberField extends StatelessWidget {
  final TextEditingController controller;
  final String label;
  final String? suffix;
  final ValueChanged<String>? onChanged;

  const _NumberField({
    required this.controller,
    required this.label,
    this.suffix,
    this.onChanged,
  });

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 150,
      child: TextField(
        controller: controller,
        keyboardType: const TextInputType.numberWithOptions(decimal: true),
        style: AppTheme.numeric(context),
        onChanged: onChanged,
        decoration: InputDecoration(
          labelText: label,
          suffixText: suffix,
          isDense: true,
        ),
      ),
    );
  }
}

String _field(double? value) =>
    value == null ? '' : BodyUnits.forField(value, decimals: 1);

/// Meal and nutrition label: what it was, its tags and portion, the four
/// numbers (for a label, per serving with the servings eaten and the
/// totals), then Save and Discard.
class _FoodForm extends StatefulWidget {
  final ChatItem item;
  final AssistantChatProvider chat;
  final PhotoAnalysis result;

  const _FoodForm({
    super.key,
    required this.item,
    required this.chat,
    required this.result,
  });

  static const String whatLabel = 'What it was';
  static const String guessLine = PhotoCard.guessLine;
  static const String labelLine =
      'Read from the label, per serving. Change anything that looks wrong.';
  static const String servingsLabel = PhotoCard.servingsLabel;
  static const String energyLabel = 'Energy';
  static const String proteinLabel = 'Protein';
  static const String carbsLabel = 'Carbohydrate';
  static const String fatLabel = 'Fat';
  static const String totalsTitle = 'In total';
  static const String badNumber =
      'Use numbers only, or leave a box empty to leave that number out.';
  static const String badServings = 'Servings eaten is a number above 0.';

  @override
  State<_FoodForm> createState() => _FoodFormState();
}

class _FoodFormState extends State<_FoodForm> {
  final _text = TextEditingController();
  final _energy = TextEditingController();
  final _protein = TextEditingController();
  final _carbs = TextEditingController();
  final _fat = TextEditingController();
  final _servings = TextEditingController(text: '1');
  final Set<String> _tags = {};
  JournalPortion? _portion;
  JournalKind _kind = JournalKind.food;
  String _localError = '';

  bool get _isLabel => widget.result.shownKind == PhotoKind.nutritionLabel;

  @override
  void initState() {
    super.initState();
    final meal = widget.result.meal;
    final label = widget.result.label;
    if (_isLabel && label != null) {
      _text.text = label.product ?? '';
      _energy.text = _field(label.energyKcal);
      _protein.text = _field(label.proteinG);
      _carbs.text = _field(label.carbsG);
      _fat.text = _field(label.fatG);
    } else if (meal != null) {
      _text.text = meal.text;
      _tags.addAll(meal.tags);
      _portion = meal.portion;
      _energy.text = _field(meal.energyKcal);
      _protein.text = _field(meal.proteinG);
      _carbs.text = _field(meal.carbsG);
      _fat.text = _field(meal.fatG);
    }
  }

  @override
  void dispose() {
    for (final c in [_text, _energy, _protein, _carbs, _fat, _servings]) {
      c.dispose();
    }
    super.dispose();
  }

  /// The typed number, null when the box is empty; throws when it is not a
  /// number at or above 0.
  static double? _read(TextEditingController c) {
    final text = c.text.trim();
    if (text.isEmpty) return null;
    final value = BodyUnits.parse(text);
    if (value == null || value < 0) throw const FormatException();
    return value;
  }

  double? get _servingsEaten {
    final value = BodyUnits.parse(_servings.text);
    if (value == null || value <= 0) return null;
    return value;
  }

  /// The numbers to save, or null when one box does not hold a number.
  JournalNutrition? _numbers() {
    final double? energy, protein, carbs, fat;
    try {
      energy = _read(_energy);
      protein = _read(_protein);
      carbs = _read(_carbs);
      fat = _read(_fat);
    } on FormatException {
      return null;
    }
    if (!_isLabel) {
      return JournalNutrition(
        basis: NutritionBasis.estimate,
        energyKcal: energy,
        proteinG: protein,
        carbsG: carbs,
        fatG: fat,
      );
    }
    final servings = _servingsEaten ?? 1;
    return JournalNutrition(
      basis: NutritionBasis.label,
      energyKcal: labelTotal(energy, servings),
      proteinG: labelTotal(protein, servings),
      carbsG: labelTotal(carbs, servings),
      fatG: labelTotal(fat, servings),
    );
  }

  Future<void> _save() async {
    final chat = widget.chat;
    final item = widget.item;
    if (_isLabel && _servingsEaten == null) {
      setState(() => _localError = _FoodForm.badServings);
      return;
    }
    final numbers = _numbers();
    if (numbers == null) {
      setState(() => _localError = _FoodForm.badNumber);
      return;
    }
    setState(() => _localError = '');
    final journal = context.read<JournalProvider>();
    final text = _text.text.trim();
    final draft = JournalDraft(
      kind: _kind,
      text: text.isEmpty ? null : text,
      tags: _isLabel ? const [] : _tags.toList(),
      portion: _isLabel ? null : _portion,
      nutrition: numbers.hasAny ? numbers : null,
    );
    chat.photoSaving(item, saving: true);
    final entry = await journal.add(draft);
    if (entry == null) {
      chat.photoSaving(item, saving: false, error: journal.saveError);
      return;
    }
    final what = text.isEmpty ? '' : ': $text';
    chat.photoSaved(
      item,
      line: '${_kind.displayName}$what',
      kind: _kind,
      entryId: entry.id,
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final journal = context.watch<JournalProvider>();
    final photo = widget.item.photo!;
    final label = widget.result.label;
    final tagChoices = <String>{
      ...journal.tagsFor(JournalKind.food).map((t) => t.tag),
      ..._tags,
    };
    final servingSize = label?.servingSize?.trim() ?? '';
    final perPack = label?.servingsPerContainer;
    final totals = _isLabel ? _numbers() : null;
    final error = _localError.isNotEmpty
        ? _localError
        : (photo.saveError ?? '');

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        TextField(
          controller: _text,
          maxLength: 200,
          buildCounter:
              (
                context, {
                required currentLength,
                required isFocused,
                maxLength,
              }) => null,
          decoration: const InputDecoration(labelText: _FoodForm.whatLabel),
        ),
        const SizedBox(height: Space.x3),
        Wrap(
          spacing: Space.x2,
          runSpacing: Space.x2,
          children: [
            for (final k in const [JournalKind.food, JournalKind.drink])
              ChoiceChip(
                avatar: Icon(journalKindIcon(k), size: 16),
                label: Text(k.displayName),
                selected: _kind == k,
                onSelected: (_) => setState(() => _kind = k),
              ),
          ],
        ),
        if (!_isLabel && tagChoices.isNotEmpty) ...[
          const SizedBox(height: Space.x3),
          Wrap(
            spacing: Space.x2,
            runSpacing: Space.x2,
            children: [
              for (final tag in tagChoices)
                FilterChip(
                  label: Text(journal.labelFor(tag)),
                  selected: _tags.contains(tag),
                  onSelected: (on) =>
                      setState(() => on ? _tags.add(tag) : _tags.remove(tag)),
                ),
            ],
          ),
        ],
        if (!_isLabel) ...[
          const SizedBox(height: Space.x3),
          Wrap(
            spacing: Space.x2,
            runSpacing: Space.x2,
            children: [
              for (final p in JournalPortion.values)
                ChoiceChip(
                  label: Text(p.displayName),
                  selected: _portion == p,
                  onSelected: (on) => setState(() => _portion = on ? p : null),
                ),
            ],
          ),
        ],
        if (_isLabel && (servingSize.isNotEmpty || perPack != null)) ...[
          const SizedBox(height: Space.x3),
          Text(
            [
              if (servingSize.isNotEmpty) 'One serving: $servingSize',
              if (perPack != null)
                '${BodyUnits.forField(perPack)} servings in the pack',
            ].join('. '),
            style: theme.textTheme.bodySmall,
          ),
        ],
        const SizedBox(height: Space.x3),
        Text(
          _isLabel ? _FoodForm.labelLine : _FoodForm.guessLine,
          style: theme.textTheme.bodySmall,
        ),
        const SizedBox(height: Space.x3),
        Wrap(
          spacing: Space.x3,
          runSpacing: Space.x3,
          children: [
            _NumberField(
              controller: _energy,
              label: _FoodForm.energyLabel,
              suffix: 'kcal',
              onChanged: (_) => setState(() {}),
            ),
            _NumberField(
              controller: _protein,
              label: _FoodForm.proteinLabel,
              suffix: 'g',
              onChanged: (_) => setState(() {}),
            ),
            _NumberField(
              controller: _carbs,
              label: _FoodForm.carbsLabel,
              suffix: 'g',
              onChanged: (_) => setState(() {}),
            ),
            _NumberField(
              controller: _fat,
              label: _FoodForm.fatLabel,
              suffix: 'g',
              onChanged: (_) => setState(() {}),
            ),
          ],
        ),
        if (_isLabel) ...[
          const SizedBox(height: Space.x3),
          _NumberField(
            controller: _servings,
            label: _FoodForm.servingsLabel,
            onChanged: (_) => setState(() {}),
          ),
          const SizedBox(height: Space.x3),
          Text(_FoodForm.totalsTitle, style: theme.textTheme.labelLarge),
          const SizedBox(height: Space.x1),
          Text(
            totals != null && totals.hasAny && _servingsEaten != null
                ? _totalsLine(totals)
                : '',
            key: const ValueKey('label-totals'),
            style: AppTheme.numeric(context, base: theme.textTheme.bodyMedium),
          ),
        ],
        AttentionLine(error),
        _Actions(
          children: [
            FilledButton(
              onPressed: photo.saving ? null : _save,
              child: const Text(PhotoCard.saveLabel),
            ),
            TextButton(
              onPressed: photo.saving
                  ? null
                  : () => widget.chat.discardPhoto(widget.item),
              child: const Text(PhotoCard.discardLabel),
            ),
          ],
        ),
      ],
    );
  }

  /// "460 kcal, 6 g protein, 70 g carbohydrate, 18 g fat".
  static String _totalsLine(JournalNutrition n) => [
    if (n.energyKcal != null) '${_field(n.energyKcal)} kcal',
    if (n.proteinG != null) '${_field(n.proteinG)} g protein',
    if (n.carbsG != null) '${_field(n.carbsG)} g carbohydrate',
    if (n.fatG != null) '${_field(n.fatG)} g fat',
  ].join(', ');
}

/// A medicine or supplement label: what was read, and "Add to medicines".
class _MedicineBody extends StatelessWidget {
  final ChatItem item;
  final AssistantChatProvider chat;
  final PhotoMedicine medicine;

  const _MedicineBody({
    required this.item,
    required this.chat,
    required this.medicine,
  });

  static const String addLabel = PhotoCard.addToMedicinesLabel;
  static const String note =
      'Opens the medicines form with this filled in. Check it before you save.';

  Future<void> _add(BuildContext context) async {
    final saved = await MedicationEditor.open(
      context,
      prefill: medicine.toDraft(),
    );
    if (!saved) return;
    final name = [
      medicine.brand ?? '',
      medicine.product,
    ].where((p) => p.trim().isNotEmpty).join(' ');
    chat.photoSaved(item, line: 'Added to medicines: $name');
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final name = [
      medicine.brand ?? '',
      medicine.product,
    ].where((p) => p.trim().isNotEmpty).join(' ');
    final dose = medicine.dose?.trim() ?? '';
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(name, style: theme.textTheme.bodyLarge),
        const SizedBox(height: Space.x1),
        Text(
          dose.isEmpty
              ? medicine.kind.displayName
              : '${medicine.kind.displayName}, $dose',
          style: theme.textTheme.bodySmall,
        ),
        if (medicine.ingredients.isNotEmpty) ...[
          const SizedBox(height: Space.x2),
          for (final i in medicine.ingredients)
            Text(_ingredient(i), style: theme.textTheme.bodyMedium),
        ],
        const SizedBox(height: Space.x2),
        Text(note, style: theme.textTheme.bodySmall),
        _Actions(
          children: [
            FilledButton(
              onPressed: () => _add(context),
              child: const Text(addLabel),
            ),
            TextButton(
              onPressed: () => chat.discardPhoto(item),
              child: const Text(PhotoCard.discardLabel),
            ),
          ],
        ),
      ],
    );
  }

  static String _ingredient(MedicationIngredient i) {
    final amount = i.amount == null
        ? ''
        : ' ${BodyUnits.forField(i.amount!, decimals: 3)}';
    final unit = (i.unit ?? '').trim();
    return '${i.name}$amount${unit.isEmpty ? '' : ' $unit'}';
  }
}

/// The numbers on a display, one row each in the person's units.
class _ReadingsForm extends StatefulWidget {
  final ChatItem item;
  final AssistantChatProvider chat;
  final List<PhotoReading> readings;

  const _ReadingsForm({
    super.key,
    required this.item,
    required this.chat,
    required this.readings,
  });

  static const String note =
      'Change anything that looks wrong. Leave a box empty to leave it out.';
  static const String badNumber = 'Use numbers only.';
  static const String needOne = 'Keep at least one reading, or discard.';

  @override
  State<_ReadingsForm> createState() => _ReadingsFormState();
}

class _ReadingsFormState extends State<_ReadingsForm> {
  final List<TextEditingController> _controllers = [];

  /// Readings already saved by an earlier press, so a second press after a
  /// refusal does not save them twice.
  final Set<int> _saved = {};
  late final Units _units;
  String _localError = '';

  @override
  void initState() {
    super.initState();
    _units = readViewerUnits(context);
    for (final r in widget.readings) {
      final shown = ReadingUnits.shown(r.metricKey, r.value, _units);
      _controllers.add(
        TextEditingController(
          text: BodyUnits.forField(
            shown,
            decimals: ReadingUnits.decimals(r.metricKey),
          ),
        ),
      );
    }
  }

  @override
  void dispose() {
    for (final c in _controllers) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> _save() async {
    final chat = widget.chat;
    final item = widget.item;
    final send = <({String metricKey, double value})>[];
    final rows = <int>[];
    for (var i = 0; i < widget.readings.length; i++) {
      if (_saved.contains(i)) continue;
      final text = _controllers[i].text.trim();
      if (text.isEmpty) continue;
      final value = BodyUnits.parse(text);
      if (value == null) {
        setState(() => _localError = _ReadingsForm.badNumber);
        return;
      }
      final key = widget.readings[i].metricKey;
      send.add((
        metricKey: key,
        value: ReadingUnits.stored(key, value, _units),
      ));
      rows.add(i);
    }
    if (send.isEmpty && _saved.isEmpty) {
      setState(() => _localError = _ReadingsForm.needOne);
      return;
    }
    setState(() => _localError = '');
    final progress = context.read<ProgressProvider>();
    chat.photoSaving(item, saving: true);
    final count = await progress.addReadings(send);
    if (!mounted) return;
    setState(() => _saved.addAll(rows.take(count)));
    if (count < send.length) {
      chat.photoSaving(item, saving: false, error: progress.actionError);
      return;
    }
    final total = _saved.length;
    chat.photoSaved(
      item,
      line: total == 1 ? 'Saved 1 reading' : 'Saved $total readings',
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final photo = widget.item.photo!;
    final error = _localError.isNotEmpty
        ? _localError
        : (photo.saveError ?? '');
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (var i = 0; i < widget.readings.length; i++)
          Padding(
            padding: const EdgeInsets.only(bottom: Space.x3),
            child: Wrap(
              crossAxisAlignment: WrapCrossAlignment.center,
              spacing: Space.x3,
              runSpacing: Space.x2,
              children: [
                SizedBox(
                  width: 150,
                  child: Text(
                    ReadingUnits.name(widget.readings[i]),
                    style: theme.textTheme.bodyMedium,
                  ),
                ),
                if (_saved.contains(i))
                  Text('Saved', style: theme.textTheme.bodySmall)
                else
                  _NumberField(
                    controller: _controllers[i],
                    label: ReadingUnits.name(widget.readings[i]),
                    suffix: ReadingUnits.unit(
                      widget.readings[i].metricKey,
                      widget.readings[i].unit,
                      _units,
                    ),
                  ),
              ],
            ),
          ),
        Text(_ReadingsForm.note, style: theme.textTheme.bodySmall),
        AttentionLine(error),
        _Actions(
          children: [
            FilledButton(
              onPressed: photo.saving ? null : _save,
              child: const Text(PhotoCard.saveLabel),
            ),
            TextButton(
              onPressed: photo.saving
                  ? null
                  : () => widget.chat.discardPhoto(widget.item),
              child: const Text(PhotoCard.discardLabel),
            ),
          ],
        ),
      ],
    );
  }
}

/// A lab report: how many markers were found, and "Review the panel".
class _LabBody extends StatelessWidget {
  final ChatItem item;
  final AssistantChatProvider chat;
  final PhotoAnalysis result;

  const _LabBody({
    required this.item,
    required this.chat,
    required this.result,
  });

  static const String reviewLabel = PhotoCard.reviewLabel;

  static String foundLine(int count) => switch (count) {
    0 => 'No markers were matched. You can still type the values in.',
    1 => 'Found 1 marker on the report.',
    _ => 'Found $count markers on the report.',
  };

  Future<void> _review(BuildContext context) async {
    final lab = result.lab;
    if (lab == null) return;
    final kept = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) => LabEntryPage(prefill: lab, prefillProvider: 'other'),
      ),
    );
    if (kept == true) chat.photoSaved(item, line: 'Lab panel kept');
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final count = result.lab?.bestByMetric().length ?? 0;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(foundLine(count), style: theme.textTheme.bodyMedium),
        _Actions(
          children: [
            FilledButton(
              onPressed: () => _review(context),
              child: const Text(reviewLabel),
            ),
            TextButton(
              onPressed: () => chat.discardPhoto(item),
              child: const Text(PhotoCard.discardLabel),
            ),
          ],
        ),
      ],
    );
  }
}

/// Anything else: the description, which goes with the person's question.
class _OtherBody extends StatelessWidget {
  final ChatItem item;
  final AssistantChatProvider chat;
  final String description;

  const _OtherBody({
    required this.item,
    required this.chat,
    required this.description,
  });

  static const String askLine =
      'Ask about it below and this goes with your message, or send it as it '
      'is.';
  static const String sendLabel = PhotoCard.sendLabel;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final waiting = identical(chat.pendingPhoto, item);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(description, style: theme.textTheme.bodyMedium),
        if (waiting) ...[
          const SizedBox(height: Space.x2),
          Text(askLine, style: theme.textTheme.bodySmall),
        ],
        _Actions(
          children: [
            FilledButton(
              onPressed: waiting && chat.canSend ? () => chat.send('') : null,
              child: const Text(sendLabel),
            ),
            TextButton(
              onPressed: () => chat.discardPhoto(item),
              child: const Text(PhotoCard.discardLabel),
            ),
          ],
        ),
      ],
    );
  }
}

/// What a saved photo leaves in the thread, in the style of the capture
/// chips: "Saved", the line, and Undo for a day log entry.
class _SavedLine extends StatelessWidget {
  final ChatItem item;
  final AssistantChatProvider chat;

  const _SavedLine({required this.item, required this.chat});

  static const String savedLabel = 'Saved';
  static const String undoLabel = PhotoCard.undoLabel;
  static const String undoingLine = 'Taking it back out';
  static const String undoneLine = PhotoCard.undoneLine;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final photo = item.photo!;
    if (photo.undone) {
      return Align(
        alignment: Alignment.centerRight,
        child: Text(undoneLine, style: theme.textTheme.bodySmall),
      );
    }
    final kind = photo.savedKind;
    final journal = context.read<JournalProvider>();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.end,
      children: [
        Wrap(
          alignment: WrapAlignment.end,
          crossAxisAlignment: WrapCrossAlignment.center,
          spacing: Space.x2,
          runSpacing: Space.x2,
          children: [
            Text(savedLabel, style: theme.textTheme.bodySmall),
            ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 280),
              child: Chip(
                avatar: Icon(
                  kind == null ? Icons.photo_outlined : journalKindIcon(kind),
                  size: 16,
                ),
                label: Text(
                  photo.savedLine,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
                visualDensity: VisualDensity.compact,
              ),
            ),
            if (photo.undoing)
              Text(undoingLine, style: theme.textTheme.bodySmall)
            else if (photo.savedEntryId != null)
              TextButton(
                onPressed: () => chat.undoPhoto(
                  item,
                  journal.remove,
                  why: () => journal.saveError,
                ),
                child: const Text(undoLabel),
              ),
          ],
        ),
        if (photo.undoError != null) AttentionLine(photo.undoError!),
      ],
    );
  }
}
