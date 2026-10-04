import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/agri_widgets.dart';
import '../../app/theme.dart';
import '../../core/api/api_exception.dart';
import '../../core/photos/photo_source.dart';
import '../auth/auth_controller.dart';
import '../auth/auth_models.dart';
import 'case_models.dart';
import 'case_photos.dart';
import 'case_providers.dart';
import 'case_repository.dart';
import 'case_widgets.dart';

/// How often an open case is re-read while the agent or an agronomist is working on it.
const casePollInterval = Duration(seconds: 5);

/// One case, followed from report to prescription (§11 steps 1–3 and 8): the farmer asks for AI
/// advice here, watches it move through the agent and the agronomist, and reads the result —
/// including the date before which the crop must not be harvested.
class CaseDetailScreen extends ConsumerStatefulWidget {
  const CaseDetailScreen({required this.caseId, super.key});

  final String caseId;

  @override
  ConsumerState<CaseDetailScreen> createState() => _CaseDetailScreenState();
}

class _CaseDetailScreenState extends ConsumerState<CaseDetailScreen> {
  Timer? _poll;
  bool _requesting = false;
  bool _uploadingPhoto = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    // Keep the timer in step with the latest status, including the first load.
    ref.listenManual(
      caseDetailProvider(widget.caseId),
      (_, next) => next.whenData(_syncPolling),
      fireImmediately: true,
    );
  }

  @override
  void dispose() {
    _poll?.cancel();
    super.dispose();
  }

  /// Polls only while something can change without the farmer doing anything.
  void _syncPolling(CaseDetail detail) {
    if (detail.status.inProgress) {
      _poll ??= Timer.periodic(casePollInterval, (_) => ref.invalidate(caseDetailProvider(widget.caseId)));
    } else {
      _poll?.cancel();
      _poll = null;
    }
  }

  Future<void> _addPhoto(PhotoOrigin origin) async {
    final photo = await ref.read(photoSourceProvider).pick(origin);
    if (photo == null || !mounted) return;
    setState(() {
      _uploadingPhoto = true;
      _error = null;
    });
    try {
      await ref.read(caseRepositoryProvider).uploadPhoto(widget.caseId, photo.bytes, photo.name);
      ref.invalidate(caseDetailProvider(widget.caseId));
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _uploadingPhoto = false);
    }
  }

  Future<void> _requestAdvice() async {
    setState(() {
      _requesting = true;
      _error = null;
    });
    try {
      await ref.read(caseRepositoryProvider).requestAdvice(widget.caseId);
      ref.invalidate(caseDetailProvider(widget.caseId));
      ref.invalidate(myCasesProvider);
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _requesting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final detail = ref.watch(caseDetailProvider(widget.caseId));
    final isFarmer = ref.watch(currentUserProvider)?.role == UserRole.farmer;
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(title: Text(detail.value?.referenceNo ?? 'Case')),
      body: detail.when(
        // Keep showing the last data during a poll instead of flashing a spinner.
        skipLoadingOnRefresh: true,
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(caseDetailProvider(widget.caseId))),
        data: (c) {
          final run = c.latestRun;
          final canAsk = isFarmer && c.status.canRequestAdvice;
          final showAdvice =
              (c.status == CaseStatus.pendingApproval || c.status == CaseStatus.awaitingManualReview) && run != null && run.advice.isNotEmpty;

          return Column(
            children: [
              Expanded(
                child: RefreshIndicator(
                  onRefresh: () => ref.refresh(caseDetailProvider(widget.caseId).future),
                  child: ListView(
                    padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
                    children: [
                      _Header(detail: c),
                      const SizedBox(height: 16),
                      if (_error != null) ...[
                        Notice(message: _error!, tone: NoticeTone.error),
                        const SizedBox(height: 16),
                      ],
                      // Once there is a prescription it leads: it is what the farmer acts on.
                      if (c.status == CaseStatus.prescribed && run != null) ...[
                        _PrescriptionSection(runId: run.id),
                        const SizedBox(height: 16),
                      ],
                      AgriCard(child: _Timeline(detail: c)),
                      if (showAdvice) ...[
                        const SizedBox(height: 16),
                        AdviceCard(tips: run.advice),
                      ],
                      const SizedBox(height: 24),
                      const SectionTitle('What you reported', icon: Icons.assignment_outlined, earth: true),
                      _Reported(detail: c),
                      const SizedBox(height: 20),
                      SectionTitle('Leaf photos', icon: Icons.photo_library_outlined, earth: true,
                          trailing: Text('${c.photos.length} of $maxPhotosPerCase', style: theme.textTheme.bodySmall)),
                      if (c.photos.isNotEmpty)
                        Wrap(
                          spacing: 10,
                          runSpacing: 10,
                          children: [
                            for (var i = 0; i < c.photos.length; i++) CasePhotoThumb(caseId: c.id, photoId: c.photos[i].id, index: i),
                          ],
                        )
                      else
                        Text('No photos yet.', style: theme.textTheme.bodyMedium?.copyWith(color: AgriColors.inkMuted)),
                      if (c.status != CaseStatus.closed && c.photos.length < maxPhotosPerCase) ...[
                        const SizedBox(height: 12),
                        _uploadingPhoto
                            ? const Row(children: [
                                InlineSpinner(size: 16),
                                SizedBox(width: 10),
                                Text('Sending photo…'),
                              ])
                            : AddPhotoButtons(onPick: _addPhoto),
                      ],
                    ],
                  ),
                ),
              ),
              // Asking the AI is the one thing the farmer can do here: it sits under the thumb.
              if (canAsk)
                BottomActionBar(
                  note: 'The AI suggests a treatment; an agronomist checks it before anything is prescribed.',
                  children: [
                    Expanded(
                      child: FilledButton.icon(
                        onPressed: _requesting ? null : _requestAdvice,
                        icon: _requesting ? const InlineSpinner() : const Icon(Icons.auto_awesome_outlined),
                        label: Text(run == null ? 'Get AI advice' : 'Ask the AI again'),
                      ),
                    ),
                  ],
                ),
            ],
          );
        },
      ),
    );
  }
}

