import '../models/date_only.dart';
import '../models/weekly_review.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// The weekly review, Monday to Sunday.
class ReviewService {
  final ApiClient _api;

  ReviewService(this._api);

  /// Without [weekOf] the server picks the week: the one ending today on a
  /// Sunday, else the last complete one.
  Future<WeeklyReview> weekly({DateTime? weekOf, String? personId}) =>
      _api.get<WeeklyReview>(
        '/review/weekly',
        queryParameters: {
          'weekOf': ?(weekOf == null ? null : dateOnly(weekOf)),
          'personId': ?personId,
        },
        fromJson: (body) => WeeklyReview.fromJson(asJsonMap(body)),
      );
}
