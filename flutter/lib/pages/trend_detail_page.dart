import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../models/trend.dart';
import '../providers/trends_provider.dart';
import '../theme/app_theme.dart';
import '../theme/tokens.dart';
import '../utils/metric_format.dart';
import '../widgets/page_body.dart';
import '../widgets/status_views.dart';
import '../widgets/subpage_app_bar.dart';
import '../widgets/surface_card.dart';
import '../widgets/trend_chart.dart';
import 'trends_page.dart';

/// One metric's full chart, 28 or 90 days, opened from its row in Trends.
/// Back (or a swipe from the edge) returns to the list.
class TrendDetailPage extends StatefulWidget {
  final String metric;

  const TrendDetailPage({super.key, required this.metric});

  @override
  State<TrendDetailPage> createState() => _TrendDetailPageState();
}

class _TrendDetailPageState extends State<TrendDetailPage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _open());
  }

  Future<void> _open() async {
    if (!mounted) return;
    final trends = context.read<TrendsProvider>();
    // Opened straight from a link: the list has not loaded yet.
    if (!trends.hasLoadedMetrics && !trends.isLoadingMetrics) {
      await trends.loadMetrics();
    }
    await trends.select(widget.metric);
  }

  @override
  Widget build(BuildContext context) {
    final trends = context.watch<TrendsProvider>();
    final metric = trends.metricFor(widget.metric);
    return Scaffold(
      appBar: SubpageAppBar(
        fallback: '/trends',
        title: Text(metric?.label ?? ''),
      ),
      body: SafeArea(
        child: PageBody(
          children: [
            if (metric != null)
              TrendChartCard(metric: metric)
            else if (trends.isLoadingMetrics)
              const Padding(
                padding: EdgeInsets.symmetric(vertical: Space.x8),
                child: LoadingView(),
              )
            else
              ErrorView(
                title: 'This metric did not load.',
                detail: trends.error,
                onRetry: _open,
              ),
          ],
        ),
      ),
    );
  }
}

/// The metric's latest value, the window toggle and the chart.
class TrendChartCard extends StatelessWidget {
  final TrendMetric metric;

  const TrendChartCard({super.key, required this.metric});

  @override
  Widget build(BuildContext context) {
    final trends = context.watch<TrendsProvider>();
    final theme = Theme.of(context);
    final series = trends.selected == metric.metric ? trends.series : null;
    final wide = MediaQuery.sizeOf(context).width >= 600;

    final Widget chart;
    if (series == null && trends.seriesError == null) {
      chart = const SizedBox(height: 220, child: LoadingView());
    } else if (series == null) {
      chart = ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 220),
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

/// Latest value and where it sits, in words.
class _Headline extends StatelessWidget {
  final TrendMetric metric;

  const _Headline({required this.metric});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final latest = metric.latestValue;
    final band = metric.band;
    return Wrap(
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
    );
  }
}
