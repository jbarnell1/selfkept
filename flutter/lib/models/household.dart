import '../utils/json_reader.dart';
import 'enums.dart';

/// A household: the people who share one server.
class Household {
  final String id;
  final String name;
  final List<Member> members;

  const Household({
    required this.id,
    required this.name,
    this.members = const [],
  });

  factory Household.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return Household(
      id: r.string('id'),
      name: r.string('name'),
      members: [for (final m in r.objects('members')) Member.fromJson(m)],
    );
  }
}

/// One person in a household.
class Member {
  final String personId;
  final String userId;
  final String displayName;
  final Role role;
  final DateTime? joinedAt;

  const Member({
    required this.personId,
    required this.userId,
    required this.displayName,
    this.role = Role.member,
    this.joinedAt,
  });

  bool get isAdmin => role == Role.admin;

  factory Member.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return Member(
      personId: r.string('personId'),
      userId: r.string('userId'),
      displayName: r.string('displayName'),
      role: Role.fromString(r.stringOrNull('role')),
      joinedAt: r.dateTime('joinedAt'),
    );
  }
}

/// A one-time invite code. The server stores only a hash, so the plain code is
/// shown once when it is created.
class Invite {
  final String id;

  /// Plain code, present only on the create response.
  final String? code;
  final DateTime? expiresAt;
  final DateTime? createdAt;

  const Invite({this.id = '', this.code, this.expiresAt, this.createdAt});

  bool get isExpired {
    final at = expiresAt;
    return at != null && at.isBefore(DateTime.now().toUtc());
  }

  factory Invite.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return Invite(
      id: r.string('id'),
      code: r.stringOrNull('code'),
      expiresAt: r.dateTime('expiresAt'),
      createdAt: r.dateTime('createdAt'),
    );
  }
}
