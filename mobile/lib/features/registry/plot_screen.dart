import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/agri_widgets.dart';
import '../../app/theme.dart';
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
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 24),
            children: [
              Row(
                children: [
                  const Icon(Icons.place_outlined, size: 20, color: AgriColors.earth600),
                  const SizedBox(width: 6),
                  Expanded(
                    child: Text(
                      '${p.farmName} · ${formatHectares(p.areaHectares)} · ${p.soilType.label} soil',
                      style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: AgriColors.inkSoft),
                    ),
                  ),
                ],
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

    return AgriCard(
      child: cycle.when(
        loading: () => const LinearProgressIndicator(),
        error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(cropCycleProvider(widget.cycleId))),
        data: (c) {
          final current = CropStage.values.indexOf(c.stage);
          final next = c.allowedNextStages.firstOrNull;
          return Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(c.cropName, style: theme.textTheme.headlineSmall),
                        const SizedBox(height: 2),
                        Text(
                          'Sown ${formatDate(c.sownDate)} · harvest ${formatDate(c.harvestDate)}'
                          '${c.daysToHarvest >= 0 ? ' (in ${c.daysToHarvest} days)' : ''}',
                          style: theme.textTheme.bodySmall,
                        ),
                      ],
                    ),
                  ),
                  if (c.daysToHarvest >= 0)
                    // The headline figure: how long until harvest. The line beside it says it in words.
                    ExcludeSemantics(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.end,
                        children: [
                          Text('${c.daysToHarvest}', style: theme.textTheme.headlineMedium?.copyWith(color: AgriColors.earth700)),
                          Text('days to harvest', style: theme.textTheme.bodySmall),
                        ],
                      ),
                    ),
                ],
              ),
              const SizedBox(height: 16),
              for (final (i, stage) in CropStage.values.indexed)
                Semantics(
                  container: true,
                  label: '${stage.label}: ${i < current ? 'done' : i == current ? 'current stage' : 'to come'}',
                  excludeSemantics: true,
                  child: _StageRow(stage: stage, done: i < current, current: i == current, last: i == CropStage.values.length - 1),
                ),
              if (widget.canEdit && next != null) ...[
                const SizedBox(height: 14),
                FilledButton.icon(
                  onPressed: _saving ? null : () => _advance(c, next),
                  icon: _saving ? const InlineSpinner() : const Icon(Icons.arrow_forward),
                  label: Text('Mark as ${next.label}'),
                ),
              ],
              if (c.transitions.isNotEmpty) ...[
                const Divider(height: 28),
                Text('History', style: theme.textTheme.titleSmall),
                const SizedBox(height: 6),
                for (final t in c.transitions.reversed)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 4),
                    child: Text(
                      '${formatDate(t.at)}: ${t.from.label} → ${t.to.label}${t.note == null ? '' : ' — ${t.note}'}',
                      style: theme.textTheme.bodySmall,
                    ),
                  ),
              ],
            ],
          );
        },
      ),
    );
  }
}

/// One crop stage on the tracker: a ticked node for stages behind, a filled ring for today's, an
/// open dashed ring for those to come, joined by a line.
class _StageRow extends StatelessWidget {
  const _StageRow({required this.stage, required this.done, required this.current, required this.last});

  final CropStage stage;
  final bool done;
  final bool current;
  final bool last;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final node = done
        ? Container(
            width: 24,
            height: 24,
            decoration: const BoxDecoration(color: AgriColors.brand600, shape: BoxShape.circle),
            child: const Icon(Icons.check, size: 16, color: Colors.white),
          )
        : current
            ? Container(
                width: 24,
                height: 24,
                decoration: BoxDecoration(
                  color: AgriColors.brand100,
                  shape: BoxShape.circle,
                  border: Border.all(color: AgriColors.brand700, width: 2),
                ),
                child: Center(
                  child: Container(width: 10, height: 10, decoration: const BoxDecoration(color: AgriColors.brand700, shape: BoxShape.circle)),
                ),
              )
            : const ToneIcon(Tone.neutral, size: 24, color: AgriColors.outline);

