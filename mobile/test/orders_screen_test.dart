import 'package:agriguard_mobile/app/app.dart';
import 'package:agriguard_mobile/app/router.dart';
import 'package:agriguard_mobile/core/api/api_exception.dart';
import 'package:agriguard_mobile/core/storage/token_storage.dart';
import 'package:agriguard_mobile/features/orders/orders.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'fixtures.dart';

class MockOrderRepository extends Mock implements OrderRepository {}

FarmerOrder order({OrderStatus status = OrderStatus.packed, String? pickupCode = '482913', DateTime? packedAt, DateTime? collectedAt}) =>
    FarmerOrder.fromJson({
      'id': 'order-1',
      'orderNo': 'ORD-2026-000002',
      'status': status.wire,
      'shopName': 'Rajarata Agro Centre — Anuradhapura',
      'shopAddress': '45 Maithripala Senanayake Mawatha, Anuradhapura',
      'shopLatitude': 8.3114,
      'shopLongitude': 80.4037,
      'shopPhone': null,
      'prescriptionNo': 'RX-2026-000002',
      'sprayDate': '2026-10-01',
      'totalAmount': 9600,
      'createdAt': '2026-09-28T04:00:00Z',
      'confirmedAt': '2026-09-28T04:00:00Z',
      'packedAt': (packedAt ?? (status == OrderStatus.confirmed ? null : DateTime.utc(2026, 9, 28, 9)))?.toIso8601String(),
      'collectedAt': collectedAt?.toIso8601String(),
      'pickupCode': pickupCode,
      'lines': [
        {'productId': 'p-azo', 'productName': 'Azoxystrobin 25 SC', 'unit': 'Litre', 'packs': 2, 'quantity': 0.5, 'unitPrice': 4800, 'lineTotal': 9600},
      ],
    });

void main() {
  late MockOrderRepository repository;

  setUp(() => repository = MockOrderRepository());

  Future<void> openApp(WidgetTester tester) async {
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 2.0;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          tokenStorageProvider.overrideWithValue(InMemoryTokenStorage(farmerSession())),
          orderRepositoryProvider.overrideWithValue(repository),
        ],
        child: const AgriGuardApp(),
      ),
    );
    await tester.pumpAndSettle();
    ProviderScope.containerOf(tester.element(find.byType(AgriGuardApp))).read(routerProvider).push('/orders');
    await tester.pumpAndSettle();
  }

  /// The screen polls while an order is open: replace the app so the timer is cancelled.
  Future<void> closeApp(WidgetTester tester) => tester.pumpWidget(const SizedBox());

  testWidgets('shows a packed order ready to collect, with the code to show at the counter', (tester) async {
    when(() => repository.myOrders()).thenAnswer((_) async => [order()]);
    await openApp(tester);

    expect(find.text('Ready to collect'), findsOneWidget);
    expect(find.text('Ready to collect. Show this code at the counter:'), findsOneWidget);
    expect(find.text('482 913'), findsOneWidget);
    expect(find.text('2 × Azoxystrobin 25 SC (0.5 L)'), findsOneWidget);
    expect(find.text('Total LKR 9,600'), findsOneWidget);
    expect(find.text('45 Maithripala Senanayake Mawatha, Anuradhapura'), findsOneWidget);
    expect(find.bySemanticsLabel('Packed: 2026-09-28'), findsOneWidget);
    expect(find.bySemanticsLabel('Collected: not yet'), findsOneWidget);
    await closeApp(tester);
  });

  testWidgets('says the dealer is still packing', (tester) async {
    when(() => repository.myOrders()).thenAnswer((_) async => [order(status: OrderStatus.confirmed)]);
    await openApp(tester);

    expect(find.text('Being packed'), findsOneWidget);
    expect(find.text('Pickup code (the dealer is still packing):'), findsOneWidget);
    await closeApp(tester);
  });

  testWidgets('shows no code once the order is collected', (tester) async {
    when(() => repository.myOrders()).thenAnswer(
      (_) async => [order(status: OrderStatus.collected, pickupCode: null, collectedAt: DateTime.utc(2026, 9, 29, 5))],
    );
    await openApp(tester);

    expect(find.text('Collected'), findsWidgets);
    expect(find.textContaining('Show this code'), findsNothing);
    expect(find.bySemanticsLabel('Collected: 2026-09-29'), findsOneWidget);
    await closeApp(tester);
  });

  testWidgets('explains when there are no orders yet', (tester) async {
    when(() => repository.myOrders()).thenAnswer((_) async => []);
    await openApp(tester);

    expect(find.textContaining('No orders yet'), findsOneWidget);
  });

  testWidgets('offers to try again when the orders cannot be loaded', (tester) async {
    when(() => repository.myOrders()).thenThrow(ApiException(message: 'Could not reach AgriGuard. Check your connection.', isNetworkError: true));
    await openApp(tester);

    expect(find.text('Could not reach AgriGuard. Check your connection.'), findsOneWidget);
    expect(find.text('Try again'), findsOneWidget);
  });
}
