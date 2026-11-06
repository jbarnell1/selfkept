import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/background/background_upload.dart';
import 'package:selfkept/collectors/health_collector.dart';
import 'package:selfkept/offline/outbox_repository.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/fake_health_collector.dart';

void main() {
  late FakeApiClient api;
  late FakeHealthCollector collector;
  late MemoryOutboxRepository outbox;

  BackgroundUpload upload() =>
      BackgroundUpload(client: api, outbox: outbox, collector: collector);

  setUp(() {
    api = FakeApiClient();
    collector = FakeHealthCollector();
    outbox = MemoryOutboxRepository();
    api.on('GET', '/auth/me', (_) => Fixtures.user());
    api.on('GET', '/sources', (_) => [Fixtures.source(id: 's1')]);
    api.on('POST', '/ingest/batch', (data) {
      final body = data as Map<String, dynamic>;
      return Fixtures.ingestResult(
        accepted: (body['samples'] as List).length,
        cursor: body['cursor'] as String?,
      );
    });
  });

  tearDown(() => collector.close());

  test('a signed-in, connected phone sends every page it collected', () async {
    api.restoreResult = true;
    await outbox.writeState('collector.u1.sourceId', 's1');
    collector.pages.addAll([
      CollectResult(samples: hrSamples(3), cursor: 'c1', hasMore: true),
      CollectResult(samples: hrSamples(2, from: 3), cursor: 'c2'),
    ]);

    final sent = await upload().run();

    expect(sent, isTrue);
    expect(api.calls.where((c) => c == 'POST /ingest/batch'), hasLength(2));
    expect(await outbox.count(), 0);
    expect(await outbox.readState('collector.u1.cursor'), 'c2');
  });

  test('a signed-out phone sends nothing and asks nothing', () async {
    api.restoreResult = false;
    collector.pages.add(CollectResult(samples: hrSamples(3), cursor: 'c1'));

    expect(await upload().run(), isFalse);
    expect(api.calls, isNot(contains('POST /ingest/batch')));
    expect(collector.requested, isNull, reason: 'never asks for permission');
  });

  test('a phone that never connected Health Connect ends the pass', () async {
    api.restoreResult = true;
    collector.pages.add(CollectResult(samples: hrSamples(3), cursor: 'c1'));

    expect(await upload().run(), isFalse);
    expect(collector.cursorsSeen, isEmpty);
  });

  test('a refused batch stays in the outbox for the next pass', () async {
    api.restoreResult = true;
    await outbox.writeState('collector.u1.sourceId', 's1');
    collector.pages.add(CollectResult(samples: hrSamples(3), cursor: 'c1'));
    api.on(
      'POST',
      '/ingest/batch',
      (_) => throw const ApiException('Server error', statusCode: 500),
    );

    expect(await upload().run(), isFalse);
    expect(await outbox.count(), 3);
  });
}
