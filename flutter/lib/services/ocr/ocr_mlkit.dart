// This file is only compiled where dart:io exists (never on web).
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:google_mlkit_text_recognition/google_mlkit_text_recognition.dart';
import 'package:path_provider/path_provider.dart';

import 'ocr.dart';

TextScanner createScanner() => _MlKitScanner();

/// On-device recognition with ML Kit. Nothing leaves the phone.
class _MlKitScanner implements TextScanner {
  @override
  bool get available =>
      defaultTargetPlatform == TargetPlatform.android ||
      defaultTargetPlatform == TargetPlatform.iOS;

  @override
  Future<List<String>> linesFromImage(Uint8List jpegBytes) async {
    if (!available) {
      throw UnsupportedError('Text recognition is not available here.');
    }
    final dir = await getTemporaryDirectory();
    final file = File(
      '${dir.path}/lab-scan-${DateTime.now().microsecondsSinceEpoch}.jpg',
    );
    await file.writeAsBytes(jpegBytes, flush: true);
    final recognizer = TextRecognizer(script: TextRecognitionScript.latin);
    try {
      final recognized = await recognizer.processImage(
        InputImage.fromFilePath(file.path),
      );
      final lines = recognized.blocks.expand((b) => b.lines).toList()
        ..sort((a, b) {
          final byTop = a.boundingBox.top.compareTo(b.boundingBox.top);
          return byTop != 0
              ? byTop
              : a.boundingBox.left.compareTo(b.boundingBox.left);
        });
      return [for (final l in lines) l.text];
    } finally {
      await recognizer.close();
      try {
        await file.delete();
      } on FileSystemException {
        // A leftover temp file is harmless.
      }
    }
  }
}
