import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/lab.dart';
import 'package:selfkept/theme/app_theme.dart';
import 'package:selfkept/theme/tokens.dart';
import 'package:selfkept/widgets/lab_marker_tile.dart';
import 'package:selfkept/widgets/lab_sparkline.dart';

import '../helpers/fake_api_client.dart';

Future<void> _pump(
  WidgetTester tester,
  LabMarker marker, {
  ThemeData? theme,
  VoidCallback? onTap,
}) async {
  await tester.pumpWidget(
    MaterialApp(
      theme: theme ?? AppTheme.light,
      home: Scaffold(
        body: Center(
          child: SizedBox(
            width: 180,
            child: LabMarkerTile(marker: marker, onTap: onTap),
          ),
        ),
      ),
    ),
  );
}

Color _dotColor(WidgetTester tester) {
  final box = tester.widget<Container>(find.byKey(const Key('lab-status-dot')));
  return (box.decoration! as BoxDecoration).color!;
}

void main() {
  testWidgets('in range: value, unit, date, sage dot', (tester) async {
    final marker = LabMarker.fromJson(
      Fixtures.labMarker(
        metricKey: 'lab.glucose',
        displayName: 'Glucose',
        latestValue: 88,
        previousValue: 91,
        populationRange: const {'low': 70, 'high': 99},
        position: 'inside',
        severity: null,
        tier: null,
      ),
    );
    await _pump(tester, marker);
    expect(find.text('Glucose'), findsOneWidget);
    expect(find.text('88'), findsOneWidget);
    expect(find.text('mg/dL'), findsOneWidget);
    expect(find.text('Oct 24, 2026'), findsOneWidget);
    expect(find.text('In range'), findsOneWidget);
    expect(find.byType(LabSparkline), findsOneWidget);
    expect(_dotColor(tester), AppColors.light.band);
    expect(find.text('Down 3 from last'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('watch: amber dot and Above range', (tester) async {
    await _pump(tester, LabMarker.fromJson(Fixtures.labMarker()));
    expect(find.text('Above range'), findsOneWidget);
    expect(_dotColor(tester), AppColors.light.attention);
    expect(find.text('Up 14 from last'), findsOneWidget);
  });

  testWidgets('watch below range reads Below range', (tester) async {
    await _pump(
      tester,
      LabMarker.fromJson(Fixtures.labMarker(position: 'below')),
    );
    expect(find.text('Below range'), findsOneWidget);
  });

  testWidgets('clinician: terracotta dot and the plain words', (tester) async {
    final marker = LabMarker.fromJson(
      Fixtures.labMarker(severity: 'clinician', tier: 'clinician'),
    );
    await _pump(tester, marker, theme: AppTheme.dark);
    expect(find.text('Worth raising with a clinician'), findsOneWidget);
    expect(find.text('Above range'), findsNothing);
    expect(_dotColor(tester), AppColors.dark.clinician);
  });

  testWidgets('no position says nothing about range', (tester) async {
    final marker = LabMarker.fromJson(
      Fixtures.labMarker(position: null, severity: null, tier: null),
    );
    await _pump(tester, marker);
    expect(find.byKey(const Key('lab-status-dot')), findsNothing);
    expect(find.text('In range'), findsNothing);
  });

  testWidgets('no value: a quiet tile that still taps', (tester) async {
    var taps = 0;
    final marker = LabMarker.fromJson(
      Fixtures.labMarker(
        displayName: 'TSH',
        latestValue: null,
        latestDate: null,
        previousValue: null,
        position: null,
        severity: null,
        tier: null,
      ),
    );
    await _pump(tester, marker, onTap: () => taps++);
    expect(find.text('TSH'), findsOneWidget);
    expect(find.text('No result yet'), findsOneWidget);
    expect(find.byType(LabSparkline), findsNothing);
    await tester.tap(find.byType(LabMarkerTile));
    expect(taps, 1);
  });

  group('changeText', () {
    test('whole numbers round to whole', () {
      expect(LabMarkerTile.changeText(92, 78), 'Up 14 from last');
      expect(LabMarkerTile.changeText(85, 88), 'Down 3 from last');
      expect(LabMarkerTile.changeText(1180, 1000), 'Up 180 from last');
      expect(LabMarkerTile.changeText(88, 88), 'Same as last');
    });

    test('fractional values keep one decimal', () {
      expect(LabMarkerTile.changeText(4.2, 1.1), 'Up 3.1 from last');
      expect(LabMarkerTile.changeText(3.4, 3.8), 'Down 0.4 from last');
      expect(LabMarkerTile.changeText(92, 91.5), 'Up 0.5 from last');
    });

    test('a move that rounds to nothing is the same', () {
      expect(LabMarkerTile.changeText(3.44, 3.4), 'Same as last');
      expect(LabMarkerTile.changeText(3.4, 3.4), 'Same as last');
    });

    test('nothing to compare gives no line', () {
      expect(LabMarkerTile.changeText(88, null), isNull);
      expect(LabMarkerTile.changeText(null, 88), isNull);
    });
  });

  testWidgets('sparkline paints with a range, one point and none', (
    tester,
  ) async {
    for (final marker in [
      LabMarker.fromJson(Fixtures.labMarker()),
      LabMarker.fromJson(
        Fixtures.labMarker(
          populationRange: const {'low': 10},
          lastThree: [Fixtures.labPoint(value: 12)],
        ),
      ),
      LabMarker.fromJson(
        Fixtures.labMarker(
          latestValue: null,
          populationRange: const {'high': 5},
        ),
      ),
      LabMarker.fromJson(
        Fixtures.labMarker(latestValue: null, populationRange: null),
      ),
    ]) {
      await tester.pumpWidget(
        MaterialApp(
          theme: AppTheme.light,
          home: Scaffold(body: LabSparkline(marker: marker)),
        ),
      );
      expect(tester.getSize(find.byType(LabSparkline)).height, 36);
      expect(tester.takeException(), isNull);
    }
  });
}
