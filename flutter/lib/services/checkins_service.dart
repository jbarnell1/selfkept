import '../models/checkin.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// Mood check-ins.
class CheckinsService {
  final ApiClient _api;

  CheckinsService(this._api);

  Future<Checkin> create({required int mood, String? note, DateTime? at}) =>
      _api.post<Checkin>(
        '/checkins',
        data: Checkin.toCreateJson(mood: mood, note: note, at: at),
        fromJson: (body) => Checkin.fromJson(asJsonMap(body)),
      );

  /// Newest first, at most 500.
  Future<List<Checkin>> list({
    required DateTime from,
    required DateTime to,
    String? personId,
  }) => _api.get<List<Checkin>>(
    '/checkins',
    queryParameters: {
      'from': from.toUtc().toIso8601String(),
      'to': to.toUtc().toIso8601String(),
      'personId': ?personId,
    },
    fromJson: (body) => [for (final j in asJsonList(body)) Checkin.fromJson(j)],
  );

  /// Own check-ins only.
  Future<void> delete(String id) => _api.delete<void>('/checkins/$id');
}
