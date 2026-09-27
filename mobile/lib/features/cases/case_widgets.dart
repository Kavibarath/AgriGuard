import 'package:flutter/material.dart';

import '../../core/api/api_exception.dart';
import 'case_models.dart';

/// "2026-09-26": unambiguous in every locale the co-op works in, and what the API sends.
String formatDate(DateTime date) =>
    '${date.year}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}';

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
