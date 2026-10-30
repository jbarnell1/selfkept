import 'outbox_repository.dart';

/// Web never collects: the browser build has no health collector, so the
/// outbox only exists to satisfy the engine. Nothing is written to it.
OutboxRepository openOutboxRepository() => MemoryOutboxRepository();
