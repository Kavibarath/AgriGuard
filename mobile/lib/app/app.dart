import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'router.dart';
import 'theme.dart';

class AgriGuardApp extends ConsumerWidget {
  const AgriGuardApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return MaterialApp.router(
      title: 'AgriGuard',
      routerConfig: ref.watch(routerProvider),
      // Same palette and type roles as the web console (app/theme.dart, docs/design/TOKENS.md).
      theme: buildAgriTheme(),
    );
  }
}
