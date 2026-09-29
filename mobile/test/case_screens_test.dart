import 'dart:convert';
import 'dart:typed_data';

import 'package:agriguard_mobile/app/app.dart';
import 'package:agriguard_mobile/app/router.dart';
import 'package:agriguard_mobile/core/api/api_exception.dart';
import 'package:agriguard_mobile/core/location/location_service.dart';
import 'package:agriguard_mobile/core/photos/photo_source.dart';
import 'package:agriguard_mobile/core/storage/token_storage.dart';
import 'package:agriguard_mobile/features/cases/case_detail_screen.dart';
import 'package:agriguard_mobile/features/cases/case_models.dart';
import 'package:agriguard_mobile/features/cases/case_repository.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'fixtures.dart';

class MockCaseRepository extends Mock implements CaseRepository {}

/// Hands back the next queued photo, as if the farmer had taken it; empty means "cancelled".
class FakePhotos implements PhotoSource {
  final List<PickedPhoto> queue = [];

  @override
  Future<PickedPhoto?> pick(PhotoOrigin origin) async => queue.isEmpty ? null : queue.removeAt(0);
}

/// A real 1×1 PNG, so Image.memory has something it can decode.
final tinyPng = base64Decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==');

class FakeLocation implements LocationService {
  FakeLocation(this.fix);

  final GeoFix? fix;

  @override
  Future<GeoFix?> currentFix() async => fix;
}

const plot = ReportablePlot(
  plotId: 'plot-1',
  plotCode: 'P-01',
  farmName: 'Green Acres',
  cropCycleId: 'cycle-1',
  cropName: 'Tomato',
  stage: 'Flowering',
  latitude: 6.9497,
  longitude: 80.7891,
);

const symptoms = [
  Symptom(code: 'leaf_brown_patches', label: 'Large brown or black patches on leaves'),
  Symptom(code: 'leaf_water_soaked_lesions', label: 'Water-soaked patches on leaves'),
];

CaseDetail caseDetail({
  CaseStatus status = CaseStatus.submitted,
  List<RunSummary> runs = const [],
}) =>
    CaseDetail(
      id: 'case-1',
      referenceNo: 'AG-2026-000007',
      status: status,
      severity: CaseSeverity.high,
      plotCode: 'P-01',
      cropName: 'Tomato',
      stage: 'Flowering',
      symptoms: symptoms,
      farmerNote: 'Spreading fast after the rain.',
      createdAt: DateTime.utc(2026, 9, 26),
      runs: runs,
    );

final prescription = Prescription(
  prescriptionNo: 'RX-2026-000002',
  productName: 'Mancozeb 80 WP',
  unit: 'Kilogram',
  dosePerHectare: 2.0,
  totalQuantity: 1.6,
  // Plain dates arrive as "yyyy-MM-dd" and parse as local dates, never UTC ones.
  sprayDate: DateTime(2026, 9, 27),
  earliestSafeHarvestDate: DateTime(2026, 10, 4),
  instructions: 'Spray 2 kg/ha of Mancozeb 80 WP on 2026-09-27.',
  orderNo: 'ORD-2026-000002',
  dealerName: 'Kandy Agro Supplies',
  packs: 2,
  orderTotal: 4800,
);

