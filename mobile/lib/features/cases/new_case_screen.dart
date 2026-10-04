import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../app/agri_widgets.dart';
import '../../app/theme.dart';
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
import 'gps_card.dart';

/// The API accepts at most this many symptoms and this long a note (CreateCaseRequestValidator).
const maxSymptoms = 10;
const maxNoteLength = 1000;

/// While the location step is open, the phone keeps asking for a better fix this often, until
/// the fix is good or the farmer moves on.
const gpsRefineInterval = Duration(seconds: 4);

enum _Step {
  photo('Photo'),
  location('Location'),
  symptoms('Symptoms'),
  notes('Notes'),
  review('Review');

  const _Step(this.label);

  final String label;
}

/// "Report a crop problem" (§8) as five short steps: a photo, where it is, what the farmer sees
/// and how bad it is, anything else, then a check before sending. Symptoms come from the API's
/// closed catalogue, so the agent gets structured facts rather than only free text; the phone
/// adds where the farmer is standing.
class NewCaseScreen extends ConsumerStatefulWidget {
  const NewCaseScreen({super.key});

  @override
  ConsumerState<NewCaseScreen> createState() => _NewCaseScreenState();
}

class _NewCaseScreenState extends ConsumerState<NewCaseScreen> {
  final _locationForm = GlobalKey<FormState>();
  final _note = TextEditingController();
  final Set<String> _symptoms = {};
  final List<PickedPhoto> _photos = [];
  ReportablePlot? _plot;
  CaseSeverity _severity = CaseSeverity.medium;
  _Step _step = _Step.photo;

