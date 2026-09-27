import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../auth/auth_controller.dart';
import '../auth/auth_models.dart';
import 'case_providers.dart';
import 'case_widgets.dart';

/// The farmer's reported problems, newest first (an agronomist sees their district's).
/// Pull down to refresh; tap a case to follow it.
class CasesScreen extends ConsumerWidget {
  const CasesScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final cases = ref.watch(myCasesProvider);
    final isFarmer = ref.watch(currentUserProvider)?.role == UserRole.farmer;

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
        onRefresh: () => ref.refresh(myCasesProvider.future),
        child: cases.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (error, _) => ListView(
            children: [ErrorRetry(error: error, onRetry: () => ref.invalidate(myCasesProvider))],
          ),
          data: (items) => items.isEmpty
              // A ListView even when empty, so pull-to-refresh still works.
              ? ListView(
                  padding: const EdgeInsets.all(32),
                  children: [
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
                  ],
                )
              : ListView.separated(
                  padding: const EdgeInsets.fromLTRB(8, 8, 8, 88),
                  itemCount: items.length,
                  separatorBuilder: (_, _) => const Divider(height: 1),
                  itemBuilder: (context, index) {
                    final c = items[index];
                    return ListTile(
                      title: Text('${c.cropName} · ${c.plotCode}'),
                      subtitle: Text('${c.referenceNo} · reported ${formatDate(c.createdAt)}'),
                      trailing: CaseStatusChip(c.status),
                      onTap: () => context.push('/cases/${c.id}'),
                    );
                  },
                ),
        ),
      ),
    );
  }
}
