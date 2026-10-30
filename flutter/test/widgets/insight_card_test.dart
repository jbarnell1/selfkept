import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/insight.dart';
import 'package:selfkept/theme/tokens.dart';
import 'package:selfkept/widgets/insight_card.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  Insight make(String severity, {String? reaction}) => Insight.fromJson(
    Fixtures.insight(severity: severity, reaction: reaction),
  );

  Color ruleColor(WidgetTester tester) {
    final box = tester.widget<DecoratedBox>(
      find.byKey(InsightCard.severityRuleKey),
    );
    final decoration = box.decoration as BoxDecoration;
    return (decoration.border! as Border).left.color;
  }

  for (final entry in {
    'notice': (AppColors c) => c.band,
    'watch': (AppColors c) => c.attention,
    'clinician': (AppColors c) => c.clinician,
  }.entries) {
    testBothThemes('${entry.key} uses its token colour for the rule', (
      tester,
      theme,
    ) async {
      await usePhoneSize(tester);
      await tester.pumpWidget(
        MaterialApp(
          theme: theme,
          home: Scaffold(
            body: InsightCard(insight: make(entry.key), onReact: (_) {}),
          ),
        ),
      );
      await tester.pumpAndSettle();
      final colors = theme.extension<AppColors>()!;
      expect(ruleColor(tester), entry.value(colors));
      expect(ruleColor(tester), isNot(Colors.red));
    });
  }

  testWidgets('shows the label, text, metric chips and three reactions', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: InsightCard(
            insight: make('watch'),
            onReact: (_) {},
            onDismiss: () {},
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Watch'), findsOneWidget);
    expect(find.textContaining('HRV has sat below'), findsOneWidget);
    expect(find.text('HRV (SDNN)'), findsOneWidget);
    for (final r in InsightReaction.values) {
      expect(find.text(r.displayName), findsOneWidget);
    }
    expect(find.byTooltip('Dismiss'), findsOneWidget);
  });

  testWidgets('tapping a reaction and dismiss calls back', (tester) async {
    await usePhoneSize(tester);
    InsightReaction? got;
    var dismissed = 0;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: InsightCard(
            insight: make('notice'),
            onReact: (r) => got = r,
            onDismiss: () => dismissed += 1,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Knew that'));
    expect(got, InsightReaction.knew);
    await tester.tap(find.byTooltip('Dismiss'));
    expect(dismissed, 1);
  });

  testWidgets('the chosen reaction is marked and a dismissed card says so', (
    tester,
  ) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: Column(
            children: [
              InsightCard(
                insight: make('notice', reaction: 'helpful'),
                onReact: (_) {},
                animateIn: false,
              ),
              InsightCard(
                insight: Insight.fromJson(
                  Fixtures.insight(
                    id: 'd',
                    dismissedAt: '2026-10-29T10:00:00Z',
                  ),
                ),
                onDismiss: () {},
                animateIn: false,
              ),
            ],
          ),
        ),
      ),
    );
    await tester.pump();
    final helpful = find.widgetWithText(TextButton, 'Helpful');
    final button = tester.widget<TextButton>(helpful);
    expect(button.style?.backgroundColor?.resolve({}), isNotNull);
    expect(find.text('Dismissed'), findsOneWidget);
    // A dismissed card offers no dismiss button.
    expect(find.byTooltip('Dismiss'), findsNothing);
  });

  testWidgets('fades in over the standard motion', (tester) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(body: InsightCard(insight: make('notice'))),
      ),
    );
    await tester.pump();
    final early = tester.widget<Opacity>(find.byType(Opacity).first).opacity;
    expect(early, lessThan(1));
    await tester.pump(Motion.standard);
    await tester.pump();
    final late = tester.widget<Opacity>(find.byType(Opacity).first).opacity;
    expect(late, 1);
  });

  testWidgets('chips show the names the server sends, not metric keys', (
    tester,
  ) async {
    await usePhoneSize(tester);
    final json = Fixtures.insight(metrics: const ['lab.hscrp', 'lab.apob'])
      ..['metricNames'] = ['hs-CRP', 'ApoB'];
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(body: InsightCard(insight: Insight.fromJson(json))),
      ),
    );
    expect(find.text('hs-CRP'), findsOneWidget);
    expect(find.text('ApoB'), findsOneWidget);
    expect(find.text('lab.hscrp'), findsNothing);
  });
}
