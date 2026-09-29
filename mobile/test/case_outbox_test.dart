import 'dart:typed_data';

import 'package:agriguard_mobile/core/api/api_exception.dart';
import 'package:agriguard_mobile/core/photos/photo_source.dart';
import 'package:agriguard_mobile/core/storage/local_store.dart';
import 'package:agriguard_mobile/features/auth/auth_controller.dart';
import 'package:agriguard_mobile/features/auth/auth_models.dart';
import 'package:agriguard_mobile/features/cases/case_models.dart';
import 'package:agriguard_mobile/features/cases/case_outbox.dart';
import 'package:agriguard_mobile/features/cases/case_repository.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'fixtures.dart';

class MockCaseRepository extends Mock implements CaseRepository {}

const neighbour = UserSummary(
  id: '01a0aaaf-0000-0000-0000-000000000009',
  email: 'farmer.kandapola@agriguard.demo',
  fullName: 'Kamala Herath',
  role: UserRole.farmer,
  districtId: null,
);

NewCase report(String reference, {String plotId = 'plot-1'}) => NewCase(
      plotId: plotId,
      cropCycleId: 'cycle-1',
      symptomCodes: const ['leaf_brown_patches'],
      severity: CaseSeverity.high,
      latitude: 6.95,
      longitude: 80.79,
      clientReference: reference,
    );

CaseDetail created(String id) => CaseDetail(
      id: id,
      referenceNo: 'AG-2026-000009',
      status: CaseStatus.submitted,
      severity: CaseSeverity.high,
      plotCode: 'P-01',
      cropName: 'Tomato',
      stage: 'Flowering',
      symptoms: const [],
      farmerNote: null,
      createdAt: DateTime.utc(2026, 9, 29),
      runs: const [],
    );

final offline = ApiException(message: 'Could not reach AgriGuard. Check your connection.', isNetworkError: true);
final leaf = PickedPhoto(bytes: Uint8List.fromList([1, 2, 3]), name: 'leaf.jpg');

