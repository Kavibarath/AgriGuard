import 'package:agriguard_mobile/app/app.dart';
import 'package:agriguard_mobile/app/router.dart';
import 'package:agriguard_mobile/core/api/api_exception.dart';
import 'package:agriguard_mobile/core/storage/token_storage.dart';
import 'package:agriguard_mobile/features/cases/case_models.dart';
import 'package:agriguard_mobile/features/cases/case_repository.dart';
import 'package:agriguard_mobile/features/harvest/harvest_models.dart';
import 'package:agriguard_mobile/features/harvest/harvest_repository.dart';
import 'package:agriguard_mobile/features/harvest/harvest_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'fixtures.dart';

class MockCaseRepository extends Mock implements CaseRepository {}

class MockHarvestRepository extends Mock implements HarvestRepository {}

final today = DateTime(DateTime.now().year, DateTime.now().month, DateTime.now().day);

const plot = ReportablePlot(
  plotId: 'plot-1',
  plotCode: 'A-01',
  farmName: 'Dry Zone Farm',
  cropCycleId: 'cycle-1',
  cropName: 'Tomato',
  stage: 'Flowering',
  latitude: 8.351,
  longitude: 80.504,
);

HarvestWindow window({DateTime? safeFrom}) => HarvestWindow(
      cropCycleId: 'cycle-1',
      maturityDate: today.add(const Duration(days: 3)),
      safeFromDate: safeFrom,
      safetyReason: safeFrom == null ? null : 'Mancozeb 80 WP was sprayed recently; its 7-day pre-harvest interval clears soon.',
      forecastAvailable: true,
      summary: 'Best: the day after maturity.',
      days: [
        HarvestDay(date: today.add(const Duration(days: 4)), score: 100, recommended: true, reasons: const ['at maturity', 'forecast dry while harvesting']),
        HarvestDay(date: today.add(const Duration(days: 3)), score: 60, recommended: false, reasons: const ['at maturity', '90% chance of 6 mm of rain while harvesting']),
      ],
    );

SprayWindow sprayWeek() => SprayWindow(
      forecastAvailable: true,
      summary: 'Suitable to spray on 5 of 7 days.',
      days: [
        for (var i = 0; i < 7; i++)
          SprayDay(
            date: today.add(Duration(days: i)),
            suitable: i != 2,
            problems: i == 2 ? const ['wind up to 21 km/h would cause drift'] : const [],
            rainProbabilityPercent: 20,
            windSpeedKph: i == 2 ? 21 : 6,
          ),
      ],
    );

CollectionBooking booking({String status = 'Booked'}) => CollectionBooking(
      id: 'bk-1',
      bookingNo: 'BK-2026-000001',
      status: status,
      centreName: 'Mihintale Collection Point',
      slotDate: today.add(const Duration(days: 4)),
      startTime: '07:00',
      endTime: '09:00',
      plotCode: 'A-01',
      cropName: 'Tomato',
      quantityKg: 400,
      distanceKm: 0.8,
    );

