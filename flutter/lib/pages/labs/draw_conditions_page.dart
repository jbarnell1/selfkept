import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/lab_review.dart';
import '../../services/lab_review_service.dart';
import '../../theme/tokens.dart';
import '../../utils/api_error.dart';
import '../../widgets/page_body.dart';
import '../../widgets/status_views.dart';
import '../../widgets/subpage_app_bar.dart';

/// How the blood was drawn, asked once a panel is kept and editable later:
/// fasting, the time of the draw, and two optional yes or no answers. A
/// panel the server pre-filled (Rythm) opens with its answers in place.
/// Pops with the conditions as stored, or null when nothing was changed.
class DrawConditionsPage extends StatefulWidget {
  final String reportId;
  final DrawConditions initial;

  /// "Rythm": named in the pre-fill note.
  final String providerLabel;

  const DrawConditionsPage({
    super.key,
    required this.reportId,
    this.initial = const DrawConditions(),
    this.providerLabel = '',
  });

  static const String title = 'How was the blood drawn?';
  static const String subtitle =
      'Every answer is optional. Fasting and the time of day change some results, so the review reads a panel against the last one with them in mind.';
  static const String fastingLabel = 'Were you fasting?';
  static const String hoursLabel = 'Hours since you last ate';
  static const String timeLabel = 'Time of the draw';
  static const String noTime = 'Not known';
  static const String periodLabel = 'Or the part of the day';
  static const String fromTimeNote = 'Worked out from the time.';
  static const String trainingLabel = 'Hard training in the days before?';
  static const String unwellLabel = 'Unwell around the draw?';
  static const String saveLabel = 'Save';
  static const String skipLabel = 'Skip for now';
  static const String clearTimeTooltip = 'Clear the time';
  static const String wholeHours = 'Enter whole hours.';
  static const String yesLabel = 'Yes';
  static const String noLabel = 'No';
  static const String notSureLabel = 'Not sure';

  /// Above the form while the answers are the provider's pre-fill.
  static String prefillNote(String provider) =>
      'Filled in from how ${provider.isEmpty ? 'this lab' : provider} asks for blood to be drawn. Change anything that was different.';

  @override
  State<DrawConditionsPage> createState() => _DrawConditionsPageState();
}

