import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/assistant_lookup.dart';
import '../../providers/assistant_lookup_providers.dart';
import '../../services/assistant_service.dart';
import '../../theme/tokens.dart';
import '../status_views.dart';

/// What the sheet brings back when the answer arrived: the done event and
/// the provider's name the draft gave.
class LookupSheetResult {
  final LookupDoneEvent done;
  final String? provider;

  const LookupSheetResult(this.done, {this.provider});
}

/// Opens the lookup sheet for one user message. Opening it sends nothing;
/// only Send does, once.
Future<LookupSheetResult?> showLookupSheet(
  BuildContext context, {
  required String messageId,
  String? provider,
}) => showModalBottomSheet<LookupSheetResult>(
  context: context,
  isScrollControlled: true,
  useSafeArea: true,
  enableDrag: false,
  isDismissible: false,
  builder: (_) => LookupSheet(messageId: messageId, provider: provider),
);

/// The lookup screen (docs/lookup-design.md, "The lookup screen"): a warning
/// band that never scrolls away, the mode, what was changed, the exact text
/// in a box the person can edit, and Send in the warning colour.
class LookupSheet extends StatelessWidget {
  final String messageId;
  final String? provider;

  const LookupSheet({super.key, required this.messageId, this.provider});

  static const String title = 'Look this up with a cloud model';
  static const String changesTitle = 'What was changed';
  static const String noChanges = 'Nothing needed changing.';
  static const String rewordedLine =
      'Your home model reworded it as a general question.';
  static const String textLabel = 'The exact text that will be sent';
  static const String scrubberLine =
      'Read it before you send it. Only names, contact details, dates and '
      'numbers are caught for you.';
  static const String detailsLine =
      'This includes readings and medicines from your record.';
  static const String cancelLabel = 'Cancel';
  static const String closeLabel = 'Close';
  static const String sendingLine = 'Sending...';
  static const String closeNote =
      'It has already gone. Closing this only stops waiting here.';
  static const String retryLabel = 'Try again';

  /// Used only until the server has named the provider.
  static const String unnamedProvider = 'the cloud provider';

  static String warning(String provider) =>
      'This leaves your server. The text below goes to $provider over the '
      'internet. Once it is sent it cannot be taken back.';
  static String accountLine(String provider) =>
      '$provider can tell the request came from this server\'s account, '
      'whatever the text says.';
  static String sendLabel(String provider) => 'Send to $provider';
  static String waitingLine(String provider) =>
      'Sent. Waiting for $provider...';

  static String modeLine(LookupMode mode) => switch (mode) {
    LookupMode.anonymized =>
      'Your question only. Names, contact details and dates are taken out '
          'and numbers are rounded.',
    LookupMode.detailed =>
      'Your question as you wrote it, minus names and contact details, with '
          'an About me block from your record.',
  };

  /// "This still contains a name, so it was not sent."
  static String findingsLine(List<LookupChange> findings) {
    final parts = [for (final f in findings) f.foundLabel];
    final String list;
    if (parts.length <= 1) {
      list = parts.isEmpty ? 'a name or contact detail' : parts.single;
    } else {
      list =
          '${parts.sublist(0, parts.length - 1).join(', ')} and ${parts.last}';
    }
    return 'This still contains $list, so it was not sent. Take it out, '
        'then send again.';
  }

  @override
  Widget build(BuildContext context) {
    return ChangeNotifierProvider(
      create: (context) => LookupSheetProvider(
        service: context.read<AssistantService>(),
        messageId: messageId,
        provider: provider,
      )..start(),
      child: const _LookupSheetBody(),
    );
  }
}

class _LookupSheetBody extends StatefulWidget {
  const _LookupSheetBody();

  @override
  State<_LookupSheetBody> createState() => _LookupSheetBodyState();
}

class _LookupSheetBodyState extends State<_LookupSheetBody> {
  final TextEditingController _text = TextEditingController();
  late final LookupSheetProvider _sheet;
  int _seenDraft = 0;

  @override
  void initState() {
    super.initState();
    _sheet = context.read<LookupSheetProvider>();
    _sheet.addListener(_syncText);
  }

  /// A new draft replaces what is in the box.
  void _syncText() {
    if (_sheet.draftVersion == _seenDraft) return;
    _seenDraft = _sheet.draftVersion;
    _text.value = TextEditingValue(
      text: _sheet.text,
      selection: TextSelection.collapsed(offset: _sheet.text.length),
    );
  }

  @override
  void dispose() {
    _sheet.removeListener(_syncText);
    _text.dispose();
    super.dispose();
  }

