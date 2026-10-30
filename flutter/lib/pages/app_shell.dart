import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../theme/tokens.dart';

/// One tab of the authenticated shell.
class ShellTab {
  final String label;
  final String path;
  final IconData icon;
  final IconData selectedIcon;

  const ShellTab({
    required this.label,
    required this.path,
    required this.icon,
    required this.selectedIcon,
  });

  static const List<ShellTab> all = [
    ShellTab(
      label: 'Today',
      path: '/',
      icon: Icons.wb_sunny_outlined,
      selectedIcon: Icons.wb_sunny,
    ),
    ShellTab(
      label: 'Trends',
      path: '/trends',
      icon: Icons.show_chart_outlined,
      selectedIcon: Icons.show_chart,
    ),
    ShellTab(
      label: 'Labs',
      path: '/labs',
      icon: Icons.science_outlined,
      selectedIcon: Icons.science,
    ),
    ShellTab(
      label: 'Insights',
      path: '/insights',
      icon: Icons.auto_awesome_outlined,
      selectedIcon: Icons.auto_awesome,
    ),
    ShellTab(
      label: 'You',
      path: '/you',
      icon: Icons.person_outline,
      selectedIcon: Icons.person,
    ),
  ];
}

/// The authenticated frame: a bottom bar on narrow screens, a rail on wide
/// ones. The body is the StatefulShellRoute's navigation shell.
class AppShell extends StatelessWidget {
  final StatefulNavigationShell navigationShell;

  const AppShell({super.key, required this.navigationShell});

  /// Below this width the bottom bar is used.
  static const double railBreakpoint = 840;

  void _select(int index) {
    navigationShell.goBranch(
      index,
      initialLocation: index == navigationShell.currentIndex,
    );
  }

  @override
  Widget build(BuildContext context) {
    final width = MediaQuery.sizeOf(context).width;
    final wide = width >= railBreakpoint;
    final colors = AppColors.of(context);

    if (wide) {
      return Scaffold(
        body: Row(
          children: [
            NavigationRail(
              selectedIndex: navigationShell.currentIndex,
              onDestinationSelected: _select,
              extended: width >= 1200,
              minExtendedWidth: 200,
              leading: const SizedBox(height: Space.x4),
              destinations: [
                for (final tab in ShellTab.all)
                  NavigationRailDestination(
                    icon: Icon(tab.icon),
                    selectedIcon: Icon(tab.selectedIcon),
                    label: Text(tab.label),
                  ),
              ],
            ),
            VerticalDivider(width: 1, thickness: 1, color: colors.hairline),
            Expanded(child: navigationShell),
          ],
        ),
      );
    }

    return Scaffold(
      body: navigationShell,
      bottomNavigationBar: DecoratedBox(
        decoration: BoxDecoration(
          border: Border(top: BorderSide(color: colors.hairline)),
        ),
        child: NavigationBar(
          selectedIndex: navigationShell.currentIndex,
          onDestinationSelected: _select,
          animationDuration: Motion.standard,
          labelBehavior: NavigationDestinationLabelBehavior.alwaysShow,
          destinations: [
            for (final tab in ShellTab.all)
              NavigationDestination(
                icon: Icon(tab.icon),
                selectedIcon: Icon(tab.selectedIcon),
                label: tab.label,
              ),
          ],
        ),
      ),
    );
  }
}
