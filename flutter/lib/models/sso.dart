import '../utils/json_reader.dart';

/// Whether the server offers provider sign-in (Pocket ID and the like), and
/// the name for the button.
class SsoInfo {
  final bool enabled;
  final String? name;

  const SsoInfo({required this.enabled, this.name});

  static const SsoInfo off = SsoInfo(enabled: false);

  /// The name with a capital, for the start of a sentence.
  String get displayName {
    final n = name?.trim();
    if (n == null || n.isEmpty) return 'Single sign-on';
    return n[0].toUpperCase() + n.substring(1);
  }

  factory SsoInfo.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return SsoInfo(enabled: r.boolean('enabled'), name: r.stringOrNull('name'));
  }
}

/// The signed-in account's provider link.
class SsoLinkStatus {
  final SsoInfo info;
  final bool linked;
  final String? email;
  final DateTime? linkedAt;

  const SsoLinkStatus({
    required this.info,
    required this.linked,
    this.email,
    this.linkedAt,
  });

  factory SsoLinkStatus.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return SsoLinkStatus(
      info: SsoInfo.fromJson(json),
      linked: r.boolean('linked'),
      email: r.stringOrNull('email'),
      linkedAt: r.dateTime('linkedAt'),
    );
  }
}

/// The reason codes the server puts on a failed trip (`?sso=` on the web,
/// `?error=` on the app link), as sentences.
abstract final class SsoMessages {
  static const String linked = 'Connected. Next time, sign in with it.';

  static String forError(String code, SsoInfo info) {
    final name = info.displayName;
    return switch (code) {
      'disabled' => 'Single sign-on is not set up on this server.',
      'expired' => 'That took too long. Try again.',
      'cancelled' => 'Sign-in with $name was cancelled.',
      'not_linked' =>
        'No account here is connected to that $name login yet. Sign in with '
            'your password, then connect $name under You, Account.',
      'linked_elsewhere' =>
        'That $name login is already connected to another account.',
      _ => '$name did not answer. Check that it is running, then try again.',
    };
  }
}
