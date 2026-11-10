import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/lab.dart';
import '../../models/lab_review.dart';
import '../../providers/auth_provider.dart';
import '../../services/lab_review_service.dart';
import '../../theme/app_theme.dart';
import '../../theme/tokens.dart';
import '../../utils/api_error.dart';
import '../../utils/body_units.dart';
import '../../utils/open_url.dart';
import '../../widgets/page_body.dart';
import '../../widgets/status_views.dart';
import '../../widgets/surface_card.dart';
import '../../widgets/viewer_units.dart';
import '../systems/systems_common.dart';
import 'draw_conditions_page.dart';

/// The colour of a verdict's dot. Never red: a real move the wrong way is
/// amber, a real move the right way is the brand green, the rest are quiet.
Color verdictColor(LabVerdict verdict, BuildContext context) {
  final colors = AppColors.of(context);
  final scheme = Theme.of(context).colorScheme;
  return switch (verdict) {
    LabVerdict.working => scheme.primary,
    LabVerdict.rightDirection => colors.band,
    LabVerdict.noClearChange => colors.textSoft,
    LabVerdict.notWorking => colors.attention,
    LabVerdict.cannotTell => colors.hairline,
  };
}

/// A draw condition said plainly: "Fasting", "Afternoon", "Yes".
String conditionValueText(String condition, String? value) {
  if (value == null) return 'Not known';
  if (condition == 'fasting') return DrawFasting.fromString(value).displayName;
  if (condition == 'drawPeriod') {
    return DrawPeriod.fromString(value).displayName;
  }
  return switch (value) {
    'yes' => 'Yes',
    'no' => 'No',
    _ => value,
  };
}

/// The name of a draw condition or substance.
String conditionName(String condition) {
  if (condition.startsWith('substance:')) {
    final tag = condition.substring('substance:'.length).replaceAll('_', ' ');
    return tag.isEmpty
        ? 'A substance'
        : tag[0].toUpperCase() + tag.substring(1);
  }
  return switch (condition) {
    'fasting' => 'Fasting',
    'drawPeriod' => 'Time of day',
    'hardTraining' => 'Hard training before',
    'unwell' => 'Unwell around the draw',
    _ => condition,
  };
}

/// One panel against the one before it: a verdict per marker judged against
/// how much that test varies by itself, the draw conditions that differ,
/// what was taken then and now, weight and the trials that ran between.
class LabReviewPage extends StatefulWidget {
  final String reportId;

  const LabReviewPage({super.key, required this.reportId});

  static const String loadErrorTitle = 'The review did not load.';
  static const String conditionsTitle = 'How the blood was drawn';
  static const String differTitle = 'What differed between the draws';
  static const String markersTitle = 'Marker by marker';
  static const String takingTitle = 'What you were taking';
  static const String weightTitle = 'Weight';
  static const String trialsTitle = 'Trials in between';
  static const String editConditionsLabel = 'Change how it was drawn';
  static const String noPrevious =
      'This is the first panel, so there is nothing to compare it with yet.';
  static const String nothingTaken = 'Nothing listed.';
  static const String sharedCreditLine =
      'Another trial in the same window touched the same markers, so they share the credit.';
  static const String sourceLabel = 'Where the variation figure comes from';
  static const String beyondUnknown =
      'No checked figure for how much this test varies by itself.';

  /// Beyond or within the usual test-to-test variation, with the figure the
  /// server sent.
  static String variationLine(LabReviewMarker m) {
    final rcv = m.rcvPercent;
    final beyond = m.beyondRcv;
    if (rcv == null || beyond == null) return beyondUnknown;
    final figure = formatLabValue(rcv);
    return beyond
        ? 'Beyond the usual test-to-test variation of $figure percent.'
        : 'Within the usual test-to-test variation of $figure percent.';
  }

  /// "Down 12 U/L (30 percent) from 40 U/L".
  static String changeLine(LabReviewMarker m) {
    final previous = m.previousValue;
    final change = m.change;
    if (previous == null || change == null) return '';
    final unit = m.displayUnit.isEmpty ? '' : ' ${m.displayUnit}';
    final from = '${formatLabValue(previous)}$unit';
    if (change == 0) return 'No change from $from';
    final word = change > 0 ? 'Up' : 'Down';
    final percent = m.changePercent == null
        ? ''
        : ' (${formatLabValue(m.changePercent!.abs())} percent)';
    return '$word ${formatLabValue(change.abs())}$unit$percent from $from';
  }

  /// "1 working, 1 cannot tell", in verdict order, from the server's counts.
  static String countsLine(Map<LabVerdict, int> counts) => [
    for (final v in LabVerdict.values)
      if ((counts[v] ?? 0) > 0) '${counts[v]} ${v.displayName.toLowerCase()}',
  ].join(', ');

  @override
  State<LabReviewPage> createState() => _LabReviewPageState();
}

