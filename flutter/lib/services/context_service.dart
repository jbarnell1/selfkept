import '../models/context.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// What is going on: illness, travel, a hard training block and the rest.
/// Reads take the person being viewed; writes are always the caller's own.
class ContextService {
  final ApiClient _api;

  ContextService(this._api);

  /// Events of the last 30 days and what counts today.
  Future<ContextList> list({String? personId}) => _api.get<ContextList>(
    '/context',
    queryParameters: {'personId': ?personId},
    fromJson: (body) => ContextList.fromJson(asJsonMap(body)),
  );

  Future<ContextEvent> create(ContextDraft draft) => _api.post<ContextEvent>(
    '/context',
    data: draft.toCreateJson(),
    fromJson: (body) => ContextEvent.fromJson(asJsonMap(body)),
  );

  /// Only the fields in [changes] change.
  Future<ContextEvent> update(String id, Map<String, dynamic> changes) =>
      _api.patch<ContextEvent>(
        '/context/$id',
        data: changes,
        fromJson: (body) => ContextEvent.fromJson(asJsonMap(body)),
      );

  Future<void> delete(String id) => _api.delete<void>('/context/$id');
}
