import 'package:flutter/material.dart';

import '../../core/api/api_exception.dart';
import 'case_models.dart';

/// "2026-09-26": unambiguous in every locale the co-op works in, and what the API sends.
///
/// Timestamps arrive in UTC and are shown on the phone's own calendar: a case reported at 04:45
/// in Sri Lanka is still the 27th in UTC. Plain dates (spray, harvest) are not shifted.
String formatDate(DateTime date) {
  final local = date.toLocal();
  return '${local.year}-${local.month.toString().padLeft(2, '0')}-${local.day.toString().padLeft(2, '0')}';
}

/// "LKR 9,600": whole rupees with thousands separators, as the dealer's receipt prints them.
String formatLkr(double amount) {
  final digits = amount.round().abs().toString();
  final grouped = digits.replaceAllMapped(RegExp(r'\B(?=(\d{3})+(?!\d))'), (_) => ',');
  return 'LKR ${amount < 0 ? '-' : ''}$grouped';
}

/// "0.6" without trailing zeros: 0.60 L reads as more precise than the rule it came from.
String formatNumber(double value) {
  final text = value.toStringAsFixed(3);
  return text.contains('.') ? text.replaceFirst(RegExp(r'\.?0+$'), '') : text;
}

/// Status as a chip: the words carry the meaning, the colour only reinforces it.
class CaseStatusChip extends StatelessWidget {
  const CaseStatusChip(this.status, {super.key});

  final CaseStatus status;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final (background, foreground) = switch (status) {
      CaseStatus.prescribed => (scheme.primaryContainer, scheme.onPrimaryContainer),
      CaseStatus.rejected || CaseStatus.awaitingManualReview => (scheme.tertiaryContainer, scheme.onTertiaryContainer),
      CaseStatus.agentProcessing || CaseStatus.pendingApproval => (scheme.secondaryContainer, scheme.onSecondaryContainer),
      _ => (scheme.surfaceContainerHighest, scheme.onSurfaceVariant),
    };
    return Chip(
      label: Text(status.label),
      backgroundColor: background,
      labelStyle: TextStyle(color: foreground, fontWeight: FontWeight.w500),
      side: BorderSide.none,
      visualDensity: VisualDensity.compact,
    );
  }
}

/// A failed load, in words a farmer can act on, with a way to try again.
class ErrorRetry extends StatelessWidget {
  const ErrorRetry({required this.error, required this.onRetry, super.key});

  final Object error;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final message = error is ApiException ? (error as ApiException).message : 'Something went wrong. Try again.';
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.cloud_off, size: 40, color: Theme.of(context).colorScheme.error),
            const SizedBox(height: 12),
            Text(message, textAlign: TextAlign.center),
            const SizedBox(height: 12),
            OutlinedButton(onPressed: onRetry, child: const Text('Try again')),
          ],
        ),
      ),
    );
  }
}

/// A coloured message block for errors, warnings and notices, announced to screen readers.
class Notice extends StatelessWidget {
  const Notice({required this.message, this.icon = Icons.info_outline, this.tone = NoticeTone.info, super.key});

  final String message;
  final IconData icon;
  final NoticeTone tone;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final (background, foreground) = switch (tone) {
      NoticeTone.error => (scheme.errorContainer, scheme.onErrorContainer),
      NoticeTone.warning => (scheme.tertiaryContainer, scheme.onTertiaryContainer),
      NoticeTone.info => (scheme.secondaryContainer, scheme.onSecondaryContainer),
    };
    return Semantics(
      liveRegion: true,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
        decoration: BoxDecoration(color: background, borderRadius: BorderRadius.circular(8)),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(icon, color: foreground, size: 20),
            const SizedBox(width: 8),
            Expanded(child: Text(message, style: TextStyle(color: foreground))),
          ],
        ),
      ),
    );
  }
}

enum NoticeTone { info, warning, error }

/// The Coordinator agent's general care tips while the farmer waits. Never a product or a dose:
/// the agent drops any tip that names a pesticide or an amount, and treatment only ever comes as a
/// prescription an agronomist has approved.
class AdviceCard extends StatelessWidget {
  const AdviceCard({required this.tips, super.key});

  final List<String> tips;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      margin: EdgeInsets.zero,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('What you can do now', style: theme.textTheme.titleSmall),
            const SizedBox(height: 8),
            for (final tip in tips)
              Padding(
                padding: const EdgeInsets.only(bottom: 6),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Padding(padding: EdgeInsets.only(top: 2), child: Icon(Icons.check_circle_outline, size: 18)),
                    const SizedBox(width: 8),
                    Expanded(child: Text(tip)),
                  ],
                ),
              ),
            Text(
              'General care only. Any treatment comes as a prescription checked by an agronomist.',
              style: theme.textTheme.bodySmall,
            ),
          ],
        ),
      ),
    );
  }
}
