import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_exception.dart';
import '../auth/auth_controller.dart';
import '../auth/auth_models.dart';
import '../cases/case_widgets.dart';
import 'registry_models.dart';
import 'registry_repository.dart';

/// "1.3 ha": up to two decimals, no trailing zeros.
String formatHectares(double value) => '${formatNumber(double.parse(value.toStringAsFixed(2)))} ha';

/// "My farms & plots" (§8, Component A): a farmer's farms; an agronomist sees their district's.
/// Only a farmer adds farms here — the API refuses anyone else.
class FarmsScreen extends ConsumerWidget {
  const FarmsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final farms = ref.watch(myFarmsProvider);
    final isFarmer = ref.watch(currentUserProvider)?.role == UserRole.farmer;

    return Scaffold(
      appBar: AppBar(title: Text(isFarmer ? 'My farms & plots' : 'Farms in my district')),
      floatingActionButton: isFarmer
          ? FloatingActionButton.extended(
              onPressed: () => showModalBottomSheet<void>(
                context: context,
                isScrollControlled: true,
                builder: (_) => const _NewFarmSheet(),
              ),
              icon: const Icon(Icons.add),
              label: const Text('Add farm'),
            )
          : null,
      body: RefreshIndicator(
        onRefresh: () => ref.refresh(myFarmsProvider.future),
        child: farms.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(myFarmsProvider)),
          data: (farms) => farms.isEmpty
              ? ListView(
                  padding: const EdgeInsets.all(24),
                  children: [
                    Notice(
                      message: isFarmer
                          ? 'No farms yet. Add your farm, then its plots, to report problems and book harvest collection.'
                          : 'No farms are registered in your district yet.',
                    ),
                  ],
                )
              : ListView.separated(
                  padding: const EdgeInsets.fromLTRB(16, 16, 16, 88),
                  itemCount: farms.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 8),
                  itemBuilder: (_, i) {
                    final f = farms[i];
                    return Card(
                      margin: EdgeInsets.zero,
                      child: ListTile(
                        leading: const Icon(Icons.agriculture),
                        title: Text(f.name),
                        subtitle: Text([
                          if (f.village != null && f.village!.isNotEmpty) f.village!,
                          f.districtName,
                          '${f.plotCount} plot${f.plotCount == 1 ? '' : 's'} · ${formatHectares(f.totalAreaHectares)}',
                        ].join(' · ')),
                        trailing: const Icon(Icons.chevron_right),
                        onTap: () => context.push('/farms/${f.id}'),
                      ),
                    );
                  },
                ),
        ),
      ),
    );
  }
}

class _NewFarmSheet extends ConsumerStatefulWidget {
  const _NewFarmSheet();

  @override
  ConsumerState<_NewFarmSheet> createState() => _NewFarmSheetState();
}

class _NewFarmSheetState extends ConsumerState<_NewFarmSheet> {
  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _village = TextEditingController();
  String? _districtId;
  bool _saving = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    // Most farmers farm where they live: start with their own district.
    _districtId = ref.read(currentUserProvider)?.districtId;
  }

  @override
  void dispose() {
    _name.dispose();
    _village.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final farm = await ref.read(registryRepositoryProvider).createFarm(name: _name.text.trim(), village: _village.text, districtId: _districtId!);
      ref.invalidate(myFarmsProvider);
      if (!mounted) return;
      Navigator.of(context).pop();
      context.push('/farms/${farm.id}');
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final districts = ref.watch(districtsProvider);
    return Padding(
      padding: EdgeInsets.fromLTRB(16, 16, 16, 16 + MediaQuery.of(context).viewInsets.bottom),
      child: Form(
        key: _formKey,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('Add a farm', style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 12),
            if (_error != null) ...[
              Notice(message: _error!, icon: Icons.error_outline, tone: NoticeTone.error),
              const SizedBox(height: 12),
            ],
            TextFormField(
              controller: _name,
              maxLength: 150,
              decoration: const InputDecoration(labelText: 'Farm name'),
              validator: (v) => (v == null || v.trim().isEmpty) ? 'Give the farm a name' : null,
            ),
            TextFormField(
              controller: _village,
              maxLength: 150,
              decoration: const InputDecoration(labelText: 'Village (optional)'),
            ),
            districts.when(
              loading: () => const LinearProgressIndicator(),
              error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(districtsProvider)),
              data: (all) => DropdownButtonFormField<String>(
                initialValue: all.any((d) => d.id == _districtId) ? _districtId : null,
                decoration: const InputDecoration(labelText: 'District'),
                items: [for (final d in all) DropdownMenuItem(value: d.id, child: Text(d.name))],
                onChanged: (id) => setState(() => _districtId = id),
                validator: (id) => id == null ? 'Choose the district' : null,
              ),
            ),
            const SizedBox(height: 20),
            FilledButton(
              onPressed: _saving ? null : _save,
              child: _saving
                  ? const SizedBox.square(dimension: 18, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Text('Add farm'),
            ),
          ],
        ),
      ),
    );
  }
}

/// One farm and its plots, each with what is growing and how far it is from harvest.
class FarmScreen extends ConsumerWidget {
  const FarmScreen({required this.farmId, super.key});

  final String farmId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final farm = ref.watch(farmProvider(farmId));
    final plots = ref.watch(farmPlotsProvider(farmId));
    final isFarmer = ref.watch(currentUserProvider)?.role == UserRole.farmer;

    return Scaffold(
      appBar: AppBar(title: Text(farm.value?.name ?? 'Farm')),
      floatingActionButton: isFarmer
          ? FloatingActionButton.extended(
              onPressed: () => context.push('/farms/$farmId/plots/new'),
              icon: const Icon(Icons.add_location_alt_outlined),
              label: const Text('Add plot'),
            )
          : null,
      body: RefreshIndicator(
        onRefresh: () => ref.refresh(farmPlotsProvider(farmId).future),
        child: plots.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(farmPlotsProvider(farmId))),
          data: (plots) => ListView(
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 88),
            children: [
              if (farm.value case final f?)
                Text(
                  [if (f.village != null && f.village!.isNotEmpty) f.village!, f.districtName, formatHectares(f.totalAreaHectares)].join(' · '),
                  style: Theme.of(context).textTheme.bodyMedium,
                ),
              const SizedBox(height: 12),
              if (plots.isEmpty)
                Notice(message: isFarmer ? 'No plots yet. Add a plot, standing in it if you can, so its location is right.' : 'This farm has no plots yet.'),
              for (final p in plots) ...[
                Card(
                  margin: EdgeInsets.zero,
                  child: ListTile(
                    leading: const Icon(Icons.grass),
                    title: Text(p.title),
                    subtitle: Text(_cropLine(p)),
                    trailing: const Icon(Icons.chevron_right),
                    onTap: () => context.push('/plots/${p.id}'),
                  ),
                ),
                const SizedBox(height: 8),
              ],
            ],
          ),
        ),
      ),
    );
  }

  static String _cropLine(Plot p) {
    final area = formatHectares(p.areaHectares);
    final cycle = p.activeCycle;
    if (cycle == null) return '$area · nothing growing';
    return '$area · ${cycle.cropName}, ${cycle.stage.label} · harvest ${formatDate(cycle.harvestDate)}';
  }
}