/// What and where, how far the crop is, and the case's status.
class _Header extends StatelessWidget {
  const _Header({required this.detail});

  final CaseDetail detail;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final c = detail;
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const GlyphTile(Icons.eco_outlined, size: 48),
        const SizedBox(width: 12),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('${c.cropName} · ${c.plotCode}', style: theme.textTheme.headlineSmall),
              const SizedBox(height: 2),
              Text('Reported ${formatDate(c.createdAt)} · ${c.stage}', style: theme.textTheme.bodyMedium?.copyWith(color: AgriColors.inkMuted)),
              const SizedBox(height: 8),
              CaseStatusChip(c.status),
            ],
          ),
        ),
      ],
    );
  }
}

/// Where a step of the case stands.
enum _StepState { done, active, waitingOnPerson, attention, failed, upcoming }

class _Step {
  const _Step(this.title, this.state, {this.note, this.message, this.headline});

  final String title;
  final _StepState state;

  /// A few words beside the title ("Approved", "Handed over").
  final String? note;

  /// What is happening now, in full, for the step that is current.
  final String? message;

  /// A bold first line over [message].
  final String? headline;

  bool get current => state == _StepState.active || state == _StepState.waitingOnPerson || state == _StepState.attention;
}

/// Reported → AI analysis → Agronomist review → Prescription, drawn as a vertical timeline. The
/// step that is under way says, in full, what is happening and whether the farmer must do anything;
/// the wait for the agronomist is drawn as a person, so it reads as someone checking, not as a
/// stalled machine.
class _Timeline extends StatelessWidget {
  const _Timeline({required this.detail});

  final CaseDetail detail;

