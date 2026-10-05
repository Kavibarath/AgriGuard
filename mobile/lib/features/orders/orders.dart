import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/api/api_client.dart';
import '../../core/api/api_exception.dart';

/// Where an order is: the dealer packs it (Confirmed → Packed), then hands it over (Collected).
enum OrderStatus {
  draft('Draft', 'Draft'),
  confirmed('Confirmed', 'Being packed'),
  packed('Packed', 'Ready to collect'),
  collected('Collected', 'Collected'),
  cancelled('Cancelled', 'Cancelled');

  const OrderStatus(this.wire, this.label);

  final String wire;
  final String label;

  static OrderStatus parse(String value) => values.firstWhere((s) => s.wire == value, orElse: () => draft);

  bool get open => this == confirmed || this == packed;
}

class OrderLine {
  const OrderLine({required this.productName, required this.unit, required this.packs, required this.quantity, required this.lineTotal});

  factory OrderLine.fromJson(Map<String, dynamic> json) => OrderLine(
        productName: json['productName'] as String,
        unit: json['unit'] as String,
        packs: json['packs'] as int,
        quantity: (json['quantity'] as num).toDouble(),
        lineTotal: (json['lineTotal'] as num).toDouble(),
      );

  final String productName;

  /// "Litre" or "Kilogram".
  final String unit;
  final int packs;
  final double quantity;
  final double lineTotal;

  String get unitLabel => unit == 'Litre' ? 'L' : 'kg';
}

/// GET /api/orders/mine: one of the farmer's input orders, with where to collect it.
class FarmerOrder {
  const FarmerOrder({
    required this.id,
    required this.orderNo,
    required this.status,
    required this.shopName,
    this.shopAddress,
    this.shopPhone,
    this.prescriptionNo,
    this.sprayDate,
    required this.totalAmount,
    this.confirmedAt,
    this.packedAt,
    this.collectedAt,
    this.pickupCode,
    required this.lines,
    this.paid = false,
    this.paidAt,
    this.paidBy,
    this.cardBrand,
    this.cardLast4,
    this.openPaymentId,
    this.canPayByCard = false,
  });

  factory FarmerOrder.fromJson(Map<String, dynamic> json) {
    DateTime? at(String key) => json[key] == null ? null : DateTime.parse(json[key] as String);
    return FarmerOrder(
      id: json['id'] as String,
      orderNo: json['orderNo'] as String,
      status: OrderStatus.parse(json['status'] as String),
      shopName: json['shopName'] as String,
      shopAddress: json['shopAddress'] as String?,
      shopPhone: json['shopPhone'] as String?,
      prescriptionNo: json['prescriptionNo'] as String?,
      sprayDate: at('sprayDate'),
      totalAmount: (json['totalAmount'] as num).toDouble(),
      confirmedAt: at('confirmedAt'),
      packedAt: at('packedAt'),
      collectedAt: at('collectedAt'),
      pickupCode: json['pickupCode'] as String?,
      lines: [for (final l in json['lines'] as List) OrderLine.fromJson(l as Map<String, dynamic>)],
      paid: json['paymentStatus'] == 'Paid',
      paidAt: at('paidAt'),
      paidBy: json['paidBy'] as String?,
      cardBrand: json['cardBrand'] as String?,
      cardLast4: json['cardLast4'] as String?,
      openPaymentId: json['openPaymentId'] as String?,
      canPayByCard: json['canPayByCard'] as bool? ?? false,
    );
  }

  final String id;
  final String orderNo;
  final OrderStatus status;
  final String shopName;
  final String? shopAddress;
  final String? shopPhone;
  final String? prescriptionNo;
  final DateTime? sprayDate;
  final double totalAmount;
  final DateTime? confirmedAt;
  final DateTime? packedAt;
  final DateTime? collectedAt;

  /// Six digits the dealer needs to hand the order over; null once collected.
  final String? pickupCode;
  final List<OrderLine> lines;

  /// Paid by card here, or in cash at the counter. The dealer hands over only a paid order.
  final bool paid;
  final DateTime? paidAt;

  /// "Card" or "Cash".
  final String? paidBy;
  final String? cardBrand;
  final String? cardLast4;

  /// A card checkout still open for this order, to check when the farmer comes back to the app.
  final String? openPaymentId;

  /// Unpaid, payable, and card payments are switched on.
  final bool canPayByCard;

