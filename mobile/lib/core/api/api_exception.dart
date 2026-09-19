import 'package:dio/dio.dart';

/// A failed API call, with the RFC 7807 problem details the API returns.
///
/// `message` is always safe to show a farmer: the API's `detail` when there is one,
/// otherwise a plain explanation of what went wrong.
class ApiException implements Exception {
  ApiException({
    required this.message,
    this.statusCode,
    this.title,
    this.code,
    this.fieldErrors = const {},
    this.isNetworkError = false,
  });

  /// Maps a Dio failure onto the problem-details shape.
  factory ApiException.fromDio(DioException e) {
    final response = e.response;
    if (response == null) {
      return ApiException(
        message: switch (e.type) {
          DioExceptionType.connectionTimeout ||
          DioExceptionType.receiveTimeout ||
          DioExceptionType.sendTimeout =>
            'The server took too long to respond. Try again.',
          _ => 'Could not reach AgriGuard. Check your connection.',
        },
        isNetworkError: true,
      );
    }

    final body = response.data;
    final problem = body is Map<String, dynamic> ? body : const <String, dynamic>{};
    final errors = problem['errors'];

    return ApiException(
      statusCode: response.statusCode,
      title: problem['title'] as String?,
      code: problem['code'] as String?,
      message: (problem['detail'] as String?) ??
          (problem['title'] as String?) ??
          'Request failed (${response.statusCode}).',
      fieldErrors: errors is Map<String, dynamic>
          ? errors.map((k, v) => MapEntry(k, List<String>.from(v as List)))
          : const {},
    );
  }

  final String message;
  final int? statusCode;
  final String? title;

  /// Stable identifier on 422 business-rule violations (e.g. `ILLEGAL_STAGE_TRANSITION`).
  final String? code;

  /// Field name → messages on 400 validation failures.
  final Map<String, List<String>> fieldErrors;
  final bool isNetworkError;

  bool get isUnauthorized => statusCode == 401;

  @override
  String toString() => 'ApiException($statusCode: $message)';
}
