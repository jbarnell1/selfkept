import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';
import 'package:selfkept/services/reachability.dart';
import 'package:selfkept/widgets/reachability_banner.dart';

import '../helpers/pump.dart';

void main() {
  late Reachability reachability;

  setUp(() => reachability = Reachability(listenToInterface: false));
  tearDown(() => reachability.dispose());

  Widget app(ThemeData theme) => ChangeNotifierProvider.value(
    value: reachability,
    child: MaterialApp(
      theme: theme,
      home: const Scaffold(body: Column(children: [ReachabilityBanner()])),
    ),
  );

  testBothThemes('hidden while reachable, shown after a failed request', (
    tester,
    theme,
  ) async {
    await tester.pumpWidget(app(theme));
    expect(find.text(ReachabilityBanner.message), findsNothing);

    reachability.reportFailure();
    await tester.pumpAndSettle();
    expect(find.text(ReachabilityBanner.message), findsOneWidget);

    reachability.reportSuccess();
    await tester.pumpAndSettle();
    expect(find.text(ReachabilityBanner.message), findsNothing);
  });

  test('the interface going away counts as unreachable', () {
    expect(reachability.isReachable, isTrue);
    reachability.debugSetInterface(false);
    expect(reachability.isReachable, isFalse);
    reachability.debugSetInterface(true);
    expect(reachability.isReachable, isTrue);
  });

  test('a restored interface resets the server guess', () {
    reachability.reportFailure();
    reachability.debugSetInterface(false);
    reachability.debugSetInterface(true);
    expect(reachability.serverAnswering, isTrue);
  });
}
