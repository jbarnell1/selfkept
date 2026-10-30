import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../models/lab.dart';
import '../services/labs_service.dart';
import '../theme/app_theme.dart';
import '../theme/tokens.dart';
import '../utils/open_url.dart';
import '../utils/api_error.dart';
import '../widgets/lab_marker_tile.dart';
import '../widgets/page_body.dart';
import '../widgets/status_views.dart';
import '../widgets/surface_card.dart';
import '../widgets/subpage_app_bar.dart';

/// One marker: the latest value, where it sits, its ranges in words, what it
/// is, what moves it and every value on record.
class LabMarkerPage extends StatefulWidget {
  final String metricKey;

  const LabMarkerPage({super.key, required this.metricKey});

  static const String sourceLabel = 'Where this range comes from';

  /// "Usual range 66 to 133 mg/dL", "Usual range up to 100 mg/dL" or
  /// "Usual range from 40 mg/dL". Null when no range is known.
  static String? populationWords(LabRange? range, String unit) {
    if (range == null || range.isEmpty) return null;
    final suffix = unit.isEmpty ? '' : ' $unit';
    final low = range.low;
    final high = range.high;
    if (low != null && high != null) {
      return 'Usual range ${formatLabValue(low)} to ${formatLabValue(high)}$suffix';
    }
    if (high != null) return 'Usual range up to ${formatLabValue(high)}$suffix';
    return 'Usual range from ${formatLabValue(low!)}$suffix';
  }

  /// "Often aimed for: below 90 mg/dL". Null when there is no optimal range.
  static String? optimalWords(LabRange? range, String unit) {
    if (range == null || range.isEmpty) return null;
    final suffix = unit.isEmpty ? '' : ' $unit';
    final low = range.low;
    final high = range.high;
    if (low != null && high != null) {
      return 'Often aimed for: ${formatLabValue(low)} to ${formatLabValue(high)}$suffix';
    }
    if (high != null) {
      return 'Often aimed for: below ${formatLabValue(high)}$suffix';
    }
    return 'Often aimed for: above ${formatLabValue(low!)}$suffix';
  }

  @override
  State<LabMarkerPage> createState() => _LabMarkerPageState();
}

class _LabMarkerPageState extends State<LabMarkerPage> {
  LabMarkerDetail? _detail;
  String? _error;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _load();
    });
  }

  Future<void> _load() async {
    final service = context.read<LabsService>();
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final detail = await service.marker(widget.metricKey);
      if (!mounted) return;
      setState(() => _detail = detail);
    } catch (e) {
      if (!mounted) return;
      setState(() => _error = describeError(e));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final detail = _detail;
    final Widget body;
    if (detail != null) {
      body = _Detail(detail: detail);
    } else if (_loading) {
      body = const Padding(
        padding: EdgeInsets.symmetric(vertical: Space.x8),
        child: LoadingView(),
      );
    } else {
      body = ErrorView(
        title: 'This marker did not load.',
        detail: _error,
        onRetry: _load,
      );
    }
    return Scaffold(
      appBar: const SubpageAppBar(fallback: '/labs', title: Text('Labs')),
      body: SafeArea(child: PageBody(children: [body])),
    );
  }
}

class _Detail extends StatelessWidget {
  final LabMarkerDetail detail;

  const _Detail({required this.detail});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final marker = detail.marker;
    final quiet = theme.textTheme.bodyMedium?.copyWith(
      color: theme.colorScheme.onSurfaceVariant,
    );
    final value = marker.latestValue;
    final population = LabMarkerPage.populationWords(
      marker.populationRange,
      marker.displayUnit,
    );
    final optimal = LabMarkerPage.optimalWords(
      marker.optimalRange,
      marker.displayUnit,
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(marker.displayName, style: theme.textTheme.headlineMedium),
        const SizedBox(height: Space.x4),
        SurfaceCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (value == null)
                Text(LabMarkerTile.noResultText, style: quiet)
              else ...[
                Wrap(
                  crossAxisAlignment: WrapCrossAlignment.end,
                  spacing: Space.x2,
                  children: [
                    Text(
                      formatLabValue(value),
                      style: AppTheme.numeric(
                        context,
                        base: theme.textTheme.displaySmall,
                      ),
                    ),
                    if (marker.displayUnit.isNotEmpty)
                      Padding(
                        padding: const EdgeInsets.only(bottom: Space.x2),
                        child: Text(marker.displayUnit, style: quiet),
                      ),
                  ],
                ),
                if (marker.latestDate != null)
                  Text(
                    DateFormat.yMMMMd().format(marker.latestDate!),
                    style: theme.textTheme.bodySmall,
                  ),
                const SizedBox(height: Space.x3),
                LabStatusLine(marker: marker),
              ],
              if (population != null) ...[
                const SizedBox(height: Space.x3),
                Text(population, style: theme.textTheme.bodyMedium),
              ],
              if (optimal != null) ...[
                const SizedBox(height: Space.x1),
                Text(optimal, style: theme.textTheme.bodyMedium),
              ],
            ],
          ),
        ),
        if (detail.blurb.isNotEmpty) ...[
          const SizedBox(height: Space.x6),
          Text(
            detail.blurb,
            style: theme.textTheme.bodyLarge?.copyWith(height: 1.55),
          ),
        ],
        if (detail.whatMovesIt.isNotEmpty) ...[
          const SectionHeader(title: 'What moves it'),
          Text(
            detail.whatMovesIt,
            style: theme.textTheme.bodyLarge?.copyWith(height: 1.55),
          ),
        ],
        if (detail.history.isNotEmpty) ...[
          const SectionHeader(title: 'History'),
          SurfaceCard(
            padding: const EdgeInsets.symmetric(
              horizontal: Space.x4,
              vertical: Space.x2,
            ),
            child: Column(
              children: [
                for (var i = 0; i < detail.history.length; i++) ...[
                  if (i > 0) const Divider(height: 1),
                  _HistoryRow(
                    point: detail.history[i],
                    unit: marker.displayUnit,
                  ),
                ],
              ],
            ),
          ),
        ],
        if (detail.sourceUrl != null) ...[
          const SizedBox(height: Space.x4),
          Align(
            alignment: Alignment.centerLeft,
            child: TextButton(
              onPressed: () => openLink(context, detail.sourceUrl!),
              child: const Text(LabMarkerPage.sourceLabel),
            ),
          ),
        ],
      ],
    );
  }
}

class _HistoryRow extends StatelessWidget {
  final LabPoint point;
  final String unit;

  const _HistoryRow({required this.point, required this.unit});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final flag = switch (point.flag.toLowerCase()) {
      'high' => 'High',
      'low' => 'Low',
      _ => null,
    };
    final value = unit.isEmpty
        ? formatLabValue(point.value)
        : '${formatLabValue(point.value)} $unit';
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: Space.x3),
      child: Row(
        children: [
          Expanded(
            child: Text(
              DateFormat.yMMMMd().format(point.date),
              style: theme.textTheme.bodyMedium,
            ),
          ),
          if (flag != null) ...[
            Text(
              flag,
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(width: Space.x3),
          ],
          Text(
            value,
            style: AppTheme.numeric(context, base: theme.textTheme.bodyMedium),
          ),
        ],
      ),
    );
  }
}