class _DrawConditionsPageState extends State<DrawConditionsPage> {
  late DrawFasting _fasting = widget.initial.fasting;
  late final TextEditingController _hours = TextEditingController(
    text: widget.initial.fastingHours?.toString() ?? '',
  );
  late String? _time = widget.initial.drawTime;
  late DrawPeriod _period = widget.initial.drawPeriod;
  late bool? _training = widget.initial.hardTraining;
  late bool? _unwell = widget.initial.unwell;
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _hours.dispose();
    super.dispose();
  }

  Future<void> _pickTime() async {
    final parts = (_time ?? '08:00').split(':');
    final picked = await showTimePicker(
      context: context,
      initialTime: TimeOfDay(
        hour: int.tryParse(parts.first) ?? 8,
        minute: parts.length > 1 ? int.tryParse(parts[1]) ?? 0 : 0,
      ),
    );
    if (picked == null || !mounted) return;
    setState(() {
      _time =
          '${picked.hour.toString().padLeft(2, '0')}:'
          '${picked.minute.toString().padLeft(2, '0')}';
    });
  }

  Future<void> _save() async {
    final text = _hours.text.trim();
    int? hours;
    if (_fasting != DrawFasting.no && text.isNotEmpty) {
      hours = int.tryParse(text);
      if (hours == null) {
        setState(() => _error = DrawConditionsPage.wholeHours);
        return;
      }
    }
    final next = DrawConditions(
      fasting: _fasting,
      fastingHours: hours,
      drawTime: _time,
      drawPeriod: _time == null ? _period : DrawPeriod.unknown,
      hardTraining: _training,
      unwell: _unwell,
    );
    // Untouched answers stay as they are, pre-fill included.
    final i = widget.initial;
    final unchanged =
        _fasting == i.fasting &&
        hours == i.fastingHours &&
        _time == i.drawTime &&
        (_time != null || _period == i.drawPeriod) &&
        _training == i.hardTraining &&
        _unwell == i.unwell;
    if (unchanged) {
      Navigator.of(context).pop();
      return;
    }
    final service = context.read<LabReviewService>();
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final report = await service.updateConditions(widget.reportId, next);
      if (!mounted) return;
      Navigator.of(context).pop(report.conditions ?? next);
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error = describeError(e);
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    Widget label(String text) => Padding(
      padding: const EdgeInsets.only(top: Space.x6, bottom: Space.x2),
      child: Text(text, style: theme.textTheme.titleSmall),
    );
    Widget yesNo(String key, bool? value, ValueChanged<bool?> onChanged) =>
        Wrap(
          spacing: Space.x2,
          children: [
            ChoiceChip(
              key: ValueKey('$key-yes'),
              label: const Text(DrawConditionsPage.yesLabel),
              selected: value == true,
              onSelected: (on) => onChanged(on ? true : null),
            ),
            ChoiceChip(
              key: ValueKey('$key-no'),
              label: const Text(DrawConditionsPage.noLabel),
              selected: value == false,
              onSelected: (on) => onChanged(on ? false : null),
            ),
          ],
        );

    return Scaffold(
      appBar: const SubpageAppBar(fallback: '/labs', title: Text('Labs')),
      body: SafeArea(
        child: PageBody(
          children: [
            const PageHeading(
              title: DrawConditionsPage.title,
              subtitle: DrawConditionsPage.subtitle,
            ),
            if (widget.initial.defaulted)
              Text(
                DrawConditionsPage.prefillNote(widget.providerLabel),
                style: theme.textTheme.bodyMedium,
              ),
            label(DrawConditionsPage.fastingLabel),
            Wrap(
              spacing: Space.x2,
              children: [
                for (final (value, text) in const [
                  (DrawFasting.yes, DrawConditionsPage.yesLabel),
                  (DrawFasting.no, DrawConditionsPage.noLabel),
                  (DrawFasting.unknown, DrawConditionsPage.notSureLabel),
                ])
                  ChoiceChip(
                    key: ValueKey('fasting-${value.wireValue}'),
                    label: Text(text),
                    selected: _fasting == value,
                    onSelected: (_) => setState(() => _fasting = value),
                  ),
              ],
            ),
            const SizedBox(height: Space.x4),
            TextField(
              key: const ValueKey('fasting-hours'),
              controller: _hours,
              enabled: _fasting != DrawFasting.no,
              keyboardType: TextInputType.number,
              decoration: const InputDecoration(
                labelText: DrawConditionsPage.hoursLabel,
              ),
            ),
            label(DrawConditionsPage.timeLabel),
            InkWell(
              onTap: _pickTime,
              borderRadius: Radii.controlRadius,
              child: InputDecorator(
                decoration: InputDecoration(
                  suffixIcon: _time == null
                      ? const Icon(Icons.schedule, size: 18)
                      : IconButton(
                          tooltip: DrawConditionsPage.clearTimeTooltip,
                          icon: const Icon(Icons.close, size: 18),
                          onPressed: () => setState(() => _time = null),
                        ),
                ),
                child: Text(
                  _time ?? DrawConditionsPage.noTime,
                  style: theme.textTheme.bodyLarge,
                ),
              ),
            ),
            label(DrawConditionsPage.periodLabel),
            if (_time != null)
              Text(
                DrawConditionsPage.fromTimeNote,
                style: theme.textTheme.bodySmall,
              )
            else
              Wrap(
                spacing: Space.x2,
                children: [
                  for (final p in const [
                    DrawPeriod.morning,
                    DrawPeriod.afternoon,
                    DrawPeriod.evening,
                  ])
                    ChoiceChip(
                      key: ValueKey('period-${p.wireValue}'),
                      label: Text(p.displayName),
                      selected: _period == p,
                      onSelected: (on) =>
                          setState(() => _period = on ? p : DrawPeriod.unknown),
                    ),
                ],
              ),
            label(DrawConditionsPage.trainingLabel),
            yesNo('training', _training, (v) => setState(() => _training = v)),
            label(DrawConditionsPage.unwellLabel),
            yesNo('unwell', _unwell, (v) => setState(() => _unwell = v)),
            const SizedBox(height: Space.x8),
            FilledButton(
              onPressed: _saving ? null : _save,
              child: const Text(DrawConditionsPage.saveLabel),
            ),
            const SizedBox(height: Space.x2),
            TextButton(
              onPressed: _saving ? null : () => Navigator.of(context).pop(),
              child: const Text(DrawConditionsPage.skipLabel),
            ),
            AttentionLine(_error ?? ''),
          ],
        ),
      ),
    );
  }
}
