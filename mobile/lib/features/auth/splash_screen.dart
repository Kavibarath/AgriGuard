import 'package:flutter/material.dart';

import '../../app/agri_widgets.dart';
import '../../app/theme.dart';

/// Shown while the stored session is read, so the login screen never flashes past. It follows
/// the Android launch screen (the mark on the warm page), so the hand-over is seamless.
class SplashScreen extends StatelessWidget {
  const SplashScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      body: Center(
        child: Semantics(
          label: 'AgriGuard is starting',
          liveRegion: true,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const BrandMark(size: 72),
              const SizedBox(height: 16),
              Text('AgriGuard', style: theme.textTheme.headlineMedium),
              const SizedBox(height: 4),
              Text('Crop advice, checked for safety', style: theme.textTheme.bodyMedium?.copyWith(color: AgriColors.inkMuted)),
              const SizedBox(height: 28),
              const SizedBox(width: 120, child: LinearProgressIndicator(minHeight: 3)),
            ],
          ),
        ),
      ),
    );
  }
}
