import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../pages/app_shell.dart';
import '../pages/connections_page.dart';
import '../pages/export_page.dart';
import '../pages/consent_page.dart';
import '../pages/insights_page.dart';
import '../pages/lab_entry_page.dart';
import '../pages/lab_marker_page.dart';
import '../pages/labs_page.dart';
import '../pages/public/join_page.dart';
import '../pages/public/login_page.dart';
import '../pages/public/setup_page.dart';
import '../pages/public/splash_page.dart';
import '../pages/sources_page.dart';
import '../pages/today_page.dart';
import '../pages/welcome/setup_steps_page.dart';
import '../pages/welcome/welcome_page.dart';
import '../pages/trends_page.dart';
import '../pages/you/you_page.dart';
import '../providers/auth_provider.dart';
import '../providers/startup_provider.dart';
import '../theme/tokens.dart';
import '../utils/return_path.dart';

/// Public paths an unauthenticated person may visit once startup is ready.
const Set<String> publicPaths = {'/login', '/join'};

/// Paths that must survive the splash and a sign-in: a person who lands on one
/// (an assistant's consent screen, or the page Oura sends the browser back to)
/// is returned to it, query included, once the app has started.
const Set<String> returnPaths = {'/connect', '/sources', '/welcome/steps'};

String _loginWith(String? from) =>
    from == null ? '/login' : '/login?from=${Uri.encodeQueryComponent(from)}';

/// The one redirect that decides where a location goes, written as a pure
/// function so it can be tested without a widget tree.
///
/// [location] is the full location including its query (`/connect?request=1`).
/// It defaults to [path].
///
/// Order: startup must finish first (everything goes to /splash until then);
/// an empty server shows /setup; signed-out people see /login or /join;
/// signed-in people never see the public pages, and land on /welcome while
/// their first-run setup is still open ([setupPending]). A `from` query value on
/// /login (or on /splash, while the app starts) carries the place to return
/// to after sign-in; only in-app locations starting with a slash are honoured.
String? resolveRedirect({
  required String path,
  required bool startupReady,
  required bool needsSetup,
  required bool authenticated,
  bool setupPending = false,
  String? location,
}) {
  final here = location ?? path;
  final from = path == '/login' || path == '/splash'
      ? safeReturnPath(Uri.tryParse(here)?.queryParameters['from'])
      : null;
  // Where the person was heading, if it is worth coming back to.
  final carried = returnPaths.contains(path) ? here : from;

  if (!startupReady) {
    if (path == '/splash') return null;
    return carried == null
        ? '/splash'
        : '/splash?from=${Uri.encodeQueryComponent(carried)}';
  }

  if (needsSetup && !authenticated) return path == '/setup' ? null : '/setup';

  if (!authenticated) {
    if (publicPaths.contains(path)) return null;
    return _loginWith(carried);
  }

  // Authenticated: leave the public, setup and splash pages behind. A place
  // worth returning to (an assistant's consent screen) still wins.
  if (publicPaths.contains(path) || path == '/setup' || path == '/splash') {
    return from ?? (setupPending ? '/welcome' : '/');
  }
  return null;
}

/// Relays change notifications from several notifiers to GoRouter.
class _Refresh extends ChangeNotifier {
  final List<Listenable> _sources;

  _Refresh(this._sources) {
    for (final s in _sources) {
      s.addListener(notifyListeners);
    }
  }

  @override
  void dispose() {
    for (final s in _sources) {
      s.removeListener(notifyListeners);
    }
    super.dispose();
  }
}

