import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../models/assistant_lookup.dart';
import '../../providers/assistant_lookup_providers.dart';
import '../../theme/tokens.dart';
import '../../widgets/assistant/assistant_feature.dart';
import '../../widgets/page_body.dart';
import '../../widgets/status_views.dart';
import '../../widgets/subpage_app_bar.dart';
import '../../widgets/surface_card.dart';

/// "Cloud lookup": the person's own switch. Off until they turn it on, and
/// turning it on needs a ticked acknowledgement. Nobody can turn it on for
/// someone else.
class CloudLookupPage extends StatelessWidget {
  const CloudLookupPage({super.key});

  static const String title = 'Cloud lookup';
  static const String subtitle =
      'Ask a large cloud model one question at a time and read its answer '
      'with sources. It is off until you turn it on, and it is only ever '
      'for you.';
  static const String howTitle = 'How it works';
  static const String howOffer =
      'With this on, the assistant offers a lookup under its answers to your '
      'questions. Turning it on sends nothing.';
  static const String howScreen =
      'Tapping the offer shows the exact text first, with names, contact '
      'details and dates taken out. You can edit it. Nothing leaves this '
      'server until you press send.';
  static const String howLocal =
      'Afterwards your home model explains, on this server, what the answer '
      'might mean for you.';
  static const String providerLabel = 'Provider';
  static const String modelLabel = 'Model';
  static const String switchLabel = 'Offer cloud lookups in my chats';
  static const String onLine = 'On. Each question still waits for you to send.';
  static const String offLine = 'Off. Nothing is offered or sent.';
  static const String sentTitle = 'Sent to the cloud';
  static const String sentLine = 'Every question you sent, and its exact text.';
  static const String unavailableTitle = 'Not offered on this server';
  static const String unavailableLine =
      'Cloud lookup needs the person who runs your server to turn it on. '
      'Until then nothing here can be sent.';
  static const String loadErrorTitle = 'This did not load.';

  static String howProvider(String provider) =>
      'What you send goes to $provider over the internet. Once it is sent it '
      'cannot be taken back.';
  static String acknowledgement(String provider) =>
      'I understand that what I send leaves this server, goes to $provider, '
      'and cannot be taken back.';

  @override
  Widget build(BuildContext context) {
    return const Scaffold(
      appBar: SubpageAppBar(fallback: '/you', title: Text('You')),
      body: _CloudLookupBody(),
    );
  }
}

class _CloudLookupBody extends StatefulWidget {
  const _CloudLookupBody();

  @override
  State<_CloudLookupBody> createState() => _CloudLookupBodyState();
}

class _CloudLookupBodyState extends State<_CloudLookupBody> {
  bool _asked = false;

  void _loadOnce() {
    if (_asked) return;
    _asked = true;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) context.read<AssistantLookupProvider>().loadStatus();
    });
  }

  @override
  Widget build(BuildContext context) {
    final available = LookupFeature.watch(context);
    final Widget content;
    if (!available) {
      content = const _Unavailable();
    } else {
      _loadOnce();
      final lookups = context.watch<AssistantLookupProvider>();
      final status = lookups.status;
      if (lookups.statusRefusal != null ||
          (status != null && !status.available)) {
        content = const _Unavailable();
      } else if (status == null) {
        content = lookups.statusError != null && !lookups.isLoadingStatus
            ? ErrorView(
                title: CloudLookupPage.loadErrorTitle,
                detail: lookups.statusError,
                onRetry: lookups.loadStatus,
              )
            : const LoadingView();
      } else {
        content = _Settings(status: status);
      }
    }
    return PageBody(
      children: [
        const PageHeading(
          title: CloudLookupPage.title,
          subtitle: CloudLookupPage.subtitle,
        ),
        content,
      ],
    );
  }
}

class _Unavailable extends StatelessWidget {
  const _Unavailable();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            CloudLookupPage.unavailableTitle,
            style: theme.textTheme.titleMedium,
          ),
          const SizedBox(height: Space.x2),
          Text(
            CloudLookupPage.unavailableLine,
            style: theme.textTheme.bodyMedium,
          ),
        ],
      ),
    );
  }
}

class _Settings extends StatefulWidget {
  final LookupStatus status;

