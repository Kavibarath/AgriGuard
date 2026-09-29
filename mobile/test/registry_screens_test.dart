import 'package:agriguard_mobile/app/app.dart';
import 'package:agriguard_mobile/app/router.dart';
import 'package:agriguard_mobile/core/api/api_exception.dart';
import 'package:agriguard_mobile/core/location/location_service.dart';
import 'package:agriguard_mobile/core/storage/token_storage.dart';
import 'package:agriguard_mobile/features/auth/auth_models.dart';
import 'package:agriguard_mobile/features/registry/registry_models.dart';
import 'package:agriguard_mobile/features/registry/registry_repository.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'fixtures.dart';

class MockRegistryRepository extends Mock implements RegistryRepository {}

class FakeLocation implements LocationService {
  FakeLocation(this.fix);

  final GeoFix? fix;

  @override
  Future<GeoFix?> currentFix() async => fix;
}

const farm = Farm(id: 'farm-1', name: 'Dry Zone Farm', village: 'Mihintale', districtName: 'Anuradhapura', plotCount: 1, totalAreaHectares: 0.8);

final growingPlot = Plot(
  id: 'plot-1',
  farmId: 'farm-1',
  farmName: 'Dry Zone Farm',
  plotCode: 'A-01',
  name: 'Tank field',
  areaHectares: 0.8,
  latitude: 8.351,
  longitude: 80.504,
  soilType: SoilType.sandyLoam,
  status: 'Active',
  activeCycle: ActiveCycle(id: 'cycle-1', cropName: 'Tomato', stage: CropStage.flowering, expectedHarvestDate: DateTime(2026, 11, 17)),
);

const emptyPlot = Plot(
  id: 'plot-2',
  farmId: 'farm-1',
  farmName: 'Dry Zone Farm',
  plotCode: 'A-03',
  areaHectares: 0.4,
  latitude: 8.352,
  longitude: 80.505,
  soilType: SoilType.loam,
  status: 'Active',
);

final cycle = CropCycle(
  id: 'cycle-1',
  plotId: 'plot-1',
  cropName: 'Tomato',
  sownDate: DateTime(2026, 7, 30),
  stage: CropStage.flowering,
  status: 'Active',
  harvestDate: DateTime(2026, 11, 17),
  daysToHarvest: 49,
  allowedNextStages: const [CropStage.fruitSet],
  transitions: [StageTransition(from: CropStage.vegetative, to: CropStage.flowering, at: DateTime.utc(2026, 9, 10), note: 'First flowers')],
);

final safety = SafetyProfile(
  cropName: 'Tomato',
  harvestDate: DateTime(2026, 11, 17),
  daysToHarvest: 49,
  applications: [AppliedTreatment(productName: 'Azoxystrobin 25 SC', date: DateTime(2026, 9, 29), status: 'Scheduled')],
  productWindows: [
    ProductWindow(
      productName: 'Mancozeb 80 WP',
      activeIngredient: 'Mancozeb',
      canSprayToday: true,
      lastSafeSprayDate: DateTime(2026, 11, 10),
      applicationsUsed: 0,
      maxApplications: 4,
    ),
    ProductWindow(
      productName: 'Azoxystrobin 25 SC',
      activeIngredient: 'Azoxystrobin',
      canSprayToday: false,
      lastSafeSprayDate: DateTime(2026, 11, 14),
      applicationsUsed: 1,
      maxApplications: 2,
      blockedExplanation: 'Sprayed on 2026-09-29; wait until 2026-10-13 before using Azoxystrobin again.',
    ),
  ],
  reEntryClearAt: DateTime.now().toUtc().add(const Duration(hours: 5)),
);

const crops = [Crop(id: 'crop-tom', name: 'Tomato', maturityDays: 110), Crop(id: 'crop-oni', name: 'Big Onion', maturityDays: 120)];
const districts = [District(id: '0199e7c2-3b1e-7f0a-9c1d-0d5f5a1c2b3e', name: 'Nuwara Eliya'), District(id: 'd-anu', name: 'Anuradhapura')];

AuthSession agronomistSession() {
  final session = farmerSession();
  return AuthSession(
    accessToken: session.accessToken,
    accessTokenExpiresAt: session.accessTokenExpiresAt,
    refreshToken: session.refreshToken,
    refreshTokenExpiresAt: session.refreshTokenExpiresAt,
    user: const UserSummary(
      id: 'agronomist-1',
      email: 'agronomist@agriguard.demo',
      fullName: 'Dr. Nimali Fernando',
      role: UserRole.fieldAgronomist,
      districtId: 'd-anu',
    ),
  );
}

