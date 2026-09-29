import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_exception.dart';
import '../../core/photos/photo_source.dart';
import '../../core/storage/local_store.dart';
import '../auth/auth_controller.dart';
import 'case_models.dart';
import 'case_repository.dart';

/// Why a queued report has not gone yet.
enum PendingState {
  /// No connection last time; it is sent on the next attempt.
  waiting,

  /// The API refused it (the crop was harvested, a symptom was withdrawn…). Retrying the same
  /// report cannot help, so it waits for the farmer to read why and discard it.
  refused,
}

/// A report made without a signal, or the photos of one whose upload was cut off.
class PendingCase {
  const PendingCase({
    required this.newCase,
    required this.ownerId,
    required this.label,
    required this.photoFiles,
    this.caseId,
    this.state = PendingState.waiting,
    this.lastError,
  });

  factory PendingCase.fromJson(Map<String, dynamic> json) => PendingCase(
        newCase: NewCase.fromJson(json['newCase'] as Map<String, dynamic>),
        ownerId: json['ownerId'] as String,
        label: json['label'] as String,
        photoFiles: [for (final f in json['photoFiles'] as List) PendingPhoto.fromJson(f as Map<String, dynamic>)],
        caseId: json['caseId'] as String?,
        state: PendingState.values.byName(json['state'] as String),
        lastError: json['lastError'] as String?,
      );

  /// Carries the client reference and the capture time: the same report on every attempt.
  final NewCase newCase;

  /// Only the farmer who made it may send it. A second person signing in on the same phone
  /// must never submit, or even see, someone else's queued report.
  final String ownerId;

  /// "P-01 · Tomato", shown in the queue.
  final String label;

  final List<PendingPhoto> photoFiles;

  /// Set once the API has the case; only photos are left to send.
  final String? caseId;
  final PendingState state;
  final String? lastError;

  String get clientReference => newCase.clientReference!;
  DateTime get capturedAt => newCase.capturedAt!;

  PendingCase copyWith({String? caseId, List<PendingPhoto>? photoFiles, PendingState? state, String? lastError}) => PendingCase(
        newCase: newCase,
        ownerId: ownerId,
        label: label,
        photoFiles: photoFiles ?? this.photoFiles,
        caseId: caseId ?? this.caseId,
        state: state ?? this.state,
        lastError: lastError ?? this.lastError,
      );

  Map<String, dynamic> toJson() => {
        'newCase': newCase.toJson(),
        'ownerId': ownerId,
        'label': label,
        'photoFiles': [for (final f in photoFiles) f.toJson()],
        'caseId': caseId,
        'state': state.name,
        'lastError': lastError,
      };
}

/// A queued photo: its bytes are in [LocalStore] under [file], not in the queue's JSON.
class PendingPhoto {
  const PendingPhoto({required this.file, required this.name});

  factory PendingPhoto.fromJson(Map<String, dynamic> json) =>
      PendingPhoto(file: json['file'] as String, name: json['name'] as String);

  final String file;
  final String name;

  Map<String, dynamic> toJson() => {'file': file, 'name': name};
}

/// What one pass over the queue achieved.
class SyncResult {
  const SyncResult({this.sent = 0, this.refused = 0, this.offline = false});

  final int sent;
  final int refused;

  /// Stopped early for want of a connection; the rest waits for the next attempt.
  final bool offline;
}

/// The phone's outbox for crop reports (§8 offline support).
///
/// Every report gets a client reference before its first attempt. If that attempt cannot reach
/// the API, the report and its photos are saved here and sent later, oldest first, with the same
/// reference. The API stores a reference once, so a report whose first attempt did arrive (only
/// the reply was lost) is not doubled: the retry gets the existing case back.
class CaseOutbox extends AsyncNotifier<List<PendingCase>> {
  static const _file = 'outbox.json';
  bool _syncing = false;

  LocalStore get _store => ref.read(localStoreProvider);

  @override
  Future<List<PendingCase>> build() async {
    final raw = await _store.readText(_file);
    if (raw == null) return [];
    try {
      return [for (final item in jsonDecode(raw) as List) PendingCase.fromJson(item as Map<String, dynamic>)];
    } on FormatException {
      // A corrupt queue must not stop the app opening; the photos stay on disk untouched.
      return [];
    }
  }

  Future<List<PendingCase>> _all() async => state.value ?? await future;

  Future<void> _save(List<PendingCase> items) async {
    state = AsyncData(items);
    await _store.writeText(_file, jsonEncode([for (final i in items) i.toJson()]));
  }

