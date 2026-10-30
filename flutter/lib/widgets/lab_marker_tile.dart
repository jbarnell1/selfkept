import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;

import '../models/band_position.dart';
import '../models/lab.dart';
import '../theme/app_theme.dart';
import '../theme/tokens.dart';
import 'lab_sparkline.dart';
import 'surface_card.dart';

/// One marker on the Labs page: name, latest value, date, a small history
/// line and where the value sits. Quiet when there is no result yet.
class LabMarkerTile extends StatelessWidget {
  final LabMarker marker;
  final VoidCallback? onTap;

  const LabMarkerTile({super.key, required this.marker, this.onTap});

  static const String noResultText = 'No result yet';

  /// "Up 14 from last", "Down 3 from last" or "Same as last". The difference
  /// is rounded to the precision of the values: one decimal when either has a
  /// fractional part, else whole.
  static String? changeText(double? latest, double? previous) {
    if (latest == null || previous == null) return null;
    final diff = latest - previous;
    final fractional =
        latest != latest.roundToDouble() ||
        previous != previous.roundToDouble();
    final amount = fractional
        ? double.parse(diff.abs().toStringAsFixed(1))
        : diff.abs().roundToDouble();
    if (amount == 0) return 'Same as last';
    final text = fractional
        ? amount.toStringAsFixed(1)
        : formatLabValue(amount.round());
    return '${diff > 0 ? 'Up' : 'Down'} $text from last';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final quiet = theme.textTheme.bodySmall?.copyWith(
      color: theme.colorScheme.onSurfaceVariant,
    );
    final value = marker.latestValue;

    if (value == null) {
      return SurfaceCard(
        onTap: onTap,
        padding: const EdgeInsets.all(Space.x4),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(marker.displayName, style: theme.textTheme.titleSmall),
            const SizedBox(height: Space.x2),
            Text(noResultText, style: quiet),
          ],
        ),
      );
    }

    final change = changeText(value, marker.previousValue);
    final date = marker.latestDate;
    return SurfaceCard(
      onTap: onTap,
      padding: const EdgeInsets.all(Space.x4),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(marker.displayName, style: theme.textTheme.titleSmall),
          const SizedBox(height: Space.x2),
          Wrap(
            crossAxisAlignment: WrapCrossAlignment.end,
            spacing: Space.x1,
            children: [
              Text(
                formatLabValue(value),
                style: AppTheme.numeric(
                  context,
                  base: theme.textTheme.headlineSmall,
                ),
              ),
              if (marker.displayUnit.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.only(bottom: Space.x1),
                  child: Text(marker.displayUnit, style: quiet),
                ),
            ],
          ),
          if (date != null) Text(DateFormat.yMMMd().format(date), style: quiet),
          const SizedBox(height: Space.x2),
          LabSparkline(marker: marker),
          const SizedBox(height: Space.x2),
          LabStatusLine(marker: marker),
          if (change != null) Text(change, style: quiet),
        ],
      ),
    );
  }
}

/// Where the latest value sits, in words, with a dot in the matching colour.
/// Sage for in range, amber for watch, terracotta for clinician. Never red.
/// Draws nothing when the server gave no position.
class LabStatusLine extends StatelessWidget {
  final LabMarker marker;

  const LabStatusLine({super.key, required this.marker});

  static const String inRangeText = 'In range';
  static const String clinicianText = 'Worth raising with a clinician';

  /// The words, or null when there is nothing to say.
  static String? textFor(LabMarker marker) {
    switch (marker.severity) {
      case LabSeverity.clinician:
        return clinicianText;
      case LabSeverity.watch:
        return switch (marker.position) {
          BandPosition.above => 'Above range',
          BandPosition.below => 'Below range',
          _ => 'Outside range',
        };
      case null:
        return marker.position == BandPosition.inside ? inRangeText : null;
    }
  }

  @override
  Widget build(BuildContext context) {
    final text = textFor(marker);
    if (text == null) return const SizedBox.shrink();
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final color = switch (marker.severity) {
      LabSeverity.clinician => colors.clinician,
      LabSeverity.watch => colors.attention,
      null => colors.band,
    };
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.only(top: 5),
          child: Container(
            key: const Key('lab-status-dot'),
            width: 8,
            height: 8,
            decoration: BoxDecoration(color: color, shape: BoxShape.circle),
          ),
        ),
        const SizedBox(width: Space.x2),
        Expanded(child: Text(text, style: theme.textTheme.bodySmall)),
      ],
    );
  }
}
