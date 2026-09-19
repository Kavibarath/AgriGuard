import 'dart:convert';

import 'package:agriguard_mobile/core/api/api_client.dart';
import 'package:agriguard_mobile/core/storage/token_storage.dart';
import 'package:agriguard_mobile/features/auth/auth_controller.dart';
import 'package:agriguard_mobile/features/auth/auth_repository.dart';
import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

import 'fixtures.dart';

/// Scripted HTTP transport: each call pops the next canned response and records the request.
class _ScriptedAdapter implements HttpClientAdapter {
  _ScriptedAdapter(this._responses);

  final List<ResponseBody> _responses;
  final List<RequestOptions> requests = [];

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<List<int>>? body, Future<void>? cancel) async {
    requests.add(options);
    if (_responses.isEmpty) throw StateError('No scripted response for ${options.method} ${options.path}');
    return _responses.removeAt(0);
  }

  @override
  void close({bool force = false}) {}
}

ResponseBody _json(int status, Object body) => ResponseBody.fromString(
      jsonEncode(body),
      status,
      headers: {
        Headers.contentTypeHeader: ['application/json'],
      },
    );

void main() {
  /// Both clients share one scripted transport, so the order of calls is visible end to end.
  ({ProviderContainer container, _ScriptedAdapter adapter}) setUpClients(List<ResponseBody> script) {
    final adapter = _ScriptedAdapter(script);
    final container = ProviderContainer(
      overrides: [tokenStorageProvider.overrideWithValue(InMemoryTokenStorage(farmerSession()))],
    );
    addTearDown(container.dispose);
    container.read(bareDioProvider).httpClientAdapter = adapter;
    container.read(apiClientProvider).httpClientAdapter = adapter;
    // Make sure the real repository is wired to these clients before any request.
    container.read(authRepositoryProvider);
    return (container: container, adapter: adapter);
  }

  test('attaches the bearer token and a correlation id', () async {
    final (:container, :adapter) = setUpClients([_json(200, farmer.toJson())]);
    await container.read(authControllerProvider.future);

    await container.read(apiClientProvider).get<Map<String, dynamic>>('/api/auth/me');

    final request = adapter.requests.single;
    expect(request.headers['Authorization'], 'Bearer access-1');
    expect(request.headers['X-Correlation-Id'], startsWith('app-'));
  });

  test('on 401: refreshes once, retries with the new token, and persists the rotation', () async {
    final (:container, :adapter) = setUpClients([
      _json(401, problemJson(401, 'expired')), // GET /me with the stale token
      _json(200, loginJson(suffix: '2')), // POST /refresh
      _json(200, farmer.toJson()), // GET /me retried
    ]);
    await container.read(authControllerProvider.future);

    final response = await container.read(apiClientProvider).get<Map<String, dynamic>>('/api/auth/me');

    expect(response.statusCode, 200);
    expect(adapter.requests.map((r) => r.path), ['/api/auth/me', '/api/auth/refresh', '/api/auth/me']);
    expect(adapter.requests[1].headers.containsKey('Authorization'), isFalse, reason: 'refresh is anonymous');
    expect(adapter.requests[2].headers['Authorization'], 'Bearer access-2');
    expect(container.read(authControllerProvider).value?.refreshToken, 'refresh-2');
  });

  test('on 401 with a dead refresh token: signs out and surfaces the 401 once', () async {
    final (:container, :adapter) = setUpClients([
      _json(401, problemJson(401, 'expired')),
      _json(401, problemJson(401, 'Invalid refresh token.')),
    ]);
    await container.read(authControllerProvider.future);

    await expectLater(
      container.read(apiClientProvider).get<void>('/api/auth/me'),
      throwsA(isA<DioException>().having((e) => e.response?.statusCode, 'status', 401)),
    );

    expect(adapter.requests.length, 2, reason: 'no second retry, no second refresh');
    expect(container.read(authControllerProvider).value, isNull);
  });

  test('anonymous requests never trigger a refresh', () async {
    final (:container, :adapter) = setUpClients([_json(401, problemJson(401, 'Invalid credentials.'))]);
    await container.read(authControllerProvider.future);

    await expectLater(
      container.read(apiClientProvider).post<void>('/api/auth/login', options: Options().anonymous),
      throwsA(isA<DioException>()),
    );

    expect(adapter.requests.length, 1);
    expect(adapter.requests.single.headers.containsKey('Authorization'), isFalse);
  });
}
