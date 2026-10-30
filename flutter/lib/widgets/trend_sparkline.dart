import 'package:flutter/material.dart';

import '../models/trend.dart';
import '../theme/tokens.dart';

/// A row-sized 28-day line with the usual range (p25 to p75) shaded behind
/// it and the latest day marked. No axes or labels: the row beside it says
/// the value and where it sits. Amber when the latest day is outside the
/// range, the quiet soft ink otherwise; never red.
class TrendSparkline extends StatelessWidget {
  final List<TrendPoint> points;
  final TrendBand? range;
  final bool outside;
  final double width;
  final double height;

  const TrendSparkline({
    super.key,
    required this.points,
    this.range,
    this.outside = false,
    this.width = 72,
    this.height = 28,
  });

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final scheme = Theme.of(context).colorScheme;
    return ExcludeSemantics(
      child: SizedBox(
        width: width,
        height: height,
        child: points.length < 2
            ? null
            : CustomPaint(
                painter: _SparklinePainter(
                  values: [for (final p in points) p.value],
                  range: range,
                  line: outside ? colors.attention : scheme.onSurfaceVariant,
                  band: colors.band.withValues(alpha: 0.28),
                ),
              ),
      ),
    );
  }
}

class _SparklinePainter extends CustomPainter {
  final List<double> values;
  final TrendBand? range;
  final Color line;
  final Color band;

  _SparklinePainter({
    required this.values,
    required this.range,
    required this.line,
    required this.band,
  });

  @override
  void paint(Canvas canvas, Size size) {
    var low = values.reduce((a, b) => a < b ? a : b);
    var high = values.reduce((a, b) => a > b ? a : b);
    final r = range;
    if (r != null) {
      if (r.p25 < low) low = r.p25;
      if (r.p75 > high) high = r.p75;
    }
    // A flat line still needs a little height to sit in the middle of.
    if (high - low < 1e-9) {
      high += 1;
      low -= 1;
    }
    const inset = 2.0;
    final span = high - low;
    double y(double v) =>
        inset + (size.height - 2 * inset) * (1 - (v - low) / span);
    double x(int i) =>
        inset + (size.width - 2 * inset) * i / (values.length - 1);

    if (r != null) {
      canvas.drawRRect(
        RRect.fromRectAndRadius(
          Rect.fromLTRB(0, y(r.p75), size.width, y(r.p25)),
          const Radius.circular(2),
        ),
        Paint()..color = band,
      );
    }

    final path = Path()..moveTo(x(0), y(values.first));
    for (var i = 1; i < values.length; i++) {
      path.lineTo(x(i), y(values[i]));
    }
    canvas.drawPath(
      path,
      Paint()
        ..color = line
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1.5
        ..strokeJoin = StrokeJoin.round
        ..strokeCap = StrokeCap.round,
    );
    canvas.drawCircle(
      Offset(x(values.length - 1), y(values.last)),
      2.5,
      Paint()..color = line,
    );
  }

  @override
  bool shouldRepaint(_SparklinePainter old) =>
      old.values != values ||
      old.range != range ||
      old.line != line ||
      old.band != band;
}
