import 'package:flutter/foundation.dart';
import 'package:image_picker/image_picker.dart';

/// Where a photo comes from.
enum PhotoSource { camera, gallery }

/// Picks one photo for the assistant. A seam so tests hand over bytes
/// without the platform picker.
abstract class PhotoPicker {
  const PhotoPicker();

  /// The long side the picker scales down to.
  static const double maxSide = 1600;

  /// JPEG quality the picker writes.
  static const int quality = 82;

  /// Null when the person backed out or the picker failed.
  Future<Uint8List?> pick(PhotoSource source);
}

/// The phone's own camera and photo picker, through image_picker. With a
/// size limit and a quality set the picker writes a JPEG no larger than
/// [PhotoPicker.maxSide] on its long side.
class ImagePhotoPicker extends PhotoPicker {
  const ImagePhotoPicker();

  @override
  Future<Uint8List?> pick(PhotoSource source) async {
    try {
      final file = await ImagePicker().pickImage(
        source: switch (source) {
          PhotoSource.camera => ImageSource.camera,
          PhotoSource.gallery => ImageSource.gallery,
        },
        maxWidth: PhotoPicker.maxSide,
        maxHeight: PhotoPicker.maxSide,
        imageQuality: PhotoPicker.quality,
      );
      if (file == null) return null;
      return await file.readAsBytes();
    } catch (e) {
      debugPrint('The photo picker did not open: $e');
      return null;
    }
  }
}
