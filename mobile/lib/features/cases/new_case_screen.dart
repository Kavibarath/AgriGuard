import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_exception.dart';
import '../../core/location/location_service.dart';
import '../../core/photos/photo_source.dart';
import '../auth/auth_controller.dart';
import 'case_outbox.dart';
import 'case_models.dart';
import 'case_photos.dart';
import 'case_providers.dart';
import 'case_repository.dart';
import 'case_widgets.dart';

/// The API accepts at most this many symptoms and this long a note (CreateCaseRequestValidator).
const maxSymptoms = 10;
const maxNoteLength = 1000;

/// "Report a crop problem" (§8): pick the plot, tick what you see, say how bad it is, and the
/// phone adds where you are standing. Symptoms come from the API's closed catalogue, so the
/// agent gets structured facts rather than only free text.
class NewCaseScreen extends ConsumerStatefulWidget {
  const NewCaseScreen({super.key});

  @override
  ConsumerState<NewCaseScreen> createState() => _NewCaseScreenState();
}

class _NewCaseScreenState extends ConsumerState<NewCaseScreen> {
  final _formKey = GlobalKey<FormState>();
  final _note = TextEditingController();
  final Set<String> _symptoms = {};
  final List<PickedPhoto> _photos = [];
  ReportablePlot? _plot;
  CaseSeverity _severity = CaseSeverity.medium;

