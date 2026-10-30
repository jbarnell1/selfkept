import 'package:flutter/material.dart';

import '../theme/tokens.dart';
import 'copyable_value.dart';

/// How to add this server to Open WebUI, in its own words and menu names
/// (checked against Open WebUI 0.12). Shown on the setup steps and on
/// Assistants, so there is one copy to keep current.
class OpenWebUiSteps extends StatelessWidget {
  /// The address Open WebUI connects to.
  final String mcpUrl;

  const OpenWebUiSteps({super.key, required this.mcpUrl});

  static const String adminStep =
      'In Open WebUI, an admin opens Settings, Admin, Integrations and adds '
      'an External Tool Server: type MCP (Streamable HTTP), this address, '
      'sign-in OAuth 2.1. Register Client, then Save.';
  static const String contextStep =
      'Give the model room for the tools: a context length of 16K or more '
      'under the model\'s Advanced Params.';
  static const String personStep =
      'Each person then turns it on in a chat (Integrations, Tools), signs in '
      'here once and picks what it may read.';

  @override
  Widget build(BuildContext context) {
    final small = Theme.of(context).textTheme.bodySmall;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Text(adminStep),
        const SizedBox(height: Space.x2),
        if (mcpUrl.isNotEmpty) CopyableValue(mcpUrl),
        const SizedBox(height: Space.x2),
        Text(contextStep, style: small),
        const SizedBox(height: Space.x1),
        Text(personStep, style: small),
      ],
    );
  }
}
