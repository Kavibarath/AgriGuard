import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_exception.dart';
import '../../core/storage/local_store.dart';
import '../auth/auth_controller.dart';
import 'case_models.dart';
import 'case_repository.dart';

final myCasesProvider = FutureProvider.autoDispose<List<CaseSummary>>(
  (ref) => ref.watch(caseRepositoryProvider).myCases(),
);

final caseDetailProvider = FutureProvider.autoDispose.family<CaseDetail, String>(
  (ref, id) => ref.watch(caseRepositoryProvider).getCase(id),
);

/// Only fetched once a case is Prescribed; keyed by the run that produced it.
final prescriptionProvider = FutureProvider.autoDispose.family<Prescription?, String>(
  (ref, runId) => ref.watch(caseRepositoryProvider).prescriptionFor(runId),
);

/// Fetched fresh when there is a signal, and saved; without one, the last saved copy, so a farmer
/// in a field with no coverage can still fill in a report for the offline queue. Kept per user,
/// because plots are.
final reportablePlotsProvider = FutureProvider.autoDispose<List<ReportablePlot>>((ref) {
  final userId = ref.watch(currentUserProvider)?.id ?? 'anonymous';
  return _cachedOnline(
    ref,
    'plots-$userId.json',
    fetch: () => ref.read(caseRepositoryProvider).reportablePlots(),
    encode: (plots) => [for (final p in plots) p.toJson()],
    decode: (json) => [for (final p in json as List) ReportablePlot.fromJson(p as Map<String, dynamic>)],
  );
});

/// A photo's bytes, keyed "caseId/photoId". Photos never change, so once loaded they are kept
/// while anything on screen shows them.
final photoBytesProvider = FutureProvider.autoDispose.family<Uint8List, String>((ref, key) {
  final [caseId, photoId] = key.split('/');
  return ref.watch(caseRepositoryProvider).photoBytes(caseId, photoId);
});

/// The checklist changes perhaps once a season: kept for the whole session once loaded, and
/// saved on the phone for the offline form like the plot list.
final symptomsProvider = FutureProvider<List<Symptom>>(
  (ref) => _cachedOnline(
    ref,
    'symptoms.json',
    fetch: () => ref.read(caseRepositoryProvider).symptoms(),
    encode: (symptoms) => [for (final s in symptoms) s.toJson()],
    decode: (json) => [for (final s in json as List) Symptom.fromJson(s as Map<String, dynamic>)],
  ),
);

/// The network first; on a network failure only, the copy saved by the last success. Any other
/// failure (403, 500) is shown as it is: an old copy must not hide a real problem.
Future<T> _cachedOnline<T>(
  Ref ref,
  String file, {
  required Future<T> Function() fetch,
  required Object Function(T value) encode,
  required T Function(Object? json) decode,
}) async {
  final store = ref.read(localStoreProvider);
  try {
    final value = await fetch();
    // Best effort: a full disk must not break the screen that just loaded fine.
    try {
      await store.writeText('lookup-$file', jsonEncode(encode(value)));
    } on Exception {
      // The next successful load tries again.
    }
    return value;
  } on ApiException catch (e) {
    if (!e.isNetworkError) rethrow;
    final saved = await store.readText('lookup-$file');
    if (saved == null) rethrow;
    return decode(jsonDecode(saved));
  }
}
