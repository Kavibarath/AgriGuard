import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/api_exception.dart';
import 'registry_models.dart';

/// Component A on the phone: the farmer's farms, plots and crop cycles, and each plot's chemical
/// safety. The API scopes every row (a farmer sees their own; an agronomist their district) and
/// refuses writes to farms the caller does not own; failures arrive as [ApiException].
abstract class RegistryRepository {
  Future<List<Farm>> farms();
  Future<Farm> farm(String id);
  Future<Farm> createFarm({required String name, String? village, required String districtId});
  Future<List<Plot>> plots(String farmId);
  Future<Plot> plot(String id);
  Future<Plot> createPlot(NewPlot plot);
  Future<CropCycle> cropCycle(String id);
  Future<CropCycle> startCycle({required String plotId, required String cropId, required DateTime sownDate});
  Future<CropCycle> advanceStage(String cycleId, CropStage to, {String? note});
  Future<SafetyProfile> safetyProfile(String plotId);
  Future<List<District>> districts();
  Future<List<Crop>> crops();
}

class HttpRegistryRepository implements RegistryRepository {
  HttpRegistryRepository(this._dio);

  final Dio _dio;

  @override
  Future<List<Farm>> farms() => _guard(() async {
        final response = await _dio.get<Map<String, dynamic>>('/api/farms', queryParameters: {'pageSize': 100, 'sortBy': 'name'});
        return [for (final f in response.data!['items'] as List) Farm.fromJson(f as Map<String, dynamic>)];
      });

  @override
  Future<Farm> farm(String id) => _guard(() async {
        final response = await _dio.get<Map<String, dynamic>>('/api/farms/$id');
        return Farm.fromJson(response.data!);
      });

  @override
  Future<Farm> createFarm({required String name, String? village, required String districtId}) => _guard(() async {
        final response = await _dio.post<Map<String, dynamic>>('/api/farms', data: {
          'name': name,
          'village': (village?.trim().isEmpty ?? true) ? null : village!.trim(),
          'districtId': districtId,
        });
        return Farm.fromJson(response.data!);
      });

  @override
  Future<List<Plot>> plots(String farmId) => _guard(() async {
        final response = await _dio.get<Map<String, dynamic>>('/api/plots', queryParameters: {'farmId': farmId, 'pageSize': 100});
        return [for (final p in response.data!['items'] as List) Plot.fromJson(p as Map<String, dynamic>)];
      });

  @override
  Future<Plot> plot(String id) => _guard(() async {
        final response = await _dio.get<Map<String, dynamic>>('/api/plots/$id');
        return Plot.fromJson(response.data!);
      });

  @override
  Future<Plot> createPlot(NewPlot plot) => _guard(() async {
        final response = await _dio.post<Map<String, dynamic>>('/api/plots', data: plot.toJson());
        return Plot.fromJson(response.data!);
      });

  @override
  Future<CropCycle> cropCycle(String id) => _guard(() async {
        final response = await _dio.get<Map<String, dynamic>>('/api/crop-cycles/$id');
        return CropCycle.fromJson(response.data!);
      });

  @override
  Future<CropCycle> startCycle({required String plotId, required String cropId, required DateTime sownDate}) => _guard(() async {
        final response = await _dio.post<Map<String, dynamic>>('/api/crop-cycles', data: {
          'plotId': plotId,
          'cropId': cropId,
          'sownDate': _iso(sownDate),
        });
        return CropCycle.fromJson(response.data!);
      });

  @override
  Future<CropCycle> advanceStage(String cycleId, CropStage to, {String? note}) => _guard(() async {
        final response = await _dio.post<Map<String, dynamic>>('/api/crop-cycles/$cycleId/advance-stage', data: {
          'toStage': to.wire,
          'note': (note?.trim().isEmpty ?? true) ? null : note!.trim(),
        });
        return CropCycle.fromJson(response.data!);
      });

  @override
  Future<SafetyProfile> safetyProfile(String plotId) => _guard(() async {
        final response = await _dio.get<Map<String, dynamic>>('/api/plots/$plotId/safety-profile');
        return SafetyProfile.fromJson(response.data!);
      });

  @override
  Future<List<District>> districts() => _guard(() async {
        final response = await _dio.get<List<dynamic>>('/api/districts');
        return [for (final d in response.data!) District.fromJson(d as Map<String, dynamic>)];
      });

  @override
  Future<List<Crop>> crops() => _guard(() async {
        final response = await _dio.get<List<dynamic>>('/api/crops');
        return [for (final c in response.data!) Crop.fromJson(c as Map<String, dynamic>)];
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

final registryRepositoryProvider = Provider<RegistryRepository>(
  (ref) => HttpRegistryRepository(ref.watch(apiClientProvider)),
);

final myFarmsProvider = FutureProvider.autoDispose<List<Farm>>((ref) => ref.watch(registryRepositoryProvider).farms());

final farmProvider = FutureProvider.autoDispose.family<Farm, String>((ref, id) => ref.watch(registryRepositoryProvider).farm(id));

final farmPlotsProvider = FutureProvider.autoDispose.family<List<Plot>, String>(
  (ref, farmId) => ref.watch(registryRepositoryProvider).plots(farmId),
);

final plotProvider = FutureProvider.autoDispose.family<Plot, String>((ref, id) => ref.watch(registryRepositoryProvider).plot(id));

final cropCycleProvider = FutureProvider.autoDispose.family<CropCycle, String>(
  (ref, id) => ref.watch(registryRepositoryProvider).cropCycle(id),
);

final safetyProfileProvider = FutureProvider.autoDispose.family<SafetyProfile, String>(
  (ref, plotId) => ref.watch(registryRepositoryProvider).safetyProfile(plotId),
);

/// Look-ups change perhaps once a season: kept for the whole session once loaded.
final districtsProvider = FutureProvider<List<District>>((ref) => ref.watch(registryRepositoryProvider).districts());

final cropsProvider = FutureProvider<List<Crop>>((ref) => ref.watch(registryRepositoryProvider).crops());
