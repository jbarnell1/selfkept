import '../models/trend.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// Baselined metrics and their daily series.
class TrendsService {
  final ApiClient _api;

  TrendsService(this._api);

  /// Metrics with at least seven days of data, in product order.
  Future<List<TrendMetric>> metrics({String? personId}) =>
      _api.get<List<TrendMetric>>(
        '/trends',
        queryParameters: {'personId': ?personId},
        fromJson: TrendMetric.listFromJson,
      );

  /// [days] is 28 or 90.
  Future<TrendSeries> series(
    String metric, {
    int days = 28,
    String? personId,
  }) => _api.get<TrendSeries>(
    '/trends/$metric',
    queryParameters: {'days': days, 'personId': ?personId},
    fromJson: (body) => TrendSeries.fromJson(asJsonMap(body)),
  );
}
