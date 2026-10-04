import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

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
}

/// Component C on the phone: the farmer's own orders. Read-only: the dealer moves them on.
abstract class OrderRepository {
  Future<List<FarmerOrder>> myOrders();
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
}

final orderRepositoryProvider = Provider<OrderRepository>((ref) => HttpOrderRepository(ref.watch(apiClientProvider)));

final myOrdersProvider = FutureProvider.autoDispose<List<FarmerOrder>>((ref) => ref.watch(orderRepositoryProvider).myOrders());
