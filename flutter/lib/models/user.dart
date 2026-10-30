import '../utils/json_reader.dart';
import 'enums.dart';
import 'person.dart';
import 'setup.dart';

/// A signed-in account. Carries its person so the shell can greet by name
/// without a second request.
class User {
  final String id;
  final String email;
  final String displayName;
  final Role role;
  final String householdId;
  final Person person;

  /// What the person said they use in the first-run questions.
  final List<SetupItem> setupPlan;

  /// Null until the person finishes or skips the first-run setup.
  final DateTime? setupCompletedAt;

  const User({
    required this.id,
    required this.email,
    required this.displayName,
    required this.role,
    required this.householdId,
    required this.person,
    this.setupPlan = const [],
    this.setupCompletedAt,
  });

  bool get isAdmin => role == Role.admin;

  /// True until the first-run setup is finished or skipped.
  bool get setupPending => setupCompletedAt == null;

  factory User.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final personJson = r.object('person');
    return User(
      id: r.string('id'),
      email: r.string('email'),
      displayName: r.string('displayName'),
      role: Role.fromString(r.stringOrNull('role')),
      householdId: r.string('householdId'),
      person: personJson == null
          ? Person(id: '', displayName: r.string('displayName'))
          : Person.fromJson(personJson),
      setupPlan: r
          .strings('setupPlan')
          .map(SetupItem.fromString)
          .whereType<SetupItem>()
          .toList(),
      setupCompletedAt: r.dateTime('setupCompletedAt'),
    );
  }

  User copyWith({Person? person, String? displayName}) => User(
    id: id,
    email: email,
    displayName: displayName ?? this.displayName,
    role: role,
    householdId: householdId,
    person: person ?? this.person,
    setupPlan: setupPlan,
    setupCompletedAt: setupCompletedAt,
  );
}

/// Body of /auth/login, /auth/register and /auth/refresh.
class AuthResponse {
  final String accessToken;
  final DateTime? accessTokenExpiresAt;

  /// Present for mobile clients only; web receives an HttpOnly cookie instead.
  final String? refreshToken;
  final User user;

  const AuthResponse({
    required this.accessToken,
    this.accessTokenExpiresAt,
    this.refreshToken,
    required this.user,
  });

  factory AuthResponse.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final userJson = r.object('user');
    if (userJson == null) {
      throw const FormatException('AuthResponse is missing its user');
    }
    return AuthResponse(
      accessToken: r.string('accessToken'),
      accessTokenExpiresAt: r.dateTime('accessTokenExpiresAt'),
      refreshToken: r.stringOrNull('refreshToken'),
      user: User.fromJson(userJson),
    );
  }
}
