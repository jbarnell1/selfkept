import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../providers/assistant_checkin_provider.dart';
import '../../providers/checkins_provider.dart';
import '../../providers/record_changes.dart';
import '../../services/assistant_service.dart';
import '../../theme/tokens.dart';
import '../../widgets/assistant/chat_view.dart';
import '../../widgets/status_views.dart';
import '../../widgets/subpage_app_bar.dart';

/// Tonight's check-in, in the same chat view as a conversation. The server
/// asks only for what today's log is missing; the person can leave at any
/// point and keep what they said.
class AssistantCheckinPage extends StatelessWidget {
  const AssistantCheckinPage({super.key});

  static const String title = 'Evening check-in';
  static const String hint = 'Answer in a few words';
  static const String leaveLabel = 'Leave for now';
  static const String resumeLabel = 'Pick up again';
  static const String finishedTitle = 'That is today closed out.';
  static const String finishedLine =
      'Anything you add in a conversation still goes on today\'s log.';
  static const String backLabel = 'Back to Today';
  static const String pausedLine =
      'Everything you said is saved. Pick it up again whenever you like.';

  @override
  Widget build(BuildContext context) {
    return ChangeNotifierProvider(
      create: (context) => AssistantCheckinProvider(
        service: context.read<AssistantService>(),
        changes: context.read<RecordChanges>(),
        onMoodChanged: context.read<CheckinsProvider>().load,
      )..load(),
      child: const _CheckinScaffold(),
    );
  }
}

class _CheckinScaffold extends StatelessWidget {
  const _CheckinScaffold();

  @override
  Widget build(BuildContext context) {
    final checkin = context.watch<AssistantCheckinProvider>();
    return Scaffold(
      appBar: const SubpageAppBar(
        fallback: '/',
        title: Text(AssistantCheckinPage.title),
      ),
      body: ChatView<AssistantCheckinProvider>(
        footer: _footer(context, checkin),
      ),
    );
  }

  Widget _footer(BuildContext context, AssistantCheckinProvider checkin) {
    final theme = Theme.of(context);
    if (checkin.isFinished || checkin.isPaused) {
      final finished = checkin.isFinished;
      return SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.all(Space.x4),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (finished) ...[
                Text(
                  AssistantCheckinPage.finishedTitle,
                  style: theme.textTheme.titleMedium,
                ),
                const SizedBox(height: Space.x1),
                Text(
                  AssistantCheckinPage.finishedLine,
                  style: theme.textTheme.bodySmall,
                ),
              ] else
                Text(
                  AssistantCheckinPage.pausedLine,
                  style: theme.textTheme.bodySmall,
                ),
              const SizedBox(height: Space.x3),
              Wrap(
                spacing: Space.x3,
                runSpacing: Space.x2,
                children: [
                  if (!finished)
                    FilledButton(
                      onPressed: checkin.isBusy ? null : checkin.resume,
                      child: const Text(AssistantCheckinPage.resumeLabel),
                    ),
                  OutlinedButton(
                    onPressed: () => context.go('/'),
                    child: const Text(AssistantCheckinPage.backLabel),
                  ),
                ],
              ),
              if (checkin.sendError != null) AttentionLine(checkin.sendError!),
            ],
          ),
        ),
      );
    }
    return ChatComposer(
      enabled: checkin.canSend,
      hint: AssistantCheckinPage.hint,
      error: checkin.sendError,
      quickReplies: checkin.quickReplies,
      onSend: checkin.send,
      trailing: Align(
        alignment: Alignment.centerLeft,
        child: TextButton.icon(
          onPressed: checkin.isBusy || !checkin.hasLoaded
              ? null
              : checkin.leave,
          icon: const Icon(Icons.pause_circle_outline, size: 18),
          label: const Text(AssistantCheckinPage.leaveLabel),
        ),
      ),
    );
  }
}
