import 'package:flutter/material.dart';

import '../../app/agri_widgets.dart';
import '../../app/theme.dart';
import '../../core/location/location_service.dart';
import 'case_widgets.dart';

/// A fix this close is as good as a plot needs.
const goodFixMetres = 10.0;

/// Says which location will be used and how good it is: the phone's own (with a three-bar
/// quality meter and the words for it), or a fallback when the phone cannot tell. Used by the
/// problem report and the new-plot form.
class GpsCard extends StatelessWidget {
  const GpsCard({
    required this.locating,
    required this.fix,
    required this.onRetry,
    this.refining = false,
    this.live = false,
    this.fixAt,
    this.message,
    super.key,
  });

  final bool locating;
  final bool refining;
  final GeoFix? fix;
  final DateTime? fixAt;
  final VoidCallback onRetry;

  /// The screen keeps asking for a closer fix by itself while the card is shown.
  final bool live;

  /// What the location means for this form; the report's wording when null.
  final String? message;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    if (locating && fix == null) {
      return _frame(
        tone: Tone.active,
        child: Row(
          children: [
            const InlineSpinner(size: 22),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Finding your location…', style: theme.textTheme.titleSmall),
                  Text('Stand in the plot, under open sky, for the best fix.', style: theme.textTheme.bodySmall),
                ],
              ),
            ),
          ],
        ),
      );
    }

    if (fix case final f?) {
      final metres = f.accuracyMetres.round();
      final (bars, quality) = switch (f.accuracyMetres) {
        <= goodFixMetres => (3, 'Good fix'),
        <= 30 => (2, 'Fair fix'),
        _ => (1, 'Weak fix'),
      };
      final tone = switch (bars) {
        3 => Tone.done,
        2 => Tone.active,
        _ => Tone.warning,
      };
      final ink = toneColors(tone).ink;
      final status = switch (bars) {
        _ when refining || locating => 'Looking for a closer fix…',
        < 3 when live => 'Move into the open for a closer fix; this updates by itself.',
        < 3 => 'Move into the open, then tap Update for a closer fix.',
        _ when fixAt != null => 'Updated at ${clockTime(fixAt!)}',
        _ => null,
      };
      return _frame(
        tone: tone,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                _SignalBars(filled: bars, color: ink),
                const SizedBox(width: 12),
                Text('± $metres m', style: theme.textTheme.headlineSmall?.copyWith(color: ink)),
                const SizedBox(width: 10),
                Expanded(child: Text(quality, style: theme.textTheme.titleSmall?.copyWith(color: ink))),
                TextButton(onPressed: locating || refining ? null : onRetry, child: const Text('Update')),
              ],
            ),
            const SizedBox(height: 6),
            Text(message ?? 'Your location will be sent (within $metres m).', style: theme.textTheme.bodyMedium),
            if (status != null) ...[
              const SizedBox(height: 2),
              Semantics(liveRegion: true, child: Text(status, style: theme.textTheme.bodySmall)),
            ],
          ],
        ),
      );
    }

    return _frame(
      tone: Tone.warning,
      child: Row(
        children: [
          const Icon(Icons.location_off_outlined, color: AgriColors.warning800),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              message ?? "Location unavailable — the plot's registered location will be used.",
              style: theme.textTheme.bodyMedium?.copyWith(color: AgriColors.warning800),
            ),
          ),
          TextButton(onPressed: locating ? null : onRetry, child: const Text('Try again')),
        ],
      ),
    );
  }

  Widget _frame({required Tone tone, required Widget child}) {
    final c = toneColors(tone);
    return Container(
      padding: const EdgeInsets.fromLTRB(14, 12, 8, 12),
      decoration: BoxDecoration(color: c.fill, borderRadius: BorderRadius.circular(12), border: Border.all(color: c.ring)),
      child: child,
    );
  }
}

/// Three rising bars, [filled] of them solid: GPS quality as a shape, not only a colour.
class _SignalBars extends StatelessWidget {
  const _SignalBars({required this.filled, required this.color});

  final int filled;
  final Color color;

  @override
  Widget build(BuildContext context) => ExcludeSemantics(
        child: SizedBox(
          width: 26,
          height: 22,
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              for (var i = 0; i < 3; i++)
                Container(
                  width: 6,
                  height: 8.0 + i * 7,
                  decoration: BoxDecoration(
                    color: i < filled ? color : Colors.transparent,
                    border: Border.all(color: color, width: 1.5),
                    borderRadius: BorderRadius.circular(2),
                  ),
                ),
            ],
          ),
        ),
      );
}
