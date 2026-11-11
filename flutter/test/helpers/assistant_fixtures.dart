import 'package:selfkept/widgets/assistant/assistant_feature.dart';

import 'fake_api_client.dart';
import 'fake_event_stream.dart';
import 'pump.dart';

/// Every key with its first letter raised, all the way down, the way a
/// PascalCase serializer would write it.
dynamic pascalCase(dynamic value) {
  if (value is Map) {
    return {
      for (final e in value.entries)
        (e.key as String).isEmpty
                ? e.key
                : '${(e.key as String)[0].toUpperCase()}${(e.key as String).substring(1)}':
            pascalCase(e.value),
    };
  }
  if (value is List) return [for (final v in value) pascalCase(v)];
  return value;
}

/// JSON the assistant routes answer with.
abstract final class AssistantFixtures {
  static Map<String, dynamic> status({
    bool enabled = true,
    bool reachable = true,
  }) => {
    'enabled': enabled,
    'model': enabled ? 'qwen3:8b' : null,
    'reachable': reachable,
  };

  static Map<String, dynamic> summary({
    String id = 'c1',
    String kind = 'chat',
    String title = 'Long run this morning',
    String? day,
  }) => {
    'id': id,
    'kind': kind,
    'title': title,
    'day': day,
    'createdAt': '2026-11-10T08:00:00Z',
    'updatedAt': '2026-11-10T08:05:00Z',
  };

  static Map<String, dynamic> message({
    String id = 'm1',
    String role = 'user',
    String content = 'Had salmon for lunch',
    String origin = 'code',
    List<Map<String, dynamic>> captures = const [],
    bool undone = false,
    bool redFlag = false,
  }) => {
    'id': id,
    'role': role,
    'content': content,
    'origin': origin,
    'createdAt': '2026-11-10T08:00:00Z',
    'captures': captures,
    'capturesUndone': undone,
    'redFlag': redFlag,
  };

  static Map<String, dynamic> conversation({
    String id = 'c1',
    String kind = 'chat',
    List<Map<String, dynamic>>? messages,
  }) => {
    ...summary(id: id, kind: kind),
    'messages':
        messages ??
        [
          message(
            captures: [
              {
                'type': 'journal',
                'id': 'j1',
                'kind': 'food',
                'tags': ['high_protein'],
              },
            ],
          ),
          message(
            id: 'm2',
            role: 'assistant',
            content: 'Saved to today\'s log.',
            origin: 'model',
          ),
        ],
  };

  static Map<String, dynamic> checkinToday({
    String status = 'not_started',
    String? conversationId,
    String? topic,
    List<String> missing = const ['eating', 'drinks', 'mood'],
    List<String> done = const [],
    List<String> skipped = const [],
  }) => {
    'day': '2026-11-10',
    'status': status,
    'conversationId': conversationId,
    'topic': topic,
    'question': topic == null ? null : 'How was your eating today?',
    'missing': missing,
    'done': done,
    'skipped': skipped,
  };

  static Map<String, dynamic> turn({
    String status = 'partial',
    String? topic = 'eating',
    String reply = 'Let\'s close the day. How was your eating today?',
    List<Map<String, dynamic>> captured = const [],
    int? mood,
    String? userMessageId,
    List<String> done = const [],
    List<String> remaining = const ['eating', 'drinks', 'mood'],
    bool redFlag = false,
  }) => {
    'conversationId': 'ck1',
    'day': '2026-11-10',
    'status': status,
    'topic': topic,
    'encouragement': null,
    'question': topic == null ? null : 'How was your eating today?',
    'reply': reply,
    'captured': captured,
    'mood': mood,
    'userMessageId': userMessageId,
    'assistantMessageId': 'a-${reply.length}',
    'done': done,
    'skipped': <String>[],
    'remaining': remaining,
    'redFlag': redFlag,
  };

  static Map<String, dynamic> brief({List<Map<String, dynamic>>? items}) => {
    'day': '2026-11-10',
    'text': 'Good morning, Ada. You slept 6 h 5 min last night.',
    'generated': false,
    'items':
        items ??
        [
          {
            'kind': 'sleep',
            'text': 'You slept 6 h 5 min last night, below your usual range.',
          },
          {'kind': 'targets', 'text': 'Yesterday you kept 2 of 3 targets.'},
        ],
    'computedAt': '2026-11-10T06:00:00Z',
  };

  static Map<String, dynamic> playbookEntry({
    String sourceUrl = 'https://doi.org/10.5665/sleep.4716',
    List<String> cautions = const [],
  }) => {
    'key': 'sleep_seven_hours',
    'topic': 'sleep',
    'title': 'Aim for seven hours',
    'text': 'Sleep researchers\' consensus for adults is 7 or more hours.',
    'evidenceGrade': 'consensus',
    'source': 'Watson 2015, Sleep 38(6):843-844',
    'sourceUrl': sourceUrl,
    'why': 'Last night was under 7 h.',
    'cautions': cautions,
  };

  static Map<String, dynamic> nudges({
    bool enabled = false,
    bool urlSet = true,
    bool tokenSet = false,
  }) => {
    'enabled': enabled,
    'urlSet': urlSet,
    'ntfyUrlMasked': urlSet ? 'https://ntfy.example.net/***' : null,
    'tokenSet': tokenSet,
    'eveningTime': '20:30',
    'timeZone': 'Europe/London',
    'lastSentOn': null,
  };

