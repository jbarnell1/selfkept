import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:selfkept/models/band_position.dart';
import 'package:selfkept/models/today.dart';
import 'package:selfkept/widgets/readiness_row.dart';
import 'package:selfkept/widgets/subpage_app_bar.dart';

import '../helpers/pump.dart';

void main() {
  GoRouter router(String initial) => GoRouter(
    initialLocation: initial,
    routes: [
      GoRoute(path: '/you', builder: (_, _) => const Text('the you tab')),
      GoRoute(
        path: '/export',
        builder: (_, _) =>
            const Scaffold(appBar: SubpageAppBar(fallback: '/you')),
      ),
    ],
  );

  testWidgets('a page opened directly still has a way back to its tab', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp.router(routerConfig: router('/export')),
    );
    await tester.pumpAndSettle();

    expect(find.byType(BackButton), findsOneWidget);
    await tester.tap(find.byType(BackButton));
    await tester.pumpAndSettle();
    expect(find.text('the you tab'), findsOneWidget);
  });

  testWidgets('a pushed page keeps the ordinary back button', (tester) async {
    final r = router('/you');
    await tester.pumpWidget(MaterialApp.router(routerConfig: r));
    await tester.pumpAndSettle();
    unawaited(r.push('/export'));
    await tester.pumpAndSettle();

    expect(find.byType(BackButton), findsOneWidget);
    await tester.tap(find.byType(BackButton));
    await tester.pumpAndSettle();
    expect(find.text('the you tab'), findsOneWidget);
  });

  testBothThemes('the readiness labels are whole on a narrow phone', (
    tester,
    theme,
  ) async {
    tester.view.devicePixelRatio = 2;
    tester.view.physicalSize = const Size(640, 1400);
    addTearDown(tester.view.reset);
    await tester.pumpWidget(
      MaterialApp(
        theme: theme,
        home: const Scaffold(
          body: Padding(
            padding: EdgeInsets.all(24),
            child: ReadinessRow(
              readiness: Readiness(
                sleep: BandPosition.below,
                hrv: BandPosition.below,
                restingHr: BandPosition.above,
              ),
              sleepMinutes: 384,
            ),
          ),
        ),
      ),
    );
    // The test font is far wider than Inter, so check the wrap rule rather
    // than measuring: the label may take a second line and is never cut.
    final label = tester.widget<Text>(find.text('Resting HR'));
    expect(label.maxLines, 2);
    expect(label.overflow, isNot(TextOverflow.ellipsis));
    expect(tester.takeException(), isNull);
  });
}
