import 'package:flutter/material.dart';

import '../../app/agri_widgets.dart';
import '../../app/theme.dart';
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

/// "10:42" on the phone's own clock.
String clockTime(DateTime at) {
  final local = at.toLocal();
  return '${local.hour.toString().padLeft(2, '0')}:${local.minute.toString().padLeft(2, '0')}';
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

/// How each case status reads to the farmer. Unlike the console, where "pending approval" is a
/// warning (an agronomist must act), on the phone it is "in progress": the farmer has nothing to
/// do but wait. A hand-off to an agronomist is flagged; a refusal is the only cross.
Tone caseStatusTone(CaseStatus status) => switch (status) {
      CaseStatus.submitted || CaseStatus.closed => Tone.neutral,
      CaseStatus.agentProcessing || CaseStatus.pendingApproval => Tone.active,
      CaseStatus.awaitingManualReview => Tone.warning,
      CaseStatus.prescribed => Tone.done,
      CaseStatus.rejected => Tone.danger,
    };

/// Status as a pill: the words carry the meaning, the icon's shape repeats it, the colour only
/// reinforces it.
class CaseStatusChip extends StatelessWidget {
  const CaseStatusChip(this.status, {super.key});

  final CaseStatus status;

  @override
  Widget build(BuildContext context) => StatusPill(label: status.label, tone: caseStatusTone(status));
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
            Container(
              width: 64,
              height: 64,
              decoration: BoxDecoration(
                color: AgriColors.danger50,
                shape: BoxShape.circle,
                border: Border.all(color: AgriColors.danger200),
              ),
              child: const Icon(Icons.cloud_off_outlined, size: 30, color: AgriColors.danger800),
            ),
            const SizedBox(height: 14),
            Text(message, textAlign: TextAlign.center, style: Theme.of(context).textTheme.bodyLarge),
            const SizedBox(height: 16),
            OutlinedButton.icon(onPressed: onRetry, icon: const Icon(Icons.refresh), label: const Text('Try again')),
          ],
        ),
      ),
    );
  }
}

/// A message block for errors, warnings, information and good news, announced to screen
/// readers. Each tone has its own shape (an octagon for errors, a triangle for warnings, a circle
/// for information, a tick for success); [icon] replaces it only where a more specific picture
/// says more, and the words always carry the meaning.
class Notice extends StatelessWidget {
  const Notice({required this.message, this.icon, this.tone = NoticeTone.info, this.title, super.key});

  final String message;
  final IconData? icon;
  final NoticeTone tone;

  /// A bold first line, for notices that need a headline.
  final String? title;

  @override
  Widget build(BuildContext context) {
    final c = toneColors(switch (tone) {
      NoticeTone.error => Tone.danger,
      NoticeTone.warning => Tone.warning,
      NoticeTone.info => Tone.active,
      NoticeTone.success => Tone.done,
    });
    final shape = icon ??
        switch (tone) {
          NoticeTone.error => Icons.report_outlined,
          NoticeTone.warning => Icons.warning_amber_rounded,
          NoticeTone.info => Icons.info_outline,
          NoticeTone.success => Icons.check_circle_outline,
        };
    return Semantics(
      liveRegion: true,
      child: Container(
        padding: const EdgeInsets.fromLTRB(12, 12, 14, 12),
        decoration: BoxDecoration(
          color: c.fill,
          borderRadius: BorderRadius.circular(10),
          border: Border.all(color: c.ring),
        ),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(shape, color: c.ink, size: 22),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  if (title != null) ...[
                    Text(title!, style: TextStyle(color: c.ink, fontWeight: FontWeight.w700, fontSize: 16, height: 1.35)),
                    const SizedBox(height: 2),
                  ],
                  Text(message, style: TextStyle(color: c.ink, fontSize: 16, height: 1.45)),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

enum NoticeTone { info, warning, error, success }

/// The Coordinator agent's general care tips while the farmer waits. Never a product or a dose:
/// the agent drops any tip that names a pesticide or an amount, and treatment only ever comes as a
/// prescription an agronomist has approved.
class AdviceCard extends StatelessWidget {
  const AdviceCard({required this.tips, super.key});

  final List<String> tips;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return AgriCard(
      color: AgriColors.brand50,
      borderColor: AgriColors.brand200,
      raised: false,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.spa_outlined, size: 20, color: AgriColors.brand700),
              const SizedBox(width: 8),
              Text('What you can do now', style: theme.textTheme.titleSmall?.copyWith(color: AgriColors.brand900)),
            ],
          ),
          const SizedBox(height: 10),
          for (final tip in tips)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Padding(
                    padding: EdgeInsets.only(top: 3),
                    child: Icon(Icons.check_circle_outline, size: 18, color: AgriColors.brand700),
                  ),
                  const SizedBox(width: 10),
                  Expanded(child: Text(tip, style: theme.textTheme.bodyMedium?.copyWith(color: AgriColors.brand900))),
                ],
              ),
            ),
          const SizedBox(height: 2),
          Text(
            'General care only. Any treatment comes as a prescription checked by an agronomist.',
            style: theme.textTheme.bodySmall?.copyWith(color: AgriColors.brand800),
          ),
        ],
      ),
    );
  }
}
