import 'package:agriguard_mobile/core/api/api_exception.dart';
import 'package:agriguard_mobile/core/storage/token_storage.dart';
import 'package:agriguard_mobile/features/auth/auth_controller.dart';
import 'package:agriguard_mobile/features/auth/auth_models.dart';
import 'package:agriguard_mobile/features/auth/auth_repository.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'fixtures.dart';

class MockAuthRepository extends Mock implements AuthRepository {}

void main() {
  late MockAuthRepository repository;
  late InMemoryTokenStorage storage;

  ProviderContainer makeContainer() {
    final container = ProviderContainer(
      overrides: [
        authRepositoryProvider.overrideWithValue(repository),
        tokenStorageProvider.overrideWithValue(storage),
      ],
    );
    addTearDown(container.dispose);
    return container;
  }

  setUp(() {
    repository = MockAuthRepository();
    storage = InMemoryTokenStorage();
  });

  group('startup', () {
    test('is signed out when nothing is stored', () async {
      final container = makeContainer();

      expect(await container.read(authControllerProvider.future), isNull);
    });

    test('restores a stored session without a network call', () async {
      storage = InMemoryTokenStorage(farmerSession());
      final container = makeContainer();

      final session = await container.read(authControllerProvider.future);

      expect(session?.user.email, farmer.email);
      verifyZeroInteractions(repository);
    });

    test('discards a stored session whose refresh token has expired', () async {
      storage = InMemoryTokenStorage(farmerSession(
        refreshExpiresAt: DateTime.now().toUtc().subtract(const Duration(days: 1)),
      ));
      final container = makeContainer();

      expect(await container.read(authControllerProvider.future), isNull);
      expect(await storage.read(), isNull);
    });
  });

  group('login', () {
    test('stores the session and exposes the user', () async {
      when(() => repository.login(any(), any())).thenAnswer((_) async => farmerSession());
      final container = makeContainer();
      await container.read(authControllerProvider.future);

      await container.read(authControllerProvider.notifier).login(farmer.email, 'pw');

      expect(container.read(currentUserProvider)?.role, UserRole.farmer);
      expect((await storage.read())?.refreshToken, 'refresh-1');
    });

    test('throws and leaves the state signed out on bad credentials', () async {
      when(() => repository.login(any(), any()))
          .thenThrow(ApiException(message: 'Invalid credentials.', statusCode: 401));
      final container = makeContainer();
      await container.read(authControllerProvider.future);

      await expectLater(
        container.read(authControllerProvider.notifier).login(farmer.email, 'wrong'),
        throwsA(isA<ApiException>().having((e) => e.message, 'message', 'Invalid credentials.')),
      );
      expect(container.read(currentUserProvider), isNull);
      expect(await storage.read(), isNull);
    });
  });

  group('refreshSession', () {
    test('rotates the tokens and returns the new access token', () async {
      storage = InMemoryTokenStorage(farmerSession());
      when(() => repository.refresh('refresh-1'))
          .thenAnswer((_) async => farmerSession(suffix: '2'));
      final container = makeContainer();
      await container.read(authControllerProvider.future);

      final token = await container.read(authControllerProvider.notifier).refreshSession();

      expect(token, 'access-2');
      expect((await storage.read())?.refreshToken, 'refresh-2');
    });

    test('collapses concurrent callers into one request', () async {
      storage = InMemoryTokenStorage(farmerSession());
      when(() => repository.refresh(any())).thenAnswer((_) async {
        await Future<void>.delayed(const Duration(milliseconds: 20));
        return farmerSession(suffix: '2');
      });
      final container = makeContainer();
      await container.read(authControllerProvider.future);
      final notifier = container.read(authControllerProvider.notifier);

      final tokens = await Future.wait([notifier.refreshSession(), notifier.refreshSession(), notifier.refreshSession()]);

      expect(tokens, everyElement('access-2'));
      // A second call with the same refresh token is a replay: the API would revoke everything.
      verify(() => repository.refresh('refresh-1')).called(1);
    });

    test('signs out when the refresh token is rejected', () async {
      storage = InMemoryTokenStorage(farmerSession());
      when(() => repository.refresh(any()))
          .thenThrow(ApiException(message: 'Invalid refresh token.', statusCode: 401));
      final container = makeContainer();
      await container.read(authControllerProvider.future);

      final token = await container.read(authControllerProvider.notifier).refreshSession();

      expect(token, isNull);
      expect(container.read(currentUserProvider), isNull);
      expect(await storage.read(), isNull);
    });
  });

  group('logout', () {
    test('clears locally even when the server call fails', () async {
      storage = InMemoryTokenStorage(farmerSession());
      when(() => repository.logout(any()))
          .thenThrow(ApiException(message: 'offline', isNetworkError: true));
      final container = makeContainer();
      await container.read(authControllerProvider.future);

      await container.read(authControllerProvider.notifier).logout();

      expect(container.read(currentUserProvider), isNull);
      expect(await storage.read(), isNull);
      verify(() => repository.logout('refresh-1')).called(1);
    });
  });
}
