import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../features/auth/auth_controller.dart';
import '../config.dart';

/// Marks a request as not needing (and not refreshing) a bearer token: login, refresh, logout.
const String anonymousRequest = 'anonymous';

extension AnonymousOptions on Options {
  Options get anonymous => copyWith(extra: {...?extra, anonymousRequest: true});
}

BaseOptions _baseOptions() => BaseOptions(
      baseUrl: apiBaseUrl,
      connectTimeout: const Duration(seconds: 10),
      receiveTimeout: const Duration(seconds: 30),
      headers: const {'Accept': 'application/json'},
      // Let 4xx/5xx reach the interceptor as DioExceptions carrying the problem-details body.
      responseType: ResponseType.json,
    );

/// Plain client with no auth interceptor. Used for the auth endpoints themselves, so a
/// refresh call can never trigger another refresh.
final bareDioProvider = Provider<Dio>((ref) => Dio(_baseOptions()));

/// The client every feature uses: attaches the bearer token and, on 401, refreshes once
/// and retries. Wraps the same base options as [bareDioProvider].
final apiClientProvider = Provider<Dio>((ref) {
  final dio = Dio(_baseOptions());
  // The interceptor retries through the very client it is attached to, so it takes the
  // instance directly — reading apiClientProvider from inside would be a self-dependency.
  dio.interceptors.add(AuthInterceptor(ref, dio));
  return dio;
});

/// Queued so that refresh + retry for one request finishes before the next 401 is handled —
/// otherwise several expired requests would each present the same refresh token, and the API
/// treats a second use as a replay and revokes every session.
class AuthInterceptor extends QueuedInterceptor {
  AuthInterceptor(this._ref, this._dio);

  final Ref _ref;
  final Dio _dio;
  static const _retried = 'retried';

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    options.headers['X-Correlation-Id'] = _correlationId();

    if (options.extra[anonymousRequest] != true) {
      final token = _ref.read(authControllerProvider).value?.accessToken;
      if (token != null) options.headers['Authorization'] = 'Bearer $token';
    }
    handler.next(options);
  }

  @override
  Future<void> onError(DioException err, ErrorInterceptorHandler handler) async {
    final options = err.requestOptions;
    final shouldRefresh = err.response?.statusCode == 401 &&
        options.extra[anonymousRequest] != true &&
        options.extra[_retried] != true;

    if (!shouldRefresh) return handler.next(err);

    final newToken = await _ref.read(authControllerProvider.notifier).refreshSession();
    if (newToken == null) return handler.next(err); // session is gone; caller sees the 401

    options.headers['Authorization'] = 'Bearer $newToken';
    options.extra[_retried] = true;
    try {
      handler.resolve(await _dio.fetch(options));
    } on DioException catch (retryError) {
      handler.next(retryError);
    }
  }

  static int _counter = 0;

  /// Compact id the API echoes and logs; time + counter is unique enough per device.
  static String _correlationId() =>
      'app-${DateTime.now().microsecondsSinceEpoch.toRadixString(36)}-${(_counter++).toRadixString(36)}';
}