class _LabReviewPageState extends State<LabReviewPage> {
  LabReview? _review;
  String? _error;
  bool _notShared = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _load();
    });
  }

  Future<void> _load() async {
    final service = context.read<LabReviewService>();
    setState(() {
      _error = null;
      _notShared = false;
    });
    try {
      final review = await service.review(widget.reportId);
      if (!mounted) return;
      setState(() => _review = review);
    } catch (e) {
      if (!mounted) return;
      setState(() {
        if (e is ApiException && e.isNotFound) {
          _notShared = true;
        } else {
          _error = describeError(e);
        }
      });
    }
  }

  Future<void> _editConditions(LabReview review) async {
    final changed = await Navigator.of(context).push<DrawConditions>(
      MaterialPageRoute(
        builder: (_) => DrawConditionsPage(
          reportId: review.reportId,
          initial: review.conditions,
        ),
      ),
    );
    if (changed != null && mounted) await _load();
  }

  @override
  Widget build(BuildContext context) {
    final review = _review;
    final ownId = context.select<AuthProvider, String?>(
      (a) => a.user?.person.id,
    );
    final List<Widget> body;
    if (_notShared) {
      body = const [NotSharedView()];
    } else if (review == null && _error == null) {
      body = const [
        Padding(
          padding: EdgeInsets.symmetric(vertical: Space.x8),
          child: LoadingView(),
        ),
      ];
    } else if (review == null) {
      body = [
        ErrorView(
          title: LabReviewPage.loadErrorTitle,
          detail: _error,
          onRetry: _load,
        ),
      ];
    } else {
      body = _sections(
        context,
        review,
        own: ownId != null && review.personId == ownId,
      );
    }
    return SubpageFrame(
      fallback: '/labs',
      backTitle: 'Labs',
      onRefresh: _load,
      children: body,
    );
  }

  List<Widget> _sections(
    BuildContext context,
    LabReview r, {
    required bool own,
  }) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final units = viewerUnitsOf(context);
    final previous = r.previousCollectedOn;
    final weight = r.weight;
    final trials = r.trials;
    return [
      PageHeading(
        title: 'Panel of ${spokenDay(r.collectedOn)}',
        subtitle: previous == null
            ? LabReviewPage.noPrevious
            : 'Against the panel of ${spokenDay(previous)}.',
      ),
      if (r.verdictCounts.isNotEmpty)
        Text(
          LabReviewPage.countsLine(r.verdictCounts),
          style: theme.textTheme.titleMedium,
        ),
      const SectionHeader(title: LabReviewPage.conditionsTitle),
      SurfaceCard(
        padding: const EdgeInsets.all(Space.x4),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            _ConditionsLine(label: 'This draw', conditions: r.conditions),
            if (r.previousConditions != null) ...[
              const SizedBox(height: Space.x2),
              _ConditionsLine(
                label: 'The one before',
                conditions: r.previousConditions!,
              ),
            ],
            if (own) ...[
              const SizedBox(height: Space.x2),
              Align(
                alignment: Alignment.centerLeft,
                child: TextButton(
                  onPressed: () => _editConditions(r),
                  child: const Text(LabReviewPage.editConditionsLabel),
                ),
              ),
            ],
          ],
        ),
      ),
      if (r.conditionsDiffer.isNotEmpty) ...[
        const SectionHeader(title: LabReviewPage.differTitle),
        SurfaceCard(
          padding: const EdgeInsets.all(Space.x4),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              for (final d in r.conditionsDiffer)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: Space.x1),
                  child: Text(
                    '${conditionName(d.condition)}: '
                    '${conditionValueText(d.condition, d.now)} this time, '
                    '${conditionValueText(d.condition, d.then).toLowerCase()} before.',
                    style: theme.textTheme.bodyMedium,
                  ),
                ),
            ],
          ),
        ),
      ],
      const SectionHeader(title: LabReviewPage.markersTitle),
      SurfaceCard(
        padding: EdgeInsets.zero,
        child: Column(
          children: [
            for (var i = 0; i < r.markers.length; i++) ...[
              if (i > 0)
                Divider(height: 1, indent: Space.x4, color: colors.hairline),
              _MarkerRow(marker: r.markers[i]),
            ],
          ],
        ),
      ),
      const SectionHeader(title: LabReviewPage.takingTitle),
      SurfaceCard(
        padding: const EdgeInsets.all(Space.x4),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('This draw', style: theme.textTheme.labelLarge),
            QuietLine(_taking(r.takingNow)),
            if (previous != null) ...[
              const SizedBox(height: Space.x2),
              Text('The one before', style: theme.textTheme.labelLarge),
              QuietLine(_taking(r.takingThen)),
            ],
          ],
        ),
      ),
      if (weight != null && (weight.now != null || weight.then != null)) ...[
        const SectionHeader(title: LabReviewPage.weightTitle),
        SurfaceCard(
          padding: const EdgeInsets.all(Space.x4),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (weight.now != null)
                Text(
                  'Near this draw: ${BodyUnits.formatMass(weight.now!.kg, units)} '
                  'on ${spokenDay(weight.now!.day)}',
                  style: theme.textTheme.bodyMedium,
                ),
              if (weight.then != null)
                Text(
                  'Near the one before: ${BodyUnits.formatMass(weight.then!.kg, units)} '
                  'on ${spokenDay(weight.then!.day)}',
                  style: theme.textTheme.bodyMedium,
                ),
              if (weight.changeKg != null)
                Text(
                  'Change: ${BodyUnits.formatMassChange(weight.changeKg!, units)}',
                  style: theme.textTheme.bodySmall,
                ),
            ],
          ),
        ),
      ],
      if (trials != null && trials.isNotEmpty) ...[
        const SectionHeader(title: LabReviewPage.trialsTitle),
        for (final t in trials)
          Padding(
            padding: const EdgeInsets.only(bottom: Space.x3),
            child: SurfaceCard(
              padding: const EdgeInsets.all(Space.x4),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(t.trial.title, style: theme.textTheme.titleSmall),
                  for (final entry in t.verdicts.entries)
                    Text(
                      '${_markerName(r, entry.key)}: ${entry.value.displayName}',
                      style: theme.textTheme.bodyMedium,
                    ),
                  if (t.sharedCredit)
                    Padding(
                      padding: const EdgeInsets.only(top: Space.x1),
                      child: Text(
                        LabReviewPage.sharedCreditLine,
                        style: theme.textTheme.bodySmall,
                      ),
                    ),
                ],
              ),
            ),
          ),
      ],
    ];
  }

  static String _taking(List<TakenItem> items) => items.isEmpty
      ? LabReviewPage.nothingTaken
      : items.map((i) => i.label).join(', ');

  static String _markerName(LabReview r, String key) {
    for (final m in r.markers) {
      if (m.metricKey == key) return m.displayName;
    }
    return key;
  }
}

