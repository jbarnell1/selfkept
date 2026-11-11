import '../models/lab.dart';
import '../models/lab_review.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// Reading panels against each other: the review of one panel, the
/// timeline, draw conditions and the readings tied to a marker.
class LabReviewService {
  final ApiClient _api;

  LabReviewService(this._api);

  /// Under the labs share of the report's owner.
  Future<LabReview> review(String reportId) => _api.get<LabReview>(
    '/labs/reports/$reportId/review',
    fromJson: (body) => LabReview.fromJson(asJsonMap(body)),
  );

  Future<LabTimeline> timeline({String? personId}) => _api.get<LabTimeline>(
    '/labs/timeline',
    queryParameters: {'personId': ?personId},
    fromJson: (body) => LabTimeline.fromJson(asJsonMap(body)),
  );

  /// Replaces every condition on one of the caller's own reports.
  Future<LabReport> updateConditions(
    String reportId,
    DrawConditions conditions,
  ) => _api.put<LabReport>(
    '/labs/reports/$reportId/conditions',
    data: conditions.toInputJson(),
    fromJson: (body) => LabReport.fromJson(asJsonMap(body)),
  );

  Future<RelatedReadings> related(String metricKey, {String? personId}) =>
      _api.get<RelatedReadings>(
        '/labs/markers/$metricKey/related',
        queryParameters: {'personId': ?personId},
        fromJson: (body) => RelatedReadings.fromJson(asJsonMap(body)),
      );
}
