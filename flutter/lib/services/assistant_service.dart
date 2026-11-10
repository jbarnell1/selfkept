import 'dart:convert';

import 'package:flutter/foundation.dart';

import '../models/assistant.dart';
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
}
