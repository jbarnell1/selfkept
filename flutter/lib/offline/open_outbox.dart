library;

/// Platform-selected outbox: SQLite through drift on iOS, Android and desktop,
/// an in-memory stub on web. Import this file, never the platform ones.
export 'open_outbox_native.dart'
    if (dart.library.js_interop) 'open_outbox_web.dart';
