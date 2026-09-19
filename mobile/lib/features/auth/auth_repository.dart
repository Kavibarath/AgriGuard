import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/api_exception.dart';
import 'auth_models.dart';

/// The four auth endpoints. Every failure surfaces as an [ApiException].
abstract class AuthRepository {
  Future<AuthSession> login(String email, String password);
  Future<AuthSession> refresh(String refreshToken);
  Future<void> logout(String refreshToken);
  Future<UserSummary> me();
}

class HttpAuthRepository implements AuthRepository {
  HttpAuthRepository({required Dio bare, required Dio authed})
      : this._(bare, authed);

  HttpAuthRepository._(this._bare, this._authed);

  final Dio _bare;
  final Dio _authed;

  @override
  Future<AuthSession> login(String email, String password) => _guard(() async {
        final response = await _bare.post<Map<String, dynamic>>(
          '/api/auth/login',
          data: {'email': email.trim(), 'password': password},
          options: Options().anonymous,
        );
        return AuthSession.fromJson(response.data!);
      });

  @override
  Future<AuthSession> refresh(String refreshToken) => _guard(() async {
        final response = await _bare.post<Map<String, dynamic>>(
          '/api/auth/refresh',
          data: {'refreshToken': refreshToken},
          options: Options().anonymous,
        );
        return AuthSession.fromJson(response.data!);
      });

  @override
  Future<void> logout(String refreshToken) => _guard(() => _bare.post<void>(
        '/api/auth/logout',
        data: {'refreshToken': refreshToken},
        options: Options().anonymous,
      ));

  @override
  Future<UserSummary> me() => _guard(() async {
        final response = await _authed.get<Map<String, dynamic>>('/api/auth/me');
        return UserSummary.fromJson(response.data!);
      });

  Future<T> _guard<T>(Future<T> Function() call) async {
    try {
      return await call();
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final authRepositoryProvider = Provider<AuthRepository>(
  (ref) => HttpAuthRepository(
    bare: ref.watch(bareDioProvider),
    authed: ref.watch(apiClientProvider),
  ),
);
