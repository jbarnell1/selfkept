import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/router/app_router.dart';

void main() {
  group('resolveRedirect', () {
    test('everything waits on /splash until startup is ready', () {
      for (final path in ['/', '/login', '/setup', '/you']) {
        expect(
          resolveRedirect(
            path: path,
            startupReady: false,
            needsSetup: false,
            authenticated: false,
          ),
          '/splash',
        );
      }
      expect(
        resolveRedirect(
          path: '/splash',
          startupReady: false,
          needsSetup: false,
          authenticated: false,
        ),
        isNull,
      );
    });

    test('an empty server shows /setup to everyone signed out', () {
      for (final path in ['/', '/login', '/join', '/splash']) {
        expect(
          resolveRedirect(
            path: path,
            startupReady: true,
            needsSetup: true,
            authenticated: false,
          ),
          '/setup',
        );
      }
      expect(
        resolveRedirect(
          path: '/setup',
          startupReady: true,
          needsSetup: true,
          authenticated: false,
        ),
        isNull,
      );
    });

    test('signed out people may visit /login and /join and nothing else', () {
      expect(
        resolveRedirect(
          path: '/login',
          startupReady: true,
          needsSetup: false,
          authenticated: false,
        ),
        isNull,
      );
      expect(
        resolveRedirect(
          path: '/join',
          startupReady: true,
          needsSetup: false,
          authenticated: false,
        ),
        isNull,
      );
      expect(
        resolveRedirect(
          path: '/',
          startupReady: true,
          needsSetup: false,
          authenticated: false,
        ),
        '/login',
      );
      expect(
        resolveRedirect(
          path: '/you',
          startupReady: true,
          needsSetup: false,
          authenticated: false,
        ),
        '/login',
      );
      expect(
        resolveRedirect(
          path: '/setup',
          startupReady: true,
          needsSetup: false,
          authenticated: false,
        ),
        '/login',
      );
    });

    test('signed in people leave the public pages for Today', () {
      for (final path in ['/login', '/join', '/setup', '/splash']) {
        expect(
          resolveRedirect(
            path: path,
            startupReady: true,
            needsSetup: false,
            authenticated: true,
          ),
          '/',
        );
      }
      expect(
        resolveRedirect(
          path: '/',
          startupReady: true,
          needsSetup: false,
          authenticated: true,
        ),
        isNull,
      );
      expect(
        resolveRedirect(
          path: '/you',
          startupReady: true,
          needsSetup: false,
          authenticated: true,
        ),
        isNull,
      );
    });

    test(
      'a stale needsSetup does not trap the first admin after registering',
      () {
        expect(
          resolveRedirect(
            path: '/setup',
            startupReady: true,
            needsSetup: true,
            authenticated: true,
          ),
          '/',
        );
      },
    );
    group('the from round trip', () {
      const connect = '/connect?request=abc';
      const login = '/login?from=%2Fconnect%3Frequest%3Dabc';

      test('signed out on /connect goes to /login with from', () {
        expect(
          resolveRedirect(
            path: '/connect',
            location: connect,
            startupReady: true,
            needsSetup: false,
            authenticated: false,
          ),
          login,
        );
      });

      test('signed in on /login with from goes back to it', () {
        expect(
          resolveRedirect(
            path: '/login',
            location: login,
            startupReady: true,
            needsSetup: false,
            authenticated: true,
          ),
          connect,
        );
      });

      test('signed out on /login with from stays put', () {
        expect(
          resolveRedirect(
            path: '/login',
            location: login,
            startupReady: true,
            needsSetup: false,
            authenticated: false,
          ),
          isNull,
        );
      });

      test('signed in on /connect is left alone', () {
        expect(
          resolveRedirect(
            path: '/connect',
            location: connect,
            startupReady: true,
            needsSetup: false,
            authenticated: true,
          ),
          isNull,
        );
      });

      test('a from that does not start with a slash is ignored', () {
        for (final from in [
          'https%3A%2F%2Fevil.example',
          'javascript%3Aalert(1)',
          '%2F%2Fevil.example',
        ]) {
          expect(
            resolveRedirect(
              path: '/login',
              location: '/login?from=$from',
              startupReady: true,
              needsSetup: false,
              authenticated: true,
            ),
            '/',
            reason: from,
          );
        }
      });

      test('other signed out locations still go to a plain /login', () {
        expect(
          resolveRedirect(
            path: '/you',
            location: '/you?tab=1',
            startupReady: true,
            needsSetup: false,
            authenticated: false,
          ),
          '/login',
        );
      });

      test('the place to return to survives the startup wait', () {
        final splash = resolveRedirect(
          path: '/connect',
          location: connect,
          startupReady: false,
          needsSetup: false,
          authenticated: false,
        );
        expect(splash, '/splash?from=%2Fconnect%3Frequest%3Dabc');

        // Startup finishes: signed out goes to login, signed in goes there.
        expect(
          resolveRedirect(
            path: '/splash',
            location: splash,
            startupReady: true,
            needsSetup: false,
            authenticated: false,
          ),
          login,
        );
        expect(
          resolveRedirect(
            path: '/splash',
            location: splash,
            startupReady: true,
            needsSetup: false,
            authenticated: true,
          ),
          connect,
        );
        // Still waiting: stay on /splash.
        expect(
          resolveRedirect(
            path: '/splash',
            location: splash,
            startupReady: false,
            needsSetup: false,
            authenticated: false,
          ),
          isNull,
        );
      });
    });
  });

  group('first-run setup', () {
    test('signing in or registering with setup open lands on /welcome', () {
      for (final path in ['/login', '/join', '/setup', '/splash']) {
        expect(
          resolveRedirect(
            path: path,
            startupReady: true,
            needsSetup: false,
            authenticated: true,
            setupPending: true,
          ),
          '/welcome',
          reason: path,
        );
      }
    });

    test('a place worth returning to still wins over /welcome', () {
      expect(
        resolveRedirect(
          path: '/login',
          location: '/login?from=%2Fconnect%3Frequest%3Dabc',
          startupReady: true,
          needsSetup: false,
          authenticated: true,
          setupPending: true,
        ),
        '/connect?request=abc',
      );
    });

    test('inside the app nothing is forced, so steps can link out', () {
      for (final path in ['/', '/labs/add', '/connections', '/welcome/steps']) {
        expect(
          resolveRedirect(
            path: path,
            startupReady: true,
            needsSetup: false,
            authenticated: true,
            setupPending: true,
          ),
          isNull,
          reason: path,
        );
      }
    });

    test('signed out, /welcome is not public', () {
      expect(
        resolveRedirect(
          path: '/welcome',
          startupReady: true,
          needsSetup: false,
          authenticated: false,
        ),
        '/login',
      );
    });
  });

  group('coming back from Oura', () {
    test('the steps page and its result survive the splash', () {
      const back = '/welcome/steps?oura=connected';
      final splash = resolveRedirect(
        path: '/welcome/steps',
        location: back,
        startupReady: false,
        needsSetup: false,
        authenticated: false,
      );
      expect(splash, '/splash?from=%2Fwelcome%2Fsteps%3Foura%3Dconnected');
      expect(
        resolveRedirect(
          path: '/splash',
          location: splash,
          startupReady: true,
          needsSetup: false,
          authenticated: true,
          setupPending: true,
        ),
        back,
      );
    });

    test('so does Sources', () {
      expect(
        resolveRedirect(
          path: '/splash',
          location: '/splash?from=%2Fsources%3Foura%3Derror',
          startupReady: true,
          needsSetup: false,
          authenticated: true,
        ),
        '/sources?oura=error',
      );
    });
  });
}
