import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../theme/app_theme.dart';
import '../theme/tokens.dart';

/// Copies [text] and says so in a snack bar.
Future<void> copyToClipboard(
  BuildContext context,
  String text, {
  String message = 'Copied.',
}) async {
  final messenger = ScaffoldMessenger.of(context);
  await Clipboard.setData(ClipboardData(text: text));
  messenger.showSnackBar(SnackBar(content: Text(message)));
}

/// A value someone has to type or paste elsewhere (an address, a code), set in
/// the data face on a raised strip with a copy button.
class CopyableValue extends StatelessWidget {
  final String value;
  final String copyTooltip;
  final String copiedMessage;

  /// The size of the value; the data face is applied on top.
  final TextStyle? style;

  /// Lines under the value, for example when a code expires.
  final Widget? caption;

  /// More buttons after the copy button.
  final List<Widget> actions;

  const CopyableValue(
    this.value, {
    super.key,
    this.copyTooltip = 'Copy',
    this.copiedMessage = 'Copied.',
    this.style,
    this.caption,
    this.actions = const [],
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    return Container(
      padding: const EdgeInsets.fromLTRB(
        Space.x4,
        Space.x2,
        Space.x1,
        Space.x2,
      ),
      decoration: BoxDecoration(
        color: colors.surfaceRaised,
        borderRadius: Radii.controlRadius,
        border: Border.all(color: colors.hairline),
      ),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                SelectableText(
                  value,
                  style: AppTheme.numeric(
                    context,
                    base: style ?? theme.textTheme.bodyMedium,
                  ),
                ),
                ?caption,
              ],
            ),
          ),
          IconButton(
            tooltip: copyTooltip,
            onPressed: () =>
                copyToClipboard(context, value, message: copiedMessage),
            icon: const Icon(Icons.copy_outlined),
          ),
          ...actions,
        ],
      ),
    );
  }
}