  Future<void> _send() async {
    final navigator = Navigator.of(context);
    final done = await _sheet.send();
    if (done == null || !mounted) return;
    navigator.pop(LookupSheetResult(done, provider: _sheet.provider));
  }

  @override
  Widget build(BuildContext context) {
    final sheet = context.watch<LookupSheetProvider>();
    final provider = sheet.provider ?? LookupSheet.unnamedProvider;
    final insets = MediaQuery.viewInsetsOf(context).bottom;
    return PopScope(
      canPop: !sheet.isBusy,
      child: FractionallySizedBox(
        heightFactor: 1,
        child: Padding(
          padding: EdgeInsets.only(bottom: insets),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              LookupWarningBand(
                provider: provider,
                detailed: sheet.mode == LookupMode.detailed,
              ),
              Expanded(
                child: Align(
                  alignment: Alignment.topCenter,
                  child: ConstrainedBox(
                    constraints: const BoxConstraints(
                      maxWidth: Space.maxContentWidth,
                    ),
                    child: ListView(
                      padding: const EdgeInsets.fromLTRB(
                        Space.x4,
                        Space.x4,
                        Space.x4,
                        Space.x6,
                      ),
                      children: _content(context, sheet, provider),
                    ),
                  ),
                ),
              ),
              _Footer(provider: provider, onSend: _send),
            ],
          ),
        ),
      ),
    );
  }

  List<Widget> _content(
    BuildContext context,
    LookupSheetProvider sheet,
    String provider,
  ) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final heading = Text(LookupSheet.title, style: theme.textTheme.titleLarge);
    switch (sheet.phase) {
      case LookupPhase.drafting:
        return [heading, const SizedBox(height: Space.x6), const LoadingView()];
      case LookupPhase.unavailable:
        final refusal = sheet.refusal;
        return [
          heading,
          const SizedBox(height: Space.x4),
          Text(
            refusal != null
                ? LookupSheetProvider.refusalLine(refusal)
                : (sheet.draftError ?? ''),
            style: theme.textTheme.bodyMedium,
          ),
          if (refusal == null) ...[
            const SizedBox(height: Space.x3),
            Align(
              alignment: Alignment.centerLeft,
              child: OutlinedButton(
                onPressed: sheet.retryDraft,
                child: const Text(LookupSheet.retryLabel),
              ),
            ),
          ],
        ];
      case LookupPhase.ready:
      case LookupPhase.sending:
      case LookupPhase.waiting:
      case LookupPhase.done:
        break;
    }
    final draft = sheet.draft;
    final changes = draft?.changes ?? const <LookupChange>[];
    return [
      heading,
      const SizedBox(height: Space.x4),
      SegmentedButton<LookupMode>(
        showSelectedIcon: false,
        segments: [
          for (final m in LookupMode.values)
            ButtonSegment(value: m, label: Text(m.displayName)),
        ],
        selected: {sheet.mode},
        onSelectionChanged: sheet.canEdit
            ? (selected) => sheet.setMode(selected.first)
            : null,
      ),
      const SizedBox(height: Space.x2),
      Text(LookupSheet.modeLine(sheet.mode), style: theme.textTheme.bodySmall),
      if (sheet.isSwitching) ...[
        const SizedBox(height: Space.x2),
        const LinearProgressIndicator(),
      ],
      const SizedBox(height: Space.x4),
      Text(LookupSheet.changesTitle, style: theme.textTheme.titleSmall),
      const SizedBox(height: Space.x2),
      if (changes.isEmpty)
        Text(LookupSheet.noChanges, style: theme.textTheme.bodyMedium)
      else
        Wrap(
          spacing: Space.x2,
          runSpacing: Space.x2,
          children: [
            for (final c in changes)
              Chip(label: Text(c.label), visualDensity: VisualDensity.compact),
          ],
        ),
      if (draft?.reworded ?? false) ...[
        const SizedBox(height: Space.x2),
        Text(LookupSheet.rewordedLine, style: theme.textTheme.bodySmall),
      ],
      const SizedBox(height: Space.x4),
      TextField(
        controller: _text,
        readOnly: !sheet.canEdit,
        minLines: 5,
        maxLines: 14,
        maxLength: LookupSheetProvider.maxLength,
        onChanged: sheet.edit,
        buildCounter:
            (context, {required currentLength, required isFocused, maxLength}) {
              if (maxLength == null || currentLength < maxLength - 500) {
                return null;
              }
              return Text(
                '$currentLength of $maxLength',
                style: theme.textTheme.bodySmall,
              );
            },
        decoration: InputDecoration(
          labelText: LookupSheet.textLabel,
          alignLabelWithHint: true,
        ),
      ),
      const SizedBox(height: Space.x3),
      _Line(
        icon: Icons.dns_outlined,
        color: colors.textSoft,
        text: LookupSheet.accountLine(provider),
      ),
      const SizedBox(height: Space.x2),
      _Line(
        icon: Icons.visibility_outlined,
        color: colors.textSoft,
        text: LookupSheet.scrubberLine,
      ),
    ];
  }
}

