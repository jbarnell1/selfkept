import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../providers/assistant_chat_provider.dart';
import '../../providers/assistant_provider.dart';
import '../../providers/checkins_provider.dart';
import '../../providers/record_changes.dart';
import '../../services/assistant_service.dart';
import '../../services/photo_picker.dart';
import '../../theme/tokens.dart';
import '../../widgets/assistant/chat_view.dart';
import '../../widgets/assistant/photo_cards.dart';
import '../../widgets/subpage_app_bar.dart';

/// One conversation. The provider lives as long as the page, so leaving
/// stops reading a reply that is still on its way.
class AssistantChatPage extends StatelessWidget {
  final String conversationId;

  /// The photo picker. Replaceable so a test can hand over a picture.
  final PhotoPicker picker;

  /// Whether the photo sheet offers the camera. Not on the web.
  final bool camera;

  const AssistantChatPage({
    super.key,
    required this.conversationId,
    this.picker = const ImagePhotoPicker(),
    this.camera = !kIsWeb,
  });

  static const String hint = 'Say how the day is going';
  static const String photoHint = 'Ask about the photo';

  @override
  Widget build(BuildContext context) {
    return ChangeNotifierProvider(
      create: (context) {
        context.read<AssistantProvider>().ensureStatus();
        return AssistantChatProvider(
          conversationId: conversationId,
          service: context.read<AssistantService>(),
          changes: context.read<RecordChanges>(),
          onMoodChanged: context.read<CheckinsProvider>().load,
        )..load();
      },
      child: _ChatScaffold(picker: picker, camera: camera),
    );
  }
}

class _ChatScaffold extends StatelessWidget {
  final PhotoPicker picker;
  final bool camera;

  const _ChatScaffold({required this.picker, required this.camera});

  @override
  Widget build(BuildContext context) {
    final chat = context.watch<AssistantChatProvider>();
    final quiet = context.select<AssistantProvider, bool>((a) => a.modelQuiet);
    final photos = context.select<AssistantProvider, bool>(
      (a) => a.status?.photos ?? false,
    );
    final asking = chat.pendingPhoto != null;
    final theme = Theme.of(context);
    return Scaffold(
      appBar: SubpageAppBar(
        fallback: '/assistant',
        title: Text(
          chat.title.isEmpty ? 'Conversation' : chat.title,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
        ),
      ),
      body: ChatView<AssistantChatProvider>(
        header: quiet ? const ModelQuietNote() : null,
        footer: chat.readOnly
            ? Padding(
                padding: const EdgeInsets.all(Space.x4),
                child: Text(
                  AssistantChatProvider.readOnlyNote,
                  style: theme.textTheme.bodySmall,
                ),
              )
            : ChatComposer(
                enabled: chat.canSend,
                hint: asking
                    ? AssistantChatPage.photoHint
                    : AssistantChatPage.hint,
                allowEmpty: asking,
                error: chat.sendError,
                onSend: chat.send,
                leading: photos
                    ? PhotoButton(
                        picker: picker,
                        camera: camera,
                        enabled: !chat.photoReading,
                      )
                    : null,
              ),
      ),
    );
  }
}
