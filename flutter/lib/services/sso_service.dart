import '../config/app_config.dart';
import '../models/sso.dart';
import '../models/user.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// Provider sign-in endpoints (docs/sso-design.md).
class SsoService {
  final ApiClient _api;

  SsoService(this._api);

  Future<SsoInfo> info() => _api.get<SsoInfo>(
    '/auth/sso',
    fromJson: (body) => SsoInfo.fromJson(asJsonMap(body)),
  );

  /// Where the sign-in button sends the browser. The server answers with a
  /// redirect to the provider.
  Uri startUrl(String client) => Uri.parse(
    '${AppConfig.apiBaseFor(_api.serverUrl)}/auth/sso/start?client=$client',
  );

  /// Trades the code from the app link for a session and stores it.
  Future<AuthResponse> exchange(String code) async {
    final response = await _api.post<AuthResponse>(
      '/auth/sso/exchange',
      data: {'code': code},
      fromJson: (body) => AuthResponse.fromJson(asJsonMap(body)),
    );
    await _api.storeSession(response);
    return response;
  }

  Future<SsoLinkStatus> linkStatus() => _api.get<SsoLinkStatus>(
    '/auth/sso/link',
    fromJson: (body) => SsoLinkStatus.fromJson(asJsonMap(body)),
  );

  /// The provider address that ties the signed-in account to a login there.
  Future<Uri> linkStartUrl(String client) => _api.post<Uri>(
    '/auth/sso/link?client=$client',
    fromJson: (body) => Uri.parse(JsonReader(asJsonMap(body)).string('url')),
  );

  Future<void> unlink() => _api.delete<void>('/auth/sso/link');
}
