import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../../features/auth/auth_models.dart';

/// Where the session lives between app launches.
///
/// The real implementation is Keystore-backed (`flutter_secure_storage`). `SharedPreferences`
/// is deliberately not used: it is a plain XML file readable by anything with the device's
/// backup or root access, and the refresh token in it is a 14-day password.
abstract class TokenStorage {
  Future<AuthSession?> read();
  Future<void> write(AuthSession session);
  Future<void> clear();
}

class SecureTokenStorage implements TokenStorage {
  // v10+ of the plugin encrypts with a Keystore-backed key by default on Android.
  SecureTokenStorage([FlutterSecureStorage? storage])
      : _storage = storage ?? const FlutterSecureStorage();

  static const _key = 'agriguard.session';
  final FlutterSecureStorage _storage;

  @override
  Future<AuthSession?> read() async {
    final raw = await _storage.read(key: _key);
    if (raw == null) return null;
    try {
      return AuthSession.fromJson(jsonDecode(raw) as Map<String, dynamic>);
    } on FormatException {
      // A corrupt entry must not brick the app: treat it as signed out.
      await clear();
      return null;
    }
  }

  @override
  Future<void> write(AuthSession session) =>
      _storage.write(key: _key, value: jsonEncode(session.toJson()));

  @override
  Future<void> clear() => _storage.delete(key: _key);
}

/// Test double: same contract, nothing persisted.
class InMemoryTokenStorage implements TokenStorage {
  InMemoryTokenStorage([this._session]);

  AuthSession? _session;

  @override
  Future<AuthSession?> read() async => _session;

  @override
  Future<void> write(AuthSession session) async => _session = session;

  @override
  Future<void> clear() async => _session = null;
}

final tokenStorageProvider = Provider<TokenStorage>((ref) => SecureTokenStorage());
