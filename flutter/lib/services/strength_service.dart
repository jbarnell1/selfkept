import '../models/strength.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// The strength log: the top set of a lift on a day.
class StrengthService {
  final ApiClient _api;

  StrengthService(this._api);

  /// Sets grouped per lift. The server returns the last 120 days.
  Future<List<StrengthLift>> list({String? personId}) =>
      _api.get<List<StrengthLift>>(
        '/strength',
        queryParameters: {'personId': ?personId},
        fromJson: StrengthLift.listFromJson,
      );

  /// Saving the same lift on the same day replaces the earlier set.
  Future<StrengthSet> save(StrengthSetDraft draft) => _api.post<StrengthSet>(
    '/strength',
    data: draft.toJson(),
    fromJson: (body) => StrengthSet.fromJson(asJsonMap(body)),
  );

  Future<void> delete(String id) => _api.delete<void>('/strength/$id');
}
