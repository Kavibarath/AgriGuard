import 'dart:io';
import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:path_provider/path_provider.dart';

/// Files the app keeps between launches that are not secrets: the offline report queue with its
/// photos, and the last copy of the plot list and symptom checklist so the report form opens
/// without a signal.
///
/// The session stays in [TokenStorage] (Keystore-backed); this is plain app-private storage.
/// Queued reports hold a location and a note, so they live in the app's support directory,
/// which other apps cannot read and Android does not clear as a cache.
abstract class LocalStore {
  Future<String?> readText(String name);
  Future<void> writeText(String name, String contents);
  Future<Uint8List?> readBytes(String name);
  Future<void> writeBytes(String name, Uint8List bytes);
  Future<void> delete(String name);
}

class FileLocalStore implements LocalStore {
  FileLocalStore([Future<Directory> Function()? root]) : _root = root ?? getApplicationSupportDirectory;

  final Future<Directory> Function() _root;
  Directory? _dir;

  Future<File> _file(String name) async {
    final dir = _dir ??= await Directory('${(await _root()).path}${Platform.pathSeparator}agriguard').create(recursive: true);
    return File('${dir.path}${Platform.pathSeparator}$name');
  }

  @override
  Future<String?> readText(String name) async {
    final file = await _file(name);
    return await file.exists() ? file.readAsString() : null;
  }

  /// Written to a temporary file and renamed over the old one, so a phone that dies mid-write
  /// leaves the previous queue intact rather than half a JSON document.
  @override
  Future<void> writeText(String name, String contents) async {
    final file = await _file(name);
    final temp = File('${file.path}.tmp');
    await temp.writeAsString(contents, flush: true);
    await temp.rename(file.path);
  }

  @override
  Future<Uint8List?> readBytes(String name) async {
    final file = await _file(name);
    return await file.exists() ? file.readAsBytes() : null;
  }

  @override
  Future<void> writeBytes(String name, Uint8List bytes) async {
    await (await _file(name)).writeAsBytes(bytes, flush: true);
  }

  @override
  Future<void> delete(String name) async {
    final file = await _file(name);
    if (await file.exists()) await file.delete();
  }
}

/// Test double: same contract, nothing written to disk.
class InMemoryLocalStore implements LocalStore {
  final Map<String, Object> files = {};

  @override
  Future<String?> readText(String name) async => files[name] as String?;

  @override
  Future<void> writeText(String name, String contents) async => files[name] = contents;

  @override
  Future<Uint8List?> readBytes(String name) async => files[name] as Uint8List?;

  @override
  Future<void> writeBytes(String name, Uint8List bytes) async => files[name] = bytes;

  @override
  Future<void> delete(String name) async => files.remove(name);
}

final localStoreProvider = Provider<LocalStore>((ref) => FileLocalStore());
