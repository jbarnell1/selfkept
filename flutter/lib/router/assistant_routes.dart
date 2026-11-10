import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../pages/assistant/assistant_chat_page.dart';
import '../pages/assistant/assistant_checkin_page.dart';
import '../pages/assistant/playbook_page.dart';
import '../pages/you/assistant_memory_page.dart';
import '../pages/you/nudges_page.dart';

/// The assistant's pages. They sit outside the tab shell, like Sources and
/// Export: a chat needs the whole height for its composer, and each has a
/// back button to the tab it came from. Listed before the shell, so
/// `/assistant/...` and `/you/...` here match first.
List<RouteBase> assistantRoutes(
  Page<void> Function(GoRouterState state, Widget child) page,
) => [
  GoRoute(
    path: '/assistant/chat/:id',
    pageBuilder: (context, state) => page(
      state,
      AssistantChatPage(conversationId: state.pathParameters['id'] ?? ''),
    ),
  ),
  GoRoute(
    path: '/assistant/checkin',
    pageBuilder: (context, state) => page(state, const AssistantCheckinPage()),
  ),
  GoRoute(
    path: '/assistant/playbook',
    pageBuilder: (context, state) => page(state, const PlaybookPage()),
  ),
  GoRoute(
    path: '/you/assistant-memory',
    pageBuilder: (context, state) => page(state, const AssistantMemoryPage()),
  ),
  GoRoute(
    path: '/you/evening-reminder',
    pageBuilder: (context, state) => page(state, const NudgesPage()),
  ),
];
