import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../brand/brand.dart';
import '../../providers/server_settings.dart';
import '../../providers/startup_provider.dart';
import '../../theme/tokens.dart';
import '../../widgets/public_scaffold.dart';
import '../../widgets/server_address_field.dart';
import '../../widgets/status_views.dart';

/// First screen. Runs the version handshake and, when the server answers,
/// hands over to the router. When it does not, says why and offers the server
/// address field, because on a phone the usual cause is a missing tailnet.
class SplashPage extends StatefulWidget {
  const SplashPage({super.key});

  @override
  State<SplashPage> createState() => _SplashPageState();
}

class _SplashPageState extends State<SplashPage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final startup = context.read<StartupProvider>();
      if (startup.phase == StartupPhase.pending && !startup.isRunning) {
        startup.run();
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final startup = context.watch<StartupProvider>();
    final theme = Theme.of(context);

    switch (startup.phase) {
      case StartupPhase.pending:
      case StartupPhase.ready:
        return Scaffold(
          body: SafeArea(
            child: Center(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const BrandMark(align: TextAlign.center),
                  const SizedBox(height: Space.x8),
                  const LoadingView(message: 'Reaching your server'),
                ],
              ),
            ),
          ),
        );
      case StartupPhase.unreachable:
        return PublicScaffold(
          title: 'Not reachable.',
          lead:
              'Are you on the tailnet? The app asked '
              '${context.read<ServerSettings>().currentUrl} and got no answer.',
          children: [
            ServerAddressField(
              onSaved: () => context.read<StartupProvider>().run(),
            ),
            const SizedBox(height: Space.x6),
            OutlinedButton(
              onPressed: startup.isRunning ? null : () => startup.run(),
              child: const Text('Try again'),
            ),
          ],
        );
      case StartupPhase.serverNeedsUpdate:
        final result = startup.versionResult;
        return PublicScaffold(
          title: 'Your server needs an update.',
          lead:
              'This app needs server ${result?.requiredServerVersion ?? ''} or newer. '
              'The server reports ${result?.serverVersion ?? 'an older version'}. '
              'Pull the new image on the host, then try again.',
          children: [
            OutlinedButton(
              onPressed: startup.isRunning ? null : () => startup.run(),
              child: const Text('Try again'),
            ),
            const SizedBox(height: Space.x4),
            Text(
              'Help is at ${Brand.docsUrl}',
              style: theme.textTheme.bodySmall,
            ),
          ],
        );
      case StartupPhase.appNeedsUpdate:
        final result = startup.versionResult;
        return PublicScaffold(
          title: 'This app needs an update.',
          lead:
              'Your server wants app ${result?.requiredAppVersion ?? ''} or newer; '
              'this build is ${result?.appVersion ?? ''}. Update the app, then open it again.',
          children: [
            OutlinedButton(
              onPressed: startup.isRunning ? null : () => startup.run(),
              child: const Text('Check again'),
            ),
          ],
        );
    }
  }
}
