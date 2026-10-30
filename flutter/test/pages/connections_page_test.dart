import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/pages/connections_page.dart';
import 'package:selfkept/widgets/open_webui_steps.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

/// Tall enough that every section is on screen at once.
void useTallSurface(WidgetTester tester) {
  tester.view.physicalSize = const Size(900, 2600);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
}

void main() {
  late TestHarness h;

  String? copied;

  setUp(() async {
    copied = null;
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(SystemChannels.platform, (call) async {
          if (call.method == 'Clipboard.setData') {
            copied = (call.arguments as Map)['text'] as String?;
          }
          return null;
        });
    h = TestHarness();
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
    h.api.on('GET', '/connect', (_) => Fixtures.connections());
    h.api.on('GET', '/connect/memory', (_) => [Fixtures.memoryItem()]);
  });
  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(SystemChannels.platform, null);
    h.dispose();
  });

  Finder inDialog(Finder finder) =>
      find.descendant(of: find.byType(AlertDialog), matching: finder);

  testWidgets('loading: a spinner until the lists arrive', (tester) async {
    useTallSurface(tester);
    h.api.on(
      'GET',
      '/connect',
      (_) => Completer<Map<String, dynamic>>().future,
    );
    await tester.pumpWidget(h.wrap(const ConnectionsPage()));
    await tester.pump();
    await tester.pump();
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    expect(find.text('Connect an assistant'), findsNothing);
  });

  testWidgets('error: explains and retries', (tester) async {
    useTallSurface(tester);
    h.api.fail(
      'GET',
      '/connect',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await tester.pumpWidget(h.wrap(const ConnectionsPage()));
    await tester.pumpAndSettle();
    expect(find.text('Assistants did not load.'), findsOneWidget);
    expect(find.text('The server had a problem.'), findsOneWidget);

    h.api.on('GET', '/connect', (_) => Fixtures.connections());
    await tester.tap(find.text('Try again'));
    await tester.pumpAndSettle();
    expect(find.text('Open WebUI'), findsOneWidget);
  });

  testBothThemes('data: assistant, token, memory and the address', (
    tester,
    theme,
  ) async {
    useTallSurface(tester);
    await tester.pumpWidget(h.wrap(const ConnectionsPage(), theme: theme));
    await tester.pumpAndSettle();

    expect(find.text('Connect an assistant'), findsOneWidget);
    expect(find.text(ConnectionsPage.intro), findsOneWidget);
    expect(find.text('https://box.tailnet.ts.net/mcp'), findsOneWidget);
    expect(find.text(OpenWebUiSteps.adminStep), findsOneWidget);

    expect(find.text('Connected assistants'), findsOneWidget);
    expect(find.text('Open WebUI'), findsOneWidget);
    expect(find.textContaining('Connected Oct 1, 2026'), findsOneWidget);
    expect(find.textContaining('Last used'), findsWidgets);

    expect(find.text('Access tokens'), findsOneWidget);
    expect(find.text(ConnectionsPage.tokensLine), findsOneWidget);
    expect(find.text('Kitchen tablet'), findsOneWidget);
    expect(find.text('pat_Ab12Cd'), findsOneWidget);
    expect(find.text('Can remember notes'), findsOneWidget);
    expect(find.text('Create token'), findsOneWidget);

    expect(find.text('What assistants remember'), findsOneWidget);
    expect(
      find.text('Aiming for seven and a half hours of sleep'),
      findsOneWidget,
    );
    expect(find.text('Goal'), findsOneWidget);
    expect(find.text('Remembered by Open WebUI'), findsOneWidget);
    expect(find.byTooltip('Forget'), findsOneWidget);
  });

  testWidgets('empty: calm sentences instead of lists', (tester) async {
    useTallSurface(tester);
    h.api.on(
      'GET',
      '/connect',
      (_) => Fixtures.connections(assistants: [], tokens: []),
    );
    h.api.on('GET', '/connect/memory', (_) => <Map<String, dynamic>>[]);
    await tester.pumpWidget(h.wrap(const ConnectionsPage()));
    await tester.pumpAndSettle();
    expect(find.text(ConnectionsPage.noAssistants), findsOneWidget);
    expect(find.text(ConnectionsPage.noMemory), findsOneWidget);
    expect(find.text('Revoke'), findsNothing);
  });

  testWidgets('memory without displayText reads key: value', (tester) async {
    useTallSurface(tester);
    h.api.on(
      'GET',
      '/connect/memory',
      (_) => [Fixtures.memoryItem(displayText: null, source: null)],
    );
    await tester.pumpWidget(h.wrap(const ConnectionsPage()));
    await tester.pumpAndSettle();
    expect(find.text('goal.sleep: seven and a half hours'), findsOneWidget);
    expect(find.textContaining('Remembered by'), findsNothing);
  });

  testWidgets('create token: shows the secret once in a dialog', (
    tester,
  ) async {
    useTallSurface(tester);
    h.api.on('POST', '/connect/tokens', (_) => Fixtures.createdToken());
    await tester.pumpWidget(h.wrap(const ConnectionsPage()));
    await tester.pumpAndSettle();

    await tester.tap(find.text('Create token'));
    await tester.pumpAndSettle();
    expect(find.text('Let it remember notes'), findsOneWidget);

    // A name is required.
    await tester.tap(find.widgetWithText(FilledButton, 'Create'));
    await tester.pumpAndSettle();
    expect(find.text('Give it a name.'), findsOneWidget);
    expect(h.api.calls, isNot(contains('POST /connect/tokens')));

    await tester.enterText(find.byType(TextFormField), 'Kitchen tablet');
    await tester.tap(find.byType(Switch));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Create'));
    await tester.pumpAndSettle();

    final i = h.api.calls.indexOf('POST /connect/tokens');
    expect(h.api.bodies[i], {
      'name': 'Kitchen tablet',
      'allowMemoryWrite': false,
    });
    expect(find.text('pat_Ab12CdEfGh34IjKl56MnOp78QrSt90'), findsOneWidget);
    expect(find.text(ConnectionsPage.secretNote), findsOneWidget);

    await tester.tap(find.byTooltip('Copy').last);
    await tester.pump();
    expect(find.text('Copied.'), findsOneWidget);

    await tester.tap(find.text('Done'));
    await tester.pumpAndSettle();
    expect(find.text('pat_Ab12CdEfGh34IjKl56MnOp78QrSt90'), findsNothing);
  });

  testWidgets('create token: a failure stays in the dialog', (tester) async {
    useTallSurface(tester);
    h.api.fail(
      'POST',
      '/connect/tokens',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await tester.pumpWidget(h.wrap(const ConnectionsPage()));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Create token'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextFormField), 'Tablet');
    await tester.tap(find.widgetWithText(FilledButton, 'Create'));
    await tester.pumpAndSettle();
    expect(inDialog(find.text('The server had a problem.')), findsOneWidget);
    expect(find.text('Your new token'), findsNothing);
  });

  testWidgets('revoke: Keep sends nothing, Revoke sends DELETE', (
    tester,
  ) async {
    useTallSurface(tester);
    h.api.on('DELETE', '/connect/tokens/t1', (_) => null);
    await tester.pumpWidget(h.wrap(const ConnectionsPage()));
    await tester.pumpAndSettle();

    await tester.tap(find.widgetWithText(TextButton, 'Revoke'));
    await tester.pumpAndSettle();
    expect(
      find.text('Revoke Kitchen tablet? Anything using it stops working.'),
      findsOneWidget,
    );
    await tester.tap(inDialog(find.text('Keep')));
    await tester.pumpAndSettle();
    expect(h.api.calls, isNot(contains('DELETE /connect/tokens/t1')));

    await tester.tap(find.widgetWithText(TextButton, 'Revoke'));
    await tester.pumpAndSettle();
    await tester.tap(inDialog(find.text('Revoke')));
    await tester.pumpAndSettle();
    expect(h.api.calls, contains('DELETE /connect/tokens/t1'));
  });

  testWidgets('disconnect: confirms, then deletes by encoded client id', (
    tester,
  ) async {
    useTallSurface(tester);
    final path =
        '/connect/assistants/${Uri.encodeComponent('https://webui.example.test/client.json')}';
    h.api.on('DELETE', path, (_) => null);
    await tester.pumpWidget(h.wrap(const ConnectionsPage()));
    await tester.pumpAndSettle();

    await tester.tap(find.widgetWithText(TextButton, 'Disconnect'));
    await tester.pumpAndSettle();
    expect(
      find.text(
        'Disconnect Open WebUI? It will need your permission again to read your data.',
      ),
      findsOneWidget,
    );
    await tester.tap(inDialog(find.text('Disconnect')));
    await tester.pumpAndSettle();
    expect(h.api.calls, contains('DELETE $path'));
  });

  testWidgets('forget: confirms, then deletes the memory', (tester) async {
    useTallSurface(tester);
    h.api.on('DELETE', '/connect/memory/m1', (_) => null);
    await tester.pumpWidget(h.wrap(const ConnectionsPage()));
    await tester.pumpAndSettle();

    await tester.tap(find.byTooltip('Forget'));
    await tester.pumpAndSettle();
    expect(find.text('Forget this?'), findsOneWidget);
    await tester.tap(inDialog(find.text('Forget')));
    await tester.pumpAndSettle();
    expect(h.api.calls, contains('DELETE /connect/memory/m1'));
  });

  testWidgets('a failed revoke shows a calm line and keeps the lists', (
    tester,
  ) async {
    useTallSurface(tester);
    h.api.fail(
      'DELETE',
      '/connect/tokens/t1',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await tester.pumpWidget(h.wrap(const ConnectionsPage()));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(TextButton, 'Revoke'));
    await tester.pumpAndSettle();
    await tester.tap(inDialog(find.text('Revoke')));
    await tester.pumpAndSettle();
    expect(find.text('The server had a problem.'), findsOneWidget);
    expect(find.text('Kitchen tablet'), findsOneWidget);
  });

  testWidgets('copy puts the address on the clipboard', (tester) async {
    useTallSurface(tester);
    await tester.pumpWidget(h.wrap(const ConnectionsPage()));
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('Copy'));
    await tester.pump();
    expect(copied, 'https://box.tailnet.ts.net/mcp');
    expect(find.text('Copied.'), findsOneWidget);
  });

  testWidgets('lays out at phone width without overflow', (tester) async {
    tester.view.physicalSize = const Size(390, 2600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(h.wrap(const ConnectionsPage()));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    expect(find.text('Open WebUI'), findsOneWidget);
  });
}
