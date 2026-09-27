import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';

/// A photo taken or chosen on the phone, already reduced for upload.
class PickedPhoto {
  const PickedPhoto({required this.bytes, required this.name});

  final Uint8List bytes;
  final String name;
}

enum PhotoOrigin { camera, gallery }

/// The camera and gallery, behind an interface so screens and tests do not depend on the device.
abstract class PhotoSource {
  /// Null when the farmer cancels.
  Future<PickedPhoto?> pick(PhotoOrigin origin);
}

class ImagePickerPhotoSource implements PhotoSource {
  ImagePickerPhotoSource([ImagePicker? picker]) : _picker = picker ?? ImagePicker();

  final ImagePicker _picker;

  /// Resized on the phone before it leaves: a 12-megapixel leaf photo becomes a few hundred
  /// kilobytes, well inside the API's 2 MB limit and kind to a rural data plan. 1600 px keeps
  /// enough detail to see lesions and mould.
  static const _maxDimension = 1600.0;
  static const _jpegQuality = 80;

  @override
  Future<PickedPhoto?> pick(PhotoOrigin origin) async {
    final file = await _picker.pickImage(
      source: origin == PhotoOrigin.camera ? ImageSource.camera : ImageSource.gallery,
      maxWidth: _maxDimension,
      maxHeight: _maxDimension,
      imageQuality: _jpegQuality,
    );
    if (file == null) return null;
    return PickedPhoto(bytes: await file.readAsBytes(), name: file.name);
  }
}

final photoSourceProvider = Provider<PhotoSource>((ref) => ImagePickerPhotoSource());