GoRouter buildRouter({
  required AuthProvider auth,
  required StartupProvider startup,
  String initialLocation = '/splash',
}) {
  return GoRouter(
    initialLocation: initialLocation,
    refreshListenable: _Refresh([auth, startup]),
    debugLogDiagnostics: false,
    redirect: (context, state) => resolveRedirect(
      path: state.uri.path,
      startupReady: startup.isReady,
      needsSetup: startup.needsSetup,
      authenticated: auth.isAuthenticated,
      setupPending: auth.user?.setupPending ?? false,
      location: state.uri.toString(),
    ),
    routes: [
      GoRoute(
        path: '/splash',
        pageBuilder: (context, state) => _fade(state, const SplashPage()),
      ),
      GoRoute(
        path: '/setup',
        pageBuilder: (context, state) => _fade(state, const SetupPage()),
      ),
      GoRoute(
        path: '/login',
        pageBuilder: (context, state) =>
            _fade(state, LoginPage(from: state.uri.queryParameters['from'])),
      ),
      // First-run setup: the questions, then one step per answer. Outside
      // the shell so nothing else competes for attention.
      GoRoute(
        path: '/welcome',
        pageBuilder: (context, state) => _fade(state, const WelcomePage()),
      ),
      GoRoute(
        path: '/welcome/steps',
        pageBuilder: (context, state) => _fade(
          state,
          SetupStepsPage(ouraResult: state.uri.queryParameters['oura']),
        ),
      ),
      GoRoute(
        path: '/join',
        pageBuilder: (context, state) => _fade(
          state,
          JoinPage(initialCode: state.uri.queryParameters['code']),
        ),
      ),
      // Outside the shell: it is reached from Today and You and has a back
      // button rather than a tab.
      GoRoute(
        path: '/sources',
        pageBuilder: (context, state) => _fade(
          state,
          SourcesPage(ouraResult: state.uri.queryParameters['oura']),
        ),
      ),
      GoRoute(
        path: '/export',
        pageBuilder: (context, state) => _fade(state, const ExportPage()),
      ),
      GoRoute(
        path: '/connections',
        pageBuilder: (context, state) => _fade(state, const ConnectionsPage()),
      ),
      // Where an assistant sends a person to say yes or no. Signed-out people
      // reach it through /login and come back.
      GoRoute(
        path: '/connect',
        pageBuilder: (context, state) => _fade(
          state,
          ConsentPage(requestId: state.uri.queryParameters['request']),
        ),
      ),
      // Also outside the shell: the scan and review flow, and one marker.
      GoRoute(
        path: '/labs/add',
        pageBuilder: (context, state) => _fade(state, const LabEntryPage()),
      ),
      GoRoute(
        path: '/labs/marker/:key',
        pageBuilder: (context, state) => _fade(
          state,
          LabMarkerPage(metricKey: state.pathParameters['key'] ?? ''),
        ),
      ),
      StatefulShellRoute.indexedStack(
        pageBuilder: (context, state, shell) =>
            _fade(state, AppShell(navigationShell: shell)),
        branches: [
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/',
                builder: (context, state) => const TodayPage(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/trends',
                builder: (context, state) => const TrendsPage(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/labs',
                builder: (context, state) => const LabsPage(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/insights',
                builder: (context, state) => const InsightsPage(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/you',
                builder: (context, state) => const YouPage(),
              ),
            ],
          ),
        ],
      ),
    ],
    errorBuilder: (context, state) => Scaffold(
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(Space.x8),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                'There is nothing at ${state.uri.path}.',
                style: Theme.of(context).textTheme.headlineSmall,
              ),
              const SizedBox(height: Space.x6),
              OutlinedButton(
                onPressed: () => context.go('/'),
                child: const Text('Go to Today'),
              ),
            ],
          ),
        ),
      ),
    ),
  );
}

/// A slow cross-fade between top-level pages.
CustomTransitionPage<void> _fade(GoRouterState state, Widget child) {
  return CustomTransitionPage<void>(
    key: state.pageKey,
    child: child,
    transitionDuration: Motion.standard,
    reverseTransitionDuration: Motion.short,
    transitionsBuilder: (context, animation, secondaryAnimation, child) {
      final curved = CurvedAnimation(parent: animation, curve: Motion.curve);
      return FadeTransition(opacity: curved, child: child);
    },
  );
}
