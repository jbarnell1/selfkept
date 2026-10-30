import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

/// The bar above a page opened from a tab. When the page was opened
/// directly (a bookmark, a reload, a link) there is nothing to pop back to,
/// so the back button goes to [fallback], the tab the page belongs to,
/// instead of leaving the person with no way out.
class SubpageAppBar extends StatelessWidget implements PreferredSizeWidget {
  final String fallback;
  final Widget? title;

  const SubpageAppBar({super.key, required this.fallback, this.title});

  @override
  Size get preferredSize => const Size.fromHeight(kToolbarHeight);

  @override
  Widget build(BuildContext context) {
    final canPop = Navigator.of(context).canPop();
    return AppBar(
      title: title,
      leading: canPop
          ? null
          : BackButton(onPressed: () => GoRouter.of(context).go(fallback)),
    );
  }
}
