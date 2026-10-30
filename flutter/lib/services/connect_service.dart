import '../models/connect.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// Assistants, access tokens, what assistants remember, and the consent
/// screen an assistant sends a person to.
class ConnectService {
  final ApiClient _api;

  ConnectService(this._api);

  Future<Connections> connections() => _api.get<Connections>(
    '/connect',
    fromJson: (body) => Connections.fromJson(asJsonMap(body)),
  );

  /// The secret in the answer is shown once and never again.
  Future<CreatedToken> createToken(
    String name, {
    bool allowMemoryWrite = true,
  }) => _api.post<CreatedToken>(
    '/connect/tokens',
    data: {'name': name, 'allowMemoryWrite': allowMemoryWrite},
    fromJson: (body) => CreatedToken.fromJson(asJsonMap(body)),
  );

  Future<void> revokeToken(String id) =>
      _api.delete<void>('/connect/tokens/${Uri.encodeComponent(id)}');

  /// [clientId] can be an https URL, so it is encoded as one path segment.
  Future<void> revokeAssistant(String clientId) =>
      _api.delete<void>('/connect/assistants/${Uri.encodeComponent(clientId)}');

  Future<List<AiMemoryItem>> memory() => _api.get<List<AiMemoryItem>>(
    '/connect/memory',
    fromJson: AiMemoryItem.listFromJson,
  );

  Future<void> deleteMemory(String id) =>
      _api.delete<void>('/connect/memory/${Uri.encodeComponent(id)}');

  /// Who is asking and for what. A 404 means expired or already answered.
  Future<ConsentRequest> consent(String requestId) => _api.get<ConsentRequest>(
    '/oauth/request/${Uri.encodeComponent(requestId)}',
    fromJson: (body) => ConsentRequest.fromJson(asJsonMap(body)),
  );

  /// Answers the request. Returns the URL to send the browser to.
  Future<String> decide(String requestId, bool approve) => _api.post<String>(
    '/oauth/request/${Uri.encodeComponent(requestId)}/decision',
    data: {'approve': approve},
    fromJson: (body) => JsonReader(asJsonMap(body)).string('redirectUrl'),
  );
}
