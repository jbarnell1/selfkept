import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;

import '../models/trend.dart';
import '../theme/app_theme.dart';
import '../theme/tokens.dart';
import '../utils/metric_format.dart';

/// One metric over 28 or 90 days, drawn by hand: the p25 to p75 band as a
/// soft sage fill, the 28-day median as a hairline, the daily values as a
/// line with points, and anything outside p10 to p90 ringed in amber. Tap a
/// point and the caption beneath says the day, the value and its range. No
/// floating tooltips.
class TrendChart extends StatefulWidget {
  final TrendSeries series;
  final int days;
  final double height;

  const TrendChart({
    super.key,
    required this.series,
    required this.days,
    this.height = 220,
  });

  static const String learningText =
      'Still learning your normal. The band appears after 14 days.';

  @override
  State<TrendChart> createState() => _TrendChartState();
}

class _TrendChartState extends State<TrendChart> {
  int? _selected;

  @override
  void didUpdateWidget(TrendChart old) {
    super.didUpdateWidget(old);
    if (old.series != widget.series || old.days != widget.days) {
      _selected = null;
    }
  }

  void _selectAt(Offset local, Size size) {
    final geometry = _Geometry(widget.series, widget.days, size);
    final index = geometry.nearestIndex(local);
    if (index == _selected) return;
    setState(() => _selected = index);
  }

  void _toggleAt(Offset local, Size size) {
    final geometry = _Geometry(widget.series, widget.days, size);
    final index = geometry.nearestIndex(local);
    setState(() => _selected = index == _selected ? null : index);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final series = widget.series;
    final labelStyle = AppTheme.numeric(
      context,
      base: theme.textTheme.labelSmall,
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SizedBox(
          height: widget.height,
          child: LayoutBuilder(
            builder: (context, constraints) {
              final size = Size(constraints.maxWidth, widget.height);
              return GestureDetector(
                behavior: HitTestBehavior.opaque,
                onTapUp: (d) => _toggleAt(d.localPosition, size),
                onHorizontalDragUpdate: (d) => _selectAt(d.localPosition, size),
                child: Semantics(
                  label: _describe(series),
                  child: CustomPaint(
                    size: size,
                    painter: _TrendPainter(
                      series: series,
                      days: widget.days,
                      selected: _selected,
                      line: theme.colorScheme.primary,
                      band: colors.band,
                      median: colors.band,
                      outlier: colors.attention,
                      hairline: colors.hairline,
                      ink: theme.colorScheme.onSurface,
                      labelStyle: labelStyle.copyWith(
                        color: theme.colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ),
                ),
              );
            },
          ),
        ),
        const SizedBox(height: Space.x3),
        AnimatedSwitcher(
          duration: Motion.short,
          switchInCurve: Motion.curve,
          child: _Caption(
            key: ValueKey(_selected),
            series: series,
            selected: _selected,
          ),
        ),
      ],
    );
  }

  String _describe(TrendSeries series) {
    final n = series.points.length;
    return '$n ${n == 1 ? 'day' : 'days'} of ${series.label.toLowerCase()} '
        'over ${widget.days} days';
  }
}

/// Under the chart: the tapped day, or the band in words.
class _Caption extends StatelessWidget {
  final TrendSeries series;
  final int? selected;

  const _Caption({super.key, required this.series, required this.selected});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final numeric = AppTheme.numeric(context, base: theme.textTheme.bodySmall);
    final index = selected;
    final band = series.band;
    final metric = series.metric;
    final unit = series.unit;

    if (index != null && index >= 0 && index < series.points.length) {
      final p = series.points[index];
      final day = DateFormat.MMMEd().format(p.day);
      final value = formatMetricWithUnit(metric, p.value, unit: unit);
      final parts = <String>[];
      if (p.min != null) {
        parts.add('low ${formatMetricValue(metric, p.min!, unit: unit)}');
      }
      if (p.max != null) {
        parts.add('high ${formatMetricValue(metric, p.max!, unit: unit)}');
      }
      final position = band?.positionOf(p.value).displayName;
      return Wrap(
        spacing: Space.x3,
        runSpacing: Space.x1,
        crossAxisAlignment: WrapCrossAlignment.center,
        children: [
          Text(day, style: theme.textTheme.bodySmall),
          Text(
            value,
            style: numeric.copyWith(color: theme.colorScheme.onSurface),
          ),
          if (parts.isNotEmpty) Text(parts.join(', '), style: numeric),
          if (position != null)
            Text(position, style: theme.textTheme.bodySmall),
        ],
      );
    }

