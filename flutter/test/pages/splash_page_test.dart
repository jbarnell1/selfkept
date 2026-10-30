import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/pages/public/splash_page.dart';
import 'package:selfkept/providers/startup_provider.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  setUp(() => h = TestHarness());
  tearDown(() => h.dispose());

  testBothThemes('loading: shows the wordmark and a spinner while checking', (
    tester,
    theme,
  ) async {
    // A handler that never completes keeps startup pending.
    h.api.on(
      'GET',
      '/system/version',
      (_) => Completer<Map<String, dynamic>>().future,
    );
    await tester.pumpWidget(h.wrap(const SplashPage(), theme: theme));
    await tester.pump();
    expect(find.text('Reaching your server'), findsOneWidget);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    expect(h.startup.phase, StartupPhase.pending);
  });

  testBothThemes(
    'success: a compatible server leaves the splash ready for the router',
    (tester, theme) async {
      h.api.on('GET', '/system/version', (_) => Fixtures.version());
      h.api.on('GET', '/system/setup-status', (_) => Fixtures.setupStatus());
      await tester.pumpWidget(h.wrap(const SplashPage(), theme: theme));
      await pumpFrames(tester);
      expect(h.startup.phase, StartupPhase.ready);
      expect(h.api.calls, contains('GET /system/setup-status'));
      // Still a calm loading surface: the router performs the redirect.
      expect(find.byType(CircularProgressIndicator), findsOneWidget);
    },
  );

  testBothThemes(
    'error: an unreachable server asks about the tailnet and offers the address field',
    (tester, theme) async {
      h.api.fail('GET', '/system/version', const ApiException.network());
      await tester.pumpWidget(h.wrap(const SplashPage(), theme: theme));
      await pumpFrames(tester);
      expect(find.text('Not reachable.'), findsOneWidget);
      expect(find.textContaining('Are you on the tailnet?'), findsOneWidget);
      expect(find.widgetWithText(TextField, 'Server address'), findsOneWidget);
      expect(find.text('Try again'), findsOneWidget);

      // Saving a new address re-runs the handshake against the new server.
      h.api.on('GET', '/system/version', (_) => Fixtures.version());
      await tester.enterText(find.byType(TextField), 'box.tailnet.ts.net');
      await tester.tap(find.text('Save address'));
      await pumpFrames(tester);
      expect(h.settings.currentUrl, 'http://box.tailnet.ts.net');
      expect(h.startup.phase, StartupPhase.ready);
    },
  );

  testWidgets('error: an old server explains what to update', (tester) async {
    h.api.on('GET', '/system/version', (_) => Fixtures.version(v: '0.0.1'));
    await tester.pumpWidget(h.wrap(const SplashPage()));
    await pumpFrames(tester);
    expect(find.text('Your server needs an update.'), findsOneWidget);
    expect(find.textContaining('0.0.1'), findsOneWidget);
  });

  testWidgets('error: a server wanting a newer app says so', (tester) async {
    h.api.on(
      'GET',
      '/system/version',
      (_) => {...Fixtures.version(), 'minClientVersion': '9.0.0'},
    );
    await tester.pumpWidget(h.wrap(const SplashPage()));
    await pumpFrames(tester);
    expect(find.text('This app needs an update.'), findsOneWidget);
    expect(find.textContaining('9.0.0'), findsOneWidget);
  });
}