  /// "Visa •••• 4242", "Paid in cash at the counter".
  String? get paidByLabel => switch (paidBy) {
        'Cash' => 'Paid in cash at the counter',
        'Card' => cardLast4 == null ? 'Paid by card' : 'Paid by ${cardBrandLabel(cardBrand)} •••• $cardLast4',
        // Handed over before payments were recorded: settled at the counter.
        _ => paid ? 'Settled at the counter' : null,
      };
}

String cardBrandLabel(String? brand) => switch (brand) {
      'visa' => 'Visa',
      'mastercard' => 'Mastercard',
      'amex' => 'American Express',
      null => 'card',
      _ => brand,
    };

/// POST /api/orders/mine/{id}/payments/card: the provider's payment page for one attempt.
class CardCheckout {
  const CardCheckout({required this.paymentId, required this.checkoutUrl, required this.expiresAt});

  factory CardCheckout.fromJson(Map<String, dynamic> json) => CardCheckout(
        paymentId: json['paymentId'] as String,
        checkoutUrl: Uri.parse(json['checkoutUrl'] as String),
        expiresAt: DateTime.parse(json['expiresAt'] as String),
      );

  final String paymentId;
  final Uri checkoutUrl;
  final DateTime expiresAt;
}

/// POST …/payments/{paymentId}/sync: how the order's money stands, as the provider reports it.
class OrderPayment {
  const OrderPayment({required this.orderNo, required this.paid, this.latestAttemptStatus, this.latestAttemptFailure});

  factory OrderPayment.fromJson(Map<String, dynamic> json) => OrderPayment(
        orderNo: json['orderNo'] as String,
        paid: json['paymentStatus'] == 'Paid',
        latestAttemptStatus: json['latestAttemptStatus'] as String?,
        latestAttemptFailure: json['latestAttemptFailure'] as String?,
      );

  final String orderNo;
  final bool paid;

  /// "Pending", "Succeeded", "Failed", "Expired" or "Cancelled".
  final String? latestAttemptStatus;
  final String? latestAttemptFailure;
}

/// Component C on the phone: the farmer's own orders, and paying for them by card. The dealer
/// moves them on; the provider, not the phone, decides whether a payment went through.
abstract class OrderRepository {
  Future<List<FarmerOrder>> myOrders();

  /// Opens (or reopens) a card checkout for the order.
  Future<CardCheckout> startCardPayment(String orderId);

  /// Asks the server to check the attempt with the provider, after the farmer comes back.
  Future<OrderPayment> syncCardPayment(String orderId, String paymentId);
}

class HttpOrderRepository implements OrderRepository {
  HttpOrderRepository(this._dio);

  final Dio _dio;

  @override
  Future<List<FarmerOrder>> myOrders() async {
    try {
      final response = await _dio.get<Map<String, dynamic>>('/api/orders/mine', queryParameters: {'pageSize': 50});
      return [for (final o in response.data!['items'] as List) FarmerOrder.fromJson(o as Map<String, dynamic>)];
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  @override
  Future<CardCheckout> startCardPayment(String orderId) async {
    try {
      final response = await _dio.post<Map<String, dynamic>>('/api/orders/mine/$orderId/payments/card');
      return CardCheckout.fromJson(response.data!);
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  @override
  Future<OrderPayment> syncCardPayment(String orderId, String paymentId) async {
    try {
      final response = await _dio.post<Map<String, dynamic>>('/api/orders/mine/$orderId/payments/$paymentId/sync');
      return OrderPayment.fromJson(response.data!);
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

/// Opens the provider's payment page. Behind a provider so tests never open a real browser.
typedef CheckoutLauncher = Future<bool> Function(Uri url);

/// A browser tab over the app (Chrome Custom Tabs), so closing it comes straight back here; a
/// separate browser if the phone has no tab support. The card is typed on the provider's page,
/// never in AgriGuard.
final checkoutLauncherProvider = Provider<CheckoutLauncher>((ref) => (url) async {
      if (await launchUrl(url, mode: LaunchMode.inAppBrowserView)) return true;
      return launchUrl(url, mode: LaunchMode.externalApplication);
    });

final orderRepositoryProvider = Provider<OrderRepository>((ref) => HttpOrderRepository(ref.watch(apiClientProvider)));

final myOrdersProvider = FutureProvider.autoDispose<List<FarmerOrder>>((ref) => ref.watch(orderRepositoryProvider).myOrders());
