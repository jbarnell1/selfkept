import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../brand/brand.dart';
import '../../../models/setup.dart';
import '../../../models/source.dart';
import '../../../providers/collector_provider.dart';
import '../../../providers/server_settings.dart';
import '../../../providers/sources_provider.dart';
import '../../../theme/tokens.dart';
import '../../../utils/open_url.dart';
import '../../../utils/relative_time.dart';
import '../../../widgets/copyable_value.dart';
import '../../../widgets/setup_step_card.dart';
import '../../../widgets/status_views.dart';
import '../setup_icons.dart';

/// Apple Health or Health Connect. On the matching phone it connects right
/// here; anywhere else it says how to get the phone app onto that phone.
class PhoneStep extends StatelessWidget {
  final SetupItem item;

  const PhoneStep({super.key, required this.item})
    : assert(item == SetupItem.appleHealth || item == SetupItem.healthConnect);

  SourceKind get _kind => item == SetupItem.appleHealth
      ? SourceKind.healthkit
      : SourceKind.healthConnect;
  String get _phone =>
      item == SetupItem.appleHealth ? 'iPhone' : 'Android phone';
  String get _appUrl =>
      item == SetupItem.appleHealth ? Brand.iosAppUrl : Brand.androidAppUrl;

  @override
  Widget build(BuildContext context) {
    final collector = context.watch<CollectorProvider>();
    final sources = context.watch<SourcesProvider>();
    final onThisPhone = collector.isAvailable && collector.kind == _kind;
    final sending = sources.sources.where((s) => s.kind == _kind).toList();
    final done = (onThisPhone && collector.isConnected) || sending.isNotEmpty;

    final Widget body;
    if (onThisPhone && !collector.isConnected) {
      body = _ConnectHere(collector: collector);
    } else if (sending.isNotEmpty) {
      body = _Sending(sources: sending);
    } else {
      body = _OnYourPhone(
        phone: _phone,
        appUrl: _appUrl,
        fitbitHint: item == SetupItem.healthConnect,
      );
    }
    return SetupStepCard(
      icon: setupItemIcon(item),
      title: _kind.displayName,
      done: done,
      doneLabel: 'Connected',
      child: body,
    );
  }
}

class _ConnectHere extends StatelessWidget {
  final CollectorProvider collector;

  const _ConnectHere({required this.collector});

  @override
  Widget build(BuildContext context) {
    final name = collector.kind.displayName;
    final connecting = collector.status == CollectorStatus.connecting;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          'Read steps, sleep, heart rate and the rest from $name on this '
          'phone, and keep them on your own server.',
        ),
        const SizedBox(height: Space.x3),
        FilledButton.icon(
          onPressed: connecting ? null : collector.connect,
          icon: const Icon(Icons.favorite_outline, size: 18),
          label: Text(connecting ? 'Connecting' : 'Connect $name'),
        ),
        if (collector.progress != null)
          Padding(
            padding: const EdgeInsets.only(top: Space.x2),
            child: Text(
              collector.progress!,
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ),
        AttentionLine(collector.error ?? ''),
      ],
    );
  }
}

class _Sending extends StatelessWidget {
  final List<Source> sources;

  const _Sending({required this.sources});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        for (final s in sources)
          Text(
            '${s.deviceName.isEmpty ? s.kind.displayName : s.deviceName}: '
            '${s.sampleCount} ${s.sampleCount == 1 ? 'reading' : 'readings'}, '
            'last sync ${relativeTimeInSentence(s.lastSyncAt)}.',
            style: theme.textTheme.bodyMedium,
          ),
        const SizedBox(height: Space.x2),
        Text(
          'New readings arrive whenever the app opens on that phone.',
          style: theme.textTheme.bodySmall,
        ),
      ],
    );
  }
}

class _OnYourPhone extends StatelessWidget {
  final String phone;
  final String appUrl;
  final bool fitbitHint;

  const _OnYourPhone({
    required this.phone,
    required this.appUrl,
    required this.fitbitHint,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final server = context.read<ServerSettings>().currentUrl;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          'The phone app reads it. On your $phone, install ${Brand.shortName}, '
          'open it, and sign in to this server with the same account:',
        ),
        const SizedBox(height: Space.x2),
        CopyableValue(server),
        if (appUrl.isNotEmpty) ...[
          const SizedBox(height: Space.x3),
          OutlinedButton.icon(
            onPressed: () => openLink(context, appUrl),
            icon: const Icon(Icons.download_outlined, size: 18),
            label: Text('Get the $phone app'),
          ),
        ],
        if (fitbitHint) ...[
          const SizedBox(height: Space.x3),
          Text(
            'A Pixel Watch or Fitbit writes to Health Connect through the '
            'Fitbit app: turn on Health Connect in its settings first.',
            style: theme.textTheme.bodySmall,
          ),
        ],
        const SizedBox(height: Space.x2),
        Text(
          'This step ticks itself once that phone has sent its first readings.',
          style: theme.textTheme.bodySmall,
        ),
      ],
    );
  }
}
