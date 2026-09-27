import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/api_exception.dart';
import 'case_models.dart';

/// The farmer's side of Component B. Every failure surfaces as an [ApiException] whose
/// `message` is safe to show.
abstract class CaseRepository {
  Future<List<CaseSummary>> myCases();
  Future<CaseDetail> getCase(String id);
  Future<CaseDetail> reportCase(NewCase newCase);

  /// Starts the agent. Returns the run's status right away; the run itself takes about a minute.
  Future<String> requestAdvice(String caseId);

  /// The issued prescription for an approved run, or null.
  Future<Prescription?> prescriptionFor(String runId);

  Future<List<ReportablePlot>> reportablePlots();
  Future<List<Symptom>> symptoms();
}

class HttpCaseRepository implements CaseRepository {
  HttpCaseRepository(this._dio);

  final Dio _dio;

  @override
  Future<List<CaseSummary>> myCases() => _guard(() async {
        // Newest first: a farmer looks for the problem they just reported.
        final response = await _dio.get<Map<String, dynamic>>(
          '/api/cases',
          queryParameters: {'sortBy': 'createdAt', 'desc': true, 'pageSize': 50},
        );
        return [
          for (final c in response.data!['items'] as List) CaseSummary.fromJson(c as Map<String, dynamic>),
        ];
      });

  @override
  Future<CaseDetail> getCase(String id) => _guard(() async {
        final response = await _dio.get<Map<String, dynamic>>('/api/cases/$id');
        return CaseDetail.fromJson(response.data!);
      });

  @override
  Future<CaseDetail> reportCase(NewCase newCase) => _guard(() async {
        final response = await _dio.post<Map<String, dynamic>>('/api/cases', data: newCase.toJson());
        return CaseDetail.fromJson(response.data!);
      });

  @override
  Future<String> requestAdvice(String caseId) => _guard(() async {
        final response = await _dio.post<Map<String, dynamic>>('/api/cases/$caseId/agent-runs');
        return response.data!['status'] as String;
      });

  @override
  Future<Prescription?> prescriptionFor(String runId) => _guard(() async {
        final response = await _dio.get<Map<String, dynamic>>('/api/agent-runs/$runId');
        final prescription = response.data!['prescription'] as Map<String, dynamic>?;
        return prescription == null ? null : Prescription.fromJson(prescription);
      });

  @override
  Future<List<ReportablePlot>> reportablePlots() => _guard(() async {
        final response = await _dio.get<Map<String, dynamic>>('/api/plots', queryParameters: {'pageSize': 100});
        // Plots with nothing growing are left out: there is no crop to report on.
        return [
          for (final p in response.data!['items'] as List) ?ReportablePlot.fromPlotJson(p as Map<String, dynamic>),
        ];
      });

  @override
  Future<List<Symptom>> symptoms() => _guard(() async {
        final response = await _dio.get<List<dynamic>>('/api/symptoms');
        return [for (final s in response.data!) Symptom.fromJson(s as Map<String, dynamic>)];
      });

  Future<T> _guard<T>(Future<T> Function() call) async {
    try {
      return await call();
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final caseRepositoryProvider = Provider<CaseRepository>(
  (ref) => HttpCaseRepository(ref.watch(apiClientProvider)),
);