  /// Saves a report that could not be sent, with its photos. [newCase] must carry a client
  /// reference; its capture time is set here if it has none. [caseId] is set when the case was
  /// created and only some photos are left.
  Future<void> enqueue({
    required String ownerId,
    required NewCase newCase,
    required String label,
    List<PickedPhoto> photos = const [],
    String? caseId,
  }) async {
    final reference = newCase.clientReference;
    if (reference == null) throw ArgumentError('A queued report needs a client reference.');
    final files = <PendingPhoto>[];
    for (var i = 0; i < photos.length; i++) {
      final file = 'outbox-$reference-$i';
      await _store.writeBytes(file, photos[i].bytes);
      files.add(PendingPhoto(file: file, name: photos[i].name));
    }
    final item = PendingCase(
      newCase: newCase.capturedAt == null ? newCase.withCapturedAt(DateTime.now().toUtc()) : newCase,
      ownerId: ownerId,
      label: label,
      photoFiles: files,
      caseId: caseId,
    );
    await _save([...await _all(), item]);
  }

  /// Sends what the signed-in farmer has waiting, oldest first. Stops at the first sign of no
  /// connection, because the rest would fail the same way. Safe to call at any time; a second
  /// call while one is running does nothing.
  Future<SyncResult> sync() async {
    final user = ref.read(currentUserProvider);
    if (_syncing || user == null) return const SyncResult();
    _syncing = true;
    var sent = 0;
    var refused = 0;
    try {
      final repository = ref.read(caseRepositoryProvider);
      for (final original in await _all()) {
        if (original.ownerId != user.id || original.state != PendingState.waiting) continue;
        var item = original;

        if (item.caseId == null) {
          try {
            final created = await repository.reportCase(item.newCase);
            item = item.copyWith(caseId: created.id);
            await _replace(original.clientReference, item);
          } on ApiException catch (e) {
            if (_isTransient(e)) return SyncResult(sent: sent, refused: refused, offline: true);
            refused++;
            await _replace(original.clientReference, item.copyWith(state: PendingState.refused, lastError: e.message));
            continue;
          }
        }

        // Photos one at a time, each removed from the queue as soon as it is stored.
        for (final photo in [...item.photoFiles]) {
          final bytes = await _store.readBytes(photo.file);
          if (bytes != null) {
            try {
              await repository.uploadPhoto(item.caseId!, bytes, photo.name);
            } on ApiException catch (e) {
              if (_isTransient(e)) return SyncResult(sent: sent, refused: refused, offline: true);
              // Refused (the case already has three photos, say): retrying cannot change that.
            }
          }
          await _store.delete(photo.file);
          item = item.copyWith(photoFiles: [...item.photoFiles]..remove(photo));
          await _replace(original.clientReference, item);
        }

        await _remove(original.clientReference);
        sent++;
      }
      return SyncResult(sent: sent, refused: refused);
    } finally {
      _syncing = false;
    }
  }

  /// Removes a report the API refused, with its photos. Only its owner may.
  Future<void> discard(String clientReference) async {
    final user = ref.read(currentUserProvider);
    final item = (await _all()).where((i) => i.clientReference == clientReference).firstOrNull;
    if (item == null || item.ownerId != user?.id) return;
    for (final photo in item.photoFiles) {
      await _store.delete(photo.file);
    }
    await _remove(clientReference);
  }

  /// No connection, a timeout, a server fault or rate limiting: worth trying again later.
  /// A 401 is treated the same way: the session will be refreshed or the farmer signs in again,
  /// and the report must survive either.
  static bool _isTransient(ApiException e) =>
      e.isNetworkError || e.isUnauthorized || e.statusCode == 429 || (e.statusCode ?? 0) >= 500;

  Future<void> _replace(String reference, PendingCase item) async =>
      _save([for (final i in await _all()) i.clientReference == reference ? item : i]);

  Future<void> _remove(String reference) async =>
      _save([for (final i in await _all()) if (i.clientReference != reference) i]);
}

final caseOutboxProvider = AsyncNotifierProvider<CaseOutbox, List<PendingCase>>(CaseOutbox.new);

/// The signed-in farmer's own queued reports; nobody else's are ever shown.
final myPendingCasesProvider = Provider<List<PendingCase>>((ref) {
  final userId = ref.watch(currentUserProvider)?.id;
  final all = ref.watch(caseOutboxProvider).value ?? const [];
  return [for (final item in all) if (item.ownerId == userId) item];
});
