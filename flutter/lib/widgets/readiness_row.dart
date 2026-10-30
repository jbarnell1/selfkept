import 'package:flutter/material.dart';

import '../models/band_position.dart';
import '../models/today.dart';
import '../theme/app_theme.dart';
import '../theme/tokens.dart';
import '../utils/metric_format.dart';

/// Which side of the band is the one worth a second look.
enum _WorseSide { below, above }

/// Sleep, HRV and Resting HR in one quiet panel split by hairlines, each
/// saying where last night sat against the person's own band. The dot before
/// the words is sage when the reading is inside the band or on its better
/// side and amber on the side worth watching; with no band yet there is no
/// dot. Never red.
class ReadinessRow extends StatelessWidget {
  final Readiness readiness;
  final int? sleepMinutes;

  const ReadinessRow({super.key, required this.readiness, this.sleepMinutes});

  static const String noBandText = 'no range yet';

  /// Exposed for tests: the dot colour for a position on a given metric.
  static Color dotColor(
    BandPosition? position,
    AppColors colors, {
    required bool lowerIsWorse,
  }) {
    if (position == null) return colors.hairline;
    final worse = lowerIsWorse ? BandPosition.below : BandPosition.above;
    return position == worse ? colors.attention : colors.band;
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final sleep = sleepMinutes;
    final divider = VerticalDivider(
      width: 1,
      thickness: 1,
      color: colors.hairline,
    );
    return DecoratedBox(
      decoration: BoxDecoration(
        color: colors.surfaceRaised,
        borderRadius: Radii.controlRadius,
        border: Border.all(color: colors.hairline),
      ),
      child: IntrinsicHeight(
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Expanded(
              child: _ReadinessCell(
                label: 'Sleep',
                position: readiness.sleep,
                worse: _WorseSide.below,
                value: sleep == null ? null : formatSleepMinutes(sleep),
              ),
            ),
            divider,
            Expanded(
              child: _ReadinessCell(
                label: 'HRV',
                position: readiness.hrv,
                worse: _WorseSide.below,
              ),
            ),
            divider,
            Expanded(
              child: _ReadinessCell(
                label: 'Resting HR',
                position: readiness.restingHr,
                worse: _WorseSide.above,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _ReadinessCell extends StatelessWidget {
  final String label;
  final BandPosition? position;
  final _WorseSide worse;
  final String? value;

  const _ReadinessCell({
    required this.label,
    required this.position,
    required this.worse,
    this.value,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final dot = ReadinessRow.dotColor(
      position,
      colors,
      lowerIsWorse: worse == _WorseSide.below,
    );
    final words = position?.displayName ?? ReadinessRow.noBandText;
    return Semantics(
      label: '$label: ${value == null ? '' : '$value, '}$words',
      excludeSemantics: true,
      child: Padding(
        padding: const EdgeInsets.all(Space.x3),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(label, style: theme.textTheme.labelMedium, maxLines: 2),
            if (value != null) ...[
              const SizedBox(height: Space.x1),
              Text(
                value!,
                style: AppTheme.numeric(
                  context,
                  base: theme.textTheme.titleMedium,
                ),
              ),
            ],
            const SizedBox(height: Space.x1),
            Text.rich(
              TextSpan(
                children: [
                  // No band yet means nothing to colour, so no dot.
                  if (position != null)
                    WidgetSpan(
                      alignment: PlaceholderAlignment.middle,
                      child: Padding(
                        padding: const EdgeInsets.only(right: Space.x2),
                        child: Container(
                          width: 8,
                          height: 8,
                          decoration: BoxDecoration(
                            color: dot,
                            shape: BoxShape.circle,
                          ),
                        ),
                      ),
                    ),
                  TextSpan(text: words),
                ],
              ),
              style: theme.textTheme.bodySmall?.copyWith(
                color: position == null
                    ? theme.colorScheme.onSurfaceVariant
                    : theme.colorScheme.onSurface,
              ),
            ),
          ],
        ),
      ),
    );
  }
}
