import 'dart:async';
import 'dart:convert';

import 'package:flutter/foundation.dart';

import '../models/assistant.dart';
import '../models/assistant_lookup.dart';
import '../models/connect.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';
import 'sse/event_stream_client.dart';
import 'sse/sse_parser.dart';

/// The in-app assistant under /assistant. Everything is the caller's own:
/// there is no personId on any route.
class AssistantService {
  final ApiClient _api;
  final EventStreamClient _events;

  AssistantService(this._api, this._events);

  Future<AssistantStatus> status() => _api.get<AssistantStatus>(
    '/assistant/status',
    fromJson: (body) => AssistantStatus.fromJson(asJsonMap(body)),
  );

  /// Up to 50, most recently updated first.
  Future<List<ConversationSummary>> conversations() =>
      _api.get<List<ConversationSummary>>(
        '/assistant/conversations',
        fromJson: ConversationSummary.listFromJson,
      );

  Future<ConversationSummary> create({String? title}) =>
      _api.post<ConversationSummary>(
        '/assistant/conversations',
        data: {'title': ?title},
        fromJson: (body) => ConversationSummary.fromJson(asJsonMap(body)),
      );

  Future<Conversation> conversation(String id) => _api.get<Conversation>(
    '/assistant/conversations/$id',
    fromJson: (body) => Conversation.fromJson(asJsonMap(body)),
  );

  Future<void> deleteConversation(String id) =>
      _api.delete<void>('/assistant/conversations/$id');

  /// Sends a message and streams the reply: a capture, deltas, at most one
  /// replace, then done. Events the client does not know are skipped.
  Stream<AssistantStreamEvent> send(String conversationId, String text) =>
      _events
          .post(
            '/assistant/conversations/$conversationId/messages',
            data: {'text': text},
          )
          .map(eventFrom)
          .where((e) => e != null)
          .cast<AssistantStreamEvent>();

  /// The typed event for one frame, or null for one this client ignores.
  @visibleForTesting
  static AssistantStreamEvent? eventFrom(SseEvent frame) {
    final Map<String, dynamic> json;
    try {
      json = asJsonMap(jsonDecode(frame.data));
    } on FormatException {
      return null;
    }
    return switch (frame.event) {
      'capture' => CaptureEvent.fromJson(json),
      'delta' => DeltaEvent.fromJson(json),
      'replace' => ReplaceEvent.fromJson(json),
      'done' => DoneEvent.fromJson(json),
      _ => null,
    };
  }

  /// Undo: removes the entries, facts and mood one user message saved.
  Future<UndoResult> undo(String messageId) => _api.delete<UndoResult>(
    '/assistant/messages/$messageId/captures',
    fromJson: (body) => UndoResult.fromJson(asJsonMap(body)),
  );

  /// Starts, resumes, answers or leaves today's check-in. With neither an
  /// answer nor leave it starts or resumes.
  Future<CheckinTurn> checkin({String? answer, bool leave = false}) =>
      _api.post<CheckinTurn>(
        '/assistant/checkin',
        data: {'answer': ?answer, if (leave) 'leave': true},
        fromJson: (body) => CheckinTurn.fromJson(asJsonMap(body)),
      );

  Future<CheckinToday> checkinToday() => _api.get<CheckinToday>(
    '/assistant/checkin/today',
    fromJson: (body) => CheckinToday.fromJson(asJsonMap(body)),
  );

  Future<MorningBrief> brief() => _api.get<MorningBrief>(
    '/assistant/brief',
    fromJson: (body) => MorningBrief.fromJson(asJsonMap(body)),
  );

  /// Newest first, including facts an assistant wrote over /mcp.
  Future<List<AiMemoryItem>> memory() => _api.get<List<AiMemoryItem>>(
    '/assistant/memory',
    fromJson: AiMemoryItem.listFromJson,
  );

  Future<void> deleteMemory(String id) =>
      _api.delete<void>('/assistant/memory/$id');

  Future<List<PlaybookEntry>> playbook() => _api.get<List<PlaybookEntry>>(
    '/assistant/playbook',
    fromJson: PlaybookEntry.listFromJson,
  );

  Future<NudgeSettings> nudges() => _api.get<NudgeSettings>(
    '/assistant/nudges',
    fromJson: (body) => NudgeSettings.fromJson(asJsonMap(body)),
  );