  static Map<String, dynamic> modelCheck() => {
    'enabled': true,
    'model': 'qwen3:8b',
    'passed': false,
    'passedCount': 5,
    'total': 6,
    'cases': [
      {
        'name': 'restates_a_number',
        'checks': 'uses a number from the brief',
        'passed': true,
        'detail': 'passed',
        'latencyMs': 812,
      },
      {
        'name': 'no_dose',
        'checks': 'gives no zinc dose',
        'passed': false,
        'detail': 'refused by the guard: dose unit',
        'latencyMs': 1022,
      },
    ],
    'checkedAt': '2026-11-10T09:00:00Z',
  };
}

/// JSON the cloud lookup routes answer with.
abstract final class LookupFixtures {
  static Map<String, dynamic> status({
    bool available = true,
    bool enabled = false,
    bool localModel = false,
  }) => {
    'available': available,
    'enabled': enabled,
    'provider': 'Anthropic',
    'model': 'claude-opus-5-5',
    'localModel': localModel,
  };

  static Map<String, dynamic> draft({
    String mode = 'anonymized',
    String? text,
    List<Map<String, dynamic>>? changes,
    bool reworded = false,
  }) => {
    'text':
        text ??
        (mode == 'detailed'
            ? 'Does magnesium glycinate help with sleep? About me: in my 40s, male. Sleep 6 h 5 min.'
            : 'Does magnesium glycinate help with sleep in adults in their 40s?'),
    'mode': mode,
    'changes':
        changes ??
        (mode == 'detailed'
            ? [
                {'kind': 'name', 'count': 1},
                {'kind': 'record', 'count': 1},
              ]
            : [
                {'kind': 'name', 'count': 1},
                {'kind': 'date', 'count': 2},
                {'kind': 'number', 'count': 3},
              ]),
    'reworded': reworded,
    'provider': 'Anthropic',
    'model': 'claude-opus-5-5',
  };

  static Map<String, dynamic> answer({
    String id = 'm-cloud',
    String? lookupId = 'lk1',
    List<Map<String, dynamic>>? sources,
  }) => {
    ...AssistantFixtures.message(
      id: id,
      role: 'assistant',
      content:
          'Small trials suggest magnesium may help sleep a little in older adults. The evidence is weak.',
      origin: 'cloud',
    ),
    'sources':
        sources ??
        [
          {
            'title': 'Magnesium and sleep, a review',
            'url': 'https://example.test/magnesium',
          },
          {'title': '', 'url': 'ftp://example.test/plain'},
        ],
    'lookupId': lookupId,
  };

  static Map<String, dynamic> applied() => AssistantFixtures.message(
    id: 'm-applied',
    role: 'assistant',
    content: 'Your sleep has been short this week, so this may be worth a try.',
    origin: 'model',
  );

  static Map<String, dynamic> done({bool withApplied = true}) => {
    'lookupId': 'lk1',
    'message': answer(),
    'applied': withApplied ? applied() : null,
  };

  static Map<String, dynamic> record({
    String id = 'lk1',
    String mode = 'anonymized',
    String status = 'answered',
    String text =
        'Does magnesium glycinate help with sleep in adults in their 40s?',
  }) => {
    'id': id,
    'conversationId': 'c1',
    'mode': mode,
    'status': status,
    'provider': 'Anthropic',
    'model': 'claude-opus-5-5',
    'text': text,
    'sentAt': '2026-11-10T03:10:00Z',
  };

  /// The chat done event with the lookup offer.
  static Map<String, dynamic> offeredDone({bool offered = true}) => {
    ...StreamFixtures.done(text: 'Magnesium is a common supplement.'),
    'lookup': {'offered': offered, 'reason': 'question'},
  };
}

/// Signs in and runs startup against a server that has (or lacks) the
/// assistant, and the cloud lookup when [lookup] is set.
Future<void> signInWithAssistant(
  TestHarness h, {
  bool feature = true,
  bool lookup = false,
  String role = 'admin',
}) async {
  h.api.on('GET', '/system/version', (_) {
    final v = Fixtures.version();
    v['features'] = [
      ...(v['features'] as List),
      if (feature) AssistantFeature.name,
      if (lookup) LookupFeature.name,
    ];
    return v;
  });
  h.api.on('GET', '/system/setup-status', (_) => Fixtures.setupStatus());
  h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse(role: role));
  await h.auth.login(email: 'a@b', password: 'x');
  await h.startup.run();
}

/// The frames of one ordinary streamed reply.
void streamReply(
  FakeEventStreamTransport streams, {
  List<Map<String, dynamic>> entries = const [],
  List<String> deltas = const ['Noted.'],
  String? doneText,
  String origin = 'model',
  bool redFlag = false,
}) {
  streams.frame('capture', StreamFixtures.capture(entries: entries));
  for (final d in deltas) {
    streams.frame('delta', {'text': d});
  }
  streams.frame(
    'done',
    StreamFixtures.done(
      text: doneText ?? deltas.join(),
      origin: origin,
      redFlag: redFlag,
    ),
  );
}
