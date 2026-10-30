import 'dart:typed_data';

import 'ocr.dart';

TextScanner createScanner() => const _UnavailableScanner();

class _UnavailableScanner implements TextScanner {
  const _UnavailableScanner();

  @override
  bool get available => false;

  @override
  Future<List<String>> linesFromImage(Uint8List jpegBytes) =>
      Future.error(UnsupportedError('Text recognition is not available here.'));
}
