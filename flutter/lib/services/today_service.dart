import '../models/today.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// GET /today: the day's sentence, readiness and cards. `personId` absent
/// means the caller.
class TodayService {
  final ApiClient _api;

  TodayService(this._api);

  Future<Today> today({String? personId}) => _api.get<Today>(
    '/today',
    queryParameters: {'personId': ?personId},
    fromJson: (body) => Today.fromJson(asJsonMap(body)),
  );
}
