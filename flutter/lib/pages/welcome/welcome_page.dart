import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../brand/brand.dart';
import '../../models/setup.dart';
import '../../providers/auth_provider.dart';
import '../../providers/setup_provider.dart';
import '../../theme/tokens.dart';
import '../../widgets/page_body.dart';
import '../../widgets/status_views.dart';
import '../../widgets/surface_card.dart';
import 'setup_icons.dart';

/// The first-run questions: what the person uses. The answers decide which
/// steps come next; nothing is set up for things they do not use.
class WelcomePage extends StatefulWidget {
  const WelcomePage({super.key});

  /// What to tick before the person does: the health store of the phone the
  /// app is running on. Nothing on web.
  static List<SetupItem> suggested({
    bool isWeb = kIsWeb,
    TargetPlatform? platform,
  }) {
    if (isWeb) return const [];
    return switch (platform ?? defaultTargetPlatform) {
      TargetPlatform.iOS => const [SetupItem.appleHealth],
      TargetPlatform.android => const [SetupItem.healthConnect],
      _ => const [],
    };
  }

  static const List<SetupItem> devices = [
    SetupItem.appleHealth,
    SetupItem.healthConnect,
    SetupItem.oura,
    SetupItem.labs,
  ];

  @override
  State<WelcomePage> createState() => _WelcomePageState();
}

class _WelcomePageState extends State<WelcomePage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      context.read<SetupProvider>().seed(suggested: WelcomePage.suggested());
    });
  }

  Future<void> _continue() async {
    final setup = context.read<SetupProvider>();
    final router = GoRouter.of(context);
    if (setup.chosen.isEmpty) {
      if (await setup.skip()) router.go('/');
      return;
    }
    if (await setup.saveAnswers()) router.go('/welcome/steps');
  }

  Future<void> _skip() async {
    final router = GoRouter.of(context);
    if (await context.read<SetupProvider>().skip()) router.go('/');
  }

  @override
  Widget build(BuildContext context) {
    final setup = context.watch<SetupProvider>();
    final auth = context.watch<AuthProvider>();
    final first = (auth.user?.displayName ?? '').trim().split(' ').first;
    final others = [if (auth.isAdmin) SetupItem.household, SetupItem.assistant];

    return Scaffold(
      body: SafeArea(
        child: PageBody(
          children: [
            PageHeading(
              title: first.isEmpty ? 'Welcome.' : 'Welcome, $first.',
              subtitle:
                  'A few questions so ${Brand.shortName} sets up only what you '
                  'use. You can change any of it later under You.',
            ),
            const SectionHeader(title: 'What do you wear or use?'),
            for (final item in WelcomePage.devices) _Choice(item: item),
            const SectionHeader(title: 'Anything else?'),
            for (final item in others) _Choice(item: item),
            AttentionLine(setup.error ?? ''),
            const SizedBox(height: Space.x6),
            Wrap(
              spacing: Space.x3,
              runSpacing: Space.x3,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                FilledButton(
                  onPressed: setup.saving ? null : _continue,
                  child: Text(setup.saving ? 'Saving' : 'Continue'),
                ),
                TextButton(
                  onPressed: setup.saving ? null : _skip,
                  child: const Text('Skip for now'),
                ),
              ],
            ),
            const SizedBox(height: Space.x8),
          ],
        ),
      ),
    );
  }
}

/// One answer: a card that ticks on and off.
class _Choice extends StatelessWidget {
  final SetupItem item;

  const _Choice({required this.item});

  @override
  Widget build(BuildContext context) {
    final setup = context.watch<SetupProvider>();
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final chosen = setup.isChosen(item);

    return Padding(
      padding: const EdgeInsets.only(bottom: Space.x3),
      child: MergeSemantics(
        child: SurfaceCard(
          key: ValueKey('choice-${item.wireValue}'),
          padding: const EdgeInsets.fromLTRB(
            Space.x4,
            Space.x3,
            Space.x3,
            Space.x3,
          ),
          color: chosen ? colors.band.withValues(alpha: 0.18) : null,
          onTap: () => setup.toggle(item),
          child: Row(
            children: [
              Icon(setupItemIcon(item), size: 22),
              const SizedBox(width: Space.x4),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(item.displayName, style: theme.textTheme.titleMedium),
                    const SizedBox(height: 2),
                    Text(item.description, style: theme.textTheme.bodySmall),
                  ],
                ),
              ),
              Checkbox(value: chosen, onChanged: (_) => setup.toggle(item)),
            ],
          ),
        ),
      ),
    );
  }
}
