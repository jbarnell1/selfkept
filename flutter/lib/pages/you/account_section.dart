import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../brand/brand.dart';
import '../../providers/auth_provider.dart';
import '../../theme/tokens.dart';
import '../../widgets/key_value_row.dart';
import '../../widgets/page_body.dart';
import '../../utils/open_url.dart';
import '../../widgets/surface_card.dart';

/// The signed-in account and signing out.
class AccountSection extends StatelessWidget {
  const AccountSection({super.key});

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
