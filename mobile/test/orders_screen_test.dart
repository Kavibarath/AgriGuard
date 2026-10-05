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

FarmerOrder order({
  OrderStatus status = OrderStatus.packed,
  String? pickupCode = '482913',
  DateTime? packedAt,
  DateTime? collectedAt,
  bool paid = false,
  String? paidBy,
  String? cardBrand,
  String? cardLast4,
  String? openPaymentId,
  bool canPayByCard = true,
}) =>
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
      'paymentStatus': paid ? 'Paid' : 'Unpaid',
      'paidAt': paid ? '2026-09-28T10:00:00Z' : null,
      'paidBy': paidBy,
      'cardBrand': cardBrand,
      'cardLast4': cardLast4,
      'openPaymentId': openPaymentId,
      'canPayByCard': canPayByCard && !paid,
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

  /// Every payment page the app tried to open, instead of a real browser.
  late List<Uri> opened;

  setUp(() {
    repository = MockOrderRepository();
    opened = [];
  });

  Future<void> openApp(WidgetTester tester) async {
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 2.0;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          tokenStorageProvider.overrideWithValue(InMemoryTokenStorage(farmerSession())),
          orderRepositoryProvider.overrideWithValue(repository),
          checkoutLauncherProvider.overrideWithValue((url) async {
            opened.add(url);
            return true;
          }),
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

  testWidgets('pays by card on the provider\'s page, and shows it paid only once the server has checked', (tester) async {
    var orders = [order()];
    when(() => repository.myOrders()).thenAnswer((_) async => orders);
    when(() => repository.startCardPayment('order-1')).thenAnswer(
      (_) async => CardCheckout(
        paymentId: 'pay-1',
        checkoutUrl: Uri.parse('https://checkout.stripe.test/c/pay/cs_test_1'),
        expiresAt: DateTime.utc(2026, 9, 28, 11),
      ),
    );
    await openApp(tester);

    expect(find.text('To pay LKR 9,600'), findsOneWidget);
    expect(find.text('Unpaid'), findsOneWidget);
    await tester.ensureVisible(find.text('Pay by card'));
    await tester.tap(find.text('Pay by card'));
    await tester.pumpAndSettle();
    expect(opened, [Uri.parse('https://checkout.stripe.test/c/pay/cs_test_1')]);

    // The farmer pays on the provider's page, then comes back to the app.
    orders = [order(paid: true, paidBy: 'Card', cardBrand: 'visa', cardLast4: '4242')];
    when(() => repository.syncCardPayment('order-1', 'pay-1')).thenAnswer(
      (_) async => const OrderPayment(orderNo: 'ORD-2026-000002', paid: true, latestAttemptStatus: 'Succeeded'),
    );
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await tester.pumpAndSettle();

    verify(() => repository.syncCardPayment('order-1', 'pay-1')).called(1);
    expect(find.text('Payment received for ORD-2026-000002. Thank you.'), findsOneWidget);
    expect(find.text('Paid LKR 9,600'), findsOneWidget);
    expect(find.text('Paid by Visa •••• 4242 · 2026-09-28'), findsOneWidget);
    expect(find.text('Pay by card'), findsNothing);
    await closeApp(tester);
  });

  testWidgets('says nothing was charged when the payment page closed unpaid', (tester) async {
    when(() => repository.myOrders()).thenAnswer((_) async => [order()]);
    when(() => repository.startCardPayment('order-1')).thenAnswer(
      (_) async => CardCheckout(paymentId: 'pay-1', checkoutUrl: Uri.parse('https://checkout.stripe.test/x'), expiresAt: DateTime.utc(2026, 9, 28, 11)),
    );
    when(() => repository.syncCardPayment('order-1', 'pay-1')).thenAnswer(
      (_) async => const OrderPayment(orderNo: 'ORD-2026-000002', paid: false, latestAttemptStatus: 'Expired'),
    );
    await openApp(tester);

    await tester.ensureVisible(find.text('Pay by card'));
    await tester.tap(find.text('Pay by card'));
    await tester.pumpAndSettle();
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await tester.pumpAndSettle();

    expect(find.text('The payment page closed before you paid. Nothing was charged.'), findsOneWidget);
    expect(find.text('Unpaid'), findsOneWidget);
    await closeApp(tester);
  });

  testWidgets('offers to continue a payment page left open, and checks it on arrival', (tester) async {
    when(() => repository.myOrders()).thenAnswer((_) async => [order(openPaymentId: 'pay-9')]);
    when(() => repository.syncCardPayment('order-1', 'pay-9')).thenAnswer(
      (_) async => const OrderPayment(orderNo: 'ORD-2026-000002', paid: false, latestAttemptStatus: 'Pending'),
    );
    await openApp(tester);

    verify(() => repository.syncCardPayment('order-1', 'pay-9')).called(1);
    expect(find.text('Continue paying by card'), findsOneWidget);
    await closeApp(tester);
  });

  testWidgets('tells the farmer when card payments cannot be taken, and that cash still works', (tester) async {
    when(() => repository.myOrders()).thenAnswer((_) async => [order()]);
    when(() => repository.startCardPayment('order-1')).thenThrow(
      ApiException(message: 'The card payment service did not answer. Try again in a minute, or pay in cash when you collect the order.'),
    );
    await openApp(tester);

    await tester.ensureVisible(find.text('Pay by card'));
    await tester.tap(find.text('Pay by card'));
    await tester.pumpAndSettle();

    expect(find.textContaining('did not answer'), findsOneWidget);
    expect(opened, isEmpty);
    await closeApp(tester);
  });

  testWidgets('offers only cash when card payments are switched off', (tester) async {
    when(() => repository.myOrders()).thenAnswer((_) async => [order(canPayByCard: false)]);
    await openApp(tester);

    expect(find.text('Pay by card'), findsNothing);
    expect(find.textContaining('Pay in cash at the shop'), findsOneWidget);
    await closeApp(tester);
  });

  testWidgets('shows an order paid in cash at the counter', (tester) async {
    when(() => repository.myOrders()).thenAnswer((_) async => [order(paid: true, paidBy: 'Cash')]);
    await openApp(tester);

    expect(find.text('Paid in cash at the counter · 2026-09-28'), findsOneWidget);
    expect(find.text('Unpaid'), findsNothing);
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