void main() {
  late MockCaseRepository cases;
  late MockHarvestRepository harvest;

  setUpAll(() => registerFallbackValue(DateTime(2026)));

  setUp(() {
    cases = MockCaseRepository();
    harvest = MockHarvestRepository();
    when(() => cases.reportablePlots()).thenAnswer((_) async => [plot]);
    when(() => harvest.harvestWindow('cycle-1')).thenAnswer((_) async => window());
    when(() => harvest.sprayWindow('plot-1')).thenAnswer((_) async => sprayWeek());
    when(() => harvest.myBookings()).thenAnswer((_) async => []);
  });

  Future<void> openHarvest(WidgetTester tester) async {
    tester.view.physicalSize = const Size(1080, 4000);
    tester.view.devicePixelRatio = 2.0;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          tokenStorageProvider.overrideWithValue(InMemoryTokenStorage(farmerSession())),
          caseRepositoryProvider.overrideWithValue(cases),
          harvestRepositoryProvider.overrideWithValue(harvest),
        ],
        child: const AgriGuardApp(),
      ),
    );
    await tester.pumpAndSettle();
    ProviderScope.containerOf(tester.element(find.byType(AgriGuardApp))).read(routerProvider).push('/harvest');
    await tester.pumpAndSettle();
  }

  testWidgets('ranks the harvest days and marks the best', (tester) async {
    await openHarvest(tester);

    expect(find.text(dayLabel(today.add(const Duration(days: 4)))), findsOneWidget);
    expect(find.text('Best'), findsOneWidget);
    expect(find.textContaining('90% chance of 6 mm of rain'), findsOneWidget);
  });

  testWidgets('shows the chemical-safety line when a spray is still clearing', (tester) async {
    when(() => harvest.harvestWindow('cycle-1')).thenAnswer((_) async => window(safeFrom: today.add(const Duration(days: 5))));

    await openHarvest(tester);

    expect(find.textContaining('pre-harvest interval clears'), findsOneWidget);
  });

  testWidgets('shows a week of spray weather, with the reason a day does not suit', (tester) async {
    await openHarvest(tester);

    expect(find.text('Spraying this week'), findsOneWidget);
    expect(find.byIcon(Icons.close), findsOneWidget);
    expect(find.bySemanticsLabel(RegExp('wind up to 21 km/h')), findsOneWidget);
  });

  testWidgets('checks the booking form before sending anything', (tester) async {
    await openHarvest(tester);

    await tester.tap(find.text('Book a slot'));
    await tester.pumpAndSettle();

    expect(find.text('Choose the collection day.'), findsOneWidget);
    expect(find.text('Enter how many kg you will bring.'), findsOneWidget);
    verifyNever(() => harvest.book(cropCycleId: any(named: 'cropCycleId'), quantityKg: any(named: 'quantityKg'), preferredDate: any(named: 'preferredDate')));
  });

  testWidgets('books a slot on the best day and lists it', (tester) async {
    when(() => harvest.book(cropCycleId: 'cycle-1', quantityKg: 400, preferredDate: any(named: 'preferredDate')))
        .thenAnswer((_) async => booking());
    await openHarvest(tester);

    await tester.tap(find.text('Choose a day'));
    await tester.pumpAndSettle();
    // The picker opens on the best-ranked day; accept it.
    await tester.tap(find.text('OK'));
    await tester.pumpAndSettle();
    await tester.enterText(find.widgetWithText(TextFormField, 'Quantity (kg)'), '400');
    when(() => harvest.myBookings()).thenAnswer((_) async => [booking()]);
    await tester.tap(find.text('Book a slot'));
    await tester.pumpAndSettle();

    final sent = verify(() => harvest.book(cropCycleId: 'cycle-1', quantityKg: 400, preferredDate: captureAny(named: 'preferredDate'))).captured;
    expect(sent.single, today.add(const Duration(days: 4)));
    expect(find.textContaining('BK-2026-000001 · 400 kg Tomato'), findsOneWidget);
    expect(find.text('Cancel'), findsOneWidget);
  });

  testWidgets('shows why the server refused a booking', (tester) async {
    when(() => harvest.book(cropCycleId: any(named: 'cropCycleId'), quantityKg: any(named: 'quantityKg'), preferredDate: any(named: 'preferredDate')))
        .thenThrow(ApiException(message: 'No collection slot has room for 400 kg between those days.', statusCode: 422, code: 'NO_CAPACITY'));
    await openHarvest(tester);

    await tester.tap(find.text('Choose a day'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('OK'));
    await tester.pumpAndSettle();
    await tester.enterText(find.widgetWithText(TextFormField, 'Quantity (kg)'), '400');
    await tester.tap(find.text('Book a slot'));
    await tester.pumpAndSettle();

    expect(find.textContaining('No collection slot has room'), findsOneWidget);
  });
}
