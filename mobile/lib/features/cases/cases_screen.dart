import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../auth/auth_controller.dart';
import '../auth/auth_models.dart';
import 'case_outbox.dart';
import 'case_providers.dart';
import 'case_widgets.dart';

/// The farmer's reported problems, newest first (an agronomist sees their district's).
/// Pull down to refresh; tap a case to follow it.
///
/// Reports saved without a signal are listed above the cases. The phone tries to send them when
/// this screen opens, when the app comes back to the foreground, on pull-to-refresh and on "Send
/// now". The list of cases may fail to load offline; the queue is shown regardless.
class CasesScreen extends ConsumerStatefulWidget {
  const CasesScreen({super.key});

  @override
  ConsumerState<CasesScreen> createState() => _CasesScreenState();
}

class _CasesScreenState extends ConsumerState<CasesScreen> {
  late final AppLifecycleListener _lifecycle;
  bool _sending = false;

  @override
  void initState() {
    super.initState();
    // Back in the app is the likeliest moment the farmer is back in coverage.
    _lifecycle = AppLifecycleListener(onResume: () => _send(quiet: true));
    WidgetsBinding.instance.addPostFrameCallback((_) => _send(quiet: true));
  }

  @override
  void dispose() {
    _lifecycle.dispose();
    super.dispose();
  }

  /// One pass over the queue. [quiet] passes say nothing when there was nothing to report.
  Future<void> _send({bool quiet = false}) async {
    await ref.read(caseOutboxProvider.future);
    if (!mounted || ref.read(myPendingCasesProvider).every((p) => p.state != PendingState.waiting)) return;
    setState(() => _sending = true);
    final result = await ref.read(caseOutboxProvider.notifier).sync();
    if (!mounted) return;
    setState(() => _sending = false);
    if (result.sent > 0) ref.invalidate(myCasesProvider);

    final message = switch (result) {
      SyncResult(sent: > 0, offline: false) => 'Sent ${result.sent} saved report${result.sent == 1 ? '' : 's'}.',
      SyncResult(sent: > 0, offline: true) => 'Sent ${result.sent}; the rest will go when the connection is better.',
      SyncResult(offline: true) when !quiet => 'Still no connection. Your reports are safe on this phone.',
      SyncResult(refused: > 0) => 'A saved report was refused. See why below.',
      _ => null,
    };
    // Newer news replaces an older message (such as "saved on this phone") rather than queueing behind it.
    if (message != null) {
      ScaffoldMessenger.of(context)
        ..hideCurrentSnackBar()
        ..showSnackBar(SnackBar(content: Text(message)));
    }
  }

  @override
  Widget build(BuildContext context) {
    final cases = ref.watch(myCasesProvider);
    final pending = ref.watch(myPendingCasesProvider);
    final isFarmer = ref.watch(currentUserProvider)?.role == UserRole.farmer;

    final List<Widget> content = cases.when(
      loading: () => [const Padding(padding: EdgeInsets.all(32), child: Center(child: CircularProgressIndicator()))],
      error: (error, _) => [ErrorRetry(error: error, onRetry: () => ref.invalidate(myCasesProvider))],
      data: (items) => items.isEmpty
          ? [
              const SizedBox(height: 24),
              const Icon(Icons.eco_outlined, size: 48),
              const SizedBox(height: 12),
              Text(
                isFarmer ? 'No problems reported yet' : 'No cases in your district',
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.titleMedium,
              ),
              if (isFarmer)
                const Text(
                  'If you see spots, pests or wilting on a crop, report it and get advice.',
                  textAlign: TextAlign.center,
                ),
            ]
          : [
              for (final (index, c) in items.indexed) ...[
                if (index > 0) const Divider(height: 1),
                ListTile(
                  title: Text('${c.cropName} · ${c.plotCode}'),
                  subtitle: Text('${c.referenceNo} · reported ${formatDate(c.createdAt)}'),
                  trailing: CaseStatusChip(c.status),
                  onTap: () => context.push('/cases/${c.id}'),
                ),
              ],
            ],
    );

    return Scaffold(
      appBar: AppBar(title: Text(isFarmer ? 'My crop problems' : 'Cases in my district')),
      floatingActionButton: isFarmer
          ? FloatingActionButton.extended(
              onPressed: () => context.push('/cases/new'),
              icon: const Icon(Icons.add_a_photo_outlined),
              label: const Text('Report a problem'),
            )
          : null,
      body: RefreshIndicator(
        onRefresh: () async {
          await _send(quiet: true);
          await ref.refresh(myCasesProvider.future).catchError((_) => const <Never>[]);
        },
        // One scrolling list, even when empty or offline, so pull-to-refresh always works.
        child: ListView(
          padding: const EdgeInsets.fromLTRB(8, 8, 8, 88),
          children: [
            if (pending.isNotEmpty) ...[
              PendingReportsCard(pending: pending, sending: _sending, onSend: _send),
              const SizedBox(height: 8),
            ],
            ...content,
          ],
        ),
      ),
    );
  }
}

/// The reports waiting on this phone: what each is, when it was made, and why it has not gone.
class PendingReportsCard extends ConsumerWidget {
  const PendingReportsCard({required this.pending, required this.sending, required this.onSend, super.key});

  final List<PendingCase> pending;
  final bool sending;
  final VoidCallback onSend;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final waiting = pending.where((p) => p.state == PendingState.waiting).length;

    return Card(
      margin: EdgeInsets.zero,
      color: theme.colorScheme.secondaryContainer,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 12, 8, 8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                const Icon(Icons.cloud_upload_outlined),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    waiting > 0 ? 'Saved on this phone, waiting to send ($waiting)' : 'Saved reports',
                    style: theme.textTheme.titleSmall,
                  ),
                ),
                if (waiting > 0)
                  TextButton.icon(
                    onPressed: sending ? null : onSend,
                    icon: sending
                        ? const SizedBox.square(dimension: 16, child: CircularProgressIndicator(strokeWidth: 2))
                        : const Icon(Icons.send, size: 18),
                    label: const Text('Send now'),
                  ),
              ],
            ),
            for (final item in pending)
              ListTile(
                contentPadding: const EdgeInsets.only(left: 32),
                dense: true,
                title: Text(item.label),
                subtitle: Text(switch (item.state) {
                  PendingState.refused => 'Not accepted: ${item.lastError ?? 'the report was refused.'}',
                  PendingState.waiting when item.caseId != null =>
                    'Reported; ${item.photoFiles.length} photo${item.photoFiles.length == 1 ? '' : 's'} still to send',
                  PendingState.waiting => 'Made ${_timeOf(item.capturedAt)} · will be sent when online',
                }),
                trailing: item.state == PendingState.refused
                    ? IconButton(
                        tooltip: 'Discard',
                        icon: const Icon(Icons.delete_outline),
                        onPressed: () => ref.read(caseOutboxProvider.notifier).discard(item.clientReference),
                      )
                    : null,
              ),
          ],
        ),
      ),
    );
  }

  /// "2026-09-29 10:42" on the phone's own clock.
  static String _timeOf(DateTime at) {
    final local = at.toLocal();
    String two(int n) => n.toString().padLeft(2, '0');
    return '${formatDate(local)} ${two(local.hour)}:${two(local.minute)}';
  }
}
