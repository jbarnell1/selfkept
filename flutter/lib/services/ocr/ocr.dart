import 'dart:typed_data';

import 'ocr_stub.dart' if (dart.library.io) 'ocr_mlkit.dart' as impl;

/// Reads text from a photo or screenshot of a results screen.
abstract class TextScanner {
  /// The lines of text in [jpegBytes], top to bottom.
  Future<List<String>> linesFromImage(Uint8List jpegBytes);

  /// False where no on-device recognition exists (web and desktop).
  bool get available;
}

/// The scanner for this platform: ML Kit on Android and iOS, none elsewhere.
TextScanner createTextScanner() => impl.createScanner();
