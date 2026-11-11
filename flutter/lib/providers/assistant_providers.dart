import 'package:flutter/foundation.dart';
import 'package:provider/provider.dart';
import 'package:provider/single_child_widget.dart';

import '../services/api_client.dart';
import '../services/assistant_service.dart';
import '../services/sse/event_stream_client.dart';
import '../services/sse/event_stream_transport.dart';
import 'assistant_lookup_providers.dart';
import 'assistant_provider.dart';
import 'assistant_today_provider.dart';
import 'auth_provider.dart';
import 'record_changes.dart';

/// The in-app assistant's service and app-wide providers, built and
/// disposed together so the composition root registers them in one place.
class AssistantProviders {
  final AssistantService service;
  final AssistantProvider assistant;
  final AssistantTodayProvider today;

  /// The cloud lookup's settings and sent list. Asks nothing until a page
  /// that exists only with `assistant.lookup` asks it to.
  final AssistantLookupProvider lookups;

  AssistantProviders._(this.service, this.assistant, this.today, this.lookups);

  factory AssistantProviders({
    required ApiClient api,
    required EventStreamClient events,
    required AuthProvider auth,
    required RecordChanges changes,
  }) {
    final service = AssistantService(api, events);
    return AssistantProviders._(
      service,
      AssistantProvider(service: service, auth: auth),
      AssistantTodayProvider(service: service, auth: auth, changes: changes),
      AssistantLookupProvider(service: service, auth: auth),
    );
  }

  /// The app's own client: the same server address and session as every
  /// other call, with a streaming transport for this platform.
  factory AssistantProviders.forClient({
    required DioApiClient client,
    required AuthProvider auth,
    required RecordChanges changes,
  }) => AssistantProviders(
    api: client,
    events: EventStreamClient(
      transport: createEventStreamTransport(),
      serverUrl: () => client.serverUrl,
      accessToken: client.accessToken,
      refresh: client.refreshAfterUnauthorized,
      isMobile: !kIsWeb,
    ),
    auth: auth,
    changes: changes,
  );

  List<SingleChildWidget> get providers => [
    Provider<AssistantService>.value(value: service),
    ChangeNotifierProvider.value(value: assistant),
    ChangeNotifierProvider.value(value: today),
    ChangeNotifierProvider.value(value: lookups),
  ];

  void dispose() {
    lookups.dispose();
    today.dispose();
    assistant.dispose();
  }
}