    return IntrinsicHeight(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SizedBox(
            width: 24,
            child: Column(
              children: [
                node,
                if (!last)
                  Expanded(
                    child: Container(
                      width: 2,
                      margin: const EdgeInsets.symmetric(vertical: 2),
                      color: done ? AgriColors.brand300 : AgriColors.borderStrong,
                    ),
                  ),
              ],
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Padding(
              padding: EdgeInsets.only(top: 2, bottom: last ? 0 : 12),
              child: Row(
                children: [
                  Text(
                    stage.label,
                    style: theme.textTheme.bodyMedium?.copyWith(
                      fontWeight: current ? FontWeight.w700 : FontWeight.w500,
                      color: done || current ? AgriColors.ink : AgriColors.inkMuted,
                    ),
                  ),
                  if (current) ...[
                    const SizedBox(width: 8),
                    const StatusPill(label: 'Now', tone: Tone.active),
                  ],
                ],
              ),
            ),
          ),
        ],
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
    final theme = Theme.of(context);
    return AgriCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              const GlyphTile(Icons.spa_outlined),
              const SizedBox(width: 12),
              Expanded(child: Text('Start a crop', style: theme.textTheme.headlineSmall)),
            ],
          ),
          const SizedBox(height: 8),
          Text('Nothing is growing here yet. Record what you sowed and when.', style: theme.textTheme.bodyMedium),
          const SizedBox(height: 16),
          if (_error != null) ...[
            Notice(message: _error!, tone: NoticeTone.error),
            const SizedBox(height: 12),
          ],
          crops.when(
            loading: () => const LinearProgressIndicator(),
            error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(cropsProvider)),
            data: (all) => DropdownButtonFormField<Crop>(
              initialValue: _crop,
              decoration: const InputDecoration(labelText: 'Crop', prefixIcon: Icon(Icons.eco_outlined)),
              items: [for (final c in all) DropdownMenuItem(value: c, child: Text('${c.name} (${c.maturityDays} days)'))],
              onChanged: (c) => setState(() => _crop = c),
            ),
          ),
          const SizedBox(height: 12),
          OutlinedButton.icon(
            onPressed: _pickDate,
            icon: const Icon(Icons.event_outlined),
            label: Text('Sown on ${formatDate(_sown)}'),
          ),
          const SizedBox(height: 16),
          FilledButton(
            onPressed: _saving ? null : _start,
            child: _saving ? const InlineSpinner() : const Text('Start crop'),
          ),
        ],
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
        return AgriCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const SectionTitle('Spray safety', icon: Icons.health_and_safety_outlined),
              if (reEntry != null && reEntry.isAfter(DateTime.now())) ...[
                Notice(
                  message: 'Keep people out of the field until ${formatDate(reEntry)} ${_time(reEntry)}: it was sprayed recently.',
                  icon: Icons.do_not_step,
                  tone: NoticeTone.warning,
                ),
                const SizedBox(height: 12),
              ],
              if (s.productWindows.isEmpty) Text('No product is approved for this crop.', style: theme.textTheme.bodyMedium),
              for (final (i, w) in s.productWindows.indexed) ...[
                if (i > 0) const Divider(height: 20),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Padding(
                      padding: const EdgeInsets.only(top: 2),
                      child: ToneIcon(w.canSprayToday ? Tone.done : Tone.danger, size: 22),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text('${w.productName} (${w.activeIngredient})', style: theme.textTheme.titleSmall),
                          // A blocked product is flagged in so many words; a sprayable one says so in
                          // its first sentence, beside the tick.
                          if (!w.canSprayToday) ...[
                            const SizedBox(height: 6),
                            const StatusPill(label: 'Not today', tone: Tone.danger),
                          ],
                          const SizedBox(height: 4),
                          Text(
                            w.canSprayToday
                                ? 'Can be sprayed today. Last safe day before harvest: ${formatDate(w.lastSafeSprayDate)}. '
                                    '${w.applicationsUsed} of ${w.maxApplications} sprays used.'
                                : w.blockedExplanation ?? 'Cannot be sprayed today.',
                            style: theme.textTheme.bodySmall?.copyWith(color: AgriColors.inkSoft),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
              ],
              if (s.applications.isNotEmpty) ...[
                const Divider(height: 28),
                Text('Sprays this season', style: theme.textTheme.titleSmall),
                const SizedBox(height: 6),
                for (final a in s.applications)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 4),
                    child: Row(
                      children: [
                        const Icon(Icons.water_drop_outlined, size: 16, color: AgriColors.inkMuted),
                        const SizedBox(width: 8),
                        Expanded(
                          child: Text('${formatDate(a.date)} · ${a.productName} · ${a.status.toLowerCase()}', style: theme.textTheme.bodySmall),
                        ),
                      ],
                    ),
                  ),
              ],
            ],
          ),
        );
      },
    );
  }

  static String _time(DateTime utc) {
    final local = utc.toLocal();
    return '${local.hour.toString().padLeft(2, '0')}:${local.minute.toString().padLeft(2, '0')}';
  }
}
