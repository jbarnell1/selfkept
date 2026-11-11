import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../models/metric.dart';
import '../providers/collector_provider.dart';
import '../theme/tokens.dart';
import 'surface_card.dart';

/// Asks, once, for kinds of reading this phone can now read but has not been
/// allowed to (a newer build reads more than the person allowed when they
/// first connected). Plain view; [MissingMetricsTodayCard] wires it up.
class MissingMetricsCard extends StatelessWidget {
  /// The missing kinds in everyday words, for example "food and drink, lean
  /// mass, bone mass and body water".
  final String kinds;

  /// The phone's health store by name, for example "Health Connect".
  final String storeName;
  final VoidCallback? onAllow;
  final VoidCallback? onNotNow;

  /// True while the system sheet is up.
  final bool busy;

  const MissingMetricsCard({
    super.key,
    required this.kinds,
    required this.storeName,
    this.onAllow,
    this.onNotNow,
    this.busy = false,
  });

  static const String title = 'New things this phone can read';
  static String kindsLine(String kinds) => 'Not allowed yet: $kinds.';
  static String storeLine(String storeName) =>
      'It only reads what $storeName already holds, and keeps it on your own server.';
  static const String allowLabel = 'Allow';
  static const String notNowLabel = 'Not now';

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(title, style: theme.textTheme.titleMedium),
          const SizedBox(height: Space.x2),
          Text(kindsLine(kinds), style: theme.textTheme.bodyMedium),
          const SizedBox(height: Space.x1),
          Text(storeLine(storeName), style: theme.textTheme.bodySmall),
          const SizedBox(height: Space.x4),
          Wrap(
            spacing: Space.x2,
            runSpacing: Space.x2,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              FilledButton(
                onPressed: busy ? null : onAllow,
                child: const Text(allowLabel),
              ),
              TextButton(
                onPressed: busy ? null : onNotNow,
                child: const Text(notNowLabel),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

/// The Today placement: shown only while [CollectorProvider.offerMissingMetrics]
/// holds, with the space below it that Today's cards use.
class MissingMetricsTodayCard extends StatelessWidget {
  const MissingMetricsTodayCard({super.key});

  @override
  Widget build(BuildContext context) {
    final collector = context.watch<CollectorProvider>();
    if (!collector.offerMissingMetrics) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.x6),
      child: MissingMetricsCard(
        kinds: MetricKey.everydayList(collector.missingMetrics),
        storeName: collector.kind.displayName,
        busy: collector.isAskingForMore,
        onAllow: collector.askForMissingMetrics,
        onNotNow: collector.notNowForMissingMetrics,
      ),
    );
  }
}