class _ConditionsLine extends StatelessWidget {
  final String label;
  final DrawConditions conditions;

  const _ConditionsLine({required this.label, required this.conditions});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final c = conditions;
    final parts = <String>[
      switch (c.fasting) {
        // A pre-filled figure means "this many or more".
        DrawFasting.yes when c.fastingHours != null =>
          'Fasting, ${c.fastingHours} hours${c.defaulted ? ' or more' : ''}',
        DrawFasting.unknown => 'Fasting not known',
        _ => c.fasting.displayName,
      },
      if (c.drawTime != null)
        '${c.drawPeriod.displayName.toLowerCase()} at ${c.drawTime}'
      else if (c.drawPeriod != DrawPeriod.unknown)
        c.drawPeriod.displayName.toLowerCase(),
      if (c.hardTraining == true) 'hard training before',
      if (c.unwell == true) 'unwell',
    ];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: theme.textTheme.labelLarge),
        Text(parts.join(', '), style: theme.textTheme.bodyMedium),
      ],
    );
  }
}

class _MarkerRow extends StatelessWidget {
  final LabReviewMarker marker;

  const _MarkerRow({required this.marker});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final m = marker;
    final unit = m.displayUnit.isEmpty ? '' : ' ${m.displayUnit}';
    final change = LabReviewPage.changeLine(m);
    final variation = m.variation;
    final small = theme.textTheme.bodySmall;
    return Padding(
      key: ValueKey('review-${m.metricKey}'),
      padding: const EdgeInsets.all(Space.x4),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(m.displayName, style: theme.textTheme.titleSmall),
              ),
              Text(
                '${formatLabValue(m.value)}$unit',
                style: AppTheme.numeric(
                  context,
                  base: theme.textTheme.titleSmall,
                ),
              ),
            ],
          ),
          const SizedBox(height: Space.x1),
          Row(
            children: [
              Container(
                width: 8,
                height: 8,
                decoration: BoxDecoration(
                  color: verdictColor(m.verdict, context),
                  shape: BoxShape.circle,
                ),
              ),
              const SizedBox(width: Space.x2),
              Text(m.verdict.displayName, style: theme.textTheme.labelLarge),
            ],
          ),
          if (change.isNotEmpty) Text(change, style: small),
          if (m.previousValue != null)
            Text(LabReviewPage.variationLine(m), style: small),
          for (final reason in m.reasons)
            if (reason != 'no_variation_figure' || m.previousValue == null)
              Text(labReasonText(reason), style: small),
          for (final care in m.readWithCare) ...[
            const SizedBox(height: Space.x2),
            Text(care.explanation, style: theme.textTheme.bodyMedium),
            if (care.source.isNotEmpty) Text(care.source, style: small),
          ],
          for (final line in m.prep) ...[
            const SizedBox(height: Space.x2),
            Text(line, style: theme.textTheme.bodyMedium),
          ],
          if (m.retest != null) ...[
            const SizedBox(height: Space.x2),
            Text(m.retest!, style: theme.textTheme.bodyMedium),
          ],
          if (variation != null && variation.sourceUrl.isNotEmpty)
            TextButton(
              style: TextButton.styleFrom(padding: EdgeInsets.zero),
              onPressed: () => openLink(context, variation.sourceUrl),
              child: Text(
                variation.source.isEmpty
                    ? LabReviewPage.sourceLabel
                    : variation.source,
                textAlign: TextAlign.left,
              ),
            ),
          if (variation?.caveat != null) Text(variation!.caveat!, style: small),
        ],
      ),
    );
  }
}
