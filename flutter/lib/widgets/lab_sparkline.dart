import 'dart:math' as math;

import 'package:flutter/foundation.dart' show listEquals;
import 'package:flutter/material.dart';

import '../models/lab.dart';
import '../theme/tokens.dart';

/// The last few values of one marker as a small line, oldest on the left,
/// with the population range behind it as a soft sage band. A range with
/// only one bound is drawn as a hairline. With no value, only the band shows.
class LabSparkline extends StatelessWidget {
  final LabMarker marker;
  final double height;

  const LabSparkline({super.key, required this.marker, this.height = 36});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    return SizedBox(
      height: height,
      width: double.infinity,
      child: CustomPaint(
        painter: LabSparklinePainter(
          values: [for (final p in marker.lastThree) p.value],
          range: marker.populationRange,
          line: theme.colorScheme.primary,
          band: colors.band,
        ),
      ),
    );
  }
}

/// Public so tests can build one directly.
class LabSparklinePainter extends CustomPainter {
  final List<double> values;
  final LabRange? range;
  final Color line;
  final Color band;

  const LabSparklinePainter({
    required this.values,
    required this.range,
    required this.line,
    required this.band,
  });

  static const double _inset = 4;

  @override
  void paint(Canvas canvas, Size size) {
    final r = range;
    var lo = double.infinity;
    var hi = double.negativeInfinity;
    for (final v in values) {
      lo = math.min(lo, v);
      hi = math.max(hi, v);
    }
    for (final bound in [r?.low, r?.high]) {
      if (bound == null) continue;
      lo = math.min(lo, bound);
      hi = math.max(hi, bound);
    }
    if (!lo.isFinite || !hi.isFinite) return;
    if (hi - lo < 1e-9) {
      final half = math.max(lo.abs() * 0.05, 0.5);
      lo -= half;
      hi += half;
    }
    final pad = (hi - lo) * 0.1;
    final yMin = lo - pad;
    final yMax = hi + pad;
    double yFor(double v) =>
        size.height - size.height * ((v - yMin) / (yMax - yMin));

    if (r != null) {
      if (r.hasBoth) {
        canvas.drawRect(
          Rect.fromLTRB(0, yFor(r.high!), size.width, yFor(r.low!)),
          Paint()..color = band.withValues(alpha: 0.18),
        );
      } else {
        final y = yFor((r.low ?? r.high)!);
        canvas.drawLine(
          Offset(0, y),
          Offset(size.width, y),
          Paint()
            ..color = band
            ..strokeWidth = 1,
        );
      }
    }

    if (values.isEmpty) return;
    final usable = size.width - _inset * 2;
    Offset at(int i) {
      final x = values.length == 1
          ? size.width / 2
          : _inset + usable * (i / (values.length - 1));
      return Offset(x, yFor(values[i]));
    }

    if (values.length > 1) {
      final path = Path()..moveTo(at(0).dx, at(0).dy);
      for (var i = 1; i < values.length; i++) {
        final o = at(i);
        path.lineTo(o.dx, o.dy);
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
    }
    final dot = Paint()..color = line;
    for (var i = 0; i < values.length; i++) {
      canvas.drawCircle(at(i), i == values.length - 1 ? 3 : 2, dot);
    }
  }

  @override
  bool shouldRepaint(LabSparklinePainter old) =>
      !listEquals(old.values, values) ||
      old.range != range ||
      old.line != line ||
      old.band != band;
}
