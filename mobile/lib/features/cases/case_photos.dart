import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/theme.dart';
import '../../core/photos/photo_source.dart';
import 'case_providers.dart';

const _thumbSize = 96.0;

/// A photo still on the phone (not uploaded yet), with a way to drop it.
class LocalPhotoThumb extends StatelessWidget {
  const LocalPhotoThumb({required this.bytes, required this.onRemove, required this.index, this.size = _thumbSize, super.key});

  final Uint8List bytes;
  final VoidCallback onRemove;
  final int index;
  final double size;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      // Room above and to the right for the remove button's full 48dp target.
      width: size + 16,
      height: size + 16,
      child: Stack(
        children: [
          Positioned(
            left: 0,
            bottom: 0,
            child: Container(
              decoration: BoxDecoration(
                borderRadius: BorderRadius.circular(10),
                border: Border.all(color: AgriColors.borderStrong),
              ),
              child: ClipRRect(
                borderRadius: BorderRadius.circular(9),
                child: Image.memory(
                  bytes,
                  width: size,
                  height: size,
                  fit: BoxFit.cover,
                  semanticLabel: 'Photo ${index + 1}',
                  errorBuilder: (_, _, _) => _Placeholder(icon: Icons.broken_image_outlined, size: size),
                ),
              ),
            ),
          ),
          // A small dark disc over the photo's corner, inside a full 48dp target.
          Positioned(
            right: 0,
            top: 0,
            child: IconButton(
              style: IconButton.styleFrom(minimumSize: const Size(48, 48), padding: EdgeInsets.zero),
              tooltip: 'Remove photo ${index + 1}',
              onPressed: onRemove,
              icon: Container(
                width: 30,
                height: 30,
                decoration: BoxDecoration(
                  color: AgriColors.ink,
                  shape: BoxShape.circle,
                  border: Border.all(color: Colors.white, width: 2),
                ),
                child: const Icon(Icons.close, size: 18, color: Colors.white),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// A stored photo, fetched with the signed-in token (a plain image URL could not carry it).
class CasePhotoThumb extends ConsumerWidget {
  const CasePhotoThumb({required this.caseId, required this.photoId, required this.index, super.key});

  final String caseId;
  final String photoId;
  final int index;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final bytes = ref.watch(photoBytesProvider('$caseId/$photoId'));
    return ClipRRect(
      borderRadius: BorderRadius.circular(10),
      child: bytes.when(
        loading: () => const _Placeholder(),
        error: (_, _) => const _Placeholder(icon: Icons.broken_image_outlined),
        data: (b) => GestureDetector(
          onTap: () => showDialog<void>(
            context: context,
            builder: (_) => Dialog(child: InteractiveViewer(child: Image.memory(b))),
          ),
          child: Image.memory(
            b,
            width: _thumbSize,
            height: _thumbSize,
            fit: BoxFit.cover,
            semanticLabel: 'Photo ${index + 1} — tap to enlarge',
            errorBuilder: (_, _, _) => const _Placeholder(icon: Icons.broken_image_outlined),
          ),
        ),
      ),
    );
  }
}

class _Placeholder extends StatelessWidget {
  const _Placeholder({this.icon, this.size = _thumbSize});

  final IconData? icon;
  final double size;

  @override
  Widget build(BuildContext context) => Container(
        width: size,
        height: size,
        color: AgriColors.surfaceInset,
        alignment: Alignment.center,
        child: icon == null
            ? const SizedBox.square(dimension: 20, child: CircularProgressIndicator(strokeWidth: 2))
            : Icon(icon, color: AgriColors.inkMuted),
      );
}

/// "Take photo" and "From gallery" side by side, each half the width and a full touch target.
class AddPhotoButtons extends StatelessWidget {
  const AddPhotoButtons({required this.onPick, this.enabled = true, super.key});

  final void Function(PhotoOrigin origin) onPick;
  final bool enabled;

  @override
  Widget build(BuildContext context) => Row(
        children: [
          Expanded(
            child: OutlinedButton.icon(
              onPressed: enabled ? () => onPick(PhotoOrigin.camera) : null,
              icon: const Icon(Icons.photo_camera_outlined),
              label: const Text('Take photo'),
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: OutlinedButton.icon(
              onPressed: enabled ? () => onPick(PhotoOrigin.gallery) : null,
              icon: const Icon(Icons.photo_library_outlined),
              label: const Text('From gallery'),
            ),
          ),
        ],
      );
}
