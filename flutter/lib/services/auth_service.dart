import '../models/user.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// Account endpoints. Successful login and register store the session on the
/// client so the next request carries the bearer token.
class AuthService {
  final ApiClient _api;

  AuthService(this._api);

  Future<AuthResponse> login({
    required String email,
    required String password,
  }) async {
    final response = await _api.post<AuthResponse>(
      '/auth/login',
      data: {'email': email.trim(), 'password': password},
      fromJson: (body) => AuthResponse.fromJson(asJsonMap(body)),
    );
    await _api.storeSession(response);
    return response;
  }

  /// The first account on a server becomes admin and needs no invite code.
  /// Every later account needs one.
  Future<AuthResponse> register({
    required String email,
    required String password,
    required String displayName,
    String? inviteCode,
  }) async {
    final data = <String, dynamic>{
      'email': email.trim(),
      'password': password,
      'displayName': displayName.trim(),
    };
    final code = inviteCode?.trim();
    if (code != null && code.isNotEmpty) data['inviteCode'] = code;
    final response = await _api.post<AuthResponse>(
      '/auth/register',
      data: data,
      fromJson: (body) => AuthResponse.fromJson(asJsonMap(body)),
    );
    await _api.storeSession(response);
    return response;
  }

  Future<User> me() => _api.get<User>(
    '/auth/me',
    fromJson: (body) => User.fromJson(asJsonMap(body)),
  );

  /// Revokes the refresh token on the server, then forgets it locally. The
  /// local part always happens, even when the server is unreachable.
  Future<void> logout() async {
    try {
      await _api.post<void>('/auth/logout');
    } catch (_) {
      // Signing out locally must not depend on the server.
    } finally {
      await _api.clearSession();
    }
  }
}
