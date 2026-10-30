import 'dart:io';

import 'package:drift/drift.dart';
import 'package:drift/native.dart';
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';

import 'drift_outbox_repository.dart';
import 'outbox_database.dart';
import 'outbox_repository.dart';

/// Opens the SQLite outbox in the app's support directory (not Documents, so
/// it is never shown to the user or backed up as a document).
OutboxRepository openOutboxRepository() {
  final executor = LazyDatabase(() async {
    final dir = await getApplicationSupportDirectory();
    final file = File(p.join(dir.path, 'outbox.sqlite'));
    return NativeDatabase.createInBackground(file);
  });
  return DriftOutboxRepository(OutboxDatabase(executor));
}
