import 'dart:typed_data';

Future<void> saveAndOpenPdf(Uint8List bytes, String fileName) =>
    Future.error(UnsupportedError('Saving a PDF is not supported here.'));

Future<void> saveFile(Uint8List bytes, String fileName, String mimeType) =>
    Future.error(UnsupportedError('Saving a file is not supported here.'));
