import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/assistant.dart';
import '../../providers/assistant_pages_providers.dart';
import '../../services/assistant_service.dart';
import '../../theme/app_theme.dart';
import '../../theme/tokens.dart';
import '../../widgets/page_body.dart';
import '../../widgets/status_views.dart';
import '../../widgets/subpage_app_bar.dart';
import '../../widgets/surface_card.dart';

/// "Evening reminder": one nudge an evening, sent by the server to the
/// person's own ntfy topic when the day is not closed out. The server keeps
/// the address and returns it masked; the token never comes back.
class NudgesPage extends StatelessWidget {
  const NudgesPage({super.key});

  static const String title = 'Evening reminder';
  static const String subtitle =
      'Your server sends one short nudge to your own ntfy topic when the '
      'evening check-in is still open. The message holds no health values.';
  static const String enabledLabel = 'Send the evening reminder';
  static const String timeLabel = 'Time';
  static const String addressLabel = 'ntfy topic address';
  static const String addressHint = 'https://ntfy.example.net/evening';
  static const String newAddressLabel = 'New topic address';
  static const String tokenLabel = 'Access token (optional)';
  static const String newTokenLabel = 'New access token';
  static const String tokenSaved = 'A token is saved.';
  static const String clearAddressLabel = 'Remove address';
  static const String clearTokenLabel = 'Remove token';
  static const String saveLabel = 'Save';
  static const String savedMessage = 'Saved.';
  static const String testLabel = 'Send a test';
  static const String testSent = 'Sent. It should arrive in a moment.';
  static const String loadErrorTitle = 'Reminder settings did not load.';

  static String savedAddress(String masked) => 'Saved address: $masked';

  @override
  Widget build(BuildContext context) {
    return ChangeNotifierProvider(
      create: (context) =>
          AssistantNudgesProvider(context.read<AssistantService>())..load(),
      child: const Scaffold(
        appBar: SubpageAppBar(fallback: '/you', title: Text('You')),
        body: _NudgesBody(),
      ),
    );
  }
}

class _NudgesBody extends StatelessWidget {
  const _NudgesBody();

  @override
  Widget build(BuildContext context) {
    final nudges = context.watch<AssistantNudgesProvider>();
    final settings = nudges.settings;
    final Widget content;
    if (settings == null) {
      content = nudges.error != null && !nudges.isLoading
          ? ErrorView(
              title: NudgesPage.loadErrorTitle,
              detail: nudges.error,
              onRetry: nudges.load,
            )
          : const LoadingView();
    } else {
      content = _NudgesForm(settings: settings);
    }
    return PageBody(
      children: [
        const PageHeading(
          title: NudgesPage.title,
          subtitle: NudgesPage.subtitle,
        ),
        content,
      ],
    );
  }
}

class _NudgesForm extends StatefulWidget {
  final NudgeSettings settings;

  const _NudgesForm({required this.settings});

  @override
  State<_NudgesForm> createState() => _NudgesFormState();
}

class _NudgesFormState extends State<_NudgesForm> {
  // What the person types. Cleared after each save, so the full address is
  // only ever on screen while it is being entered.
  final TextEditingController _address = TextEditingController();
  final TextEditingController _token = TextEditingController();
  late bool _enabled = widget.settings.enabled;
  late String _time = widget.settings.eveningTime;
  bool _clearAddress = false;
  bool _clearToken = false;

  @override
  void dispose() {
    _address.dispose();
    _token.dispose();
    super.dispose();
  }

  TimeOfDay get _timeOfDay {
    final parts = _time.split(':');
    final hour = int.tryParse(parts.first) ?? 20;
    final minute = parts.length > 1 ? int.tryParse(parts[1]) ?? 0 : 0;
    return TimeOfDay(hour: hour, minute: minute);
  }

  static String _format(TimeOfDay t) =>
      '${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}';

  Future<void> _pickTime() async {
    final picked = await showTimePicker(
      context: context,
      initialTime: _timeOfDay,
    );
    if (picked != null && mounted) setState(() => _time = _format(picked));
  }

