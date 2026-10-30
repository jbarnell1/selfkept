import 'dart:typed_data';

import 'pdf_saver_stub.dart'
    if (dart.library.js_interop) 'pdf_saver_web.dart'
    if (dart.library.io) 'pdf_saver_io.dart'
    as impl;

/// Hands a PDF to the person: a new browser tab on web, the share sheet on a
/// phone.
Future<void> saveAndOpenPdf(Uint8List bytes, String fileName) =>
    impl.saveAndOpenPdf(bytes, fileName);

/// Hands any file to the person: a browser download on web, the share sheet on
/// a phone. Used for exports (Markdown bundles, JSON).
Future<void> saveFile(Uint8List bytes, String fileName, String mimeType) =>
    impl.saveFile(bytes, fileName, mimeType);
