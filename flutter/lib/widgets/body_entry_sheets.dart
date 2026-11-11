import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../models/enums.dart';
import '../models/progress.dart';
import '../models/strength.dart';
import '../providers/progress_provider.dart';
import '../theme/tokens.dart';
import '../utils/body_units.dart';
import 'dialogs.dart';
import 'status_views.dart';
import 'viewer_units.dart';

/// Shows the "saved" snackbar with an Undo that runs [undo].
void _showUndo(
  ScaffoldMessengerState messenger,
  String text,
  Future<bool> Function() undo,
) {
  messenger
    ..hideCurrentSnackBar()
    ..showSnackBar(
      SnackBar(
        content: Text(text),
        action: SnackBarAction(
          label: 'Undo',
          onPressed: () async {
            final ok = await undo();
            showMessage(
              messenger,
              ok ? 'Taken back out.' : 'Undo did not work.',
            );
          },
        ),
      ),
    );
}

/// The sheet behind "Add a reading": weight, body fat or waist, typed in the
/// viewer's units and stored metric.
class MeasurementSheet extends StatefulWidget {
  final BodyMeasure initial;

  const MeasurementSheet({super.key, this.initial = BodyMeasure.weight});

  static const String title = 'Add a reading';
  static const String savedMessage = 'Reading saved.';
  static const String saveLabel = 'Save';
  static const String savingLabel = 'Saving';
  static const String notANumber = 'Type a number, for example 82.5.';

  static Future<void> open(BuildContext context) async {
    final provider = context.read<ProgressProvider>();
    final messenger = ScaffoldMessenger.of(context);
    provider.clearActionError();
    final saved = await showModalBottomSheet<Measurement>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (_) => ChangeNotifierProvider.value(
        value: provider,
        child: const MeasurementSheet(),
      ),
    );
    if (saved == null) return;
    final measure = BodyMeasure.fromString(saved.metricKey);
    _showUndo(
      messenger,
      savedMessage,
      () => provider.deleteMeasurement(measure, saved.id),
    );
  }

  /// The unit beside the field for [measure].
  static String unitFor(BodyMeasure measure, Units units) => switch (measure) {
    BodyMeasure.weight => BodyUnits.massUnit(units),
    BodyMeasure.bodyFat => '%',
    BodyMeasure.waist => BodyUnits.lengthUnit(units),
  };

  /// What is stored for a typed [value].
  static double toStored(BodyMeasure measure, double value, Units units) =>
      switch (measure) {
        BodyMeasure.weight => BodyUnits.massToKg(value, units),
        BodyMeasure.bodyFat => value,
        BodyMeasure.waist => BodyUnits.lengthToCm(value, units),
      };

  @override
  State<MeasurementSheet> createState() => _MeasurementSheetState();
}

class _MeasurementSheetState extends State<MeasurementSheet> {
  final _value = TextEditingController();
  late BodyMeasure _measure = widget.initial;
  String _localError = '';

  @override
  void dispose() {
    _value.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final typed = BodyUnits.parse(_value.text);
    if (typed == null) {
      setState(() => _localError = MeasurementSheet.notANumber);
      return;
    }
    setState(() => _localError = '');
    final units = readViewerUnits(context);
    final provider = context.read<ProgressProvider>();
    final saved = await provider.addMeasurement(
      _measure,
      MeasurementSheet.toStored(_measure, typed, units),
    );
    if (saved != null && mounted) Navigator.of(context).pop(saved);
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<ProgressProvider>();
    final theme = Theme.of(context);
    final units = viewerUnitsOf(context);
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
          Text(MeasurementSheet.title, style: theme.textTheme.headlineSmall),
          const SizedBox(height: Space.x6),
          SegmentedButton<BodyMeasure>(
            segments: [
              for (final m in BodyMeasure.values)
                ButtonSegment(value: m, label: Text(m.displayName)),
            ],
            selected: {_measure},
            showSelectedIcon: false,
            onSelectionChanged: (v) => setState(() => _measure = v.first),
          ),
          const SizedBox(height: Space.x6),
          TextField(
            controller: _value,
            autofocus: true,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            decoration: InputDecoration(
              labelText: _measure.displayName,
              suffixText: MeasurementSheet.unitFor(_measure, units),
            ),
            onSubmitted: (_) => _save(),
          ),
          AttentionLine(
            _localError.isNotEmpty ? _localError : (provider.actionError ?? ''),
          ),
          const SizedBox(height: Space.x6),
          FilledButton(
            onPressed: provider.isSaving ? null : _save,
            child: Text(
              provider.isSaving
                  ? MeasurementSheet.savingLabel
                  : MeasurementSheet.saveLabel,
            ),
          ),
        ],
      ),
    );
  }
}

/// The sheet behind "Log a set": the lift, the weight, the reps and an
/// optional effort score. One top set per lift per day.
class StrengthSetSheet extends StatefulWidget {
  const StrengthSetSheet({super.key});

