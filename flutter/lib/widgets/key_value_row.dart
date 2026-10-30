import 'package:flutter/material.dart';

import '../theme/app_theme.dart';
import '../theme/tokens.dart';

/// A small label beside its value, for detail lists (profile, server, sync).
class KeyValueRow extends StatelessWidget {
  final String label;
  final String value;

  /// Sets the value in the data face, for numbers, dates and addresses.
  final bool numeric;
  final double labelWidth;

  const KeyValueRow(
    this.label,
    this.value, {
    super.key,
    this.numeric = false,
    this.labelWidth = 120,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final base = theme.textTheme.bodyMedium;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: Space.x1),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: labelWidth,
            child: Text(label, style: theme.textTheme.bodySmall),
          ),
          Expanded(
            child: Text(
              value,
              style: numeric ? AppTheme.numeric(context, base: base) : base,
            ),
          ),
        ],
      ),
    );
  }
}
