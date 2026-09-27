import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';

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

final reportablePlotsProvider = FutureProvider.autoDispose<List<ReportablePlot>>(
  (ref) => ref.watch(caseRepositoryProvider).reportablePlots(),
);

/// A photo's bytes, keyed "caseId/photoId". Photos never change, so once loaded they are kept
/// while anything on screen shows them.
final photoBytesProvider = FutureProvider.autoDispose.family<Uint8List, String>((ref, key) {
  final [caseId, photoId] = key.split('/');
  return ref.watch(caseRepositoryProvider).photoBytes(caseId, photoId);
});

/// The checklist changes perhaps once a season: kept for the whole session once loaded.
final symptomsProvider = FutureProvider<List<Symptom>>(
  (ref) => ref.watch(caseRepositoryProvider).symptoms(),
);