    if (band == null) {
      return Text(TrendChart.learningText, style: theme.textTheme.bodySmall);
    }
    final low = formatMetricValue(metric, band.p25, unit: unit);
    final high = formatMetricValue(metric, band.p75, unit: unit);
    final median = formatMetricWithUnit(metric, band.median28, unit: unit);
    return Text.rich(
      TextSpan(
        style: theme.textTheme.bodySmall,
        children: [
          const TextSpan(text: 'Your usual range is '),
          TextSpan(text: low, style: numeric),
          const TextSpan(text: ' to '),
          TextSpan(text: high, style: numeric),
          const TextSpan(text: ', median '),
          TextSpan(text: median, style: numeric),
          const TextSpan(text: '. Tap a point for its day.'),
        ],
      ),
    );
  }
}

/// Maps days and values to pixels. Shared by the painter and the tap handler
/// so a tap lands on the point that was drawn.
class _Geometry {
  final TrendSeries series;
  final int days;
  final Size size;

  static const double leftGutter = 52;
  static const double rightPad = Space.x3;
  static const double topPad = Space.x3;
  static const double bottomGutter = 24;

  late final DateTime end;
  late final DateTime start;
  late final double yMin;
  late final double yMax;
  late final List<double> yTicks;

  _Geometry(this.series, this.days, this.size) {
    final points = series.points;
    final last = points.isEmpty ? DateTime.now() : points.last.day;
    end = DateTime(last.year, last.month, last.day);
    start = end.subtract(Duration(days: days - 1));

    var lo = double.infinity;
    var hi = double.negativeInfinity;
    for (final p in points) {
      lo = math.min(lo, math.min(p.value, p.min ?? p.value));
      hi = math.max(hi, math.max(p.value, p.max ?? p.value));
    }
    final band = series.band;
    if (band != null) {
      lo = math.min(lo, band.p10);
      hi = math.max(hi, band.p90);
    }
    if (!lo.isFinite || !hi.isFinite) {
      lo = 0;
      hi = 1;
    }
    if (hi - lo < 1e-9) {
      lo -= 1;
      hi += 1;
    }
    final ticks = _niceTicks(lo, hi, sleep: series.metric == 'sleep_stage');
    yTicks = ticks;
    yMin = ticks.first;
    yMax = ticks.last;
  }

  Rect get plot => Rect.fromLTRB(
    leftGutter,
    topPad,
    size.width - rightPad,
    size.height - bottomGutter,
  );

  double xFor(DateTime day) {
    final d = DateTime(day.year, day.month, day.day).difference(start).inDays;
    final span = math.max(1, days - 1);
    return plot.left + plot.width * (d / span);
  }

  double yFor(double value) {
    final t = (value - yMin) / (yMax - yMin);
    return plot.bottom - plot.height * t.clamp(0.0, 1.0);
  }

  Offset offsetOf(TrendPoint p) => Offset(xFor(p.day), yFor(p.value));

  /// Nearest point by x within reach, or null.
  int? nearestIndex(Offset local) {
    final points = series.points;
    if (points.isEmpty) return null;
    int? best;
    var bestDx = double.infinity;
    for (var i = 0; i < points.length; i++) {
      final dx = (xFor(points[i].day) - local.dx).abs();
      if (dx < bestDx) {
        bestDx = dx;
        best = i;
      }
    }
    return bestDx <= 28 ? best : null;
  }

  /// Week ticks counted back from the last day. Ninety days thins to every
  /// second week, or every fourth on a narrow screen.
  List<DateTime> get xTicks {
    final step = days <= 28 ? 7 : (plot.width < 480 ? 28 : 14);
    return [
      for (var d = 0; d < days; d += step) end.subtract(Duration(days: d)),
    ];
  }

  /// Three to five round values spanning the data. Sleep ticks in half and
  /// whole hours; everything else in 1, 2, 2.5 or 5 times a power of ten.
  static List<double> _niceTicks(double lo, double hi, {required bool sleep}) {
    final candidates = sleep
        ? <double>[15, 30, 60, 90, 120, 180, 240, 360]
        : _powerSteps(hi - lo);
    for (final step in candidates) {
      final first = (lo / step).floor() * step;
      final last = (hi / step).ceil() * step;
      final count = ((last - first) / step).round() + 1;
      if (count <= 5) {
        return [for (var i = 0; i < count; i++) first + i * step];
      }
    }
    final step = candidates.last;
    final first = (lo / step).floor() * step;
    final last = (hi / step).ceil() * step;
    return [first, (first + last) / 2, last];
  }

  static List<double> _powerSteps(double range) {
    final magnitude = math.pow(10, (math.log(range) / math.ln10).floor() - 1);
    return [
      for (final m in [1, 2, 2.5, 5, 10, 20, 25, 50, 100])
        m * magnitude.toDouble(),
    ];
  }
}

class _TrendPainter extends CustomPainter {
  final TrendSeries series;
  final int days;
  final int? selected;
  final Color line;
  final Color band;
  final Color median;
  final Color outlier;
  final Color hairline;
  final Color ink;
  final TextStyle labelStyle;

  const _TrendPainter({
    required this.series,
    required this.days,
    required this.selected,
    required this.line,
    required this.band,
    required this.median,
    required this.outlier,
    required this.hairline,
    required this.ink,
    required this.labelStyle,
  });

