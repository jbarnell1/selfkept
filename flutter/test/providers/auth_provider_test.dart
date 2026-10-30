import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/person.dart';
import 'package:selfkept/providers/auth_provider.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';

void main() {
  late FakeApiClient api;
  late AuthProvider auth;
  late List<AuthStatus> seen;

  setUp(() {
    api = FakeApiClient();
    auth = AuthProvider(api);
    seen = [];
    auth.addListener(() => seen.add(auth.status));
  });

  tearDown(() => auth.dispose());

  group('bootstrap', () {
    test('starts initial and not bootstrapped', () {
      expect(auth.status, AuthStatus.initial);
      expect(auth.isBootstrapped, isFalse);
      expect(auth.isAuthenticated, isFalse);
    });

    test(
      'with no stored session settles unauthenticated without calling /auth/me',
      () async {
        api.restoreResult = false;
        await auth.bootstrap();
        expect(seen.first, AuthStatus.loading);
        expect(auth.status, AuthStatus.unauthenticated);
        expect(auth.isBootstrapped, isTrue);
        expect(api.calls, isEmpty);
      },
    );

    test(
      'with a stored session loads the user and settles authenticated',
      () async {
        api.restoreResult = true;
        api.on('GET', '/auth/me', (_) => Fixtures.user(pascal: true));
        await auth.bootstrap();
        expect(auth.status, AuthStatus.authenticated);
        expect(auth.user?.email, 'ada@example.test');
        expect(auth.isAdmin, isTrue);
        expect(seen, [
          AuthStatus.loading,
          AuthStatus.authenticated,
          AuthStatus.authenticated,
        ]);
      },
    );

    test('a 401 from /auth/me clears the dead session', () async {
      api.restoreResult = true;
      api.fail('GET', '/auth/me', const ApiException('nope', statusCode: 401));
      await auth.bootstrap();
      expect(auth.status, AuthStatus.unauthenticated);
      expect(api.clearCount, 1);
    });

    test('an unreachable server leaves the stored session alone', () async {
      api.restoreResult = true;
      api.fail('GET', '/auth/me', const ApiException.network());
      await auth.bootstrap();
      expect(auth.status, AuthStatus.unauthenticated);
      expect(api.clearCount, 0);
      expect(auth.isBootstrapped, isTrue);
    });
  });

  group('login', () {
    test('success stores the user and reports authenticated', () async {
      api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
      final ok = await auth.login(email: ' ada@example.test ', password: 'pw');
      expect(ok, isTrue);
      expect(auth.status, AuthStatus.authenticated);
      expect(auth.user?.id, 'u1');
      expect(api.storedSession?.accessToken, 'access-1');
      expect((api.bodies.single as Map)['email'], 'ada@example.test');
      expect(seen, [AuthStatus.loading, AuthStatus.authenticated]);
    });

    test(
      'failure moves to error with the server message, then clearError returns to unauthenticated',
      () async {
        api.fail(
          'POST',
          '/auth/login',
          const ApiException('Wrong email or password.', statusCode: 401),
        );
        final ok = await auth.login(email: 'a@b', password: 'x');
        expect(ok, isFalse);
        expect(auth.status, AuthStatus.error);
        expect(auth.errorMessage, 'Wrong email or password.');
        expect(auth.user, isNull);
        auth.clearError();
        expect(auth.status, AuthStatus.unauthenticated);
        expect(auth.errorMessage, isNull);
      },
    );

    test('network failure reads as a plain sentence', () async {
      api.fail('POST', '/auth/login', const ApiException.network());
      await auth.login(email: 'a@b', password: 'x');
      expect(auth.errorMessage, 'The server could not be reached.');
    });
  });

  group('register', () {
    test('sends the invite code only when given', () async {
      api.on('POST', '/auth/register', (_) => Fixtures.authResponse());
      await auth.register(
        email: 'a@b',
        password: 'longenough1',
        displayName: 'Ada',
      );
      expect((api.bodies.last as Map).containsKey('inviteCode'), isFalse);

      await auth.register(
        email: 'a@b',
        password: 'longenough1',
        displayName: 'Ada',
        inviteCode: ' CODE ',
      );
      expect((api.bodies.last as Map)['inviteCode'], 'CODE');
      expect(auth.isAuthenticated, isTrue);
    });
  });

  group('logout', () {
    test(
      'clears the session locally even when the server call fails',
      () async {
        api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
        await auth.login(email: 'a@b', password: 'x');
        api.fail('POST', '/auth/logout', const ApiException.network());
        await auth.logout();
        expect(auth.status, AuthStatus.unauthenticated);
        expect(auth.user, isNull);
        expect(api.clearCount, 1);
      },
    );
  });

  group('forced sign-out', () {
    test(
      'the client reporting an unauthorized session flips state and flags it once',
      () async {
        api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
        await auth.login(email: 'a@b', password: 'x');
        api.fireUnauthorized();
        expect(auth.status, AuthStatus.unauthenticated);
        expect(auth.consumeSessionExpired(), isTrue);
        expect(auth.consumeSessionExpired(), isFalse);
      },
    );

    test('is ignored while bootstrap is deciding', () async {
      api.fireUnauthorized();
      expect(auth.status, AuthStatus.initial);
      expect(auth.consumeSessionExpired(), isFalse);
    });
  });

  group('applyPerson', () {
    test('updates the signed-in user without a request', () async {
      api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
      await auth.login(email: 'a@b', password: 'x');
      final before = api.calls.length;
      auth.applyPerson(const Person(id: 'p1', displayName: 'Ada L.'));
      expect(auth.user?.person.displayName, 'Ada L.');
      expect(auth.user?.displayName, 'Ada L.');
      expect(api.calls.length, before);
    });
  });
}
