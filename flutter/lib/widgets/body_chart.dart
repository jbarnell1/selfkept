import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;

import '../models/enums.dart';
import '../models/progress.dart';
import '../theme/app_theme.dart';
import '../theme/tokens.dart';
import '../utils/body_units.dart';

/// A body series drawn small: the readings as dots and, for weight, the
/// smoothed line through them. Values are shown in the viewer's units.
class BodyChart extends StatelessWidget {
  final BodySeries series;
  final Units units;
  final double height;

  const BodyChart({
    super.key,
    required this.series,
    required this.units,
    this.height = 180,
  });

  static const String trendLegend =
      'Dots are readings. The line is the smoothed trend.';
  static const String dotsLegend = 'Each dot is a reading.';

  /// "28 readings from 12 Jul to 9 Nov", for a screen reader.
  static String describe(BodySeries series) {
    final points = series.points;
    if (points.isEmpty) return 'No readings of ${series.displayName}';
    final format = DateFormat.MMMd();
    final n = points.length;
    return '$n ${n == 1 ? 'reading' : 'readings'} of '
        '${series.displayName.toLowerCase()} from '
        '${format.format(points.first.day)} to ${format.format(points.last.day)}';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final labelStyle = AppTheme.numeric(
      context,
      base: theme.textTheme.labelSmall,
    ).copyWith(color: theme.colorScheme.onSurfaceVariant);
    double shown(double v) => BodyUnits.fromStored(v, series.unit, units);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Semantics(
          label: describe(series),
          child: SizedBox(
            height: height,
            child: CustomPaint(
              painter: _BodyPainter(
                points: [
                  for (final p in series.points) (p.day, shown(p.value)),
                ],
                trend: [for (final p in series.trend) (p.day, shown(p.value))],
                dot: series.trend.isEmpty
                    ? theme.colorScheme.primary
                    : colors.band,
                line: theme.colorScheme.primary,
                hairline: colors.hairline,
                labelStyle: labelStyle,
              ),
            ),
          ),
        ),
        const SizedBox(height: Space.x2),
        Text(
          series.trend.isEmpty ? dotsLegend : trendLegend,
          style: theme.textTheme.bodySmall,
        ),
      ],
    );
  }
}

class _BodyPainter extends CustomPainter {
  final List<(DateTime, double)> points;
  final List<(DateTime, double)> trend;
  final Color dot;
  final Color line;
  final Color hairline;
  final TextStyle labelStyle;

  _BodyPainter({
    required this.points,
    required this.trend,
    required this.dot,
    required this.line,
    required this.hairline,
    required this.labelStyle,
  });

  static const double _labelWidth = 44;
  static const double _labelHeight = 18;

  @override
  void paint(Canvas canvas, Size size) {
    final all = [...points, ...trend];
    if (all.isEmpty) return;
    var low = all.map((p) => p.$2).reduce(math.min);
    var high = all.map((p) => p.$2).reduce(math.max);
    if (high - low < 1) {
      final mid = (high + low) / 2;
      low = mid - 0.5;
      high = mid + 0.5;
    }
    final pad = (high - low) * 0.1;
    low -= pad;
    high += pad;
    final first = all.map((p) => p.$1).reduce((a, b) => a.isBefore(b) ? a : b);
    final last = all.map((p) => p.$1).reduce((a, b) => a.isAfter(b) ? a : b);
    final span = math.max(1, last.difference(first).inDays);

    final plot = Rect.fromLTRB(
      _labelWidth,
      Space.x2,
      size.width - Space.x2,
      size.height - _labelHeight,
    );
    Offset at(DateTime day, double value) => Offset(
      plot.left + plot.width * (day.difference(first).inDays / span),
      plot.bottom - plot.height * ((value - low) / (high - low)),
    );

    final grid = Paint()
      ..color = hairline
      ..strokeWidth = 1;
    for (final y in [plot.top, plot.center.dy, plot.bottom]) {
      canvas.drawLine(Offset(plot.left, y), Offset(plot.right, y), grid);
    }
    _label(canvas, (high - pad).toStringAsFixed(1), Offset(0, plot.top));
    _label(canvas, (low + pad).toStringAsFixed(1), Offset(0, plot.bottom - 12));
    final dates = DateFormat.MMMd();
    _label(canvas, dates.format(first), Offset(plot.left, plot.bottom + 4));
    final lastText = dates.format(last);
    _label(
      canvas,
      lastText,
      Offset(plot.right - _measure(lastText).width, plot.bottom + 4),
    );

    final dotPaint = Paint()..color = dot;
    for (final p in points) {
      canvas.drawCircle(at(p.$1, p.$2), 3, dotPaint);
    }
    if (trend.length >= 2) {
      final path = Path();
      for (var i = 0; i < trend.length; i++) {
        final o = at(trend[i].$1, trend[i].$2);
        if (i == 0) {
          path.moveTo(o.dx, o.dy);
        } else {
          path.lineTo(o.dx, o.dy);
        }
      }
      canvas.drawPath(
        path,
        Paint()
          ..color = line
          ..style = PaintingStyle.stroke
          ..strokeWidth = 2
          ..strokeCap = StrokeCap.round
          ..strokeJoin = StrokeJoin.round,
      );
    }
  }

  Size _measure(String text) {
    final painter = TextPainter(
      text: TextSpan(text: text, style: labelStyle),
      textDirection: TextDirection.ltr,
    )..layout();
    return painter.size;
  }

  void _label(Canvas canvas, String text, Offset offset) {
    TextPainter(
        text: TextSpan(text: text, style: labelStyle),
        textDirection: TextDirection.ltr,
      )
      ..layout()
      ..paint(canvas, offset);
  }

  @override
  bool shouldRepaint(_BodyPainter old) =>
      old.points != points ||
      old.trend != trend ||
      old.dot != dot ||
      old.line != line;
}
