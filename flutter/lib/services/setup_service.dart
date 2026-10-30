import '../models/setup.dart';
import '../models/user.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// The signed-in person's first-run answers.
class SetupService {
  final ApiClient _api;

  SetupService(this._api);

  /// Saves the answers; [completed] marks setup finished. Returns the account
  /// as the server now has it.
  Future<User> save({required List<SetupItem> plan, required bool completed}) =>
      _api.put<User>(
        '/me/setup',
        data: {
          'plan': [for (final item in plan) item.toJson()],
          'completed': completed,
        },
        fromJson: (body) => User.fromJson(asJsonMap(body)),
      );
}
