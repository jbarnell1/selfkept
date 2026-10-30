import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/trend.dart';
import 'package:selfkept/widgets/trend_chart.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  TrendSeries series({bool withBand = true, int days = 28}) =>
      TrendSeries.fromJson(
        Fixtures.trendSeries(withBand: withBand, days: days),
      );

  testBothThemes('draws without error and captions the band', (
    tester,
    theme,
  ) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(
      MaterialApp(
        theme: theme,
        home: Scaffold(
          body: Padding(
            padding: const EdgeInsets.all(16),
            child: TrendChart(series: series(), days: 28),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byType(CustomPaint), findsWidgets);
    expect(find.textContaining('Your usual range is'), findsOneWidget);
    expect(find.textContaining('7 h 16'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('tapping near the last point captions that day', (tester) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: Padding(
            padding: const EdgeInsets.all(16),
            child: TrendChart(series: series(), days: 28),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    final rect = tester.getRect(find.byType(CustomPaint).last);
    await tester.tapAt(Offset(rect.right - 14, rect.center.dy));
    await tester.pumpAndSettle();
    expect(find.textContaining('Oct 29'), findsOneWidget);
    expect(find.textContaining('low 6 h 50'), findsOneWidget);
    expect(find.text('7 h 10'), findsOneWidget);

    // Tapping the same point again clears the selection.
    await tester.tapAt(Offset(rect.right - 14, rect.center.dy));
    await tester.pumpAndSettle();
    expect(find.textContaining('Oct 29'), findsNothing);
    expect(find.textContaining('Your usual range is'), findsOneWidget);
  });

  testWidgets('without a band the caption says it is still learning', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: TrendChart(series: series(withBand: false), days: 28),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text(TrendChart.learningText), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('ninety days at laptop width draws without error', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1280, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: SizedBox(
            width: 700,
            child: TrendChart(series: series(days: 90), days: 90, height: 280),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
  });
}