  @override
  void paint(Canvas canvas, Size size) {
    final g = _Geometry(series, days, size);
    final plot = g.plot;
    final metric = series.metric;
    final unit = series.unit;

    // Grid and y labels.
    final gridPaint = Paint()
      ..color = hairline
      ..strokeWidth = 1;
    for (final tick in g.yTicks) {
      final y = g.yFor(tick);
      canvas.drawLine(Offset(plot.left, y), Offset(plot.right, y), gridPaint);
      _text(
        canvas,
        formatMetricValue(metric, tick, unit: unit),
        Offset(plot.left - Space.x2, y),
        align: Alignment.centerRight,
      );
    }

    // Week ticks, newest first. A label that would run into the one to its
    // right (the newest is pulled in from the edge) is left out; the tick
    // mark stays.
    var nextLeft = double.infinity;
    for (final day in g.xTicks) {
      final x = g.xFor(day);
      canvas.drawLine(
        Offset(x, plot.bottom),
        Offset(x, plot.bottom + 4),
        gridPaint,
      );
      final drawn = _text(
        canvas,
        DateFormat.MMMd().format(day),
        Offset(x, plot.bottom + 6),
        align: Alignment.topCenter,
        clampTo: plot,
        rightLimit: nextLeft - Space.x2,
      );
      if (drawn != null) nextLeft = drawn.left;
    }

    // The band and the median.
    final b = series.band;
    if (b != null) {
      final top = g.yFor(b.p75);
      final bottom = g.yFor(b.p25);
      canvas.drawRect(
        Rect.fromLTRB(plot.left, top, plot.right, bottom),
        Paint()..color = band.withValues(alpha: 0.22),
      );
      final my = g.yFor(b.median28);
      canvas.drawLine(
        Offset(plot.left, my),
        Offset(plot.right, my),
        Paint()
          ..color = median
          ..strokeWidth = 1,
      );
    }

    final points = series.points;
    if (points.isEmpty) return;

    // The line.
    final path = Path();
    for (var i = 0; i < points.length; i++) {
      final o = g.offsetOf(points[i]);
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
        ..strokeWidth = 1.5
        ..strokeJoin = StrokeJoin.round
        ..strokeCap = StrokeCap.round,
    );

    // Points, outliers and the selection.
    final dot = Paint()..color = line;
    final ring = Paint()
      ..color = outlier
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.5;
    final dense = points.length > 40;
    for (var i = 0; i < points.length; i++) {
      final p = points[i];
      final o = g.offsetOf(p);
      final isOutlier = b?.isOutlier(p.value) ?? false;
      if (!dense || isOutlier || i == selected) {
        canvas.drawCircle(o, 2.5, dot);
      }
      if (isOutlier) canvas.drawCircle(o, 5.5, ring);
    }

    final s = selected;
    if (s != null && s >= 0 && s < points.length) {
      final p = points[s];
      final o = g.offsetOf(p);
      canvas.drawLine(
        Offset(o.dx, plot.top),
        Offset(o.dx, plot.bottom),
        Paint()
          ..color = ink.withValues(alpha: 0.35)
          ..strokeWidth = 1,
      );
      if (p.min != null && p.max != null) {
        canvas.drawLine(
          Offset(o.dx, g.yFor(p.min!)),
          Offset(o.dx, g.yFor(p.max!)),
          Paint()
            ..color = line.withValues(alpha: 0.5)
            ..strokeWidth = 3
            ..strokeCap = StrokeCap.round,
        );
      }
      canvas.drawCircle(
        o,
        5,
        Paint()
          ..color = ink
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1.5,
      );
    }
  }

  /// Paints [text] and returns where it went, or null when it would cross
  /// [rightLimit] and was skipped.
  Rect? _text(
    Canvas canvas,
    String text,
    Offset at, {
    required Alignment align,
    Rect? clampTo,
    double rightLimit = double.infinity,
  }) {
    final painter = TextPainter(
      text: TextSpan(text: text, style: labelStyle),
      textDirection: TextDirection.ltr,
    )..layout();
    var dx = at.dx - painter.width * (align.x + 1) / 2;
    final dy = at.dy - painter.height * (align.y + 1) / 2;
    if (clampTo != null) {
      dx = dx.clamp(clampTo.left - Space.x4, clampTo.right - painter.width);
    }
    if (dx + painter.width > rightLimit) return null;
    painter.paint(canvas, Offset(dx, dy));
    return Offset(dx, dy) & painter.size;
  }

  @override
  bool shouldRepaint(_TrendPainter old) =>
      old.series != series ||
      old.days != days ||
      old.selected != selected ||
      old.line != line ||
      old.band != band ||
      old.median != median ||
      old.outlier != outlier ||
      old.hairline != hairline ||
      old.ink != ink ||
      old.labelStyle != labelStyle;
}