void main() {
  late MockCaseRepository repository;
  late InMemoryLocalStore store;

  setUpAll(() {
    registerFallbackValue(report('fallback'));
    registerFallbackValue(Uint8List(0));
  });

  setUp(() {
    repository = MockCaseRepository();
    store = InMemoryLocalStore();
  });

  ProviderContainer container({UserSummary? as = farmer}) {
    final c = ProviderContainer(overrides: [
      caseRepositoryProvider.overrideWithValue(repository),
      localStoreProvider.overrideWithValue(store),
      currentUserProvider.overrideWithValue(as),
    ]);
    addTearDown(c.dispose);
    return c;
  }

  Future<CaseOutbox> outboxOf(ProviderContainer c) async {
    await c.read(caseOutboxProvider.future);
    return c.read(caseOutboxProvider.notifier);
  }

  test('a queued report survives the app being closed', () async {
    final first = container();
    await (await outboxOf(first)).enqueue(ownerId: farmer.id, newCase: report('ref-1'), label: 'P-01 · Tomato', photos: [leaf]);

    // A new container over the same files is the app starting again.
    final restarted = container();
    final items = await restarted.read(caseOutboxProvider.future);
    expect(items.single.clientReference, 'ref-1');
    expect(items.single.label, 'P-01 · Tomato');
    expect(await store.readBytes(items.single.photoFiles.single.file), leaf.bytes);
  });

  test('sends with the same reference and the time it was made, then its photos, then forgets it', () async {
    final c = container();
    final outbox = await outboxOf(c);
    await outbox.enqueue(ownerId: farmer.id, newCase: report('ref-1'), label: 'P-01 · Tomato', photos: [leaf]);
    final capturedAt = c.read(caseOutboxProvider).value!.single.capturedAt;
    when(() => repository.reportCase(any())).thenAnswer((_) async => created('case-9'));
    when(() => repository.uploadPhoto(any(), any(), any())).thenAnswer((_) async => const CasePhoto(id: 'photo-1', fileName: 'leaf.jpg'));

    final result = await outbox.sync();

    final sent = verify(() => repository.reportCase(captureAny())).captured.single as NewCase;
    expect(sent.toJson()['clientReference'], 'ref-1');
    expect(sent.toJson()['capturedAt'], capturedAt.toIso8601String());
    verify(() => repository.uploadPhoto('case-9', leaf.bytes, 'leaf.jpg')).called(1);
    expect(result.sent, 1);
    expect(c.read(caseOutboxProvider).value, isEmpty);
    expect(store.files.keys.where((k) => k.startsWith('outbox-')), isEmpty);
  });

  test('sends the oldest first and stops at the first sign of no connection', () async {
    final c = container();
    final outbox = await outboxOf(c);
    await outbox.enqueue(ownerId: farmer.id, newCase: report('ref-1'), label: 'P-01 · Tomato');
    await outbox.enqueue(ownerId: farmer.id, newCase: report('ref-2'), label: 'P-02 · Chilli');
    when(() => repository.reportCase(any())).thenThrow(offline);

    final result = await outbox.sync();

    expect(result.offline, isTrue);
    // One attempt, not one per report: the second would fail the same way.
    verify(() => repository.reportCase(any())).called(1);
    expect(c.read(caseOutboxProvider).value!.map((p) => p.clientReference), ['ref-1', 'ref-2']);
  });

  test('a case made before the photos were cut off is not reported twice', () async {
    final c = container();
    final outbox = await outboxOf(c);
    await outbox.enqueue(ownerId: farmer.id, newCase: report('ref-1'), label: 'P-01 · Tomato', photos: [leaf], caseId: 'case-9');
    when(() => repository.uploadPhoto(any(), any(), any())).thenAnswer((_) async => const CasePhoto(id: 'photo-1', fileName: 'leaf.jpg'));

    await outbox.sync();

    verifyNever(() => repository.reportCase(any()));
    verify(() => repository.uploadPhoto('case-9', leaf.bytes, 'leaf.jpg')).called(1);
    expect(c.read(caseOutboxProvider).value, isEmpty);
  });

  test('a report the API refuses is kept with the reason until the farmer discards it', () async {
    final c = container();
    final outbox = await outboxOf(c);
    await outbox.enqueue(ownerId: farmer.id, newCase: report('ref-1'), label: 'P-01 · Tomato', photos: [leaf]);
    when(() => repository.reportCase(any())).thenThrow(
      ApiException(message: 'This crop cycle is Harvested. Report problems against the crop currently growing.', statusCode: 422),
    );

    final result = await outbox.sync();

    expect(result.refused, 1);
    final item = c.read(caseOutboxProvider).value!.single;
    expect(item.state, PendingState.refused);
    expect(item.lastError, contains('Harvested'));

    // Not retried: sending the same refused report again cannot succeed.
    await outbox.sync();
    verify(() => repository.reportCase(any())).called(1);

    await outbox.discard('ref-1');
    expect(c.read(caseOutboxProvider).value, isEmpty);
    expect(store.files.keys.where((k) => k.startsWith('outbox-')), isEmpty);
  });

  test('another farmer on the same phone neither sees nor sends the queue', () async {
    final mine = container();
    await (await outboxOf(mine)).enqueue(ownerId: farmer.id, newCase: report('ref-1'), label: 'P-01 · Tomato');

    final theirs = container(as: neighbour);
    final outbox = await outboxOf(theirs);
    await outbox.sync();
    await outbox.discard('ref-1');

    expect(theirs.read(myPendingCasesProvider), isEmpty);
    verifyNever(() => repository.reportCase(any()));
    // Still there for its owner.
    expect(theirs.read(caseOutboxProvider).value!.single.ownerId, farmer.id);
  });

  test('client references are random version 4 UUIDs', () {
    final a = newClientReference();
    final b = newClientReference();
    expect(a, matches(RegExp(r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$')));
    expect(a, isNot(b));
  });
}
