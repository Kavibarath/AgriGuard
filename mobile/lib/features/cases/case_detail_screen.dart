import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

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

    return Scaffold(
      appBar: AppBar(title: Text(detail.value?.referenceNo ?? 'Case')),
      body: detail.when(
        // Keep showing the last data during a poll instead of flashing a spinner.
        skipLoadingOnRefresh: true,
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(caseDetailProvider(widget.caseId))),
        data: (c) => RefreshIndicator(
          onRefresh: () => ref.refresh(caseDetailProvider(widget.caseId).future),
          child: ListView(
            padding: const EdgeInsets.all(16),
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text('${c.cropName} · ${c.plotCode}', style: Theme.of(context).textTheme.titleLarge),
                  ),
                  CaseStatusChip(c.status),
                ],
              ),
              Text('Reported ${formatDate(c.createdAt)} · ${c.stage}'),
              const SizedBox(height: 16),
              _Progress(status: c.status),
              const SizedBox(height: 16),
              if (_error != null) ...[
                Notice(message: _error!, icon: Icons.error_outline, tone: NoticeTone.error),
                const SizedBox(height: 12),
              ],
              ..._statusSection(context, c, isFarmer),
              const SizedBox(height: 24),
              Text('What you reported', style: Theme.of(context).textTheme.titleMedium),
              const SizedBox(height: 8),
              Wrap(
                spacing: 6,
                runSpacing: 4,
                children: [for (final s in c.symptoms) Chip(label: Text(s.label), visualDensity: VisualDensity.compact)],
              ),
              if (c.farmerNote != null) ...[
                const SizedBox(height: 8),
                Text('“${c.farmerNote}”', style: const TextStyle(fontStyle: FontStyle.italic)),
              ],
              if (c.photos.isNotEmpty) ...[
                const SizedBox(height: 12),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    for (var i = 0; i < c.photos.length; i++)
                      CasePhotoThumb(caseId: c.id, photoId: c.photos[i].id, index: i),
                  ],
                ),
              ],
              if (c.status != CaseStatus.closed && c.photos.length < maxPhotosPerCase) ...[
                const SizedBox(height: 12),
                _uploadingPhoto
                    ? const Row(children: [
                        SizedBox.square(dimension: 16, child: CircularProgressIndicator(strokeWidth: 2)),
                        SizedBox(width: 8),
                        Text('Sending photo…'),
                      ])
                    : AddPhotoButtons(onPick: _addPhoto),
              ],
            ],
          ),
        ),
      ),
    );
  }

  /// What the farmer can do, or should know, at each point of the case's life.
  List<Widget> _statusSection(BuildContext context, CaseDetail c, bool isFarmer) {
    final run = c.latestRun;
    return switch (c.status) {
      CaseStatus.submitted || CaseStatus.awaitingManualReview => [
          if (c.status == CaseStatus.awaitingManualReview)
            Notice(
              message: switch (run) {
                RunSummary(escalated: true) =>
                  'The AI thinks an agronomist should look at this first. ${run.failureReason ?? ''}'.trim(),
                RunSummary(status: 'Failed' || 'TimedOut') =>
                  'The AI could not finish this time. An agronomist will look at your case, or you can try again.',
                _ => 'An agronomist is looking at your case.',
              },
              icon: Icons.support_agent,
              tone: NoticeTone.warning,
            ),
          if (c.status == CaseStatus.awaitingManualReview && run != null && run.advice.isNotEmpty) ...[
            const SizedBox(height: 12),
            AdviceCard(tips: run.advice),
          ],
          if (isFarmer && c.status.canRequestAdvice) ...[
            const SizedBox(height: 12),
            FilledButton.icon(
              onPressed: _requesting ? null : _requestAdvice,
              icon: _requesting
                  ? const SizedBox.square(dimension: 18, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Icon(Icons.auto_awesome),
              label: Text(run == null ? 'Get AI advice' : 'Ask the AI again'),
            ),
            const SizedBox(height: 4),
            const Text(
              'The AI suggests a treatment; an agronomist checks it before anything is prescribed.',
              style: TextStyle(fontSize: 12),
            ),
          ],
        ],
      CaseStatus.agentProcessing => const [
          Notice(
            message: 'The AI is studying your case. This usually takes about a minute — this page updates by itself.',
            icon: Icons.hourglass_top,
          ),
        ],
      CaseStatus.pendingApproval => [
          const Notice(
            message: 'A treatment has been proposed and passed the safety checks. An agronomist is reviewing it now.',
            icon: Icons.fact_check_outlined,
          ),
          if (run != null && run.advice.isNotEmpty) ...[
            const SizedBox(height: 12),
            AdviceCard(tips: run.advice),
          ],
        ],
      CaseStatus.prescribed => [if (run != null) _PrescriptionSection(runId: run.id)],
      CaseStatus.rejected => [
          Notice(
            message: 'The agronomist did not approve the proposed treatment.'
                '${_reason(run?.failureReason)} Please contact your agronomist for advice.',
            icon: Icons.block,
            tone: NoticeTone.warning,
          ),
        ],
      CaseStatus.closed => const [Notice(message: 'This case is closed.')],
    };
  }

  /// `Rejected by the agronomist: reason` → ` Reason: reason`.
  static String _reason(String? failureReason) {
    const prefix = 'Rejected by the agronomist: ';
    if (failureReason == null || !failureReason.startsWith(prefix)) return '';
    return ' Reason: ${failureReason.substring(prefix.length)}';
  }
}

