import '../models/person.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// The signed-in person's own profile.
class ProfileService {
  final ApiClient _api;

  ProfileService(this._api);

  Future<Person> getProfile() => _api.get<Person>(
    '/me/profile',
    fromJson: (body) => Person.fromJson(asJsonMap(body)),
  );

  Future<Person> updateProfile(Person person) => _api.put<Person>(
    '/me/profile',
    data: person.toUpdateJson(),
    fromJson: (body) => Person.fromJson(asJsonMap(body)),
  );

  Future<void> changePassword({
    required String currentPassword,
    required String newPassword,
  }) => _api.put<void>(
    '/me/password',
    data: {'currentPassword': currentPassword, 'newPassword': newPassword},
  );
}
