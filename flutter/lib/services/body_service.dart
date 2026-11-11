import '../models/progress.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// Typed-in body readings: weight, body fat and waist.
class BodyService {
  final ApiClient _api;

  BodyService(this._api);

  /// [value] is metric: kg, percent or cm. [at] defaults to now on the
  /// server.
  Future<Measurement> addMeasurement(
    BodyMeasure measure,
    double value, {
    DateTime? at,
  }) => _api.post<Measurement>(
    '/body/measurements',
    data: {
      'metricKey': measure.wireValue,
      'value': value,
      'at': at?.toUtc().toIso8601String(),
    },
    fromJson: (body) => Measurement.fromJson(asJsonMap(body)),
  );

  Future<void> deleteMeasurement(BodyMeasure measure, String id) =>
      _api.delete<void>('/body/measurements/${measure.wireValue}/$id');
}