void main() {
  late MockRegistryRepository repository;

  setUpAll(() {
    registerFallbackValue(const NewPlot(farmId: '', plotCode: '', areaHectares: 0, latitude: 0, longitude: 0, soilType: SoilType.loam));
    registerFallbackValue(CropStage.sown);
    registerFallbackValue(DateTime(2026));
  });

  setUp(() {
    repository = MockRegistryRepository();
    when(() => repository.farms()).thenAnswer((_) async => [farm]);
    when(() => repository.farm('farm-1')).thenAnswer((_) async => farm);
    when(() => repository.plots('farm-1')).thenAnswer((_) async => [growingPlot, emptyPlot]);
    when(() => repository.plot('plot-1')).thenAnswer((_) async => growingPlot);
    when(() => repository.plot('plot-2')).thenAnswer((_) async => emptyPlot);
    when(() => repository.cropCycle('cycle-1')).thenAnswer((_) async => cycle);
    when(() => repository.safetyProfile(any())).thenAnswer((_) async => safety);
    when(() => repository.districts()).thenAnswer((_) async => districts);
    when(() => repository.crops()).thenAnswer((_) async => crops);
  });

  Future<void> openApp(WidgetTester tester, String path, {GeoFix? fix, AuthSession? session}) async {
    tester.view.physicalSize = const Size(1080, 3200);
    tester.view.devicePixelRatio = 2.0;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          tokenStorageProvider.overrideWithValue(InMemoryTokenStorage(session ?? farmerSession())),
          registryRepositoryProvider.overrideWithValue(repository),
          locationServiceProvider.overrideWithValue(FakeLocation(fix)),
        ],
        child: const AgriGuardApp(),
      ),
    );
    await tester.pumpAndSettle();
    ProviderScope.containerOf(tester.element(find.byType(AgriGuardApp))).read(routerProvider).push(path);
    await tester.pumpAndSettle();
  }

  group('farms', () {
    testWidgets('lists the farmer’s farms and opens one to its plots', (tester) async {
      await openApp(tester, '/farms');

      expect(find.text('My farms & plots'), findsOneWidget);
      expect(find.text('Mihintale · Anuradhapura · 1 plot · 0.8 ha'), findsOneWidget);
      await tester.tap(find.text('Dry Zone Farm'));
      await tester.pumpAndSettle();

      expect(find.text('A-01 · Tank field'), findsOneWidget);
      expect(find.text('0.8 ha · Tomato, Flowering · harvest 2026-11-17'), findsOneWidget);
      expect(find.text('0.4 ha · nothing growing'), findsOneWidget);
    });

    testWidgets('adds a farm in the farmer’s own district and opens it', (tester) async {
      when(() => repository.createFarm(name: any(named: 'name'), village: any(named: 'village'), districtId: any(named: 'districtId')))
          .thenAnswer((_) async => farm);
      await openApp(tester, '/farms');

      await tester.tap(find.text('Add farm'));
      await tester.pumpAndSettle();
      await tester.enterText(find.widgetWithText(TextFormField, 'Farm name'), 'Hill Farm');
      await tester.tap(find.widgetWithText(FilledButton, 'Add farm'));
      await tester.pumpAndSettle();

      verify(() => repository.createFarm(name: 'Hill Farm', village: '', districtId: farmer.districtId!)).called(1);
      expect(find.text('A-01 · Tank field'), findsOneWidget);
    });

    testWidgets('lets an agronomist look but not add', (tester) async {
      await openApp(tester, '/farms', session: agronomistSession());

      expect(find.text('Farms in my district'), findsOneWidget);
      expect(find.text('Add farm'), findsNothing);
    });
  });

  group('adding a plot', () {
    testWidgets('takes its location from the phone', (tester) async {
      when(() => repository.createPlot(any())).thenAnswer((_) async => emptyPlot);
      await openApp(tester, '/farms/farm-1/plots/new', fix: const GeoFix(latitude: 8.3521, longitude: 80.5049, accuracyMetres: 6));

      expect(find.textContaining('within 6 m'), findsOneWidget);
      await tester.enterText(find.widgetWithText(TextFormField, 'Plot code'), 'A-03');
      await tester.enterText(find.widgetWithText(TextFormField, 'Area (hectares)'), '0.4');
      await tester.tap(find.widgetWithText(FilledButton, 'Add plot'));
      await tester.pumpAndSettle();

      final sent = verify(() => repository.createPlot(captureAny())).captured.single as NewPlot;
      expect((sent.farmId, sent.plotCode, sent.areaHectares, sent.latitude, sent.longitude), ('farm-1', 'A-03', 0.4, 8.3521, 80.5049));
      // Opened the new plot, ready to start a crop.
      expect(find.text('Start a crop'), findsOneWidget);
    });

    testWidgets('without a location fix, asks for the coordinates before sending anything', (tester) async {
      await openApp(tester, '/farms/farm-1/plots/new');

      expect(find.textContaining('Location unavailable'), findsOneWidget);
      await tester.enterText(find.widgetWithText(TextFormField, 'Plot code'), 'A-03');
      await tester.enterText(find.widgetWithText(TextFormField, 'Area (hectares)'), '0');
      await tester.tap(find.widgetWithText(FilledButton, 'Add plot'));
      await tester.pumpAndSettle();

      expect(find.text('Enter the area in hectares, more than 0'), findsOneWidget);
      expect(find.text('Between -90 and 90'), findsOneWidget);
      verifyNever(() => repository.createPlot(any()));
    });

    testWidgets('puts the server’s reason on the field it is about', (tester) async {
      when(() => repository.createPlot(any())).thenThrow(ApiException(
            message: 'One or more validation errors occurred.',
            statusCode: 400,
            fieldErrors: const {'areaHectares': ['That area looks wrong — enter hectares, not square metres.']},
          ));
      await openApp(tester, '/farms/farm-1/plots/new', fix: const GeoFix(latitude: 8.35, longitude: 80.5, accuracyMetres: 10));

      await tester.enterText(find.widgetWithText(TextFormField, 'Plot code'), 'A-03');
      await tester.enterText(find.widgetWithText(TextFormField, 'Area (hectares)'), '4000');
      await tester.tap(find.widgetWithText(FilledButton, 'Add plot'));
      await tester.pumpAndSettle();

      expect(find.text('That area looks wrong — enter hectares, not square metres.'), findsOneWidget);
    });
  });

  group('a plot', () {
    testWidgets('shows the crop’s stage among all the stages, with its history', (tester) async {
      await openApp(tester, '/plots/plot-1');

      expect(find.textContaining('Sown 2026-07-30 · harvest 2026-11-17 (in 49 days)'), findsOneWidget);
      expect(find.bySemanticsLabel('Vegetative: done'), findsOneWidget);
      expect(find.bySemanticsLabel('Flowering: current stage'), findsOneWidget);
      expect(find.bySemanticsLabel('Harvested: to come'), findsOneWidget);
      expect(find.textContaining('Vegetative → Flowering — First flowers'), findsOneWidget);
    });

    testWidgets('moves the crop on one stage after the farmer confirms', (tester) async {
      when(() => repository.advanceStage('cycle-1', CropStage.fruitSet, note: any(named: 'note'))).thenAnswer((_) async => cycle);
      await openApp(tester, '/plots/plot-1');

      await tester.tap(find.text('Mark as Fruit set'));
      await tester.pumpAndSettle();
      await tester.enterText(find.widgetWithText(TextField, 'Note (optional)'), 'Most trusses set');
      await tester.tap(find.text('Confirm'));
      await tester.pumpAndSettle();

      verify(() => repository.advanceStage('cycle-1', CropStage.fruitSet, note: 'Most trusses set')).called(1);
    });

    testWidgets('explains a stage change the server refuses', (tester) async {
      when(() => repository.advanceStage(any(), any(), note: any(named: 'note'))).thenThrow(
        ApiException(message: 'This crop cycle is Harvested and can no longer change stage.', statusCode: 422, code: 'CYCLE_NOT_ACTIVE'),
      );
      await openApp(tester, '/plots/plot-1');

      await tester.tap(find.text('Mark as Fruit set'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Confirm'));
      await tester.pumpAndSettle();

      expect(find.text('This crop cycle is Harvested and can no longer change stage.'), findsOneWidget);
    });

    testWidgets('starts a crop on an empty plot', (tester) async {
      when(() => repository.startCycle(plotId: any(named: 'plotId'), cropId: any(named: 'cropId'), sownDate: any(named: 'sownDate')))
          .thenAnswer((_) async => cycle);
      await openApp(tester, '/plots/plot-2');

      await tester.tap(find.text('Start crop'));
      await tester.pumpAndSettle();
      expect(find.text('Choose the crop you sowed.'), findsOneWidget);

      await tester.tap(find.byType(DropdownButtonFormField<Crop>));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Big Onion (120 days)').last);
      await tester.pumpAndSettle();
      await tester.tap(find.text('Start crop'));
      await tester.pumpAndSettle();

      final today = DateUtils.dateOnly(DateTime.now());
      verify(() => repository.startCycle(plotId: 'plot-2', cropId: 'crop-oni', sownDate: today)).called(1);
    });

    testWidgets('says which products may be sprayed today, which may not and why', (tester) async {
      await openApp(tester, '/plots/plot-1');

      expect(find.textContaining('Keep people out of the field until'), findsOneWidget);
      expect(find.textContaining('Can be sprayed today. Last safe day before harvest: 2026-11-10. 0 of 4 sprays used.'), findsOneWidget);
      expect(find.text('Sprayed on 2026-09-29; wait until 2026-10-13 before using Azoxystrobin again.'), findsOneWidget);
      expect(find.text('2026-09-29 · Azoxystrobin 25 SC · scheduled'), findsOneWidget);
    });

    testWidgets('shows an agronomist the plot but no buttons to change it', (tester) async {
      await openApp(tester, '/plots/plot-1', session: agronomistSession());

      expect(find.bySemanticsLabel('Flowering: current stage'), findsOneWidget);
      expect(find.text('Mark as Fruit set'), findsNothing);
    });
  });
}