  List<_Step> _steps() {
    final run = detail.latestRun;
    const reported = _Step('Reported', _StepState.done);
    return switch (detail.status) {
      CaseStatus.submitted => const [
          reported,
          _Step('AI analysis', _StepState.upcoming, note: 'Not started'),
          _Step('Agronomist review', _StepState.upcoming),
          _Step('Prescription', _StepState.upcoming),
        ],
      CaseStatus.agentProcessing => const [
          reported,
          _Step('AI analysis', _StepState.active,
              message: 'The AI is studying your case. This usually takes about a minute — this page updates by itself.'),
          _Step('Agronomist review', _StepState.upcoming),
          _Step('Prescription', _StepState.upcoming),
        ],
      CaseStatus.pendingApproval => const [
          reported,
          _Step('AI analysis', _StepState.done, note: 'Treatment proposed'),
          _Step('Agronomist review', _StepState.waitingOnPerson,
              headline: 'A person is checking it',
              message: 'A treatment has been proposed and passed the safety checks. An agronomist is reviewing it now. '
                  'You do not need to do anything: nothing is prescribed until they approve it, and this page updates by itself.'),
          _Step('Prescription', _StepState.upcoming),
        ],
      CaseStatus.awaitingManualReview => [
          reported,
          switch (run) {
            RunSummary(escalated: true) => const _Step('AI analysis', _StepState.done, note: 'Handed to an agronomist'),
            RunSummary(status: 'Failed' || 'TimedOut') => const _Step('AI analysis', _StepState.failed, note: 'Could not finish'),
            _ => const _Step('AI analysis', _StepState.upcoming, note: 'Not needed'),
          },
          _Step(
            'Agronomist review',
            _StepState.attention,
            message: switch (run) {
              RunSummary(escalated: true) => 'The AI thinks an agronomist should look at this first. ${run.failureReason ?? ''}'.trim(),
              RunSummary(status: 'Failed' || 'TimedOut') =>
                'The AI could not finish this time. An agronomist will look at your case, or you can try again.',
              _ => 'An agronomist is looking at your case.',
            },
          ),
          const _Step('Prescription', _StepState.upcoming),
        ],
      CaseStatus.prescribed => const [
          reported,
          _Step('AI analysis', _StepState.done, note: 'Treatment proposed'),
          _Step('Agronomist review', _StepState.done, note: 'Approved'),
          _Step('Prescription', _StepState.done, note: 'Issued: see above'),
        ],
      CaseStatus.rejected => [
          reported,
          const _Step('AI analysis', _StepState.done, note: 'Treatment proposed'),
          _Step(
            'Agronomist review',
            _StepState.failed,
            note: 'Not approved',
            message: 'The agronomist did not approve the proposed treatment.'
                '${_reason(run?.failureReason)} Please contact your agronomist for advice.',
          ),
          const _Step('Prescription', _StepState.upcoming, note: 'None issued'),
        ],
      CaseStatus.closed => const [
          reported,
          _Step('Closed', _StepState.done, message: 'This case is closed.'),
        ],
    };
  }

  /// `Rejected by the agronomist: reason` → ` Reason: reason`.
  static String _reason(String? failureReason) {
    const prefix = 'Rejected by the agronomist: ';
    if (failureReason == null || !failureReason.startsWith(prefix)) return '';
    return ' Reason: ${failureReason.substring(prefix.length)}';
  }

  @override
  Widget build(BuildContext context) {
    final steps = _steps();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionTitle('Progress', icon: Icons.timeline),
        for (final (i, step) in steps.indexed) _TimelineRow(step: step, last: i == steps.length - 1),
      ],
    );
  }
}

class _TimelineRow extends StatelessWidget {
  const _TimelineRow({required this.step, required this.last});

  final _Step step;
  final bool last;

  static String _stateWord(_StepState s) => switch (s) {
        _StepState.done => 'done',
        _StepState.active => 'in progress',
        _StepState.waitingOnPerson => 'waiting for the agronomist',
        _StepState.attention => 'needs a person',
        _StepState.failed => 'stopped',
        _StepState.upcoming => 'to come',
      };

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final tone = switch (step.state) {
      _StepState.done => Tone.done,
      _StepState.active || _StepState.waitingOnPerson => Tone.active,
      _StepState.attention => Tone.warning,
      _StepState.failed => Tone.danger,
      _StepState.upcoming => Tone.neutral,
    };
    final c = toneColors(tone);
    final muted = step.state == _StepState.upcoming;

    final node = switch (step.state) {
      _StepState.done => Container(
          width: 30,
          height: 30,
          decoration: const BoxDecoration(color: AgriColors.brand600, shape: BoxShape.circle),
          child: const Icon(Icons.check, size: 18, color: Colors.white),
        ),
      _StepState.upcoming => Container(
          width: 30,
          height: 30,
          decoration: const BoxDecoration(color: AgriColors.surfaceCard, shape: BoxShape.circle),
          child: const ToneIcon(Tone.neutral, size: 30, color: AgriColors.outline),
        ),
      _ => Container(
          width: 30,
          height: 30,
          decoration: BoxDecoration(color: c.fill, shape: BoxShape.circle, border: Border.all(color: c.ink, width: 2)),
          child: step.state == _StepState.waitingOnPerson
              ? Icon(Icons.person_search_outlined, size: 17, color: c.ink)
              : ToneIcon(tone, size: 17),
        ),
    };

