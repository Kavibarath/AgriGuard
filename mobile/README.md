# AgriGuard mobile (Flutter)

Field app for farmers (and agronomist triage). Flutter 3.x, Riverpod, go_router, dio,
flutter_secure_storage. Android only.

```bash
flutter pub get
flutter run                                  # Android emulator: API at http://10.0.2.2:5000
flutter run --dart-define=API_BASE_URL=http://192.168.1.20:5000   # physical phone on the LAN
flutter analyze
flutter test
flutter build apk --release --dart-define=API_BASE_URL=https://<render-api>
```

Debug builds allow plain HTTP to the dev API (`android/app/src/debug/AndroidManifest.xml`);
release builds do not.

## Layout

```
lib/
  app/            MaterialApp, router (auth redirect guard)
  core/
    api/          dio clients, auth interceptor (bearer + refresh-once), ApiException
    storage/      TokenStorage (Keystore-backed) + in-memory test double
  features/
    auth/         models, repository, controller (Riverpod AsyncNotifier), login screen
    home/         role-aware landing screen
test/             unit (controller, interceptor) and widget (login flow) tests
```
