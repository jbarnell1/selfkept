import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/lab.dart';
import '../../models/lab_review.dart';
import '../../services/lab_review_service.dart';
import '../../theme/tokens.dart';
import '../../utils/api_error.dart';
import '../../utils/metric_format.dart';
import '../../utils/open_url.dart';
import '../../widgets/page_body.dart';
import '../../widgets/surface_card.dart';
import '../systems/systems_common.dart';

/// On a marker's page: the readings a checked source ties to it, each with
/// the latest value when it is shared, the link in the source's words and
/// the source's name, which opens the source.
class RelatedReadingsSection extends StatefulWidget {
  final String metricKey;
  final String? personId;

  const RelatedReadingsSection({
    super.key,
    required this.metricKey,
    this.personId,
  });

  static const String title = 'Related readings';
  static const String notSharedLine = 'Not shared with you.';
  static const String errorLine = 'Related readings did not load.';

  /// The latest value as text, or null when there is none to show.
  static String? valueLine(RelatedReading r) {
    final value = r.latestValue;
    if (!r.shared || value == null) return null;
    final unit = r.unit ?? '';
    final text = r.kind == 'lab'
        ? (unit.isEmpty || unit == 'ratio'
              ? formatLabValue(value)
              : '${formatLabValue(value)} $unit')
        : formatMetricWithUnit(r.key, value, unit: unit);
    final date = r.latestDate;
    return date == null ? text : '$text on ${spokenDay(date)}';
  }

  @override
  State<RelatedReadingsSection> createState() => _RelatedReadingsSectionState();
}

class _RelatedReadingsSectionState extends State<RelatedReadingsSection> {
  RelatedReadings? _related;
  String? _error;
  bool _hidden = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _load();
    });
  }

  Future<void> _load() async {
    final service = context.read<LabReviewService>();
    setState(() => _error = null);
    try {
      final related = await service.related(
        widget.metricKey,
        personId: widget.personId,
      );
      if (!mounted) return;
      setState(() => _related = related);
    } catch (e) {
      if (!mounted) return;
      setState(() {
        // Not shared, or an unknown marker: nothing to add to the page.
        if (e is ApiException && e.isNotFound) {
          _hidden = true;
        } else {
          _error = describeError(e);
        }
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final related = _related;
    if (_hidden || (related != null && related.related.isEmpty)) {
      return const SizedBox.shrink();
    }
    final colors = AppColors.of(context);
    final Widget content;
    if (related == null && _error == null) {
      content = const SizedBox(
        height: 24,
        child: Center(
          child: SizedBox(
            width: 18,
            height: 18,
            child: CircularProgressIndicator(strokeWidth: 2),
          ),
        ),
      );
    } else if (related == null) {
      content = Row(
        children: [
          const Expanded(child: QuietLine(RelatedReadingsSection.errorLine)),
          TextButton(onPressed: _load, child: const Text('Try again')),
        ],
      );
    } else {
      content = Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          for (var i = 0; i < related.related.length; i++) ...[
            if (i > 0) Divider(height: Space.x6, color: colors.hairline),
            _RelatedRow(reading: related.related[i]),
          ],
        ],
      );
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader(title: RelatedReadingsSection.title),
        SurfaceCard(padding: const EdgeInsets.all(Space.x4), child: content),
      ],
    );
  }
}

class _RelatedRow extends StatelessWidget {
  final RelatedReading reading;

  const _RelatedRow({required this.reading});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final value = RelatedReadingsSection.valueLine(reading);
    final showsValue = reading.kind == 'lab' || reading.kind == 'metric';
    return Column(
      key: ValueKey('related-${reading.key}'),
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(reading.displayName, style: theme.textTheme.titleSmall),
        if (showsValue && !reading.shared)
          Text(
            RelatedReadingsSection.notSharedLine,
            style: theme.textTheme.bodySmall,
          )
        else if (value != null)
          Text(value, style: theme.textTheme.bodySmall),
        for (final link in reading.links) ...[
          const SizedBox(height: Space.x2),
          Text(link.explanation, style: theme.textTheme.bodyMedium),
          if (link.source.isNotEmpty)
            Align(
              alignment: Alignment.centerLeft,
              child: link.sourceUrl.isEmpty
                  ? Text(link.source, style: theme.textTheme.bodySmall)
                  : TextButton(
                      style: TextButton.styleFrom(
                        padding: EdgeInsets.zero,
                        visualDensity: VisualDensity.compact,
                      ),
                      onPressed: () => openLink(context, link.sourceUrl),
                      child: Text(link.source, textAlign: TextAlign.left),
                    ),
            ),
        ],
      ],
    );
  }
}
