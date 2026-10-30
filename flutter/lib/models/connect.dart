import '../utils/json_reader.dart';

/// What an assistant remembers about a person.
enum MemoryType {
  goal,
  condition,
  medication,
  preference,
  lifeEvent,
  household;

  String get wireValue => switch (this) {
    goal => 'goal',
    condition => 'condition',
    medication => 'medication',
    preference => 'preference',
    lifeEvent => 'life_event',
    household => 'household',
  };

  String toJson() => wireValue;

  static MemoryType fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : preference;

  /// Accepts `life_event` and `lifeEvent`. Unknown values read as preference.
  static MemoryType fromString(String? value) {
    final key = value?.trim().toLowerCase().replaceAll('_', '');
    for (final t in values) {
      if (t.wireValue.replaceAll('_', '') == key) return t;
    }
    return preference;
  }

  String get displayName => switch (this) {
    goal => 'Goal',
    condition => 'Condition',
    medication => 'Medication',
    preference => 'Preference',
    lifeEvent => 'Life event',
    household => 'Household',
  };
}

/// An assistant that has been allowed to read this person's data.
class ConnectedAssistant {
  final String clientId;
  final String clientName;
  final String? clientUri;

  /// `dcr`, `cimd` or `configured`: how the assistant registered.
  final String kind;
  final DateTime? connectedAt;
  final DateTime? lastUsedAt;

  const ConnectedAssistant({
    required this.clientId,
    required this.clientName,
    this.clientUri,
    this.kind = 'dcr',
    this.connectedAt,
    this.lastUsedAt,
  });

  factory ConnectedAssistant.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return ConnectedAssistant(
      clientId: r.string('clientId'),
      clientName: r.string('clientName'),
      clientUri: r.stringOrNull('clientUri'),
      kind: r.string('kind', fallback: 'dcr'),
      connectedAt: r.dateTime('connectedAt'),
      lastUsedAt: r.dateTime('lastUsedAt'),
    );
  }
}

/// A personal access token. The secret itself is never listed.
class PersonalToken {
  final String id;
  final String name;

  /// The first characters of the secret, enough to recognise it.
  final String prefix;

  /// Space separated: `health:read` or `health:read memory:write`.
  final String scope;
  final DateTime? createdAt;
  final DateTime? lastUsedAt;

  const PersonalToken({
    required this.id,
    required this.name,
    required this.prefix,
    this.scope = 'health:read',
    this.createdAt,
    this.lastUsedAt,
  });

  /// True when the token may write notes for the assistant to remember.
  bool get canRemember => scope.split(' ').contains('memory:write');

  factory PersonalToken.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return PersonalToken(
      id: r.string('id'),
      name: r.string('name'),
      prefix: r.string('prefix'),
      scope: r.string('scope', fallback: 'health:read'),
      createdAt: r.dateTime('createdAt'),
      lastUsedAt: r.dateTime('lastUsedAt'),
    );
  }
}

/// Answer of GET /connect.
class Connections {
  final List<ConnectedAssistant> assistants;
  final List<PersonalToken> tokens;

  /// The address an assistant connects to.
  final String mcpUrl;

  const Connections({
    this.assistants = const [],
    this.tokens = const [],
    this.mcpUrl = '',
  });

  factory Connections.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return Connections(
      assistants: [
        for (final a in r.objects('assistants')) ConnectedAssistant.fromJson(a),
      ],
      tokens: [for (final t in r.objects('tokens')) PersonalToken.fromJson(t)],
      mcpUrl: r.string('mcpUrl'),
    );
  }
}

/// Answer of POST /connect/tokens. [secret] is shown once and not stored.
class CreatedToken {
  final PersonalToken token;
  final String secret;

  const CreatedToken({required this.token, required this.secret});

  factory CreatedToken.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return CreatedToken(
      token: PersonalToken.fromJson(r.object('token') ?? const {}),
      secret: r.string('secret'),
    );
  }
}

/// One thing an assistant was asked to remember.
class AiMemoryItem {
  final String id;
  final MemoryType type;
  final String key;
  final String value;
  final String? displayText;

  /// Which assistant or token wrote it.
  final String? source;
  final DateTime? createdAt;
  final DateTime? updatedAt;

  const AiMemoryItem({
    required this.id,
    this.type = MemoryType.preference,
    required this.key,
    required this.value,
    this.displayText,
    this.source,
    this.createdAt,
    this.updatedAt,
  });

  /// What to show: the written sentence, or `key: value`.
  String get text => displayText ?? '$key: $value';

  factory AiMemoryItem.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return AiMemoryItem(
      id: r.string('id'),
      type: MemoryType.fromString(r.stringOrNull('type')),
      key: r.string('key'),
      value: r.string('value'),
      displayText: r.stringOrNull('displayText'),
      source: r.stringOrNull('source'),
      createdAt: r.dateTime('createdAt'),
      updatedAt: r.dateTime('updatedAt'),
    );
  }

  static List<AiMemoryItem> listFromJson(dynamic body) => [
    for (final m in asJsonList(body)) AiMemoryItem.fromJson(m),
  ];
}

/// One permission an assistant is asking for.
class ConsentScope {
  final String name;
  final String description;

  const ConsentScope({required this.name, this.description = ''});

  factory ConsentScope.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return ConsentScope(
      name: r.string('name'),
      description: r.string('description'),
    );
  }
}

/// Answer of GET /oauth/request/{id}: who is asking and for what.
class ConsentRequest {
  final String requestId;
  final String clientName;
  final String? clientUri;
  final String? logoUri;

  /// Host the person returns to after answering.
  final String redirectHost;
  final List<ConsentScope> scopes;
  final DateTime? expiresAt;

  const ConsentRequest({
    required this.requestId,
    required this.clientName,
    this.clientUri,
    this.logoUri,
    this.redirectHost = '',
    this.scopes = const [],
    this.expiresAt,
  });

  factory ConsentRequest.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return ConsentRequest(
      requestId: r.string('requestId'),
      clientName: r.string('clientName'),
      clientUri: r.stringOrNull('clientUri'),
      logoUri: r.stringOrNull('logoUri'),
      redirectHost: r.string('redirectHost'),
      scopes: [for (final s in r.objects('scopes')) ConsentScope.fromJson(s)],
      expiresAt: r.dateTime('expiresAt'),
    );
  }
}

/// Answer of GET /oura.
class OuraStatus {
  /// False when the server has no Oura application credentials.
  final bool configured;
  final bool connected;
  final DateTime? connectedAt;
  final DateTime? lastPullAt;
  final String? lastError;
  final int sampleCount;

  const OuraStatus({
    this.configured = false,
    this.connected = false,
    this.connectedAt,
    this.lastPullAt,
    this.lastError,
    this.sampleCount = 0,
  });

  factory OuraStatus.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return OuraStatus(
      configured: r.boolean('configured'),
      connected: r.boolean('connected'),
      connectedAt: r.dateTime('connectedAt'),
      lastPullAt: r.dateTime('lastPullAt'),
      lastError: r.stringOrNull('lastError'),
      sampleCount: r.integer('sampleCount'),
    );
  }
}
