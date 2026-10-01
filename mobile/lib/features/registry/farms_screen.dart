import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../app/agri_widgets.dart';
import '../../app/theme.dart';
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
                  children: [
                    EmptyState(
                      earth: true,
                      title: isFarmer ? 'No farms yet' : 'No farms in your district',
                      message: isFarmer
                          ? 'Add your farm, then its plots, to report problems and book harvest collection.'
                          : 'Farms appear here once farmers in your district register them.',
                    ),
                  ],
                )
              : ListView.separated(
                  padding: const EdgeInsets.fromLTRB(16, 16, 16, 96),
                  // With more than one farm, the totals lead the list.
                  itemCount: farms.length + (farms.length > 1 ? 1 : 0),
                  separatorBuilder: (_, _) => const SizedBox(height: 10),
                  itemBuilder: (_, i) {
                    if (farms.length > 1 && i == 0) {
                      final plots = farms.fold<int>(0, (sum, f) => sum + f.plotCount);
                      final area = farms.fold<double>(0, (sum, f) => sum + f.totalAreaHectares);
                      return _Totals(farms: farms.length, plots: plots, area: area);
                    }
                    final f = farms[farms.length > 1 ? i - 1 : i];
                    return AgriCard(
                      padding: const EdgeInsets.fromLTRB(14, 14, 8, 14),
                      onTap: () => context.push('/farms/${f.id}'),
                      child: Row(
                        children: [
                          const GlyphTile(Icons.agriculture_outlined, earth: true),
                          const SizedBox(width: 14),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(f.name, style: Theme.of(context).textTheme.titleSmall),
                                const SizedBox(height: 2),
                                Text(
                                  [
                                    if (f.village != null && f.village!.isNotEmpty) f.village!,
                                    f.districtName,
                                    '${f.plotCount} plot${f.plotCount == 1 ? '' : 's'} · ${formatHectares(f.totalAreaHectares)}',
                                  ].join(' · '),
                                  style: Theme.of(context).textTheme.bodySmall,
                                ),
                              ],
                            ),
                          ),
                          const Icon(Icons.chevron_right, color: AgriColors.inkMuted),
                        ],
                      ),
                    );
                  },
                ),
        ),
      ),
    );
  }
}

/// The holding at a glance: how many farms and plots, and the land they cover.
class _Totals extends StatelessWidget {
  const _Totals({required this.farms, required this.plots, required this.area});

  final int farms;
  final int plots;
  final double area;

  @override
  Widget build(BuildContext context) {
    Widget figure(String value, String label) => Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(value, style: Theme.of(context).textTheme.headlineSmall),
              Text(label, style: Theme.of(context).textTheme.bodySmall),
            ],
          ),
        );
    return Semantics(
      container: true,
      label: '$farms farm${farms == 1 ? '' : 's'}, $plots plot${plots == 1 ? '' : 's'}, ${formatHectares(area)}',
      excludeSemantics: true,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(4, 0, 4, 6),
        child: Row(
          children: [
            figure('$farms', farms == 1 ? 'farm' : 'farms'),
            figure('$plots', plots == 1 ? 'plot' : 'plots'),
            figure(formatNumber(double.parse(area.toStringAsFixed(2))), 'hectares'),
          ],
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
      padding: EdgeInsets.fromLTRB(20, 0, 20, 20 + MediaQuery.of(context).viewInsets.bottom),
      child: Form(
        key: _formKey,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('Add a farm', style: Theme.of(context).textTheme.headlineSmall),
            const SizedBox(height: 4),
            Text('Then add its plots, standing in each one.', style: Theme.of(context).textTheme.bodySmall),
            const SizedBox(height: 16),
            if (_error != null) ...[
              Notice(message: _error!, tone: NoticeTone.error),
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
            const SizedBox(height: 24),
            FilledButton(
              onPressed: _saving ? null : _save,
              style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(54)),
              child: _saving ? const InlineSpinner() : const Text('Add farm'),
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
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 96),
            children: [
              if (farm.value case final f?)
                Row(
                  children: [
                    const Icon(Icons.place_outlined, size: 20, color: AgriColors.earth600),
                    const SizedBox(width: 6),
                    Expanded(
                      child: Text(
                        [if (f.village != null && f.village!.isNotEmpty) f.village!, f.districtName, formatHectares(f.totalAreaHectares)].join(' · '),
                        style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: AgriColors.inkSoft),
                      ),
                    ),
                  ],
                ),
              const SizedBox(height: 16),
              SectionTitle(
                'Plots',
                icon: Icons.grid_view_outlined,
                trailing: Text('${plots.length}', style: Theme.of(context).textTheme.bodySmall),
              ),
              if (plots.isEmpty)
                EmptyState(
                  earth: true,
                  title: 'No plots yet',
                  message: isFarmer ? 'Add a plot, standing in it if you can, so its location is right.' : null,
                ),
              for (final p in plots) ...[
                _PlotCard(plot: p),
                const SizedBox(height: 10),
              ],
            ],
          ),
        ),
      ),
    );
  }

}

/// One plot: its code and name, what is growing and when it is due, and how far through the
/// season the crop is.
class _PlotCard extends StatelessWidget {
  const _PlotCard({required this.plot});

  final Plot plot;

  static String _cropLine(Plot p) {
    final area = formatHectares(p.areaHectares);
    final cycle = p.activeCycle;
    if (cycle == null) return '$area · nothing growing';
    return '$area · ${cycle.cropName}, ${cycle.stage.label} · harvest ${formatDate(cycle.harvestDate)}';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cycle = plot.activeCycle;
    return AgriCard(
      padding: const EdgeInsets.fromLTRB(14, 14, 8, 14),
      onTap: () => context.push('/plots/${plot.id}'),
      child: Row(
        children: [
          GlyphTile(cycle == null ? Icons.crop_square : Icons.grass, earth: cycle == null),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(plot.title, style: theme.textTheme.titleSmall),
                const SizedBox(height: 2),
                Text(_cropLine(plot), style: theme.textTheme.bodySmall),
                if (cycle != null) ...[
                  const SizedBox(height: 10),
                  StageBar(stage: cycle.stage),
                ],
              ],
            ),
          ),
          const Icon(Icons.chevron_right, color: AgriColors.inkMuted),
        ],
      ),
    );
  }
}

/// The six crop stages as a segmented bar, filled up to the current one. Drawing only: the stage
/// is always written beside it.
class StageBar extends StatelessWidget {
  const StageBar({required this.stage, super.key});

  final CropStage stage;

  @override
  Widget build(BuildContext context) {
    final reached = CropStage.values.indexOf(stage);
    return ExcludeSemantics(
      child: Row(
        children: [
          for (var i = 0; i < CropStage.values.length; i++) ...[
            if (i > 0) const SizedBox(width: 3),
            Expanded(
              child: Container(
                height: 6,
                decoration: BoxDecoration(
                  color: i < reached ? AgriColors.brand400 : (i == reached ? AgriColors.brand700 : AgriColors.surfaceInset),
                  borderRadius: BorderRadius.circular(3),
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }
}
