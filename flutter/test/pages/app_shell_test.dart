import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:selfkept/pages/app_shell.dart';
import 'package:selfkept/pages/assistant_page.dart';
import 'package:selfkept/pages/insights_page.dart';
import 'package:selfkept/pages/labs_page.dart';
import 'package:selfkept/pages/progress_page.dart';
import 'package:selfkept/pages/systems_page.dart';
import 'package:selfkept/pages/today_page.dart';
import 'package:selfkept/pages/you/goals_page.dart';
import 'package:selfkept/pages/you/medicines_page.dart';
import 'package:selfkept/router/app_router.dart';
import 'package:selfkept/theme/app_theme.dart';

import '../helpers/body_fixtures.dart';
import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;
  late GoRouter router;

  setUp(() async {
    h = TestHarness();
    h.api.on('GET', '/system/version', (_) => Fixtures.version());
    h.api.on('GET', '/system/setup-status', (_) => Fixtures.setupStatus());
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
    await h.startup.run();
    h.api.on('GET', '/today', (_) => Fixtures.today());
    h.api.on('GET', '/progress', (_) => BodyFixtures.progress());
    h.api.on('GET', '/targets/tally', (_) => BodyFixtures.tally());
    h.api.on('GET', '/journal', (_) => <Map<String, dynamic>>[]);
    h.api.on('GET', '/journal/tags', (_) => BodyFixtures.tagGroups());
    router = buildRouter(
      auth: h.auth,
      startup: h.startup,
      initialLocation: '/',
    );
  });
  tearDown(() {
    router.dispose();
    h.dispose();
  });

  Future<void> pumpShell(
    WidgetTester tester, {
    ThemeData? theme,
    bool wide = false,
  }) async {
    if (wide) {
      tester.view.physicalSize = const Size(1280, 900);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
    } else {
      await usePhoneSize(tester);
    }
    await tester.pumpWidget(
      h.wrap(const SizedBox.shrink(), theme: theme, router: router),
    );
    await tester.pumpAndSettle();
  }

  int selectedTab(WidgetTester tester) =>
      tester.widget<NavigationBar>(find.byType(NavigationBar)).selectedIndex;

  test('the five tabs, in order', () {
    expect(ShellTab.all.map((t) => t.label), [
      'Today',
      'Progress',
      'Assistant',
      'Systems',
      'You',
    ]);
    expect(ShellTab.all.map((t) => t.path), [
      '/',
      '/progress',
      '/assistant',
      '/systems',
      '/you',
    ]);
  });

  for (final theme in [AppTheme.light, AppTheme.dark]) {
    final name = theme == AppTheme.light ? 'light' : 'dark';
    testWidgets('a bottom bar with five tabs that each open ($name)', (
      tester,
    ) async {
      await pumpShell(tester, theme: theme);
      expect(find.byType(NavigationDestination), findsNWidgets(5));
      expect(find.byType(TodayPage), findsOneWidget);

      await tester.tap(find.text('Progress'));
      await tester.pumpAndSettle();
      expect(find.byType(ProgressPage), findsOneWidget);
      expect(selectedTab(tester), 1);

      await tester.tap(find.text('Assistant'));
      await tester.pumpAndSettle();
      expect(find.text(AssistantPage.subtitle), findsOneWidget);

      await tester.tap(find.text('Systems').last);
      await tester.pumpAndSettle();
      expect(find.byType(SystemsPage), findsOneWidget);
      expect(find.text(SystemsPage.labsLine), findsOneWidget);
    });
  }

  testWidgets('a rail with the same five tabs at laptop width', (tester) async {
    await pumpShell(tester, wide: true);
    expect(find.byType(NavigationRail), findsOneWidget);
    expect(
      tester.widget<NavigationRail>(find.byType(NavigationRail)).destinations,
      hasLength(5),
    );
  });

  testWidgets('older addresses still land somewhere sensible', (tester) async {
    await pumpShell(tester);

    router.go('/trends');
    await tester.pumpAndSettle();
    expect(find.byType(SystemsPage), findsOneWidget);
    expect(selectedTab(tester), 3);

    router.go('/labs');
    await tester.pumpAndSettle();
    expect(find.byType(LabsPage), findsOneWidget);
    expect(selectedTab(tester), 3);

    router.go('/insights');
    await tester.pumpAndSettle();
    expect(find.byType(InsightsPage), findsOneWidget);
    expect(selectedTab(tester), 0);

    router.go('/you/goals');
    await tester.pumpAndSettle();
    expect(find.byType(GoalsPage), findsOneWidget);
    expect(selectedTab(tester), 4);

    router.go('/you/medicines');
    await tester.pumpAndSettle();
    expect(find.byType(MedicinesPage), findsOneWidget);
  });

  testWidgets('Systems opens Labs from its entry', (tester) async {
    await pumpShell(tester);
    router.go('/systems');
    await tester.pumpAndSettle();
    await tester.tap(find.text(SystemsPage.labsLine));
    await tester.pumpAndSettle();
    expect(find.byType(LabsPage), findsOneWidget);
  });
}
