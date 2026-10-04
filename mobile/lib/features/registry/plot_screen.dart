import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_exception.dart';
import '../auth/auth_controller.dart';
import '../auth/auth_models.dart';
import '../cases/case_widgets.dart';
import 'farms_screen.dart';
import 'registry_models.dart';
import 'registry_repository.dart';

/// One plot (§8, Component A): what is growing and at which stage (the crop-cycle tracker), and
/// its chemical safety — which products may be sprayed today, which may not and why, and when the
/// crop may be harvested.
class PlotScreen extends ConsumerWidget {
  const PlotScreen({required this.plotId, super.key});

  final String plotId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final plot = ref.watch(plotProvider(plotId));
    final isFarmer = ref.watch(currentUserProvider)?.role == UserRole.farmer;

    return Scaffold(
      appBar: AppBar(title: Text(plot.value?.title ?? 'Plot')),
      body: plot.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(plotProvider(plotId))),
        data: (p) => RefreshIndicator(
          onRefresh: () async {
            ref.invalidate(safetyProfileProvider(plotId));
            if (p.activeCycle case final c?) ref.invalidate(cropCycleProvider(c.id));
            final _ = await ref.refresh(plotProvider(plotId).future);
          },
          child: ListView(
            padding: const EdgeInsets.all(16),
            children: [
              Text(
                '${p.farmName} · ${formatHectares(p.areaHectares)} · ${p.soilType.label} soil',
                style: Theme.of(context).textTheme.bodyMedium,
              ),
              const SizedBox(height: 16),
              if (p.activeCycle case final cycle?)
                _CycleTracker(cycleId: cycle.id, plotId: plotId, canEdit: isFarmer)
              else if (isFarmer)
                _StartCrop(plot: p)
              else
                const Notice(message: 'Nothing is growing on this plot.'),
              const SizedBox(height: 16),
              _SafetySection(plotId: plotId),
            ],
          ),
        ),
      ),
    );
  }
}

/// Every provider that shows this plot's crop, refreshed together after a change.
void _refreshPlot(WidgetRef ref, String plotId, {String? cycleId, String? farmId}) {
  ref.invalidate(plotProvider(plotId));
  ref.invalidate(safetyProfileProvider(plotId));
  if (cycleId != null) ref.invalidate(cropCycleProvider(cycleId));
  if (farmId != null) ref.invalidate(farmPlotsProvider(farmId));
}

/// Sown → Vegetative → Flowering → Fruit set → Pre-harvest → Harvested, with this crop's place in it.
class _CycleTracker extends ConsumerStatefulWidget {
  const _CycleTracker({required this.cycleId, required this.plotId, required this.canEdit});

  final String cycleId;
  final String plotId;
  final bool canEdit;

  @override
  ConsumerState<_CycleTracker> createState() => _CycleTrackerState();
}

class _CycleTrackerState extends ConsumerState<_CycleTracker> {
  bool _saving = false;

