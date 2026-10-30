import 'package:flutter/material.dart';

import '../theme/tokens.dart';

/// The one card surface: 20 px radius, hairline border, no shadow.
class SurfaceCard extends StatelessWidget {
  final Widget child;
  final EdgeInsetsGeometry padding;
  final Color? color;
  final VoidCallback? onTap;

  const SurfaceCard({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(Space.x6),
    this.color,
    this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final body = Padding(padding: padding, child: child);
    return Card(
      color: color,
      child: onTap == null
          ? body
          : InkWell(onTap: onTap, borderRadius: Radii.cardRadius, child: body),
    );
  }
}