    // The step and its state are read as one label; the note and the message follow as their own
    // nodes, and the title and "Now" that the label already says are not read twice.
    return Semantics(
      container: true,
      explicitChildNodes: true,
      label: '${step.title}: ${_stateWord(step.state)}',
      child: IntrinsicHeight(
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            SizedBox(
              width: 30,
              child: Column(
                children: [
                  node,
                  if (!last)
                    Expanded(
                      child: Container(
                        width: 2,
                        margin: const EdgeInsets.symmetric(vertical: 4),
                        color: step.state == _StepState.done ? AgriColors.brand300 : AgriColors.borderStrong,
                      ),
                    ),
                ],
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Padding(
                padding: EdgeInsets.only(top: 4, bottom: last ? 0 : 18),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Wrap(
                      spacing: 8,
                      runSpacing: 4,
                      crossAxisAlignment: WrapCrossAlignment.center,
                      children: [
                        ExcludeSemantics(
                          child: Text(
                            step.title,
                            style: theme.textTheme.titleSmall?.copyWith(color: muted ? AgriColors.inkMuted : AgriColors.ink),
                          ),
                        ),
                        if (step.current) ExcludeSemantics(child: StatusPill(label: 'Now', tone: tone)),
                        if (step.note != null)
                          Text(step.note!, style: theme.textTheme.bodySmall?.copyWith(color: step.state == _StepState.failed ? c.ink : null)),
                      ],
                    ),
                    if (step.message != null) ...[
                      const SizedBox(height: 8),
                      Semantics(
                        liveRegion: step.current,
                        child: Container(
                          width: double.infinity,
                          padding: const EdgeInsets.all(12),
                          decoration: BoxDecoration(
                            color: c.fill,
                            borderRadius: BorderRadius.circular(10),
                            border: Border.all(color: c.ring),
                          ),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              if (step.headline != null) ...[
                                Text(step.headline!, style: TextStyle(color: c.ink, fontSize: 16, fontWeight: FontWeight.w700)),
                                const SizedBox(height: 4),
                              ],
                              Text(step.message!, style: TextStyle(color: c.ink, fontSize: 16, height: 1.45)),
                            ],
                          ),
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// The ticked symptoms as earth-toned chips, and the farmer's own words.
class _Reported extends StatelessWidget {
  const _Reported({required this.detail});

  final CaseDetail detail;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final s in detail.symptoms)
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
                decoration: BoxDecoration(
                  color: AgriColors.earth50,
                  borderRadius: BorderRadius.circular(999),
                  border: Border.all(color: AgriColors.earth200),
                ),
                child: Text(s.label, style: const TextStyle(fontSize: 15, color: AgriColors.earth800, fontWeight: FontWeight.w500)),
              ),
          ],
        ),
        if (detail.farmerNote != null) ...[
          const SizedBox(height: 12),
          Container(
            width: double.infinity,
            padding: const EdgeInsets.fromLTRB(14, 10, 12, 10),
            decoration: const BoxDecoration(
              color: AgriColors.surfaceSunken,
              border: Border(left: BorderSide(color: AgriColors.earth300, width: 3)),
            ),
            child: Text('“${detail.farmerNote}”', style: theme.textTheme.bodyMedium?.copyWith(fontStyle: FontStyle.italic)),
          ),
        ],
      ],
    );
  }
}

/// The approved treatment. The safe-harvest date leads: it is the one thing that protects the
/// people who will eat the crop.
class _PrescriptionSection extends ConsumerWidget {
  const _PrescriptionSection({required this.runId});

  final String runId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final prescription = ref.watch(prescriptionProvider(runId));
    final theme = Theme.of(context);

