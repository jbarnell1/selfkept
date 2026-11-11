import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/medication.dart';
import 'package:selfkept/pages/you/medicines_page.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/status_views.dart';

import '../helpers/body_fixtures.dart';
import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  setUp(() async {
    h = TestHarness();
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
    h.api.on(
      'GET',
      '/medications',
      (_) => [
        BodyFixtures.medication(),
        BodyFixtures.medication(
          id: 'm2',
          kind: 'medication',
          brand: null,
          product: 'Atorvastatin',
          active: false,
          stoppedOn: '2026-10-01',
          ingredients: const [],
          substances: const ['statin'],
        ),
      ],
    );
    h.api.on('GET', '/household', (_) => Fixtures.household());
    h.api.on('GET', '/household/shares', (_) => <Map<String, dynamic>>[]);
    h.api.on('GET', '/household/invites', (_) => <Map<String, dynamic>>[]);
    h.api.on(
      'GET',
      '/household/shared-with-me',
      (_) => <Map<String, dynamic>>[],
    );
  });
  tearDown(() => h.dispose());

  Future<void> pumpPage(WidgetTester tester, {ThemeData? theme}) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(const MedicinesPage(), theme: theme));
    await tester.pumpAndSettle();
  }

  Finder field(String label) => find.widgetWithText(TextField, label);

  testBothThemes('data: taking now and stopped, with ingredients and tags', (
    tester,
    theme,
  ) async {
    await pumpPage(tester, theme: theme);
    expect(find.text(MedicinesPage.title), findsOneWidget);
    expect(find.text(MedicinesPage.currentTitle), findsOneWidget);
    expect(find.text('Acme Daily Multi'), findsOneWidget);
    expect(
      find.text('Supplement. Name brand. Daily, with breakfast.'),
      findsOneWidget,
    );
    expect(find.text('Vitamin D3 25 mcg, Zinc 10 mg'), findsOneWidget);
    expect(find.text('Vitamin D'), findsOneWidget);
    expect(find.text('Zinc'), findsOneWidget);
    await scrollTo(tester, find.text('Atorvastatin'));
    expect(find.text(MedicinesPage.stoppedTitle), findsOneWidget);
    expect(find.text('Statin'), findsOneWidget);
    expect(find.text(MedicinesPage.addLabel), findsOneWidget);
  });

  Medication withNote(String? note, {String schedule = 'as_needed'}) {
    final json = BodyFixtures.medication(kind: 'medication');
    json['scheduleNote'] = note;
    json['schedule'] = schedule;
    json['generic'] = true;
    return Medication.fromJson(json);
  }

  test('the kind line reads as sentences and keeps the note as typed', () {
    expect(
      MedicinesPage.kindLine(withNote('For the odd headache')),
      'Medicine. Generic. As needed. For the odd headache.',
    );
    expect(
      MedicinesPage.kindLine(withNote('with breakfast', schedule: 'daily')),
      'Medicine. Generic. Daily, with breakfast.',
    );
    expect(
      MedicinesPage.kindLine(withNote('Only on bad days!')),
      'Medicine. Generic. As needed. Only on bad days!',
    );
    expect(
      MedicinesPage.kindLine(withNote(null)),
      'Medicine. Generic. As needed.',
    );
  });

  test('substance tags read with their proper names', () {
    expect(Medication.substanceLabel('vitamin_d'), 'Vitamin D');
    expect(Medication.substanceLabel('vitamin_b12'), 'Vitamin B12');
    expect(Medication.substanceLabel('omega_3'), 'Omega-3');
    expect(Medication.substanceLabel('glp1'), 'GLP-1');
    expect(Medication.substanceLabel('something_new'), 'Something new');
  });

  testWidgets('loading: one spinner and no add button yet', (tester) async {
    await usePhoneSize(tester);
    h.api.on(
      'GET',
      '/medications',
      (_) => Completer<List<Map<String, dynamic>>>().future,
    );
    await tester.pumpWidget(h.wrap(const MedicinesPage()));
    await pumpFrames(tester);
    expect(find.byType(LoadingView), findsOneWidget);
    expect(find.text(MedicinesPage.addLabel), findsNothing);
  });

  testWidgets('error: says so and tries again', (tester) async {
    h.api.fail(
      'GET',
      '/medications',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await pumpPage(tester);
    expect(find.text(MedicinesPage.loadErrorTitle), findsOneWidget);
    h.api.on('GET', '/medications', (_) => [BodyFixtures.medication()]);
    await tester.tap(find.text('Try again'));
    await tester.pumpAndSettle();
    expect(find.text('Acme Daily Multi'), findsOneWidget);
  });

  testWidgets('empty: one line and the add button', (tester) async {
    h.api.on('GET', '/medications', (_) => <Map<String, dynamic>>[]);
    await pumpPage(tester);
    expect(find.text(MedicinesPage.emptyLine), findsOneWidget);
    expect(find.text(MedicinesPage.addLabel), findsOneWidget);
  });

  testWidgets('a multivitamin is added one ingredient row at a time', (
    tester,
  ) async {
    h.api.on('POST', '/medications', (_) => BodyFixtures.medication());
    await pumpPage(tester);
    await tester.tap(find.text(MedicinesPage.addLabel));
    await tester.pumpAndSettle();
    expect(find.text(MedicationEditor.newTitle), findsOneWidget);

    await tester.enterText(field(MedicationEditor.brandLabel), 'Acme');
    await tester.enterText(field(MedicationEditor.productLabel), 'Daily Multi');
    await tester.tap(find.widgetWithText(ChoiceChip, 'Name brand'));
    await tester.enterText(field(MedicationEditor.doseLabel), '1 tablet');

    for (final row in [
      ['Vitamin D3', '25', 'mcg'],
      ['Spare', '', ''],
      ['Zinc', '10', 'mg'],
    ]) {
      await scrollTo(tester, find.text(MedicationEditor.addIngredientLabel));
      await tester.tap(find.text(MedicationEditor.addIngredientLabel));
      await tester.pumpAndSettle();
      await tester.enterText(
        field(MedicationEditor.ingredientName).last,
        row[0],
      );
      await tester.enterText(
        field(MedicationEditor.ingredientAmount).last,
        row[1],
      );
      await tester.enterText(
        field(MedicationEditor.ingredientUnit).last,
        row[2],
      );
    }
    expect(field(MedicationEditor.ingredientName), findsNWidgets(3));
    await tester.ensureVisible(
      find.byTooltip(MedicationEditor.removeIngredientTooltip).at(1),
    );
    await tester.pumpAndSettle();
    await tester.tap(
      find.byTooltip(MedicationEditor.removeIngredientTooltip).at(1),
    );
    await tester.pumpAndSettle();
    expect(field(MedicationEditor.ingredientName), findsNWidgets(2));

    await tester.ensureVisible(find.widgetWithText(FilledButton, 'Save'));

    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    final body =
        h.api.bodies[h.api.calls.indexOf('POST /medications')]
            as Map<String, dynamic>;
    expect(body['kind'], 'supplement');
    expect(body['brand'], 'Acme');
    expect(body['product'], 'Daily Multi');
    expect(body['generic'], isFalse);
    expect(body['dose'], '1 tablet');
    expect(body['schedule'], 'daily');
    expect(body['ingredients'], [
      {'name': 'Vitamin D3', 'amount': 25.0, 'unit': 'mcg'},
      {'name': 'Zinc', 'amount': 10.0, 'unit': 'mg'},
    ]);
    expect(find.text(MedicationEditor.newTitle), findsNothing);
  });

  testWidgets('an ingredient needs a name before anything is sent', (
    tester,
  ) async {
    await pumpPage(tester);
    await tester.tap(find.text(MedicinesPage.addLabel));
    await tester.pumpAndSettle();
    await tester.enterText(field(MedicationEditor.productLabel), 'Multi');
    await scrollTo(tester, find.text(MedicationEditor.addIngredientLabel));
    await tester.tap(find.text(MedicationEditor.addIngredientLabel));
    await tester.pumpAndSettle();
    await tester.enterText(field(MedicationEditor.ingredientAmount), '5');
    await tester.ensureVisible(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    expect(find.text(MedicationEditor.needIngredientName), findsOneWidget);
    expect(h.api.calls, isNot(contains('POST /medications')));
  });

  testWidgets('editing opens filled in and saves with PUT', (tester) async {
    h.api.on('PUT', '/medications/m1', (_) => BodyFixtures.medication());
    await pumpPage(tester);
    await tester.tap(find.text('Acme Daily Multi'));
    await tester.pumpAndSettle();
    expect(find.text(MedicationEditor.editTitle), findsOneWidget);
    expect(find.text('Daily Multi'), findsOneWidget);
    await scrollTo(tester, find.text('Vitamin D3'));
    await tester.ensureVisible(find.text('Weekly'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Weekly'));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    final body =
        h.api.bodies[h.api.calls.indexOf('PUT /medications/m1')]
            as Map<String, dynamic>;
    expect(body['schedule'], 'weekly');
    expect(body['ingredients'], hasLength(2));
  });

  testWidgets('removing an item asks first', (tester) async {
    h.api.on('DELETE', '/medications/m1', (_) => null);
    await pumpPage(tester);
    await tester.tap(find.text('Acme Daily Multi'));
    await tester.pumpAndSettle();
    await scrollTo(tester, find.text(MedicationEditor.deleteLabel));
    await tester.tap(find.text(MedicationEditor.deleteLabel));
    await tester.pumpAndSettle();
    expect(find.text(MedicationEditor.deleteMessage), findsOneWidget);
    await tester.tap(find.text('Remove'));
    await tester.pumpAndSettle();
    expect(h.api.calls, contains('DELETE /medications/m1'));
    expect(find.text('Acme Daily Multi'), findsNothing);
  });

  testWidgets('someone else: their list, read only', (tester) async {
    h.api.on(
      'GET',
      '/household/shared-with-me',
      (_) => [
        {
          'personId': 'p2',
          'displayName': 'Charles',
          'categories': ['labs'],
        },
      ],
    );
    await pumpPage(tester);
    await tester.tap(find.widgetWithText(ChoiceChip, 'Charles'));
    await tester.pumpAndSettle();
    final query =
        h.api.bodies[h.api.calls.lastIndexOf('GET /medications')]
            as Map<String, dynamic>;
    expect(query['personId'], 'p2');
    expect(find.text(MedicinesPage.addLabel), findsNothing);
    await tester.tap(find.text('Acme Daily Multi'));
    await tester.pumpAndSettle();
    expect(find.text(MedicationEditor.editTitle), findsNothing);
  });

  testWidgets('not shared: a 404 says so', (tester) async {
    h.api.on(
      'GET',
      '/household/shared-with-me',
      (_) => [
        {
          'personId': 'p2',
          'displayName': 'Charles',
          'categories': ['body'],
        },
      ],
    );
    await pumpPage(tester);
    h.api.fail(
      'GET',
      '/medications',
      const ApiException('Not found', statusCode: 404),
    );
    await tester.tap(find.widgetWithText(ChoiceChip, 'Charles'));
    await tester.pumpAndSettle();
    expect(find.text(NotSharedView.title), findsOneWidget);
  });
}
