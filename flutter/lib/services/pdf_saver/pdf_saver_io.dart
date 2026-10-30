// This file is only compiled where dart:io exists (never on web).
import 'dart:io';
import 'dart:typed_data';

import 'package:path_provider/path_provider.dart';
import 'package:share_plus/share_plus.dart';

/// Writes the PDF to the temp directory and opens the share sheet.
Future<void> saveAndOpenPdf(Uint8List bytes, String fileName) async {
  final dir = await getTemporaryDirectory();
  final path = '${dir.path}/$fileName';
  await File(path).writeAsBytes(bytes, flush: true);
  await SharePlus.instance.share(
    ShareParams(files: [XFile(path, mimeType: 'application/pdf')]),
  );
}

/// Writes the file to the temp directory and opens the share sheet.
Future<void> saveFile(Uint8List bytes, String fileName, String mimeType) async {
  final dir = await getTemporaryDirectory();
  final path = '${dir.path}/$fileName';
  await File(path).writeAsBytes(bytes, flush: true);
  await SharePlus.instance.share(
    ShareParams(files: [XFile(path, mimeType: mimeType)]),
  );
}