/// Reported → AI analysis → Agronomist review → Prescription, with the current step marked.
class _Progress extends StatelessWidget {
  const _Progress({required this.status});

  final CaseStatus status;

  @override
  Widget build(BuildContext context) {
    final reached = switch (status) {
      CaseStatus.submitted || CaseStatus.awaitingManualReview || CaseStatus.closed => 1,
      CaseStatus.agentProcessing => 2,
      CaseStatus.pendingApproval || CaseStatus.rejected => 3,
      CaseStatus.prescribed => 4,
    };
    const steps = ['Reported', 'AI analysis', 'Agronomist review', 'Prescription'];
    final scheme = Theme.of(context).colorScheme;

    return Semantics(
      label: 'Step $reached of 4: ${steps[reached - 1]}',
      excludeSemantics: true,
      child: Row(
        children: [
          for (var i = 0; i < steps.length; i++)
            Expanded(
              child: Column(
                children: [
                  Container(
                    height: 6,
                    margin: const EdgeInsets.symmetric(horizontal: 2),
                    decoration: BoxDecoration(
                      color: i < reached ? scheme.primary : scheme.surfaceContainerHighest,
                      borderRadius: BorderRadius.circular(3),
                    ),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    steps[i],
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      fontSize: 11,
                      fontWeight: i == reached - 1 ? FontWeight.bold : FontWeight.normal,
                    ),
                  ),
                ],
              ),
            ),
        ],
      ),
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
          : Card(
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('Prescription ${p.prescriptionNo}', style: theme.textTheme.titleMedium),
                    const SizedBox(height: 12),
                    Notice(
                      message: 'Do not harvest before ${formatDate(p.earliestSafeHarvestDate)}.',
                      icon: Icons.warning_amber,
                      tone: NoticeTone.error,
                    ),
                    const SizedBox(height: 12),
                    _Line(label: 'Product', value: p.productName),
                    _Line(label: 'Spray on', value: formatDate(p.sprayDate)),
                    _Line(
                      label: 'Dose',
                      value: p.unitSymbol.isEmpty
                          ? '${formatNumber(p.dosePerHectare)} per hectare'
                          : '${formatNumber(p.dosePerHectare)} ${p.unitSymbol} per hectare',
                    ),
                    _Line(label: 'Total to spray', value: '${formatNumber(p.totalQuantity)} ${p.unitSymbol}'.trim()),
                    const Divider(height: 24),
                    _Line(label: 'Collect from', value: p.dealerName),
                    _Line(label: 'Order', value: '${p.orderNo} · ${p.packs} pack(s) · ${formatLkr(p.orderTotal)}'),
                    if (p.instructions != null) ...[
                      const SizedBox(height: 12),
                      Text(p.instructions!),
                    ],
                  ],
                ),
              ),
            ),
    );
  }
}

class _Line extends StatelessWidget {
  const _Line({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 2),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(width: 110, child: Text(label, style: Theme.of(context).textTheme.bodySmall)),
            Expanded(child: Text(value, style: const TextStyle(fontWeight: FontWeight.w500))),
          ],
        ),
      );
}
