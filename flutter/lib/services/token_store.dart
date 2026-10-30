import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Where auth material lives.
///
/// Mobile: the OS keychain or keystore, device-bound and excluded from cloud
/// backups. Web: the access token is kept in memory only (never in storage a
/// script could read) and the refresh token is an HttpOnly cookie the browser
/// attaches itself, so nothing is written here at all.
abstract class TokenStore {
  Future<String?> readAccessToken();
  Future<void> writeAccessToken(String token);
  Future<String?> readRefreshToken();
  Future<void> writeRefreshToken(String token);
  Future<void> clear();
}

/// Production store: secure storage on mobile, memory on web.
class SecureTokenStore implements TokenStore {
  static const _accessKey = 'access_token';
  static const _refreshKey = 'refresh_token';

  final FlutterSecureStorage _secure;
  final bool _isWeb;
  String? _memoryAccess;

  SecureTokenStore({FlutterSecureStorage? storage, bool? isWeb})
    : _secure =
          storage ??
          const FlutterSecureStorage(
            iOptions: IOSOptions(
              accessibility: KeychainAccessibility.first_unlock_this_device,
            ),
            aOptions: AndroidOptions(),
          ),
      _isWeb = isWeb ?? kIsWeb;

  @override
  Future<String?> readAccessToken() async {
    if (_isWeb) return _memoryAccess;
    return _guard(() => _secure.read(key: _accessKey));
  }

  @override
  Future<void> writeAccessToken(String token) async {
    if (_isWeb) {
      _memoryAccess = token;
      return;
    }
    await _guard(() => _secure.write(key: _accessKey, value: token));
  }

  @override
  Future<String?> readRefreshToken() async {
    if (_isWeb) return null;
    return _guard(() => _secure.read(key: _refreshKey));
  }

  @override
  Future<void> writeRefreshToken(String token) async {
    if (_isWeb) return;
    await _guard(() => _secure.write(key: _refreshKey, value: token));
  }

  @override
  Future<void> clear() async {
    _memoryAccess = null;
    if (_isWeb) return;
    await _guard(() => _secure.delete(key: _accessKey));
    await _guard(() => _secure.delete(key: _refreshKey));
  }

  /// Secure storage can throw when the platform channel is missing (tests,
  /// unsupported desktop). A missing token is the right answer in those cases.
  Future<T?> _guard<T>(Future<T?> Function() action) async {
    try {
      return await action();
    } catch (_) {
      return null;
    }
  }
}

/// In-memory store for tests.
class MemoryTokenStore implements TokenStore {
  String? access;
  String? refresh;

  MemoryTokenStore({this.access, this.refresh});

  @override
  Future<String?> readAccessToken() async => access;

  @override
  Future<void> writeAccessToken(String token) async => access = token;

  @override
  Future<String?> readRefreshToken() async => refresh;

  @override
  Future<void> writeRefreshToken(String token) async => refresh = token;

  @override
  Future<void> clear() async {
    access = null;
    refresh = null;
  }
}
