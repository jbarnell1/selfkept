import 'package:flutter/foundation.dart';

import '../config/app_config.dart';
import '../services/api_client.dart';
import '../services/server_address_store.dart';

/// The server address in use and the override a person may type in.
///
/// Resolution: the stored override, else [AppConfig.defaultServerUrl].
class ServerSettings extends ChangeNotifier {
  final ServerAddressStore _store;
  final DioApiClient? _client;
  String? _override;

  ServerSettings({required this._store, this._client, String? initialOverride})
    : _override = _normalizeOrNull(initialOverride);

  /// Reads the stored override before the API client is built, so the first
  /// request already goes to the right place.
  static Future<String?> readStoredOverride(ServerAddressStore store) async =>
      _normalizeOrNull(await store.read());

  String? get overrideUrl => _override;
  bool get isOverridden => _override != null;
  String get defaultUrl => AppConfig.defaultServerUrl;
  String get currentUrl => _override ?? defaultUrl;

  /// Saves (or clears, with null or blank) the override and repoints the client.
  Future<void> setOverride(String? raw) async {
    final next = _normalizeOrNull(raw);
    if (next == _override) return;
    _override = next;
    await _store.write(next);
    await _client?.setServerUrl(currentUrl);
    notifyListeners();
  }

  static String? _normalizeOrNull(String? raw) {
    if (raw == null) return null;
    final value = AppConfig.normalizeServerUrl(raw);
    return value.isEmpty ? null : value;
  }
}
