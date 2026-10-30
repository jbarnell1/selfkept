import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../brand/brand.dart';
import '../../providers/auth_provider.dart';
import '../../providers/sso_provider.dart';
import '../../theme/tokens.dart';
import '../../widgets/key_value_row.dart';
import '../../widgets/page_body.dart';
import '../../utils/open_url.dart';
import '../../widgets/status_views.dart';
import '../../widgets/surface_card.dart';

/// The signed-in account, its provider sign-in and signing out.
class AccountSection extends StatefulWidget {
  /// The `sso` query a web Connect trip lands back with.
  final String? ssoResult;

  const AccountSection({super.key, this.ssoResult});

  @override
  State<AccountSection> createState() => _AccountSectionState();
}

class _AccountSectionState extends State<AccountSection> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) async {
      if (!mounted) return;
      final sso = context.read<SsoProvider>();
      await sso.loadLink();
      sso.showReturn(widget.ssoResult);
    });
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader(title: 'Account'),
        SurfaceCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              KeyValueRow('Email', auth.user?.email ?? ''),
              KeyValueRow('Role', auth.user?.role.displayName ?? ''),
              const _SsoRow(),
              const SizedBox(height: Space.x4),
              Align(
                alignment: Alignment.centerLeft,
                child: OutlinedButton(
                  onPressed: auth.isLoading ? null : () => auth.logout(),
                  child: const Text('Sign out'),
                ),
              ),
              const SizedBox(height: Space.x2),
              Text(
                'Signing out forgets this session on this device only.',
                style: theme.textTheme.bodySmall,
              ),
            ],
          ),
        ),
      ],
    );
  }
}

/// Connect or disconnect the household's sign-in provider. Hidden when the
/// server has none set up.
class _SsoRow extends StatelessWidget {
  const _SsoRow();

  @override
  Widget build(BuildContext context) {
    final sso = context.watch<SsoProvider>();
    final link = sso.link;
    if (link == null || !link.info.enabled) return const SizedBox.shrink();
    final theme = Theme.of(context);
    final name = link.info.displayName;
    return Padding(
      padding: const EdgeInsets.only(top: Space.x4),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (link.linked) ...[
            KeyValueRow(
              'Sign-in',
              link.email == null ? name : '$name (${link.email})',
            ),
            TextButton(
              onPressed: sso.isBusy ? null : sso.disconnect,
              child: Text('Disconnect $name'),
            ),
          ] else ...[
            Text(
              'Connect $name to sign in with it instead of a password.',
              style: theme.textTheme.bodyMedium,
            ),
            const SizedBox(height: Space.x2),
            OutlinedButton(
              onPressed: sso.isBusy ? null : sso.connect,
              child: Text('Connect $name'),
            ),
          ],
          if (sso.notice != null)
            Padding(
              padding: const EdgeInsets.only(top: Space.x2),
              child: Text(sso.notice!, style: theme.textTheme.bodySmall),
            ),
          if (sso.error != null) AttentionLine(sso.error!),
        ],
      ),
    );
  }
}

/// Version, licence and links.
class AboutSection extends StatelessWidget {
  const AboutSection({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader(title: 'About'),
        SurfaceCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(Brand.productName, style: theme.textTheme.headlineMedium),
              const SizedBox(height: Space.x1),
              Text(Brand.tagline, style: theme.textTheme.bodyMedium),
              const SizedBox(height: Space.x4),
              Text(
                'Free software under the GNU Affero General Public License, version 3 (AGPL-3.0). '
                'Your data stays on your server.',
                style: theme.textTheme.bodySmall,
              ),
              const SizedBox(height: Space.x2),
              Text(
                'Copyright ${Brand.copyrightHolder}',
                style: theme.textTheme.bodySmall,
              ),
              const SizedBox(height: Space.x4),
              Wrap(
                spacing: Space.x2,
                children: [
                  TextButton(
                    onPressed: () => openLink(context, Brand.supportUrl),
                    child: const Text('Support'),
                  ),
                  TextButton(
                    onPressed: () => openLink(context, Brand.docsUrl),
                    child: const Text('Docs'),
                  ),
                  TextButton(
                    onPressed: () => openLink(context, Brand.privacyUrl),
                    child: const Text('Privacy'),
                  ),
                ],
              ),
            ],
          ),
        ),
      ],
    );
  }
}