  GeoFix? _fix;
  DateTime? _fixAt;
  Timer? _refine;
  bool _refining = false;
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
    _refine?.cancel();
    _note.dispose();
    super.dispose();
  }

  /// Started as the screen opens, so the fix is usually ready by the time the farmer reaches the
  /// location step.
  Future<void> _locate() async {
    setState(() => _locating = true);
    final fix = await ref.read(locationServiceProvider).currentFix();
    if (mounted) {
      setState(() {
        _fix = fix;
        _fixAt = fix == null ? null : DateTime.now();
        _locating = false;
      });
      _syncRefining();
    }
  }

  /// Live accuracy: while the location step is open and the fix is still loose, ask again every
  /// few seconds and keep the closest fix. Only once a fix has been given, so a refused location
  /// permission is never asked for over and over.
  void _syncRefining() {
    final wanted = _step == _Step.location && _fix != null && _fix!.accuracyMetres > goodFixMetres;
    if (wanted) {
      _refine ??= Timer.periodic(gpsRefineInterval, (_) => _refineFix());
    } else {
      _refine?.cancel();
      _refine = null;
    }
  }

  Future<void> _refineFix() async {
    if (_refining || _locating) return;
    setState(() => _refining = true);
    final fix = await ref.read(locationServiceProvider).currentFix();
    if (!mounted) return;
    setState(() {
      _refining = false;
      if (fix != null && (_fix == null || fix.accuracyMetres <= _fix!.accuracyMetres)) {
        _fix = fix;
        _fixAt = DateTime.now();
      }
    });
    _syncRefining();
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

  void _goTo(_Step step) {
    setState(() {
      _step = step;
      _error = null;
    });
    _syncRefining();
  }

  /// On to the next step, once this one has what the report needs.
  void _next() {
    switch (_step) {
      case _Step.location when !_locationForm.currentState!.validate():
        return;
      case _Step.symptoms when _symptoms.isEmpty:
        setState(() => _error = 'Tick at least one symptom you can see.');
        return;
      default:
        _goTo(_Step.values[_step.index + 1]);
    }
  }

  Future<void> _submit() async {
    // Each step checked its own part; this catches a step changed from the review.
    if (_plot == null) {
      _goTo(_Step.location);
      return;
    }
    if (_symptoms.isEmpty) {
      _goTo(_Step.symptoms);
      setState(() => _error = 'Tick at least one symptom you can see.');
      return;
    }

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

    // The system back button steps back through the report before it leaves it, so a farmer
    // never loses a half-made report to one press.
    return PopScope(
      canPop: _step == _Step.photo,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop && !_submitting) _goTo(_Step.values[_step.index - 1]);
      },
      child: Scaffold(
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
              : Column(
                  children: [
                    _StepHeader(current: _step, onTap: _submitting ? null : _goTo),
                    Expanded(
                      child: ListView(
                        padding: const EdgeInsets.fromLTRB(16, 20, 16, 24),
                        children: [
                          if (_error != null) ...[
                            Notice(message: _error!, tone: NoticeTone.error),
                            const SizedBox(height: 16),
                          ],
                          ...switch (_step) {
                            _Step.photo => _photoStep(),
                            _Step.location => _locationStep(plots),
                            _Step.symptoms => _symptomsStep(),
                            _Step.notes => _notesStep(),
                            _Step.review => _reviewStep(),
                          },
                        ],
                      ),
                    ),
                    BottomActionBar(
                      children: [
                        if (_step != _Step.photo)
                          Expanded(
                            child: OutlinedButton.icon(
                              onPressed: _submitting ? null : () => _goTo(_Step.values[_step.index - 1]),
                              icon: const Icon(Icons.arrow_back),
                              label: const Text('Back'),
                            ),
                          ),
                        Expanded(
                          flex: 2,
                          child: _step == _Step.review
                              ? FilledButton.icon(
                                  onPressed: _submitting ? null : _submit,
                                  icon: _submitting ? const InlineSpinner() : const Icon(Icons.send_outlined),
                                  label: const Text('Report problem'),
                                )
                              : FilledButton.icon(
                                  onPressed: _next,
                                  // The label first, the arrow after it.
                                  style: FilledButton.styleFrom(iconAlignment: IconAlignment.end),
                                  icon: const Icon(Icons.arrow_forward),
                                  label: const Text('Next'),
                                ),
                        ),
                      ],
                    ),
                  ],
                ),
        ),
      ),
    );
  }

  List<Widget> _stepIntro(String title, String help) {
    final theme = Theme.of(context);
    return [
      Semantics(header: true, child: Text(title, style: theme.textTheme.headlineSmall)),
      const SizedBox(height: 4),
      Text(help, style: theme.textTheme.bodyMedium?.copyWith(color: AgriColors.inkMuted)),
      const SizedBox(height: 20),
    ];
  }

  List<Widget> _photoStep() => [
        ..._stepIntro(
          'Photograph the problem',
          'A close-up of a damaged leaf helps the most (up to $maxPhotosPerCase). You can skip this.',
        ),
        _CameraTarget(
          enabled: _photos.length < maxPhotosPerCase,
          taken: _photos.length,
          onCamera: () => _addPhoto(PhotoOrigin.camera),
        ),
        const SizedBox(height: 12),
        Center(
          child: TextButton.icon(
            onPressed: _photos.length < maxPhotosPerCase ? () => _addPhoto(PhotoOrigin.gallery) : null,
            icon: const Icon(Icons.photo_library_outlined),
            label: const Text('From gallery'),
          ),
        ),
        if (_photos.isNotEmpty) ...[
          const SizedBox(height: 12),
          Wrap(
            spacing: 4,
            runSpacing: 4,
            children: [
              for (var i = 0; i < _photos.length; i++)
                LocalPhotoThumb(bytes: _photos[i].bytes, index: i, onRemove: () => setState(() => _photos.removeAt(i))),
            ],
          ),
        ],
      ];

  List<Widget> _locationStep(List<ReportablePlot> plots) => [
        ..._stepIntro('Where is it?', 'Choose the plot with the problem. Stand in it if you can: the phone sends where you are.'),
        Form(
          key: _locationForm,
          child: DropdownButtonFormField<ReportablePlot>(
            initialValue: _plot,
            isExpanded: true,
            decoration: const InputDecoration(labelText: 'Plot', prefixIcon: Icon(Icons.grass)),
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
        ),
        const SizedBox(height: 16),
        GpsCard(
          locating: _locating,
          refining: _refining,
          live: true,
          fix: _fix,
          fixAt: _fixAt,
          onRetry: _locate,
        ),
      ];

  List<Widget> _symptomsStep() {
    final symptoms = ref.watch(symptomsProvider);
    final theme = Theme.of(context);
    return [
      ..._stepIntro('What do you see?', 'Tick everything that matches (up to $maxSymptoms).'),
      symptoms.when(
        loading: () => const LinearProgressIndicator(),
        error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(symptomsProvider)),
        data: (all) => Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            for (final group in groupSymptoms(all)) ...[
              Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: Row(
                  children: [
                    Icon(group.part.icon, size: 20, color: AgriColors.earth600),
                    const SizedBox(width: 8),
                    Semantics(header: true, child: Text(group.part.label, style: theme.textTheme.titleSmall)),
                  ],
                ),
              ),
              Wrap(
                spacing: 8,
                runSpacing: 0,
                children: [
                  for (final s in group.symptoms)
                    FilterChip(
                      label: Text(s.label),
                      selected: _symptoms.contains(s.code),
                      onSelected: (on) => setState(() {
                        if (!on) {
                          _symptoms.remove(s.code);
                        } else if (_symptoms.length < maxSymptoms) {
                          _symptoms.add(s.code);
                          _error = null;
                        }
                      }),
                    ),
                ],
              ),
              const SizedBox(height: 16),
            ],
            Text('${_symptoms.length} of $maxSymptoms ticked', style: theme.textTheme.bodySmall),
          ],
        ),
      ),
      const SizedBox(height: 24),
      Semantics(header: true, child: Text('How bad is it?', style: theme.textTheme.titleMedium)),
      const SizedBox(height: 10),
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
    ];
  }

  List<Widget> _notesStep() => [
        ..._stepIntro('Anything else?', 'When it started, how fast it is spreading, recent rain or spraying. You can leave this empty.'),
        TextFormField(
          controller: _note,
          maxLines: 6,
          maxLength: maxNoteLength,
          decoration: const InputDecoration(
            labelText: 'Anything else? (optional)',
            hintText: 'When it started, how fast it is spreading, recent rain…',
            alignLabelWithHint: true,
          ),
        ),
      ];

  List<Widget> _reviewStep() {
    final theme = Theme.of(context);
    final labels = {for (final s in ref.watch(symptomsProvider).value ?? const <Symptom>[]) s.code: s.label};
    final note = _note.text.trim();
    const severityWords = {
      CaseSeverity.low: 'A little',
      CaseSeverity.medium: 'Spreading',
      CaseSeverity.high: 'Bad',
      CaseSeverity.critical: 'Severe',
    };

    Widget section(_Step step, String title, Widget body) => Padding(
          padding: const EdgeInsets.only(bottom: 12),
          child: AgriCard(
            padding: const EdgeInsets.fromLTRB(14, 10, 6, 14),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(child: Text(title, style: theme.textTheme.titleSmall)),
                    TextButton(onPressed: _submitting ? null : () => _goTo(step), child: const Text('Change')),
                  ],
                ),
                Padding(padding: const EdgeInsets.only(right: 8), child: body),
              ],
            ),
          ),
        );

    return [
      ..._stepIntro('Check and send', 'An agronomist sees exactly this. Nothing is prescribed until they have checked it.'),
      section(
        _Step.photo,
        'Photos',
        _photos.isEmpty
            ? Text('No photo', style: theme.textTheme.bodyMedium?.copyWith(color: AgriColors.inkMuted))
            : Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  for (final (i, p) in _photos.indexed)
                    ClipRRect(
                      borderRadius: BorderRadius.circular(8),
                      child: Image.memory(p.bytes, width: 64, height: 64, fit: BoxFit.cover, semanticLabel: 'Photo ${i + 1}'),
                    ),
                ],
              ),
      ),
      section(
        _Step.location,
        'Where',
        Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              _plot == null ? 'No plot chosen' : '${_plot!.plotCode} · ${_plot!.cropName} (${_plot!.stage}) · ${_plot!.farmName}',
              style: theme.textTheme.bodyLarge,
            ),
            const SizedBox(height: 4),
            Text(
              _fix == null
                  ? "The plot's registered location will be sent."
                  : 'With your location, to within ${_fix!.accuracyMetres.round()} m.',
              style: theme.textTheme.bodySmall,
            ),
          ],
        ),
      ),
      section(
        _Step.symptoms,
        'What you see',
        Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            for (final code in _symptoms)
              Padding(
                padding: const EdgeInsets.only(bottom: 4),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Padding(padding: EdgeInsets.only(top: 3), child: Icon(Icons.check, size: 18, color: AgriColors.earth600)),
                    const SizedBox(width: 8),
                    Expanded(child: Text(labels[code] ?? code)),
                  ],
                ),
              ),
            const SizedBox(height: 4),
            Text('How bad: ${severityWords[_severity]}', style: theme.textTheme.bodyMedium?.copyWith(fontWeight: FontWeight.w600)),
          ],
        ),
      ),
      section(
        _Step.notes,
        'Your note',
        Text(
          note.isEmpty ? 'No note' : '“$note”',
          style: theme.textTheme.bodyMedium?.copyWith(
            fontStyle: note.isEmpty ? null : FontStyle.italic,
            color: note.isEmpty ? AgriColors.inkMuted : null,
          ),
        ),
      ),
      const _OfflineReassurance(),
    ];
  }
}

