import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/pages/assistant_page.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/assistant/assistant_feature.dart';
import 'package:selfkept/widgets/assistant/chat_view.dart';
import 'package:selfkept/widgets/status_views.dart';

import '../helpers/assistant_fixtures.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  setUp(() async {
    h = TestHarness();
    await signInWithAssistant(h);
    h.api.on('GET', '/assistant/status', (_) => AssistantFixtures.status());
    h.api.on(
      'GET',
      '/assistant/conversations',
      (_) => [
        AssistantFixtures.summary(),
        AssistantFixtures.summary(
          id: 'c0',
          kind: 'checkin',
          title: 'Evening check-in, 9 Nov',
          day: '2026-11-09',
        ),
      ],
    );
  });
  tearDown(() => h.dispose());

  Future<void> pumpPage(WidgetTester tester, {ThemeData? theme}) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(
      h.wrap(const Scaffold(body: AssistantPage()), theme: theme),
    );
    await tester.pumpAndSettle();
  }

  testBothThemes('data: actions, playbook link and conversations', (
    tester,
    theme,
  ) async {
    await pumpPage(tester, theme: theme);
    expect(find.text(AssistantPage.subtitle), findsOneWidget);
    expect(find.text(AssistantPage.newLabel), findsOneWidget);
    expect(find.text(AssistantPage.checkinLabel), findsOneWidget);
    expect(find.text(AssistantPage.playbookTitle), findsOneWidget);
    expect(find.text('Long run this morning'), findsOneWidget);
    expect(find.text('Evening check-in, 9 Nov'), findsOneWidget);
    expect(find.text(ModelQuietNote.text), findsNothing);
  });

  testWidgets('says plainly when the home model is not answering', (
    tester,
  ) async {
    h.api.on(
      'GET',
      '/assistant/status',
      (_) => AssistantFixtures.status(enabled: false, reachable: false),
    );
    await pumpPage(tester);
    expect(find.text(ModelQuietNote.text), findsOneWidget);
    expect(find.text('Long run this morning'), findsOneWidget);
    expect(find.byType(ErrorView), findsNothing);
  });

  testWidgets('empty: one line that says how to start', (tester) async {
    h.api.on('GET', '/assistant/conversations', (_) => []);
    await pumpPage(tester);
    expect(find.text(AssistantPage.emptyLine), findsOneWidget);
  });

  testWidgets('error: says so and loads again', (tester) async {
    h.api.fail(
      'GET',
      '/assistant/conversations',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await pumpPage(tester);
    expect(find.text(AssistantPage.loadErrorLine), findsOneWidget);
    h.api.on('GET', '/assistant/conversations', (_) => []);
    await tester.tap(find.text('Try again'));
    await tester.pumpAndSettle();
    expect(find.text(AssistantPage.emptyLine), findsOneWidget);
  });

  testWidgets('loading: a quiet spinner', (tester) async {
    final gate = Completer<List<Map<String, dynamic>>>();
    h.api.on('GET', '/assistant/conversations', (_) => gate.future);
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(const Scaffold(body: AssistantPage())));
    await pumpFrames(tester);
    expect(find.byType(LoadingView), findsOneWidget);
    gate.complete([]);
    await tester.pumpAndSettle();
    expect(find.text(AssistantPage.emptyLine), findsOneWidget);
  });

  testWidgets('delete asks inside the page, and Keep keeps it', (tester) async {
    h.api.on('DELETE', '/assistant/conversations/c1', (_) => null);
    await pumpPage(tester);
    await tester.tap(find.byTooltip(AssistantPage.deleteTooltip).first);
    await tester.pumpAndSettle();
    expect(find.text(AssistantPage.deleteQuestion), findsOneWidget);
    expect(find.byType(AlertDialog), findsNothing);

    await tester.tap(find.text(AssistantPage.keepLabel));
    await tester.pumpAndSettle();
    expect(find.text(AssistantPage.deleteQuestion), findsNothing);
    expect(find.text('Long run this morning'), findsOneWidget);

    await tester.tap(find.byTooltip(AssistantPage.deleteTooltip).first);
    await tester.pumpAndSettle();
    await tester.tap(find.text(AssistantPage.deleteLabel));
    await tester.pumpAndSettle();
    expect(h.api.calls, contains('DELETE /assistant/conversations/c1'));
    expect(find.text('Long run this morning'), findsNothing);
    expect(find.text(AssistantPage.deletedMessage), findsOneWidget);
  });

  testBothThemes('a server without the assistant asks for an update', (
    tester,
    theme,
  ) async {
    h.dispose();
    h = TestHarness();
    await signInWithAssistant(h, feature: false);
    await pumpPage(tester, theme: theme);
    expect(find.text(AssistantNeedsUpdate.title), findsOneWidget);
    expect(find.text(AssistantPage.newLabel), findsNothing);
    expect(h.api.calls.where((c) => c.contains('/assistant/')), isEmpty);
  });
}
