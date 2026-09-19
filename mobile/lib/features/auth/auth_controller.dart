import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/storage/token_storage.dart';
import 'auth_models.dart';
import 'auth_repository.dart';

/// The signed-in session, or `null` when signed out. `AsyncLoading` only while the stored
/// session is being read at launch — the router shows a splash until then.
///
/// Riverpod (ADR 2) over Bloc: one class holds the state, the transitions and the
/// dependencies, resolved by the container — no event/state boilerplate, and tests swap
/// [tokenStorageProvider] and [authRepositoryProvider] with overrides.
class AuthController extends AsyncNotifier<AuthSession?> {
  Future<String?>? _inflightRefresh;

  @override
  Future<AuthSession?> build() async {
    // Trust the stored session without a network call: the field app must open offline.
    // A stale access token is fixed by the interceptor on the first real request.
    final stored = await ref.watch(tokenStorageProvider).read();
    if (stored == null) return null;
    if (stored.refreshTokenExpiresAt.isBefore(DateTime.now().toUtc())) {
      await ref.read(tokenStorageProvider).clear();
      return null;
    }
    return stored;
  }

  /// Throws [ApiException] on failure; the existing state is left untouched so a typo does
  /// not sign out a session that was still valid.
  Future<void> login(String email, String password) async {
    final session = await ref.read(authRepositoryProvider).login(email, password);
    await ref.read(tokenStorageProvider).write(session);
    state = AsyncData(session);
  }

  /// Revokes on the server when it can; signs out locally regardless.
  Future<void> logout() async {
    final session = state.value;
    state = const AsyncData(null);
    await ref.read(tokenStorageProvider).clear();
    if (session != null) {
      try {
        await ref.read(authRepositoryProvider).logout(session.refreshToken);
      } catch (_) {
        // Offline logout is still a logout: the token expires on its own within 14 days.
      }
    }
  }

  /// Exchanges the refresh token for a new pair. Single-flight: concurrent callers share one
  /// request, because a second use of the same refresh token is a replay and revokes the account.
  /// Returns the new access token, or `null` (and signs out) when the session cannot be renewed.
  Future<String?> refreshSession() {
    return _inflightRefresh ??= _doRefresh().whenComplete(() => _inflightRefresh = null);
  }

  Future<String?> _doRefresh() async {
    final current = state.value;
    if (current == null) return null;

    try {
      final renewed = await ref.read(authRepositoryProvider).refresh(current.refreshToken);
      await ref.read(tokenStorageProvider).write(renewed);
      state = AsyncData(renewed);
      return renewed.accessToken;
    } catch (_) {
      await ref.read(tokenStorageProvider).clear();
      state = const AsyncData(null);
      return null;
    }
  }
}

final authControllerProvider =
    AsyncNotifierProvider<AuthController, AuthSession?>(AuthController.new);

/// The signed-in user, or `null`.
final currentUserProvider = Provider<UserSummary?>(
  (ref) => ref.watch(authControllerProvider).value?.user,
);