  static const String title = 'Log a set';
  static const String savedMessage = 'Set saved.';
  static const String otherLabel = 'Other';
  static const String liftNameLabel = 'Lift name';
  static const String weightLabel = 'Weight';
  static const String weightHelp = 'Use 0 for a bodyweight set.';
  static const String repsLabel = 'Reps';
  static const String rpeLabel = 'Effort, 1 to 10 (optional)';
  static const String replaceNote =
      'One top set per lift per day: saving the same lift again today '
      'replaces the earlier set.';
  static const String needLift = 'Say which lift.';
  static const String needWeight = 'Type the weight, or 0 for bodyweight.';
  static const String needReps = 'Type the number of reps.';
  static const String badRpe = 'Effort is a number from 1 to 10.';

  static Future<void> open(BuildContext context) async {
    final provider = context.read<ProgressProvider>();
    final messenger = ScaffoldMessenger.of(context);
    provider.clearActionError();
    final saved = await showModalBottomSheet<StrengthSet>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (_) => ChangeNotifierProvider.value(
        value: provider,
        child: const StrengthSetSheet(),
      ),
    );
    if (saved == null) return;
    _showUndo(
      messenger,
      savedMessage,
      () => provider.deleteStrengthSet(saved.id),
    );
  }

  @override
  State<StrengthSetSheet> createState() => _StrengthSetSheetState();
}

class _StrengthSetSheetState extends State<StrengthSetSheet> {
  final _name = TextEditingController();
  final _weight = TextEditingController();
  final _reps = TextEditingController();
  final _rpe = TextEditingController();

  /// Null means "Other": the lift is typed in.
  CommonLift? _lift = CommonLift.squat;
  String _localError = '';

  @override
  void dispose() {
    _name.dispose();
    _weight.dispose();
    _reps.dispose();
    _rpe.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final lift = _lift?.wireValue ?? _name.text.trim();
    final weight = BodyUnits.parse(_weight.text);
    final reps = int.tryParse(_reps.text.trim());
    final rpeText = _rpe.text.trim();
    final rpe = rpeText.isEmpty ? null : BodyUnits.parse(rpeText);
    final String error;
    if (lift.isEmpty) {
      error = StrengthSetSheet.needLift;
    } else if (weight == null || weight < 0) {
      error = StrengthSetSheet.needWeight;
    } else if (reps == null || reps < 1) {
      error = StrengthSetSheet.needReps;
    } else if (rpeText.isNotEmpty && (rpe == null || rpe < 1 || rpe > 10)) {
      error = StrengthSetSheet.badRpe;
    } else {
      error = '';
    }
    setState(() => _localError = error);
    if (error.isNotEmpty) return;
    final units = readViewerUnits(context);
    final provider = context.read<ProgressProvider>();
    final saved = await provider.addStrengthSet(
      StrengthSetDraft(
        lift: lift,
        weightKg: BodyUnits.massToKg(weight!, units),
        reps: reps!,
        rpe: rpe,
      ),
    );
    if (saved != null && mounted) Navigator.of(context).pop(saved);
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<ProgressProvider>();
    final theme = Theme.of(context);
    final units = viewerUnitsOf(context);
    final bottom = MediaQuery.viewInsetsOf(context).bottom;
    const number = TextInputType.numberWithOptions(decimal: true);
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
          Text(StrengthSetSheet.title, style: theme.textTheme.headlineSmall),
          const SizedBox(height: Space.x4),
          Wrap(
            spacing: Space.x2,
            runSpacing: Space.x2,
            children: [
              for (final lift in CommonLift.values)
                ChoiceChip(
                  label: Text(lift.displayName),
                  selected: _lift == lift,
                  onSelected: (_) => setState(() => _lift = lift),
                ),
              ChoiceChip(
                label: const Text(StrengthSetSheet.otherLabel),
                selected: _lift == null,
                onSelected: (_) => setState(() => _lift = null),
              ),
            ],
          ),
          if (_lift == null) ...[
            const SizedBox(height: Space.x4),
            TextField(
              controller: _name,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(
                labelText: StrengthSetSheet.liftNameLabel,
              ),
            ),
          ],
          const SizedBox(height: Space.x4),
          Row(
            children: [
              Expanded(
                child: TextField(
                  controller: _weight,
                  keyboardType: number,
                  decoration: InputDecoration(
                    labelText: StrengthSetSheet.weightLabel,
                    suffixText: BodyUnits.massUnit(units),
                    helperText: StrengthSetSheet.weightHelp,
                  ),
                ),
              ),
              const SizedBox(width: Space.x4),
              Expanded(
                child: TextField(
                  controller: _reps,
                  keyboardType: TextInputType.number,
                  decoration: const InputDecoration(
                    labelText: StrengthSetSheet.repsLabel,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: Space.x4),
          TextField(
            controller: _rpe,
            keyboardType: number,
            decoration: const InputDecoration(
              labelText: StrengthSetSheet.rpeLabel,
            ),
          ),
          const SizedBox(height: Space.x3),
          Text(StrengthSetSheet.replaceNote, style: theme.textTheme.bodySmall),
          AttentionLine(
            _localError.isNotEmpty ? _localError : (provider.actionError ?? ''),
          ),
          const SizedBox(height: Space.x6),
          FilledButton(
            onPressed: provider.isSaving ? null : _save,
            child: Text(provider.isSaving ? 'Saving' : 'Save'),
          ),
        ],
      ),
    );
  }
}
