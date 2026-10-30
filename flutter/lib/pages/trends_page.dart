import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../models/trend.dart';
import '../providers/trends_provider.dart';
import '../theme/app_theme.dart';
import '../theme/tokens.dart';
import '../utils/metric_format.dart';
import '../widgets/page_body.dart';
import '../widgets/status_views.dart';
import '../widgets/surface_card.dart';
import '../widgets/trend_chart.dart';

/// Trends: one metric at a time against your own 28-day band.
class TrendsPage extends StatefulWidget {
  const TrendsPage({super.key});

  static const String emptySentence =
      'Trends start once a metric has a week of days behind it.';
  static const String emptyNote =
      'Connect a source and the first lines appear within a week.';
  static const String noPointsText = 'No days in this window yet.';

  @override
  State<TrendsPage> createState() => _TrendsPageState();
}

class _TrendsPageState extends State<TrendsPage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final trends = context.read<TrendsProvider>();
      if (!trends.hasLoadedMetrics && !trends.isLoadingMetrics) {
        trends.loadMetrics();
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final trends = context.watch<TrendsProvider>();
    final theme = Theme.of(context);

    final List<Widget> body;
    if (!trends.hasLoadedMetrics && trends.isLoadingMetrics) {
      body = const [
        Padding(
          padding: EdgeInsets.symmetric(vertical: Space.x8),
          child: LoadingView(),
        ),
      ];
    } else if (!trends.hasLoadedMetrics) {
      body = [
        ErrorView(
          title: 'Trends did not load.',
          detail: trends.error,
          onRetry: trends.loadMetrics,
        ),
      ];
    } else if (trends.metrics.isEmpty) {
      body = [
        Text(TrendsPage.emptySentence, style: theme.textTheme.headlineSmall),
        const SizedBox(height: Space.x4),
        Text(
          TrendsPage.emptyNote,
          style: theme.textTheme.bodyMedium?.copyWith(
            color: theme.colorScheme.onSurfaceVariant,
          ),
        ),
      ];
    } else {
      body = const [_MetricPicker(), SizedBox(height: Space.x6), _ChartCard()];
    }

    return SafeArea(
      child: RefreshIndicator(
        onRefresh: trends.refresh,
        color: theme.colorScheme.primary,
        child: PageBody(
          physics: const AlwaysScrollableScrollPhysics(),
          children: [
            const PageHeading(
              title: 'Trends',
              subtitle: 'Each line against your own usual, not anyone else\'s.',
            ),
            ...body,
          ],
        ),
      ),
    );
  }
}

/// A horizontal row of metric chips in product order.
class _MetricPicker extends StatelessWidget {
  const _MetricPicker();

  @override
  Widget build(BuildContext context) {
    final trends = context.watch<TrendsProvider>();
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      clipBehavior: Clip.none,
      child: Row(
        children: [
          for (final m in trends.metrics) ...[
            ChoiceChip(
              label: Text(m.label),
              selected: m.metric == trends.selected,
              showCheckmark: false,
              onSelected: (_) => trends.select(m.metric),
            ),
            const SizedBox(width: Space.x2),
          ],
        ],
      ),
    );
  }
}

/// The selected metric's latest value, the window toggle and the chart.
class _ChartCard extends StatelessWidget {
  const _ChartCard();

  @override
  Widget build(BuildContext context) {
    final trends = context.watch<TrendsProvider>();
    final theme = Theme.of(context);
    final metric = trends.selectedMetric;
    final series = trends.series;
    if (metric == null) return const SizedBox.shrink();
    final wide = MediaQuery.sizeOf(context).width >= 600;

    final Widget chart;
    if (series == null && trends.isLoadingSeries) {
      chart = const SizedBox(height: 220, child: LoadingView());
    } else if (series == null) {
      chart = SizedBox(
        height: 220,
        child: ErrorView(
          title: 'This metric did not load.',
          detail: trends.seriesError,
          onRetry: trends.retrySeries,
        ),
      );
    } else if (series.isEmpty) {
      chart = SizedBox(
        height: 220,
        child: Center(
          child: Text(
            TrendsPage.noPointsText,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
            ),
          ),
        ),
      );
    } else {
      chart = TrendChart(
        key: ValueKey('${series.metric}:${trends.days}'),
        series: series,
        days: trends.days,
        height: wide ? 280 : 220,
      );
    }

    return SurfaceCard(
      padding: const EdgeInsets.fromLTRB(
        Space.x4,
        Space.x6,
        Space.x4,
        Space.x6,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _Headline(metric: metric),
          const SizedBox(height: Space.x4),
          Align(
            alignment: Alignment.centerLeft,
            child: SegmentedButton<int>(
              segments: [
                for (final d in TrendsProvider.windows)
                  ButtonSegment(value: d, label: Text('$d days')),
              ],
              selected: {trends.days},
              showSelectedIcon: false,
              style: const ButtonStyle(visualDensity: VisualDensity.compact),
              onSelectionChanged: (v) => trends.setDays(v.first),
            ),
          ),
          const SizedBox(height: Space.x6),
          AnimatedSwitcher(
            duration: Motion.short,
            switchInCurve: Motion.curve,
            child: chart,
          ),
        ],
      ),
    );
  }
}

/// Name, latest value and where it sits, in words.
class _Headline extends StatelessWidget {
  final TrendMetric metric;

  const _Headline({required this.metric});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final latest = metric.latestValue;
    final band = metric.band;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(metric.label, style: theme.textTheme.headlineSmall),
        const SizedBox(height: Space.x2),
        Wrap(
          spacing: Space.x3,
          crossAxisAlignment: WrapCrossAlignment.end,
          children: [
            if (latest != null)
              Text(
                formatMetricWithUnit(metric.metric, latest, unit: metric.unit),
                style: AppTheme.numeric(
                  context,
                  base: theme.textTheme.headlineMedium,
                ),
              ),
            Text(
              band == null
                  ? (metric.baselineDays < 14
                        ? 'Still learning your normal.'
                        : 'Latest value.')
                  : 'Latest is ${band.displayName}.',
              style: theme.textTheme.bodySmall,
            ),
          ],
        ),
      ],
    );
  }
}