  Future<void> _advance(CropCycle cycle, CropStage next) async {
    // The note, or null when the farmer cancelled.
    final text = await showDialog<String>(
      context: context,
      builder: (_) => _AdvanceDialog(cropName: cycle.cropName, next: next),
    );
    if (text == null || !mounted) return;

    setState(() => _saving = true);
    try {
      await ref.read(registryRepositoryProvider).advanceStage(cycle.id, next, note: text);
      _refreshPlot(ref, widget.plotId, cycleId: cycle.id);
    } on ApiException catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final cycle = ref.watch(cropCycleProvider(widget.cycleId));
    final theme = Theme.of(context);

    return Card(
      margin: EdgeInsets.zero,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: cycle.when(
          loading: () => const LinearProgressIndicator(),
          error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(cropCycleProvider(widget.cycleId))),
          data: (c) {
            final current = CropStage.values.indexOf(c.stage);
            final next = c.allowedNextStages.firstOrNull;
            return Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(c.cropName, style: theme.textTheme.titleMedium),
                Text('Sown ${formatDate(c.sownDate)} · harvest ${formatDate(c.harvestDate)}'
                    '${c.daysToHarvest >= 0 ? ' (in ${c.daysToHarvest} days)' : ''}'),
                const SizedBox(height: 12),
                for (final (i, stage) in CropStage.values.indexed)
                  Semantics(
                    container: true,
                    label: '${stage.label}: ${i < current ? 'done' : i == current ? 'current stage' : 'to come'}',
                    excludeSemantics: true,
                    child: Padding(
                      padding: const EdgeInsets.symmetric(vertical: 2),
                      child: Row(
                        children: [
                          Icon(
                            i < current ? Icons.check_circle : i == current ? Icons.radio_button_checked : Icons.radio_button_unchecked,
                            size: 20,
                            color: i <= current ? theme.colorScheme.primary : theme.colorScheme.outline,
                          ),
                          const SizedBox(width: 8),
                          Text(stage.label, style: i == current ? const TextStyle(fontWeight: FontWeight.w600) : null),
                        ],
                      ),
                    ),
                  ),
                if (widget.canEdit && next != null) ...[
                  const SizedBox(height: 12),
                  FilledButton.tonalIcon(
                    onPressed: _saving ? null : () => _advance(c, next),
                    icon: _saving
                        ? const SizedBox.square(dimension: 18, child: CircularProgressIndicator(strokeWidth: 2))
                        : const Icon(Icons.arrow_forward),
                    label: Text('Mark as ${next.label}'),
                  ),
                ],
                if (c.transitions.isNotEmpty) ...[
                  const SizedBox(height: 12),
                  Text('History', style: theme.textTheme.titleSmall),
                  for (final t in c.transitions.reversed)
                    Text('${formatDate(t.at)}: ${t.from.label} → ${t.to.label}${t.note == null ? '' : ' — ${t.note}'}',
                        style: theme.textTheme.bodySmall),
                ],
              ],
            );
          },
        ),
      ),
    );
  }
}

/// Confirms a stage change and takes an optional note. Owns its text controller, so the controller
/// lives exactly as long as the dialog, closing animation included.
class _AdvanceDialog extends StatefulWidget {
  const _AdvanceDialog({required this.cropName, required this.next});

  final String cropName;
  final CropStage next;

  @override
  State<_AdvanceDialog> createState() => _AdvanceDialogState();
}

class _AdvanceDialogState extends State<_AdvanceDialog> {
  final _note = TextEditingController();

  @override
  void dispose() {
    _note.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: Text('Mark as ${widget.next.label}?'),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(widget.next == CropStage.harvested
              ? 'This ends the crop cycle for ${widget.cropName}. You can then sow the next crop on this plot.'
              : 'Your ${widget.cropName} has reached ${widget.next.label.toLowerCase()} today.'),
          TextField(controller: _note, maxLength: 500, decoration: const InputDecoration(labelText: 'Note (optional)')),
        ],
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
        FilledButton(onPressed: () => Navigator.pop(context, _note.text), child: const Text('Confirm')),
      ],
    );
  }
}

/// "Start a crop": the crop and the sowing day. The expected harvest follows from the crop's
/// maturity days, on the server.
class _StartCrop extends ConsumerStatefulWidget {
  const _StartCrop({required this.plot});

  final Plot plot;

  @override
  ConsumerState<_StartCrop> createState() => _StartCropState();
}

class _StartCropState extends ConsumerState<_StartCrop> {
  Crop? _crop;
  DateTime _sown = DateUtils.dateOnly(DateTime.now());
  bool _saving = false;
  String? _error;

  Future<void> _pickDate() async {
    final today = DateUtils.dateOnly(DateTime.now());
    final picked = await showDatePicker(
      context: context,
      initialDate: _sown,
      firstDate: today.subtract(const Duration(days: 365)),
      lastDate: today,
      helpText: 'When was it sown?',
    );
    if (picked != null) setState(() => _sown = picked);
  }