  Future<void> _save() async {
    final nudges = context.read<AssistantNudgesProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final address = _address.text.trim();
    final token = _token.text.trim();
    final ok = await nudges.save(
      NudgeUpdate(
        enabled: _enabled,
        ntfyUrl: address.isNotEmpty ? address : (_clearAddress ? '' : null),
        ntfyToken: token.isNotEmpty ? token : (_clearToken ? '' : null),
        eveningTime: _time,
      ),
    );
    if (!mounted || !ok) return;
    final saved = nudges.settings!;
    setState(() {
      _address.clear();
      _token.clear();
      _clearAddress = false;
      _clearToken = false;
      _enabled = saved.enabled;
      _time = saved.eveningTime;
    });
    messenger
      ..hideCurrentSnackBar()
      ..showSnackBar(const SnackBar(content: Text(NudgesPage.savedMessage)));
  }

  @override
  Widget build(BuildContext context) {
    final nudges = context.watch<AssistantNudgesProvider>();
    final settings = nudges.settings ?? widget.settings;
    final theme = Theme.of(context);
    final hasAddress = settings.urlSet && !_clearAddress;
    final hasToken = settings.tokenSet && !_clearToken;
    final test = nudges.testResult;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SurfaceCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              SwitchListTile(
                contentPadding: EdgeInsets.zero,
                title: const Text(NudgesPage.enabledLabel),
                value: _enabled,
                onChanged: (v) => setState(() => _enabled = v),
              ),
              ListTile(
                contentPadding: EdgeInsets.zero,
                title: const Text(NudgesPage.timeLabel),
                subtitle: settings.timeZone.isEmpty
                    ? null
                    : Text(settings.timeZone),
                trailing: TextButton(
                  onPressed: _pickTime,
                  child: Text(
                    _time,
                    style: AppTheme.numeric(
                      context,
                      base: theme.textTheme.titleMedium,
                    ),
                  ),
                ),
              ),
              const Divider(),
              const SizedBox(height: Space.x3),
              if (hasAddress && settings.ntfyUrlMasked != null) ...[
                Text(
                  NudgesPage.savedAddress(settings.ntfyUrlMasked!),
                  style: AppTheme.numeric(
                    context,
                    base: theme.textTheme.bodySmall,
                  ),
                ),
                Align(
                  alignment: Alignment.centerLeft,
                  child: TextButton(
                    onPressed: () => setState(() => _clearAddress = true),
                    child: const Text(NudgesPage.clearAddressLabel),
                  ),
                ),
              ],
              TextField(
                controller: _address,
                keyboardType: TextInputType.url,
                autocorrect: false,
                enableSuggestions: false,
                decoration: InputDecoration(
                  labelText: hasAddress
                      ? NudgesPage.newAddressLabel
                      : NudgesPage.addressLabel,
                  hintText: NudgesPage.addressHint,
                ),
              ),
              const SizedBox(height: Space.x4),
              if (hasToken)
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        NudgesPage.tokenSaved,
                        style: theme.textTheme.bodySmall,
                      ),
                    ),
                    TextButton(
                      onPressed: () => setState(() => _clearToken = true),
                      child: const Text(NudgesPage.clearTokenLabel),
                    ),
                  ],
                ),
              TextField(
                controller: _token,
                obscureText: true,
                autocorrect: false,
                enableSuggestions: false,
                decoration: InputDecoration(
                  labelText: hasToken
                      ? NudgesPage.newTokenLabel
                      : NudgesPage.tokenLabel,
                ),
              ),
              AttentionLine(nudges.saveError ?? ''),
              const SizedBox(height: Space.x4),
              Wrap(
                spacing: Space.x3,
                runSpacing: Space.x2,
                children: [
                  FilledButton(
                    onPressed: nudges.isSaving ? null : _save,
                    child: const Text(NudgesPage.saveLabel),
                  ),
                  OutlinedButton(
                    onPressed: nudges.isTesting || !settings.urlSet
                        ? null
                        : nudges.test,
                    child: const Text(NudgesPage.testLabel),
                  ),
                ],
              ),
              if (test != null && test.sent)
                Padding(
                  padding: const EdgeInsets.only(top: Space.x3),
                  child: Text(
                    NudgesPage.testSent,
                    style: theme.textTheme.bodySmall,
                  ),
                ),
              AttentionLine(
                test != null && !test.sent
                    ? (test.error ?? 'That did not arrive.')
                    : (nudges.testError ?? ''),
              ),
            ],
          ),
        ),
      ],
    );
  }
}