void main() {
  late MockCaseRepository repository;
  late FakePhotos photos;

  setUpAll(() {
    registerFallbackValue(
      const NewCase(plotId: '', cropCycleId: '', symptomCodes: [], severity: CaseSeverity.medium, latitude: 0, longitude: 0),
    );
    registerFallbackValue(Uint8List(0));
  });

  setUp(() {
    repository = MockCaseRepository();
    photos = FakePhotos();
    when(() => repository.reportablePlots()).thenAnswer((_) async => [plot]);
    when(() => repository.symptoms()).thenAnswer((_) async => symptoms);
    when(() => repository.myCases()).thenAnswer((_) async => []);
  });

  /// The real app, signed in as the farmer, opened at [path].
  Future<void> openApp(WidgetTester tester, String path, {GeoFix? fix}) async {
    // A tall phone (540 × 1600 logical px), so the whole report form is laid out at once —
    // the default 800 × 600 test window leaves the submit button unbuilt below the fold.
    tester.view.physicalSize = const Size(1080, 3200);
    tester.view.devicePixelRatio = 2.0;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          tokenStorageProvider.overrideWithValue(InMemoryTokenStorage(farmerSession())),
          caseRepositoryProvider.overrideWithValue(repository),
          locationServiceProvider.overrideWithValue(FakeLocation(fix)),
          photoSourceProvider.overrideWithValue(photos),
        ],
        child: const AgriGuardApp(),
      ),
    );
    await tester.pumpAndSettle();
    ProviderScope.containerOf(tester.element(find.byType(AgriGuardApp))).read(routerProvider).push(path);
    await tester.pumpAndSettle();
  }

  /// Replaces the app so any polling timer is cancelled before the test ends.
  Future<void> closeApp(WidgetTester tester) => tester.pumpWidget(const SizedBox());

  group('reporting a problem', () {
    Future<void> fillIn(WidgetTester tester) async {
      await tester.tap(find.byType(DropdownButtonFormField<ReportablePlot>));
      await tester.pumpAndSettle();
      await tester.tap(find.textContaining('P-01 · Tomato').last);
      await tester.pumpAndSettle();
      await tester.tap(find.text('Water-soaked patches on leaves'));
      await tester.tap(find.text('Bad'));
      await tester.enterText(find.widgetWithText(TextFormField, 'Anything else? (optional)'), '  White fuzz under leaves.  ');
      await tester.pump();
    }

    Future<void> submit(WidgetTester tester) async {
      await tester.ensureVisible(find.text('Report problem'));
      await tester.tap(find.text('Report problem'));
      await tester.pumpAndSettle();
    }

    testWidgets('sends the plot, symptoms, severity, note and the phone’s location, then opens the case', (tester) async {
      when(() => repository.reportCase(any())).thenAnswer((_) async => caseDetail());
      when(() => repository.getCase('case-1')).thenAnswer((_) async => caseDetail());
      await openApp(tester, '/cases/new', fix: const GeoFix(latitude: 6.95, longitude: 80.79, accuracyMetres: 8));

      expect(find.text('Your location will be sent (within 8 m).'), findsOneWidget);
      await fillIn(tester);
      await submit(tester);

      final sent = verify(() => repository.reportCase(captureAny())).captured.single as NewCase;
      expect(sent.toJson(), {
        'plotId': 'plot-1',
        'cropCycleId': 'cycle-1',
        'symptomCodes': ['leaf_water_soaked_lesions'],
        'severity': 'High',
        'latitude': 6.95,
        'longitude': 80.79,
        'farmerNote': 'White fuzz under leaves.',
      });
      // Straight to the new case, where the farmer can ask for advice.
      expect(find.text('AG-2026-000007'), findsOneWidget);
      expect(find.text('Get AI advice'), findsOneWidget);
    });

    testWidgets('falls back to the plot’s location when the phone cannot tell', (tester) async {
      when(() => repository.reportCase(any())).thenAnswer((_) async => caseDetail());
      when(() => repository.getCase('case-1')).thenAnswer((_) async => caseDetail());
      await openApp(tester, '/cases/new');

      expect(find.textContaining("the plot's registered location will be used"), findsOneWidget);
      await fillIn(tester);
      await submit(tester);

      final sent = verify(() => repository.reportCase(captureAny())).captured.single as NewCase;
      expect((sent.latitude, sent.longitude), (6.9497, 80.7891));
    });

    testWidgets('asks for a plot and a symptom before sending anything', (tester) async {
      await openApp(tester, '/cases/new');

      await submit(tester);

      expect(find.text('Tick at least one symptom you can see.'), findsOneWidget);
      verifyNever(() => repository.reportCase(any()));
    });

    testWidgets('shows the API’s reason when the report is refused', (tester) async {
      when(() => repository.reportCase(any())).thenThrow(
        ApiException(message: 'This crop cycle is Harvested. Report problems against the crop currently growing.', statusCode: 422),
      );
      await openApp(tester, '/cases/new');

      await fillIn(tester);
      await submit(tester);

      expect(find.textContaining('This crop cycle is Harvested'), findsOneWidget);
    });

    testWidgets('sends the photos after the case is created', (tester) async {
      when(() => repository.reportCase(any())).thenAnswer((_) async => caseDetail());
      when(() => repository.uploadPhoto(any(), any(), any()))
          .thenAnswer((_) async => const CasePhoto(id: 'photo-1', fileName: 'leaf.jpg'));
      when(() => repository.getCase('case-1')).thenAnswer((_) async => caseDetail());
      photos.queue.add(PickedPhoto(bytes: tinyPng, name: 'leaf.jpg'));
      await openApp(tester, '/cases/new');

      await tester.tap(find.text('Take photo'));
      await tester.pumpAndSettle();
      expect(find.bySemanticsLabel('Photo 1'), findsOneWidget);
      await fillIn(tester);
      await submit(tester);

      verifyInOrder([
        () => repository.reportCase(any()),
        () => repository.uploadPhoto('case-1', tinyPng, 'leaf.jpg'),
      ]);
    });

    testWidgets('keeps the report when a photo fails to send, and says so', (tester) async {
      when(() => repository.reportCase(any())).thenAnswer((_) async => caseDetail());
      when(() => repository.uploadPhoto(any(), any(), any()))
          .thenThrow(ApiException(message: 'Could not reach AgriGuard.', isNetworkError: true));
      when(() => repository.getCase('case-1')).thenAnswer((_) async => caseDetail());
      photos.queue.add(PickedPhoto(bytes: tinyPng, name: 'leaf.jpg'));
      await openApp(tester, '/cases/new');

      await tester.tap(find.text('Take photo'));
      await tester.pumpAndSettle();
      await fillIn(tester);
      await submit(tester);

      expect(find.text('AG-2026-000007'), findsOneWidget);
      expect(find.textContaining('1 photo could not be sent'), findsOneWidget);
    });

    testWidgets('explains when no plot has a crop growing', (tester) async {
      when(() => repository.reportablePlots()).thenAnswer((_) async => []);
      await openApp(tester, '/cases/new');

      expect(find.textContaining('None of your plots has a crop growing'), findsOneWidget);
    });
  });

  group('following a case', () {
    testWidgets('lists the farmer’s cases and opens one', (tester) async {
      when(() => repository.myCases()).thenAnswer((_) async => [
            CaseSummary(
              id: 'case-1',
              referenceNo: 'AG-2026-000007',
              status: CaseStatus.pendingApproval,
              severity: CaseSeverity.high,
              plotCode: 'P-01',
              cropName: 'Tomato',
              createdAt: DateTime.utc(2026, 9, 26),
            ),
          ]);
      when(() => repository.getCase('case-1')).thenAnswer((_) async => caseDetail(status: CaseStatus.prescribed));
      await openApp(tester, '/home');

      await tester.tap(find.text('My crop problems'));
      await tester.pumpAndSettle();
      expect(find.text('Awaiting agronomist review'), findsOneWidget);

      await tester.tap(find.text('Tomato · P-01'));
      await tester.pumpAndSettle();
      expect(find.text('AG-2026-000007'), findsOneWidget);
    });

    testWidgets('asks for AI advice and shows the agent at work', (tester) async {
      var status = CaseStatus.submitted;
      when(() => repository.getCase('case-1')).thenAnswer((_) async => caseDetail(status: status));
      when(() => repository.requestAdvice('case-1')).thenAnswer((_) async {
        status = CaseStatus.agentProcessing;
        return 'Planning';
      });
      await openApp(tester, '/cases/case-1');

      await tester.tap(find.text('Get AI advice'));
      await tester.pumpAndSettle();

      verify(() => repository.requestAdvice('case-1')).called(1);
      expect(find.textContaining('The AI is studying your case'), findsOneWidget);
      expect(find.text('Get AI advice'), findsNothing);
      await closeApp(tester);
    });

    testWidgets('updates by itself while someone is working on the case', (tester) async {
      var reads = 0;
      when(() => repository.getCase('case-1')).thenAnswer((_) async {
        reads += 1;
        return caseDetail(status: reads == 1 ? CaseStatus.agentProcessing : CaseStatus.pendingApproval);
      });
      await openApp(tester, '/cases/case-1');
      expect(find.textContaining('The AI is studying your case'), findsOneWidget);

      await tester.pump(casePollInterval);
      await tester.pumpAndSettle();

      expect(find.textContaining('An agronomist is reviewing it now'), findsOneWidget);
      await closeApp(tester);
    });

    testWidgets('shows the prescription with the safe-harvest date first', (tester) async {
      when(() => repository.getCase('case-1')).thenAnswer(
        (_) async => caseDetail(status: CaseStatus.prescribed, runs: [const RunSummary(id: 'run-1', status: 'Completed')]),
      );
      when(() => repository.prescriptionFor('run-1')).thenAnswer((_) async => prescription);
      await openApp(tester, '/cases/case-1');

      expect(find.text('Prescription RX-2026-000002'), findsOneWidget);
      expect(find.text('Do not harvest before 2026-10-04.'), findsOneWidget);
      expect(find.text('Mancozeb 80 WP'), findsOneWidget);
      expect(find.text('Kandy Agro Supplies'), findsOneWidget);
      // Amounts carry their unit, and money its thousands separator.
      expect(find.text('2 kg per hectare'), findsOneWidget);
      expect(find.text('1.6 kg'), findsOneWidget);
      expect(find.text('ORD-2026-000002 · 2 pack(s) · LKR 4,800'), findsOneWidget);
    });

    testWidgets('explains a rejected treatment with the agronomist’s reason', (tester) async {
      when(() => repository.getCase('case-1')).thenAnswer((_) async => caseDetail(
            status: CaseStatus.rejected,
            runs: [
              const RunSummary(id: 'run-1', status: 'Rejected', failureReason: 'Rejected by the agronomist: Looks like bacterial wilt.'),
            ],
          ));
      await openApp(tester, '/cases/case-1');

      expect(find.textContaining('did not approve'), findsOneWidget);
      expect(find.textContaining('Reason: Looks like bacterial wilt.'), findsOneWidget);
    });

    testWidgets('shows the case’s photos and adds another', (tester) async {
      var stored = [const CasePhoto(id: 'photo-1', fileName: 'leaf.jpg')];
      when(() => repository.getCase('case-1')).thenAnswer((_) async => CaseDetail(
            id: 'case-1',
            referenceNo: 'AG-2026-000007',
            status: CaseStatus.submitted,
            severity: CaseSeverity.high,
            plotCode: 'P-01',
            cropName: 'Tomato',
            stage: 'Flowering',
            symptoms: symptoms,
            farmerNote: null,
            createdAt: DateTime.utc(2026, 9, 26),
            runs: const [],
            photos: stored,
          ));
      when(() => repository.photoBytes('case-1', any())).thenAnswer((_) async => tinyPng);
      when(() => repository.uploadPhoto('case-1', any(), any())).thenAnswer((_) async {
        stored = [...stored, const CasePhoto(id: 'photo-2', fileName: 'leaf2.jpg')];
        return stored.last;
      });
      photos.queue.add(PickedPhoto(bytes: tinyPng, name: 'leaf2.jpg'));
      await openApp(tester, '/cases/case-1');

      expect(find.bySemanticsLabel(RegExp('Photo 1')), findsOneWidget);
      await tester.tap(find.text('From gallery'));
      await tester.pumpAndSettle();

      verify(() => repository.uploadPhoto('case-1', tinyPng, 'leaf2.jpg')).called(1);
      expect(find.bySemanticsLabel(RegExp('Photo 2')), findsOneWidget);
    });

    testWidgets('explains a case the AI handed to an agronomist, with what to do meanwhile', (tester) async {
      when(() => repository.getCase('case-1')).thenAnswer((_) async => caseDetail(
            status: CaseStatus.awaitingManualReview,
            runs: [
              RunSummary.fromJson({
                'id': 'run-1',
                'status': 'Escalated',
                'failureReason': 'No approved product controls Bacterial wilt on Tomato, so nothing can be prescribed.',
                'farmerAdvice': 'Pull out wilted plants and burn them away from the field.\nDo not replant tomato in this bed this season.',
              }),
            ],
          ));
      await openApp(tester, '/cases/case-1');

      expect(find.textContaining('The AI thinks an agronomist should look at this first.'), findsOneWidget);
      expect(find.textContaining('Bacterial wilt'), findsOneWidget);
      expect(find.text('What you can do now'), findsOneWidget);
      expect(find.text('Pull out wilted plants and burn them away from the field.'), findsOneWidget);
      expect(find.text('Do not replant tomato in this bed this season.'), findsOneWidget);
    });

    testWidgets('shows the care tips while a treatment awaits the agronomist', (tester) async {
      when(() => repository.getCase('case-1')).thenAnswer((_) async => caseDetail(
            status: CaseStatus.pendingApproval,
            runs: [const RunSummary(id: 'run-1', status: 'PendingApproval', advice: ['Remove and burn the worst leaves.'])],
          ));
      await openApp(tester, '/cases/case-1');

      expect(find.text('Remove and burn the worst leaves.'), findsOneWidget);
    });

    testWidgets('offers to try again when the AI could not finish', (tester) async {
      when(() => repository.getCase('case-1')).thenAnswer((_) async => caseDetail(
            status: CaseStatus.awaitingManualReview,
            runs: [const RunSummary(id: 'run-1', status: 'Failed', failureReason: 'The language model is unreachable.')],
          ));
      await openApp(tester, '/cases/case-1');

      expect(find.textContaining('The AI could not finish this time'), findsOneWidget);
      expect(find.text('Ask the AI again'), findsOneWidget);
    });
  });
}