class _Line extends StatelessWidget {
  final IconData icon;
  final Color color;
  final String text;

  const _Line({required this.icon, required this.color, required this.text});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Icon(icon, size: 18, color: color),
        const SizedBox(width: Space.x2),
        Expanded(child: Text(text, style: theme.textTheme.bodySmall)),
      ],
    );
  }
}

/// The band at the top of the lookup sheet. It sits outside the scrolling
/// part, so it is always on screen.
class LookupWarningBand extends StatelessWidget {
  final String provider;
  final bool detailed;

  const LookupWarningBand({
    super.key,
    required this.provider,
    this.detailed = false,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    return Semantics(
      container: true,
      child: DecoratedBox(
        decoration: BoxDecoration(
          color: scheme.errorContainer,
          border: Border(bottom: BorderSide(color: scheme.error, width: 1.5)),
        ),
        child: Padding(
          padding: const EdgeInsets.fromLTRB(
            Space.x4,
            Space.x4,
            Space.x4,
            Space.x3,
          ),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(Icons.public, color: scheme.error, size: 22),
              const SizedBox(width: Space.x3),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      LookupSheet.warning(provider),
                      style: theme.textTheme.bodyMedium?.copyWith(
                        color: scheme.onErrorContainer,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    if (detailed) ...[
                      const SizedBox(height: Space.x1),
                      Text(
                        LookupSheet.detailsLine,
                        style: theme.textTheme.bodyMedium?.copyWith(
                          color: scheme.onErrorContainer,
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Footer extends StatelessWidget {
  final String provider;
  final VoidCallback onSend;

  const _Footer({required this.provider, required this.onSend});

  @override
  Widget build(BuildContext context) {
    final sheet = context.watch<LookupSheetProvider>();
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final scheme = theme.colorScheme;
    final Widget body;
    if (sheet.isBusy) {
      final waiting = sheet.phase == LookupPhase.waiting;
      body = Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              const SizedBox(
                width: 18,
                height: 18,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
              const SizedBox(width: Space.x3),
              Expanded(
                child: Text(
                  waiting
                      ? LookupSheet.waitingLine(provider)
                      : LookupSheet.sendingLine,
                  style: theme.textTheme.bodyLarge,
                ),
              ),
              if (waiting)
                TextButton(
                  onPressed: () => Navigator.of(context).pop(),
                  child: const Text(LookupSheet.closeLabel),
                ),
            ],
          ),
          if (waiting) ...[
            const SizedBox(height: Space.x1),
            Text(LookupSheet.closeNote, style: theme.textTheme.bodySmall),
          ],
        ],
      );
    } else {
      body = Row(
        children: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(),
            child: const Text(LookupSheet.cancelLabel),
          ),
          const SizedBox(width: Space.x3),
          Expanded(
            child: Align(
              alignment: Alignment.centerRight,
              child: FilledButton.icon(
                key: const ValueKey('lookup-send'),
                style: FilledButton.styleFrom(
                  backgroundColor: scheme.error,
                  foregroundColor: scheme.onError,
                  disabledBackgroundColor: scheme.error.withValues(alpha: 0.3),
                  disabledForegroundColor: scheme.onError.withValues(
                    alpha: 0.8,
                  ),
                ),
                onPressed: sheet.canSend ? onSend : null,
                icon: const Icon(Icons.send_outlined, size: 18),
                label: Text(
                  LookupSheet.sendLabel(provider),
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ),
          ),
        ],
      );
    }
    return DecoratedBox(
      decoration: BoxDecoration(
        border: Border(top: BorderSide(color: colors.hairline)),
      ),
      child: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(
            Space.x4,
            Space.x3,
            Space.x4,
            Space.x3,
          ),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              // Why the last try did not go, kept next to Send so it is
              // never scrolled out of sight.
              if (sheet.findings.isNotEmpty)
                AttentionLine(LookupSheet.findingsLine(sheet.findings)),
              if (sheet.failure != null) AttentionLine(sheet.failure!),
              if (sheet.draftError != null &&
                  sheet.phase != LookupPhase.unavailable)
                AttentionLine(sheet.draftError!),
              if (sheet.findings.isNotEmpty ||
                  sheet.failure != null ||
                  (sheet.draftError != null &&
                      sheet.phase != LookupPhase.unavailable))
                const SizedBox(height: Space.x3),
              body,
            ],
          ),
        ),
      ),
    );
  }
}
