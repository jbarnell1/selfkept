import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../models/trend.dart';
import '../providers/trends_provider.dart';
import '../theme/app_theme.dart';
import '../theme/tokens.dart';
import '../utils/metric_format.dart';
import '../widgets/page_body.dart';
import '../widgets/status_views.dart';
import '../widgets/surface_card.dart';
import '../widgets/trend_sparkline.dart';

/// Trends: every metric with a week behind it, one row each, against your own
/// usual range. The ones outside it come first, in amber; tap a row for its
/// full chart.
class TrendsPage extends StatefulWidget {
  const TrendsPage({super.key});

  static const String emptySentence =
      'Trends start once a metric has a week of days behind it.';
  static const String emptyNote =
      'Connect a source and the first lines appear within a week.';
  static const String noPointsText = 'No days in this window yet.';
  static const String learningText = 'Still learning your normal';
  static const String withinText = 'Within your usual range';

  /// The second line of a row: where the latest day sits, in words.
  static String statusFor(TrendMetric m) {
    final band = m.band;
    if (band == null) {
      return m.baselineDays < 14 ? learningText : 'Latest value';
    }
    if (!m.isOutside) return withinText;
    final words = band.displayName;
    return '${words[0].toUpperCase()}${words.substring(1)}';
  }

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
      body = [_TrendList(metrics: trends.ordered)];
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

/// One card, one row per metric, split by hairlines.
class _TrendList extends StatelessWidget {
  final List<TrendMetric> metrics;

  const _TrendList({required this.metrics});

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    return SurfaceCard(
      padding: EdgeInsets.zero,
      child: Column(
        children: [
          for (var i = 0; i < metrics.length; i++) ...[
            if (i > 0)
              Divider(height: 1, indent: Space.x4, color: colors.hairline),
            TrendRow(metric: metrics[i]),
          ],
        ],
      ),
    );
  }
}

/// Name and where the latest day sits on the left; the small line and the
/// latest value on the right. The whole row opens the metric's chart.
class TrendRow extends StatelessWidget {
  final TrendMetric metric;

  const TrendRow({super.key, required this.metric});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final outside = metric.isOutside;
    final latest = metric.latestValue;
    final value = latest == null
        ? ''
        : formatMetricWithUnit(metric.metric, latest, unit: metric.unit);
    final status = TrendsPage.statusFor(metric);
    return Semantics(
      button: true,
      label: '${metric.label}, $value, $status',
      excludeSemantics: true,
      child: InkWell(
        onTap: () => context.push('/trends/${metric.metric}'),
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 68),
          child: Padding(
            padding: const EdgeInsets.fromLTRB(
              Space.x4,
              Space.x3,
              Space.x2,
              Space.x3,
            ),
            child: Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(metric.label, style: theme.textTheme.titleSmall),
                      const SizedBox(height: Space.x1),
                      Row(
                        children: [
                          if (outside) ...[
                            Container(
                              width: 8,
                              height: 8,
                              decoration: BoxDecoration(
                                color: colors.attention,
                                shape: BoxShape.circle,
                              ),
                            ),
                            const SizedBox(width: Space.x2),
                          ],
                          Flexible(
                            child: Text(
                              status,
                              style: theme.textTheme.bodySmall?.copyWith(
                                // The darkened amber reads as text on both
                                // grounds; the dot carries the brand amber.
                                color: outside
                                    ? theme.colorScheme.error
                                    : theme.colorScheme.onSurfaceVariant,
                              ),
                            ),
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: Space.x3),
                TrendSparkline(
                  points: metric.recent,
                  range: metric.range,
                  outside: outside,
                ),
                const SizedBox(width: Space.x3),
                SizedBox(
                  width: 72,
                  child: Text(
                    value,
                    textAlign: TextAlign.right,
                    style: AppTheme.numeric(
                      context,
                      base: theme.textTheme.titleSmall,
                    ),
                  ),
                ),
                Icon(
                  Icons.chevron_right,
                  size: 20,
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