    return prescription.when(
      loading: () => const LinearProgressIndicator(),
      error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(prescriptionProvider(runId))),
      data: (p) => p == null
          ? const Notice(message: 'Your prescription is being prepared.')
          : AgriCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    children: [
                      const GlyphTile(Icons.receipt_long_outlined),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text('Prescription ${p.prescriptionNo}', style: theme.textTheme.titleMedium),
                            Text('Approved by an agronomist', style: theme.textTheme.bodySmall),
                          ],
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 14),
                  _DoNotHarvest(date: p.earliestSafeHarvestDate, sprayDate: p.sprayDate),
                  const SizedBox(height: 16),
                  // The product on its own row (names run long); the two amounts side by side.
                  _FactGrid(rows: [
                    [('Product', p.productName)],
                    [
                      (
                        'Dose',
                        p.unitSymbol.isEmpty
                            ? '${formatNumber(p.dosePerHectare)} per hectare'
                            : '${formatNumber(p.dosePerHectare)} ${p.unitSymbol} per hectare',
                      ),
                      ('Total to spray', '${formatNumber(p.totalQuantity)} ${p.unitSymbol}'.trim()),
                    ],
                    [('Spray on', formatDate(p.sprayDate))],
                  ]),
                  if (p.instructions != null) ...[
                    const SizedBox(height: 14),
                    Container(
                      padding: const EdgeInsets.all(12),
                      decoration: BoxDecoration(color: AgriColors.surfaceSunken, borderRadius: BorderRadius.circular(10)),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text('How to apply', style: theme.textTheme.bodySmall),
                          const SizedBox(height: 2),
                          Text(p.instructions!),
                        ],
                      ),
                    ),
                  ],
                  const Divider(height: 32),
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const GlyphTile(Icons.storefront_outlined, earth: true, size: 40),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            LabelledValue(label: 'Collect from', value: p.dealerName),
                            const SizedBox(height: 10),
                            LabelledValue(
                              label: 'Order',
                              value: '${p.orderNo} · ${p.packs} pack(s) · ${formatLkr(p.orderTotal)}',
                              valueStyle: theme.textTheme.bodyMedium?.copyWith(fontWeight: FontWeight.w600),
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
    );
  }
}

const _weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const _months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/// The earliest safe harvest date, as a block that cannot be missed: an octagon, a heavy rule,
/// the sentence, the day in large type, and how far off it is.
///
/// It stays a warning even once the date has passed. The date assumes the spray went on the
/// prescribed day; a farmer who sprayed later must wait longer, so the phone never declares the
/// crop safe on its own.
class _DoNotHarvest extends StatelessWidget {
  const _DoNotHarvest({required this.date, required this.sprayDate});

  final DateTime date;
  final DateTime sprayDate;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final today = DateUtils.dateOnly(DateTime.now());
    final day = DateUtils.dateOnly(date);
    final daysLeft = day.difference(today).inDays;
    final waitDays = day.difference(DateUtils.dateOnly(sprayDate)).inDays;
    final asPrescribed = 'if you sprayed on ${formatDate(sprayDate)} as prescribed';
    final countdown = switch (daysLeft) {
      > 1 => '$daysLeft days from today',
      1 => 'Tomorrow',
      0 => 'Today, $asPrescribed',
      _ => 'Already passed, $asPrescribed',
    };

    return Container(
      padding: const EdgeInsets.fromLTRB(14, 14, 14, 14),
      decoration: BoxDecoration(
        color: AgriColors.danger50,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AgriColors.danger, width: 2),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Icon(Icons.report_outlined, size: 32, color: AgriColors.danger),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Do not harvest before ${formatDate(date)}.',
                  style: const TextStyle(fontSize: 17, height: 1.3, fontWeight: FontWeight.w700, color: AgriColors.danger800),
                ),
                const SizedBox(height: 6),
                ExcludeSemantics(
                  child: Text(
                    '${_weekdays[day.weekday - 1]} ${day.day} ${_months[day.month - 1]}',
                    style: theme.textTheme.headlineMedium?.copyWith(color: AgriColors.danger800, fontWeight: FontWeight.w700),
                  ),
                ),
                const SizedBox(height: 2),
                Text(countdown, style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w600, color: AgriColors.danger800)),
                const SizedBox(height: 8),
                Text(
                  waitDays > 0
                      ? 'The product needs $waitDays days after spraying to wear off. Harvesting earlier can leave residue on the food.'
                      : 'Harvesting earlier can leave spray residue on the food.',
                  style: const TextStyle(fontSize: 14, height: 1.45, color: AgriColors.danger800),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// Rows of label-over-value facts, one or two to a row, tabular numbers lined up.
class _FactGrid extends StatelessWidget {
  const _FactGrid({required this.rows});

  final List<List<(String, String)>> rows;

  @override
  Widget build(BuildContext context) {
    Widget cell((String, String) fact) => Expanded(
          child: Container(
            padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
            decoration: BoxDecoration(color: AgriColors.surfaceSunken, borderRadius: BorderRadius.circular(10)),
            child: LabelledValue(label: fact.$1, value: fact.$2),
          ),
        );

    return Column(
      children: [
        for (final (i, row) in rows.indexed) ...[
          if (i > 0) const SizedBox(height: 8),
          // Cells side by side keep one height, so the pair reads as a row.
          IntrinsicHeight(
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                for (final (j, fact) in row.indexed) ...[
                  if (j > 0) const SizedBox(width: 8),
                  cell(fact),
                ],
              ],
            ),
          ),
        ],
      ],
    );
  }
}
