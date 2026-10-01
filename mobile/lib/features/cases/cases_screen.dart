import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../app/agri_widgets.dart';
import '../../app/theme.dart';
import '../auth/auth_controller.dart';
import '../auth/auth_models.dart';
import 'case_models.dart';
import 'case_outbox.dart';
import 'case_providers.dart';
import 'case_widgets.dart';
import 'outbox_banner.dart';

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
    ref.read(outboxLastTryProvider.notifier).record(offline: result.offline);
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
              EmptyState(
                title: isFarmer ? 'No problems reported yet' : 'No cases in your district',
                message: isFarmer ? 'If you see spots, pests or wilting on a crop, report it and get advice.' : null,
              ),
            ]
          : [
              Padding(
                padding: const EdgeInsets.fromLTRB(4, 4, 4, 10),
                child: Text(
                  '${items.length} case${items.length == 1 ? '' : 's'} · newest first',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ),
              for (final c in items) ...[
                _CaseCard(summary: c),
                const SizedBox(height: 10),
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
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
          children: [
            if (pending.isNotEmpty) ...[
              PendingReportsCard(pending: pending, sending: _sending, onSend: _send),
              const SizedBox(height: 16),
            ],
            ...content,
          ],
        ),
      ),
    );
  }
}

/// One case in the list: what and where, its reference and date, and where it has got to.
class _CaseCard extends StatelessWidget {
  const _CaseCard({required this.summary});

  final CaseSummary summary;

  @override
  Widget build(BuildContext context) {
    final c = summary;
    final theme = Theme.of(context);
    return AgriCard(
      padding: const EdgeInsets.fromLTRB(12, 12, 6, 12),
      onTap: () => context.push('/cases/${c.id}'),
      child: Row(
        children: [
          const GlyphTile(Icons.eco_outlined, size: 40),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('${c.cropName} · ${c.plotCode}', style: theme.textTheme.titleSmall),
                Text('${c.referenceNo} · reported ${formatDate(c.createdAt)}', style: theme.textTheme.bodySmall),
                const SizedBox(height: 6),
                CaseStatusChip(c.status),
              ],
            ),
          ),
          const Icon(Icons.chevron_right, color: AgriColors.inkMuted),
        ],
      ),
    );
  }
}
