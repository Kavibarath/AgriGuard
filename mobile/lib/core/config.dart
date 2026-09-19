/// Build-time configuration. Override with `--dart-define=API_BASE_URL=https://...`.
///
/// The default is the Android emulator's alias for the host machine's localhost, where the
/// API runs on port 5000 in development. A physical phone needs the PC's LAN address instead.
const String apiBaseUrl = String.fromEnvironment(
  'API_BASE_URL',
  defaultValue: 'http://10.0.2.2:5000',
);