/// Five steps across the top: done ones ticked and tappable to go back, the current one marked.
class _StepHeader extends StatelessWidget {
  const _StepHeader({required this.current, required this.onTap});

  final _Step current;
  final void Function(_Step step)? onTap;

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      decoration: const BoxDecoration(
        color: AgriColors.surfaceCard,
        border: Border(bottom: BorderSide(color: AgriColors.borderSubtle)),
      ),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(8, 6, 8, 6),
        child: Semantics(
          container: true,
          label: 'Step ${current.index + 1} of ${_Step.values.length}: ${current.label}',
          child: Row(
            children: [
              for (final step in _Step.values)
                Expanded(
                  child: _StepNode(
                    step: step,
                    state: step.index < current.index
                        ? Tone.done
                        : step == current
                            ? Tone.active
                            : Tone.neutral,
                    onTap: step.index < current.index && onTap != null ? () => onTap!(step) : null,
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _StepNode extends StatelessWidget {
  const _StepNode({required this.step, required this.state, this.onTap});

  final _Step step;
  final Tone state;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final current = state == Tone.active;
    final done = state == Tone.done;
    // Only the steps the farmer can go back to are announced; the header says where they are.
    return ExcludeSemantics(
      excluding: onTap == null,
      child: Semantics(
        button: true,
        label: 'Back to ${step.label}',
        excludeSemantics: true,
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(10),
          child: ConstrainedBox(
            constraints: const BoxConstraints(minHeight: 52),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Container(
                  width: 26,
                  height: 26,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    color: done ? AgriColors.brand600 : (current ? AgriColors.brand700 : AgriColors.surfaceCard),
                    border: Border.all(color: done || current ? Colors.transparent : AgriColors.outline, width: 1.5),
                  ),
                  child: done
                      ? const Icon(Icons.check, size: 16, color: Colors.white)
                      : Text(
                          '${step.index + 1}',
                          style: TextStyle(
                            fontSize: 13,
                            fontWeight: FontWeight.w700,
                            color: current ? Colors.white : AgriColors.inkMuted,
                          ),
                        ),
                ),
                const SizedBox(height: 4),
                Text(
                  step.label,
                  style: TextStyle(
                    fontSize: 13,
                    fontWeight: current ? FontWeight.w700 : FontWeight.w500,
                    color: current ? AgriColors.brand800 : AgriColors.inkMuted,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// The big, obvious camera button: most of the photo step is a target.
class _CameraTarget extends StatelessWidget {
  const _CameraTarget({required this.enabled, required this.taken, required this.onCamera});

  final bool enabled;
  final int taken;
  final VoidCallback onCamera;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Semantics(
      button: true,
      enabled: enabled,
      child: Material(
        color: enabled ? AgriColors.brand50 : AgriColors.surfaceSunken,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(16),
          side: BorderSide(color: enabled ? AgriColors.brand400 : AgriColors.borderStrong, width: 1.5),
        ),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: enabled ? onCamera : null,
          child: SizedBox(
            height: 200,
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Container(
                  width: 76,
                  height: 76,
                  decoration: BoxDecoration(
                    color: enabled ? AgriColors.brand700 : AgriColors.surfaceInset,
                    shape: BoxShape.circle,
                  ),
                  child: Icon(Icons.photo_camera_outlined, size: 38, color: enabled ? Colors.white : AgriColors.inkMuted),
                ),
                const SizedBox(height: 14),
                Text('Take photo', style: theme.textTheme.titleMedium?.copyWith(color: enabled ? AgriColors.brand800 : AgriColors.inkMuted)),
                const SizedBox(height: 2),
                Text(
                  enabled ? '$taken of $maxPhotosPerCase taken' : 'You have $maxPhotosPerCase photos, the most a report can carry',
                  style: theme.textTheme.bodySmall,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// Before sending: what happens if there is no signal, so a farmer in a dead spot presses send
/// with confidence.
class _OfflineReassurance extends StatelessWidget {
  const _OfflineReassurance();

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(top: 4),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Icon(Icons.cloud_done_outlined, size: 20, color: AgriColors.brand700),
            const SizedBox(width: 10),
            Expanded(
              child: Text(
                'No signal? The report is kept on this phone, photos and all, and sent when you are back online.',
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ),
          ],
        ),
      );
}

/// Where on the plant a symptom shows, read from the catalogue code's first word
/// (SymptomCatalogue.cs: leaf_…, stem_…, fruit_…, insects_…). A code the phone does not know yet
/// lands under "Other signs" rather than being hidden.
enum PlantPart {
  leaves('Leaves', Icons.eco_outlined),
  stems('Stems', Icons.grass),
  flowersFruit('Flowers, fruit and grain', Icons.local_florist_outlined),
  wholePlant('Whole plant', Icons.park_outlined),
  pests('Insects and pests', Icons.bug_report_outlined),
  other('Other signs', Icons.more_horiz);

  const PlantPart(this.label, this.icon);

  final String label;
  final IconData icon;

  static PlantPart of(String code) => switch (code.split('_').first) {
        'leaf' => leaves,
        'stem' => stems,
        'flower' || 'fruit' || 'panicle' => flowersFruit,
        'plant' => wholePlant,
        'insects' || 'larvae' || 'whorl' || 'fine' => pests,
        _ => other,
      };
}

/// The checklist in plant-part order, each part's symptoms in catalogue order.
List<({PlantPart part, List<Symptom> symptoms})> groupSymptoms(List<Symptom> all) => [
      for (final part in PlantPart.values)
        if (all.where((s) => PlantPart.of(s.code) == part).toList() case final items when items.isNotEmpty)
          (part: part, symptoms: items),
    ];
