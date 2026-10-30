import '../utils/json_reader.dart';

/// One thing a person said they use in the first-run questions. The setup
/// steps are built from these.
enum SetupItem {
  appleHealth,
  healthConnect,
  oura,
  labs,
  household,
  assistant;

  /// Wire value used by the API ("appleHealth").
  String get wireValue => name;
  String toJson() => wireValue;

  static SetupItem fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : labs;

  /// Null for a value a newer server knows and this client does not.
  static SetupItem? fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final item in values) {
      if (item.name.toLowerCase() == lower) return item;
    }
    return null;
  }

  String get displayName => switch (this) {
    appleHealth => 'iPhone or Apple Watch',
    healthConnect => 'Android phone, Pixel Watch or Fitbit',
    oura => 'Oura ring',
    labs => 'Blood test results',
    household => 'Someone else in the household',
    assistant => 'A private assistant',
  };

  /// One line under the name in the questions.
  String get description => switch (this) {
    appleHealth => 'Steps, sleep, heart rate and workouts from Apple Health.',
    healthConnect => 'The same, from Health Connect on the phone.',
    oura => 'Readiness, sleep stages, HRV and temperature.',
    labs => 'Rythm, a clinic or a lab report. Scan it and check the numbers.',
    household =>
      'They get their own account. Nothing is shared until each of you says so.',
    assistant =>
      'Ask about your record in Open WebUI, with a model you run at home.',
  };
}

/// Where an integration's credentials come from.
enum CredentialSource {
  none,
  environment,
  app;

  String get wireValue => name;
  String toJson() => wireValue;

  static CredentialSource fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : none;

  static CredentialSource fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final s in values) {
      if (s.name == lower) return s;
    }
    return none;
  }

  String get displayName => switch (this) {
    none => 'Not set up',
    environment => 'Set in the server environment',
    app => 'Set in the app',
  };
}

/// The server's Oura application, as the admin sees it. The secret never
/// comes back from the server.
class OuraApp {
  final bool configured;
  final CredentialSource source;
  final String? clientId;

  /// What the admin pastes into Oura's form. Null when the server does not
  /// know its own address.
  final String? redirectUri;
  final String developerUrl;

  const OuraApp({
    this.configured = false,
    this.source = CredentialSource.none,
    this.clientId,
    this.redirectUri,
    this.developerUrl = '',
  });

  /// True when the app may change it (the environment wins and is read-only).
  bool get editable => source != CredentialSource.environment;

  factory OuraApp.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return OuraApp(
      configured: r.boolean('configured'),
      source: CredentialSource.fromString(r.stringOrNull('source')),
      clientId: r.stringOrNull('clientId'),
      redirectUri: r.stringOrNull('redirectUri'),
      developerUrl: r.string('developerUrl'),
    );
  }
}
