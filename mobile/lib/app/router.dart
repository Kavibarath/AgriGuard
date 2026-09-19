import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/auth/auth_controller.dart';
import '../features/auth/login_screen.dart';
import '../features/home/home_screen.dart';

/// Bridges Riverpod → go_router: any auth change re-evaluates `redirect`.
class _AuthChangeNotifier extends ChangeNotifier {
  void bump() => notifyListeners();
}

final routerProvider = Provider<GoRouter>((ref) {
  final authChanged = _AuthChangeNotifier();
  ref.listen(authControllerProvider, (_, _) => authChanged.bump());
  ref.onDispose(authChanged.dispose);

  return GoRouter(
    initialLocation: '/home',
    refreshListenable: authChanged,
    debugLogDiagnostics: kDebugMode,
    redirect: (context, state) {
      final auth = ref.read(authControllerProvider);
      final location = state.matchedLocation;

      // Still reading the stored session: hold on the splash rather than flashing the login screen.
      if (auth.isLoading) return location == '/splash' ? null : '/splash';

      final signedIn = auth.value != null;
      if (!signedIn) return location == '/login' ? null : '/login';
      if (location == '/login' || location == '/splash') return '/home';
      return null;
    },
    routes: [
      GoRoute(path: '/splash', builder: (_, _) => const _SplashScreen()),
      GoRoute(path: '/login', builder: (_, _) => const LoginScreen()),
      GoRoute(path: '/home', builder: (_, _) => const HomeScreen()),
    ],
  );
});

class _SplashScreen extends StatelessWidget {
  const _SplashScreen();

  @override
  Widget build(BuildContext context) =>
      const Scaffold(body: Center(child: CircularProgressIndicator()));
}
