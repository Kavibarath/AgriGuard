import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/photos/photo_source.dart';
import 'case_providers.dart';

const _thumbSize = 88.0;

/// A photo still on the phone (not uploaded yet), with a way to drop it.
class LocalPhotoThumb extends StatelessWidget {
  const LocalPhotoThumb({required this.bytes, required this.onRemove, required this.index, super.key});

  final Uint8List bytes;
  final VoidCallback onRemove;
  final int index;

  @override
  Widget build(BuildContext context) {
    return Stack(
      children: [
        ClipRRect(
          borderRadius: BorderRadius.circular(8),
          child: Image.memory(
            bytes,
            width: _thumbSize,
            height: _thumbSize,
            fit: BoxFit.cover,
            semanticLabel: 'Photo ${index + 1}',
            errorBuilder: (_, _, _) => const _Placeholder(icon: Icons.broken_image_outlined),
          ),
        ),
        Positioned(
          right: 0,
          top: 0,
          child: IconButton.filledTonal(
            iconSize: 16,
            visualDensity: VisualDensity.compact,
            tooltip: 'Remove photo ${index + 1}',
            icon: const Icon(Icons.close),
            onPressed: onRemove,
          ),
        ),
      ],
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
      borderRadius: BorderRadius.circular(8),
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
  const _Placeholder({this.icon});

  final IconData? icon;

  @override
  Widget build(BuildContext context) => Container(
        width: _thumbSize,
        height: _thumbSize,
        color: Theme.of(context).colorScheme.surfaceContainerHighest,
        alignment: Alignment.center,
        child: icon == null
            ? const SizedBox.square(dimension: 20, child: CircularProgressIndicator(strokeWidth: 2))
            : Icon(icon),
      );
}

/// "Take photo" and "From gallery" side by side.
class AddPhotoButtons extends StatelessWidget {
  const AddPhotoButtons({required this.onPick, this.enabled = true, super.key});

  final void Function(PhotoOrigin origin) onPick;
  final bool enabled;

  @override
  Widget build(BuildContext context) => Wrap(
        spacing: 8,
        children: [
          OutlinedButton.icon(
            onPressed: enabled ? () => onPick(PhotoOrigin.camera) : null,
            icon: const Icon(Icons.photo_camera_outlined),
            label: const Text('Take photo'),
          ),
          OutlinedButton.icon(
            onPressed: enabled ? () => onPick(PhotoOrigin.gallery) : null,
            icon: const Icon(Icons.photo_library_outlined),
            label: const Text('From gallery'),
          ),
        ],
      );
}