  const _Settings({required this.status});

  @override
  State<_Settings> createState() => _SettingsState();
}

class _SettingsState extends State<_Settings> {
  bool _ticked = false;

  Future<void> _set(bool on) async {
    final lookups = context.read<AssistantLookupProvider>();
    if (on && !_ticked) return;
    final ok = await lookups.setEnabled(on, acknowledged: on && _ticked);
    if (!mounted || !ok) return;
    // The tick is for one turn-on only; turning it on again asks again.
    setState(() => _ticked = false);
  }

  @override
  Widget build(BuildContext context) {
    final lookups = context.watch<AssistantLookupProvider>();
    final status = lookups.status ?? widget.status;
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final provider = status.provider;
    final enabled = status.enabled;
    final canTurnOn = _ticked && !lookups.isSaving;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SurfaceCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                CloudLookupPage.howTitle,
                style: theme.textTheme.titleMedium,
              ),
              const SizedBox(height: Space.x2),
              Text(CloudLookupPage.howOffer, style: theme.textTheme.bodyMedium),
              const SizedBox(height: Space.x2),
              Text(
                CloudLookupPage.howScreen,
                style: theme.textTheme.bodyMedium,
              ),
              const SizedBox(height: Space.x2),
              Text(
                CloudLookupPage.howProvider(provider),
                style: theme.textTheme.bodyMedium,
              ),
              if (status.localModel) ...[
                const SizedBox(height: Space.x2),
                Text(
                  CloudLookupPage.howLocal,
                  style: theme.textTheme.bodyMedium,
                ),
              ],
              const SizedBox(height: Space.x4),
              _Fact(label: CloudLookupPage.providerLabel, value: provider),
              const SizedBox(height: Space.x1),
              _Fact(label: CloudLookupPage.modelLabel, value: status.model),
            ],
          ),
        ),
        const SizedBox(height: Space.x3),
        SurfaceCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (!enabled) ...[
                CheckboxListTile(
                  key: const ValueKey('lookup-acknowledge'),
                  contentPadding: EdgeInsets.zero,
                  controlAffinity: ListTileControlAffinity.leading,
                  value: _ticked,
                  onChanged: lookups.isSaving
                      ? null
                      : (v) => setState(() => _ticked = v ?? false),
                  title: Text(
                    CloudLookupPage.acknowledgement(provider),
                    style: theme.textTheme.bodyMedium,
                  ),
                ),
                Divider(color: colors.hairline),
              ],
              SwitchListTile(
                contentPadding: EdgeInsets.zero,
                value: enabled,
                onChanged: enabled
                    ? (lookups.isSaving ? null : (v) => _set(v))
                    : (canTurnOn ? (v) => _set(v) : null),
                title: Text(
                  CloudLookupPage.switchLabel,
                  style: theme.textTheme.bodyLarge,
                ),
                subtitle: Text(
                  enabled ? CloudLookupPage.onLine : CloudLookupPage.offLine,
                  style: theme.textTheme.bodySmall,
                ),
              ),
              if (lookups.saveError != null) AttentionLine(lookups.saveError!),
            ],
          ),
        ),
        const SizedBox(height: Space.x3),
        Semantics(
          button: true,
          child: SurfaceCard(
            onTap: () => context.push('/you/sent-to-cloud'),
            child: Row(
              children: [
                Icon(Icons.outbox_outlined, color: theme.colorScheme.primary),
                const SizedBox(width: Space.x4),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        CloudLookupPage.sentTitle,
                        style: theme.textTheme.titleMedium,
                      ),
                      const SizedBox(height: Space.x1),
                      Text(
                        CloudLookupPage.sentLine,
                        style: theme.textTheme.bodySmall,
                      ),
                    ],
                  ),
                ),
                const Icon(Icons.chevron_right),
              ],
            ),
          ),
        ),
      ],
    );
  }
}

class _Fact extends StatelessWidget {
  final String label;
  final String value;

  const _Fact({required this.label, required this.value});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          width: 88,
          child: Text(label, style: theme.textTheme.bodySmall),
        ),
        Expanded(child: Text(value, style: theme.textTheme.bodyMedium)),
      ],
    );
  }
}