  Future<NudgeSettings> saveNudges(NudgeUpdate update) =>
      _api.put<NudgeSettings>(
        '/assistant/nudges',
        data: update.toJson(),
        fromJson: (body) => NudgeSettings.fromJson(asJsonMap(body)),
      );

  Future<NudgeTestResult> testNudge() => _api.post<NudgeTestResult>(
    '/assistant/nudges/test',
    fromJson: (body) => NudgeTestResult.fromJson(asJsonMap(body)),
  );

  /// Admin only.
  Future<ModelCheck> modelCheck() => _api.post<ModelCheck>(
    '/assistant/model-check',
    fromJson: (body) => ModelCheck.fromJson(asJsonMap(body)),
  );

  // The cloud lookup (docs/lookup-design.md). Every route answers 404 when
  // the server switch is off; callers ask only when the server lists
  // `assistant.lookup`. Refusals come back as [LookupRefused].

  /// The person's own switch, and the provider and model the server uses.
  Future<LookupStatus> lookupStatus() => _lookup(
    LookupRoute.status,
    () => _api.get<LookupStatus>(
      '/assistant/lookup',
      fromJson: (body) => LookupStatus.fromJson(asJsonMap(body)),
    ),
  );

  /// Turning it on needs [acknowledged]; turning it off does not.
  Future<LookupStatus> saveLookup({
    required bool enabled,
    bool acknowledged = false,
  }) => _lookup(
    LookupRoute.settings,
    () => _api.put<LookupStatus>(
      '/assistant/lookup',
      data: {'enabled': enabled, 'acknowledged': acknowledged},
      fromJson: (body) => LookupStatus.fromJson(asJsonMap(body)),
    ),
  );

  /// Builds the outgoing text for one of the person's own messages. Nothing
  /// is sent and nothing is stored.
  Future<LookupDraft> lookupDraft(String messageId, LookupMode mode) => _lookup(
    LookupRoute.draft,
    () => _api.post<LookupDraft>(
      '/assistant/lookups/draft',
      data: {'messageId': messageId, 'mode': mode.wireValue},
      fromJson: (body) => LookupDraft.fromJson(asJsonMap(body)),
    ),
  );

  /// Sends [text], exactly as the person saw it, once. The answer streams:
  /// sent, waiting while the provider works, then done or failed.
  Stream<LookupStreamEvent> sendLookup({
    required String messageId,
    required LookupMode mode,
    required String text,
  }) => _events
      .post(
        '/assistant/lookups',
        data: {
          'messageId': messageId,
          'mode': mode.wireValue,
          'text': text,
          'acknowledged': true,
        },
      )
      .transform(
        StreamTransformer<SseEvent, SseEvent>.fromHandlers(
          handleError: (error, stack, sink) => sink.addError(
            LookupRefused.from(error, LookupRoute.send) ?? error,
            stack,
          ),
        ),
      )
      .map(lookupEventFrom)
      .where((e) => e != null)
      .cast<LookupStreamEvent>();

  /// The typed event for one lookup frame, or null for one this client
  /// ignores.
  @visibleForTesting
  static LookupStreamEvent? lookupEventFrom(SseEvent frame) {
    final Map<String, dynamic> json;
    try {
      final data = frame.data.trim();
      json = data.isEmpty ? const {} : asJsonMap(jsonDecode(data));
    } on FormatException {
      return null;
    }
    return switch (frame.event) {
      'sent' => LookupSentEvent.fromJson(json),
      'waiting' => const LookupWaitingEvent(),
      'done' => LookupDoneEvent.fromJson(json),
      'failed' => LookupFailedEvent.fromJson(json),
      _ => null,
    };
  }

  /// The person's own lookups, newest first, up to 100.
  Future<List<LookupRecord>> lookups() => _lookup(
    LookupRoute.history,
    () => _api.get<List<LookupRecord>>(
      '/assistant/lookups',
      fromJson: LookupRecord.listFromJson,
    ),
  );

  /// Deletes the record and its outgoing text. The answer stays in its
  /// conversation.
  Future<void> deleteLookup(String id) => _lookup(
    LookupRoute.delete,
    () => _api.delete<void>('/assistant/lookups/$id'),
  );

  static Future<T> _lookup<T>(
    LookupRoute route,
    Future<T> Function() call,
  ) async {
    try {
      return await call();
    } catch (error, stack) {
      final refused = LookupRefused.from(error, route);
      if (refused == null) rethrow;
      Error.throwWithStackTrace(refused, stack);
    }
  }
}