  Future<void> _start() async {
    if (_crop == null) {
      setState(() => _error = 'Choose the crop you sowed.');
      return;
    }
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final cycle = await ref.read(registryRepositoryProvider).startCycle(plotId: widget.plot.id, cropId: _crop!.id, sownDate: _sown);
      _refreshPlot(ref, widget.plot.id, cycleId: cycle.id, farmId: widget.plot.farmId);
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final crops = ref.watch(cropsProvider);
    return Card(
      margin: EdgeInsets.zero,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('Start a crop', style: Theme.of(context).textTheme.titleMedium),
            const Text('Nothing is growing here yet. Record what you sowed and when.'),
            const SizedBox(height: 12),
            if (_error != null) ...[
              Notice(message: _error!, icon: Icons.error_outline, tone: NoticeTone.error),
              const SizedBox(height: 12),
            ],
            crops.when(
              loading: () => const LinearProgressIndicator(),
              error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(cropsProvider)),
              data: (all) => DropdownButtonFormField<Crop>(
                initialValue: _crop,
                decoration: const InputDecoration(labelText: 'Crop'),
                items: [for (final c in all) DropdownMenuItem(value: c, child: Text('${c.name} (${c.maturityDays} days)'))],
                onChanged: (c) => setState(() => _crop = c),
              ),
            ),
            const SizedBox(height: 8),
            OutlinedButton.icon(
              onPressed: _pickDate,
              icon: const Icon(Icons.event),
              label: Text('Sown on ${formatDate(_sown)}'),
            ),
            const SizedBox(height: 12),
            FilledButton(
              onPressed: _saving ? null : _start,
              child: _saving
                  ? const SizedBox.square(dimension: 18, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Text('Start crop'),
            ),
          ],
        ),
      ),
    );
  }
}

/// The plot's chemical safety, in the words the prescription rules use: which products may be
/// sprayed today, which may not and why (pre-harvest interval, the season's limit, the gap between
/// sprays), whether people may go into the field, and what was sprayed this season.
class _SafetySection extends ConsumerWidget {
  const _SafetySection({required this.plotId});

  final String plotId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final profile = ref.watch(safetyProfileProvider(plotId));
    final theme = Theme.of(context);

    return profile.when(
      loading: () => const LinearProgressIndicator(),
      error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(safetyProfileProvider(plotId))),
      data: (s) {
        if (s.cropName == null) return const SizedBox.shrink();
        final reEntry = s.reEntryClearAt;
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Spray safety', style: theme.textTheme.titleMedium),
            const SizedBox(height: 4),
            if (reEntry != null && reEntry.isAfter(DateTime.now())) ...[
              Notice(
                message: 'Keep people out of the field until ${formatDate(reEntry)} ${_time(reEntry)}: it was sprayed recently.',
                icon: Icons.do_not_step,
                tone: NoticeTone.warning,
              ),
              const SizedBox(height: 8),
            ],
            if (s.productWindows.isEmpty) const Text('No product is approved for this crop.'),
            for (final w in s.productWindows)
              ListTile(
                contentPadding: EdgeInsets.zero,
                leading: Icon(
                  w.canSprayToday ? Icons.check_circle_outline : Icons.block,
                  color: w.canSprayToday ? theme.colorScheme.primary : theme.colorScheme.error,
                ),
                title: Text('${w.productName} (${w.activeIngredient})'),
                subtitle: Text(w.canSprayToday
                    ? 'Can be sprayed today. Last safe day before harvest: ${formatDate(w.lastSafeSprayDate)}. '
                        '${w.applicationsUsed} of ${w.maxApplications} sprays used.'
                    : w.blockedExplanation ?? 'Cannot be sprayed today.'),
              ),
            if (s.applications.isNotEmpty) ...[
              const SizedBox(height: 8),
              Text('Sprays this season', style: theme.textTheme.titleSmall),
              for (final a in s.applications)
                Text('${formatDate(a.date)} · ${a.productName} · ${a.status.toLowerCase()}', style: theme.textTheme.bodySmall),
            ],
          ],
        );
      },
    );
  }

  static String _time(DateTime utc) {
    final local = utc.toLocal();
    return '${local.hour.toString().padLeft(2, '0')}:${local.minute.toString().padLeft(2, '0')}';
  }
}
