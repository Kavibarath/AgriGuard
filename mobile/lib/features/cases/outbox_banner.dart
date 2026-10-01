import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/agri_widgets.dart';
import '../../app/theme.dart';
import 'case_outbox.dart';
import 'case_widgets.dart';

/// The last time the phone tried to send its saved reports, and whether the connection stopped it.
/// Screen state only: it lets the banner say "no signal at 10:42" instead of leaving the farmer to
/// wonder whether anything was tried.
class OutboxTry {
  const OutboxTry({required this.at, required this.offline});

  final DateTime at;
  final bool offline;
}

class OutboxLastTry extends Notifier<OutboxTry?> {
  @override
  OutboxTry? build() => null;

  void record({required bool offline}) => state = OutboxTry(at: DateTime.now(), offline: offline);
}

final outboxLastTryProvider = NotifierProvider<OutboxLastTry, OutboxTry?>(OutboxLastTry.new);

/// The reports waiting on this phone: what each is, when it was made, why it has not gone, and
/// what the phone will do next. Shown above the case list for as long as anything is waiting.
class PendingReportsCard extends ConsumerWidget {
  const PendingReportsCard({required this.pending, required this.sending, required this.onSend, super.key});

  final List<PendingCase> pending;
  final bool sending;
  final VoidCallback onSend;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final waiting = pending.where((p) => p.state == PendingState.waiting).length;
    final lastTry = ref.watch(outboxLastTryProvider);
    final c = toneColors(Tone.active);

    final retry = switch ((sending, lastTry)) {
      (true, _) => 'Sending now…',
      (false, OutboxTry(offline: true, :final at)) =>
        'No signal at ${clockTime(at)}. The phone tries again when you open this list, pull down, or come back to the app.',
      _ => 'Your reports are safe here. They go by themselves once you have a signal.',
    };

    return Container(
      decoration: BoxDecoration(
        color: c.fill,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: c.ring),
      ),
      padding: const EdgeInsets.fromLTRB(14, 14, 14, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                width: 40,
                height: 40,
                decoration: BoxDecoration(color: AgriColors.surfaceCard, borderRadius: BorderRadius.circular(10), border: Border.all(color: c.ring)),
                child: Icon(Icons.cloud_upload_outlined, color: c.ink),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      waiting > 0 ? 'Saved on this phone, waiting to send ($waiting)' : 'Saved reports',
                      style: theme.textTheme.titleSmall?.copyWith(color: c.ink),
                    ),
                    if (waiting > 0) ...[
                      const SizedBox(height: 2),
                      Semantics(
                        liveRegion: true,
                        child: Row(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            if (sending) ...[
                              Padding(padding: const EdgeInsets.only(top: 3), child: InlineSpinner(size: 14, color: c.ink)),
                              const SizedBox(width: 6),
                            ],
                            Expanded(child: Text(retry, style: theme.textTheme.bodySmall?.copyWith(color: c.ink))),
                          ],
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
          for (final item in pending)
            Container(
              margin: const EdgeInsets.only(bottom: 8),
              padding: const EdgeInsets.fromLTRB(12, 10, 4, 10),
              decoration: BoxDecoration(
                color: AgriColors.surfaceCard,
                borderRadius: BorderRadius.circular(10),
                border: Border.all(color: item.state == PendingState.refused ? AgriColors.danger200 : AgriColors.borderSubtle),
              ),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Padding(
                    padding: const EdgeInsets.only(top: 2),
                    child: ToneIcon(item.state == PendingState.refused ? Tone.danger : Tone.active, size: 20),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(item.label, style: theme.textTheme.titleSmall),
                        Text(
                          switch (item.state) {
                            PendingState.refused => 'Not accepted: ${item.lastError ?? 'the report was refused.'}',
                            PendingState.waiting when item.caseId != null =>
                              'Reported; ${item.photoFiles.length} photo${item.photoFiles.length == 1 ? '' : 's'} still to send',
                            PendingState.waiting => 'Made ${_timeOf(item.capturedAt)} · will be sent when online',
                          },
                          style: theme.textTheme.bodySmall?.copyWith(
                            color: item.state == PendingState.refused ? AgriColors.danger800 : AgriColors.inkMuted,
                          ),
                        ),
                      ],
                    ),
                  ),
                  if (item.state == PendingState.refused)
                    IconButton(
                      tooltip: 'Discard',
                      icon: const Icon(Icons.delete_outline),
                      color: AgriColors.danger800,
                      onPressed: () => ref.read(caseOutboxProvider.notifier).discard(item.clientReference),
                    ),
                ],
              ),
            ),
          if (waiting > 0)
            OutlinedButton.icon(
              onPressed: sending ? null : onSend,
              style: OutlinedButton.styleFrom(
                backgroundColor: AgriColors.surfaceCard,
                foregroundColor: c.ink,
                side: BorderSide(color: c.ink.withValues(alpha: 0.5)),
              ),
              icon: sending ? InlineSpinner(size: 16, color: c.ink) : const Icon(Icons.send_outlined, size: 20),
              label: const Text('Send now'),
            ),
        ],
      ),
    );
  }

  /// "2026-09-29 10:42" on the phone's own clock.
  static String _timeOf(DateTime at) => '${formatDate(at)} ${clockTime(at)}';
}

/// The home screen's one-line reminder that reports are waiting, so the queue is never out of
/// sight. Tapping it opens the case list, which sends what it can as it opens.
class OutboxReminder extends ConsumerWidget {
  const OutboxReminder({required this.onOpen, super.key});

  final VoidCallback onOpen;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final pending = ref.watch(myPendingCasesProvider);
    if (pending.isEmpty) return const SizedBox.shrink();
    final refused = pending.where((p) => p.state == PendingState.refused).length;
    final waiting = pending.length - refused;
    final tone = refused > 0 ? Tone.danger : Tone.active;
    final c = toneColors(tone);
    final theme = Theme.of(context);

    String reports(int n) => '$n report${n == 1 ? '' : 's'}';
    final headline = [
      if (waiting > 0) '${reports(waiting)} waiting to send',
      if (refused > 0) '${reports(refused)} not accepted',
    ].join(' · ');

    return Material(
      color: c.fill,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12), side: BorderSide(color: c.ring)),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onOpen,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(14, 12, 10, 12),
          child: Row(
            children: [
              Icon(refused > 0 ? Icons.report_outlined : Icons.cloud_upload_outlined, color: c.ink),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(headline, style: theme.textTheme.titleSmall?.copyWith(color: c.ink)),
                    Text(
                      refused > 0 ? 'Open to see why' : 'Saved safely on this phone',
                      style: theme.textTheme.bodySmall?.copyWith(color: c.ink),
                    ),
                  ],
                ),
              ),
              Icon(Icons.chevron_right, color: c.ink),
            ],
          ),
        ),
      ),
    );
  }
}