  GeoFix? _fix;
  String? _clientReference;
  bool _locating = true;
  bool _submitting = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _locate();
  }

  @override
  void dispose() {
    _note.dispose();
    super.dispose();
  }

  /// Started as the screen opens, so the fix is usually ready by the time the form is filled.
  Future<void> _locate() async {
    setState(() => _locating = true);
    final fix = await ref.read(locationServiceProvider).currentFix();
    if (mounted) {
      setState(() {
        _fix = fix;
        _locating = false;
      });
    }
  }

  Future<void> _addPhoto(PhotoOrigin origin) async {
    final photo = await ref.read(photoSourceProvider).pick(origin);
    if (photo == null || !mounted) return; // cancelled
    setState(() {
      if (photo.bytes.length > maxPhotoBytes) {
        _error = 'That photo is too large even after resizing. Try taking it again.';
      } else {
        _photos.add(photo);
        _error = null;
      }
    });
  }

  Future<void> _submit() async {
    final formValid = _formKey.currentState!.validate();
    if (_symptoms.isEmpty) {
      setState(() => _error = 'Tick at least one symptom you can see.');
      return;
    }
    if (!formValid) return;

    final plot = _plot!;
    final user = ref.read(currentUserProvider);
    // Made once and kept for every retry of this report, so the API can tell a retry from a
    // second report even when the first attempt arrived and only its reply was lost.
    _clientReference ??= newClientReference();
    final newCase = NewCase(
      plotId: plot.plotId,
      cropCycleId: plot.cropCycleId,
      symptomCodes: _symptoms.toList(),
      severity: _severity,
      // Where the phone is; if it could not tell, where the plot is registered.
      latitude: _fix?.latitude ?? plot.latitude,
      longitude: _fix?.longitude ?? plot.longitude,
      farmerNote: _note.text,
      clientReference: _clientReference,
    );
    final label = '${plot.plotCode} · ${plot.cropName}';
    final outbox = ref.read(caseOutboxProvider.notifier);

    setState(() {
      _submitting = true;
      _error = null;
    });

    final CaseDetail created;
    try {
      created = await ref.read(caseRepositoryProvider).reportCase(newCase);
    } on ApiException catch (e) {
      if (!e.isNetworkError || user == null) {
        if (mounted) setState(() => _error = e.message);
        return;
      }
      // No signal: keep the report, photos and all, and send it when the phone is back online.
      await outbox.enqueue(ownerId: user.id, newCase: newCase, label: label, photos: _photos);
      if (!mounted) return;
      final messenger = ScaffoldMessenger.of(context);
      _leave('/cases');
      messenger.showSnackBar(const SnackBar(
        content: Text('No connection. Your report is saved on this phone and will be sent when you are back online (see My crop problems).'),
      ));
      return;
    } finally {
      if (mounted) setState(() => _submitting = false);
    }

    // The case exists now, whatever happens to the photos: a failed upload must not lose the
    // report. Photos cut off by the connection wait in the queue; any the API refuses can be
    // added again from the case page.
    final unsent = <PickedPhoto>[];
    var refused = 0;
    for (final photo in _photos) {
      try {
        await ref.read(caseRepositoryProvider).uploadPhoto(created.id, photo.bytes, photo.name);
      } on ApiException catch (e) {
        if (e.isNetworkError) {
          unsent.add(photo);
        } else {
          refused++;
        }
      }
    }
    if (unsent.isNotEmpty && user != null) {
      await outbox.enqueue(ownerId: user.id, newCase: newCase, label: label, photos: unsent, caseId: created.id);
    } else {
      // The phone is online: anything still waiting from earlier goes along now.
      unawaited(outbox.sync());
    }

    ref.invalidate(myCasesProvider);
    if (!mounted) return;
    final messenger = ScaffoldMessenger.of(context);
    context.pushReplacement('/cases/${created.id}');
    if (unsent.isNotEmpty) {
      messenger.showSnackBar(SnackBar(
        content: Text('Problem reported. ${_photoCount(unsent.length)} will be sent when the connection is back.'),
      ));
    } else if (refused > 0) {
      messenger.showSnackBar(SnackBar(
        content: Text('Problem reported, but ${_photoCount(refused)} could not be sent. Add ${refused == 1 ? 'it' : 'them'} from this page.'),
      ));
    }
  }

  static String _photoCount(int n) => '$n photo${n == 1 ? '' : 's'}';

  /// Back to where the farmer came from (usually their case list), or to [fallback].
  void _leave(String fallback) {
    if (context.canPop()) {
      context.pop();
    } else {
      context.go(fallback);
    }
  }

  @override
  Widget build(BuildContext context) {
    final plots = ref.watch(reportablePlotsProvider);
    final symptoms = ref.watch(symptomsProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Report a crop problem')),
      body: plots.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(reportablePlotsProvider)),
        data: (plots) => plots.isEmpty
            ? const Padding(
                padding: EdgeInsets.all(24),
                child: Notice(
                  message: 'None of your plots has a crop growing. Record a sowing on a plot first, then report problems on it.',
                ),
              )
            : Form(
                key: _formKey,
                child: ListView(
                  padding: const EdgeInsets.all(16),
                  children: [
                    if (_error != null) ...[
                      Notice(message: _error!, icon: Icons.error_outline, tone: NoticeTone.error),
                      const SizedBox(height: 16),
                    ],
                    DropdownButtonFormField<ReportablePlot>(
                      initialValue: _plot,
                      isExpanded: true,
                      decoration: const InputDecoration(labelText: 'Plot'),
                      items: [
                        for (final p in plots)
                          DropdownMenuItem(
                            value: p,
                            child: Text('${p.plotCode} · ${p.cropName} (${p.stage}) · ${p.farmName}', overflow: TextOverflow.ellipsis),
                          ),
                      ],
                      onChanged: (p) => setState(() => _plot = p),
                      validator: (p) => p == null ? 'Choose the plot with the problem' : null,
                    ),
                    const SizedBox(height: 20),
                    Text('What do you see?', style: Theme.of(context).textTheme.titleMedium),
                    Text('Tick everything that matches (up to $maxSymptoms).', style: Theme.of(context).textTheme.bodySmall),
                    const SizedBox(height: 8),
                    symptoms.when(
                      loading: () => const LinearProgressIndicator(),
                      error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(symptomsProvider)),
                      data: (all) => Wrap(
                        spacing: 8,
                        runSpacing: 4,
                        children: [
                          for (final s in all)
                            FilterChip(
                              label: Text(s.label),
                              selected: _symptoms.contains(s.code),
                              onSelected: (on) => setState(() {
                                if (!on) {
                                  _symptoms.remove(s.code);
                                } else if (_symptoms.length < maxSymptoms) {
                                  _symptoms.add(s.code);
                                }
                              }),
                            ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 20),
                    Text('Photos (optional)', style: Theme.of(context).textTheme.titleMedium),
                    Text('A close-up of a damaged leaf helps the most (up to $maxPhotosPerCase).',
                        style: Theme.of(context).textTheme.bodySmall),
                    const SizedBox(height: 8),
                    if (_photos.isNotEmpty) ...[
                      Wrap(
                        spacing: 8,
                        runSpacing: 8,
                        children: [
                          for (var i = 0; i < _photos.length; i++)
                            LocalPhotoThumb(
                              bytes: _photos[i].bytes,
                              index: i,
                              onRemove: () => setState(() => _photos.removeAt(i)),
                            ),
                        ],
                      ),
                      const SizedBox(height: 8),
                    ],
                    AddPhotoButtons(onPick: _addPhoto, enabled: _photos.length < maxPhotosPerCase),
                    const SizedBox(height: 20),
                    Text('How bad is it?', style: Theme.of(context).textTheme.titleMedium),
                    const SizedBox(height: 8),
                    SegmentedButton<CaseSeverity>(
                      // Four segments share a phone's width: without the tick, "Spreading" fits on one line.
                      showSelectedIcon: false,
                      segments: const [
                        ButtonSegment(value: CaseSeverity.low, label: Text('A little')),
                        ButtonSegment(value: CaseSeverity.medium, label: Text('Spreading')),
                        ButtonSegment(value: CaseSeverity.high, label: Text('Bad')),
                        ButtonSegment(value: CaseSeverity.critical, label: Text('Severe')),
                      ],
                      selected: {_severity},
                      onSelectionChanged: (s) => setState(() => _severity = s.first),
                    ),
                    const SizedBox(height: 20),
                    TextFormField(
                      controller: _note,
                      maxLines: 4,
                      maxLength: maxNoteLength,
                      decoration: const InputDecoration(
                        labelText: 'Anything else? (optional)',
                        hintText: 'When it started, how fast it is spreading, recent rain…',
                        alignLabelWithHint: true,
                      ),
                    ),
                    const SizedBox(height: 8),
                    _LocationLine(locating: _locating, fix: _fix, onRetry: _locate),
                    const SizedBox(height: 24),
                    FilledButton.icon(
                      onPressed: _submitting ? null : _submit,
                      icon: _submitting
                          ? const SizedBox.square(dimension: 18, child: CircularProgressIndicator(strokeWidth: 2))
                          : const Icon(Icons.send),
                      label: const Text('Report problem'),
                    ),
                  ],
                ),
              ),
      ),
    );
  }
}

/// Says which location will be sent: the phone's, or the plot's when the phone cannot tell.
class _LocationLine extends StatelessWidget {
  const _LocationLine({required this.locating, required this.fix, required this.onRetry});

  final bool locating;
  final GeoFix? fix;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    if (locating) {
      return const Row(
        children: [
          SizedBox.square(dimension: 16, child: CircularProgressIndicator(strokeWidth: 2)),
          SizedBox(width: 8),
          Text('Finding your location…'),
        ],
      );
    }
    if (fix case final f?) {
      return Row(
        children: [
          const Icon(Icons.my_location, size: 18),
          const SizedBox(width: 8),
          Expanded(child: Text('Your location will be sent (within ${f.accuracyMetres.round()} m).')),
        ],
      );
    }
    return Row(
      children: [
        const Icon(Icons.location_off_outlined, size: 18),
        const SizedBox(width: 8),
        const Expanded(child: Text("Location unavailable — the plot's registered location will be used.")),
        TextButton(onPressed: onRetry, child: const Text('Try again')),
      ],
    );
  }
}
