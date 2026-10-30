import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Persists the in-app server address override. A phone on a tailnet reaches
/// the server by a Tailscale hostname that no build-time define can know, so
/// the person types it once on the setup screen and it is kept here.
abstract class ServerAddressStore {
  Future<String?> read();
  Future<void> write(String? url);
}

class SecureServerAddressStore implements ServerAddressStore {
  static const _key = 'server_url';
  final FlutterSecureStorage _secure;

  SecureServerAddressStore({FlutterSecureStorage? storage})
    : _secure = storage ?? const FlutterSecureStorage();

  @override
  Future<String?> read() async {
    try {
      final value = await _secure.read(key: _key);
      return (value == null || value.trim().isEmpty) ? null : value;
    } catch (_) {
      return null;
    }
  }

  @override
  Future<void> write(String? url) async {
    try {
      if (url == null || url.trim().isEmpty) {
        await _secure.delete(key: _key);
      } else {
        await _secure.write(key: _key, value: url.trim());
      }
    } catch (_) {
      // Storage unavailable: the override still applies for this session.
    }
  }
}

class MemoryServerAddressStore implements ServerAddressStore {
  String? value;
  MemoryServerAddressStore([this.value]);

  @override
  Future<String?> read() async => value;

  @override
  Future<void> write(String? url) async => value = url;
}
