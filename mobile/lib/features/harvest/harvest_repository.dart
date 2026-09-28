import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/api_exception.dart';
import 'harvest_models.dart';

/// The farmer's side of Component D. Every failure surfaces as an [ApiException] whose `message`
/// is safe to show — including the booking refusals (not safe to harvest yet, no room).
abstract class HarvestRepository {
  Future<HarvestWindow> harvestWindow(String cropCycleId);
  Future<SprayWindow> sprayWindow(String plotId);
  Future<List<CollectionBooking>> myBookings();
  Future<CollectionBooking> book({required String cropCycleId, required double quantityKg, required DateTime preferredDate});
  Future<CollectionBooking> cancel(String bookingId);
}

class HttpHarvestRepository implements HarvestRepository {
  HttpHarvestRepository(this._dio);

  final Dio _dio;

  @override
  Future<HarvestWindow> harvestWindow(String cropCycleId) => _guard(() async {
        final response = await _dio.get<Map<String, dynamic>>('/api/harvest-windows/$cropCycleId');
        return HarvestWindow.fromJson(response.data!);
      });

  @override
  Future<SprayWindow> sprayWindow(String plotId) => _guard(() async {
        final response = await _dio.get<Map<String, dynamic>>(
          '/api/weather/spray-window',
          queryParameters: {'plotId': plotId, 'days': 7},
        );
        return SprayWindow.fromJson(response.data!);
      });

  @override
  Future<List<CollectionBooking>> myBookings() => _guard(() async {
        final response = await _dio.get<Map<String, dynamic>>(
          '/api/collection-bookings',
          queryParameters: {'sortBy': 'slotDate', 'desc': true, 'pageSize': 50},
        );
        return [for (final b in response.data!['items'] as List) CollectionBooking.fromJson(b as Map<String, dynamic>)];
      });

  @override
  Future<CollectionBooking> book({required String cropCycleId, required double quantityKg, required DateTime preferredDate}) =>
      _guard(() async {
        final response = await _dio.post<Map<String, dynamic>>('/api/collection-bookings/allocate', data: {
          'cropCycleId': cropCycleId,
          'quantityKg': quantityKg,
          'preferredDate': _iso(preferredDate),
        });
        return CollectionBooking.fromJson(response.data!);
      });

  @override
  Future<CollectionBooking> cancel(String bookingId) => _guard(() async {
        final response = await _dio.post<Map<String, dynamic>>('/api/collection-bookings/$bookingId/cancel');
        return CollectionBooking.fromJson(response.data!);
      });

  static String _iso(DateTime d) => '${d.year}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';

  Future<T> _guard<T>(Future<T> Function() call) async {
    try {
      return await call();
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final harvestRepositoryProvider = Provider<HarvestRepository>(
  (ref) => HttpHarvestRepository(ref.watch(apiClientProvider)),
);

final harvestWindowProvider = FutureProvider.autoDispose.family<HarvestWindow, String>(
  (ref, cropCycleId) => ref.watch(harvestRepositoryProvider).harvestWindow(cropCycleId),
);

final sprayWindowProvider = FutureProvider.autoDispose.family<SprayWindow, String>(
  (ref, plotId) => ref.watch(harvestRepositoryProvider).sprayWindow(plotId),
);

final myBookingsProvider = FutureProvider.autoDispose<List<CollectionBooking>>(
  (ref) => ref.watch(harvestRepositoryProvider).myBookings(),
);
