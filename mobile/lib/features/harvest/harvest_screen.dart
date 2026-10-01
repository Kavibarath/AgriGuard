import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/agri_widgets.dart';
import '../../app/theme.dart';
import '../../core/api/api_exception.dart';
import '../cases/case_models.dart';
import '../cases/case_providers.dart';
import '../cases/case_widgets.dart';
import 'harvest_models.dart';
import 'harvest_repository.dart';

const _weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const _months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/// "Thu 1 Oct": how a farmer reads a day.
String dayLabel(DateTime d) => '${_weekdays[d.weekday - 1]} ${d.day} ${_months[d.month - 1]}';

DateTime _dateOnly(DateTime d) => DateTime(d.year, d.month, d.day);

/// Harvest planning and collection booking (§8, Component D): when to harvest, whether this week
/// suits spraying, and a collection slot for the harvest. The server decides everything that
/// matters — safety, capacity, which centre — and this screen shows its reasons.
class HarvestScreen extends ConsumerStatefulWidget {
  const HarvestScreen({super.key});

  @override
  ConsumerState<HarvestScreen> createState() => _HarvestScreenState();
}

class _HarvestScreenState extends ConsumerState<HarvestScreen> {
  ReportablePlot? _crop;
  DateTime? _date;
  final _quantity = TextEditingController();
  final _formKey = GlobalKey<FormState>();
  bool _booking = false;
  String? _bookingError;

  @override
  void dispose() {
    _quantity.dispose();
    super.dispose();
  }

  Future<void> _pickDate(HarvestWindow window) async {
    final today = _dateOnly(DateTime.now());
    // Days before the pre-harvest interval clears cannot even be chosen.
    final first = window.safeFromDate != null && window.safeFromDate!.isAfter(today) ? window.safeFromDate! : today;
    final suggested = _date ?? window.bestDay ?? first;
    final picked = await showDatePicker(
      context: context,
      firstDate: first,
      lastDate: today.add(const Duration(days: 60)),
      initialDate: suggested.isBefore(first) ? first : suggested,
      helpText: 'Collection day',
    );
    if (picked != null) setState(() => _date = picked);
  }

  Future<void> _book(ReportablePlot crop) async {
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _booking = true;
      _bookingError = null;
    });
    try {
      final booking = await ref.read(harvestRepositoryProvider).book(
            cropCycleId: crop.cropCycleId,
            quantityKg: double.parse(_quantity.text.trim()),
            preferredDate: _date!,
          );
      ref.invalidate(myBookingsProvider);
      if (!mounted) return;
      _quantity.clear();
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text('Booked ${booking.bookingNo}: ${booking.centreName}, ${dayLabel(booking.slotDate)} ${booking.startTime}–${booking.endTime}.'),
      ));
    } on ApiException catch (e) {
      setState(() => _bookingError = e.message);
    } finally {
      if (mounted) setState(() => _booking = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final plots = ref.watch(reportablePlotsProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Harvest & collection')),
      body: plots.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (error, _) => ListView(children: [ErrorRetry(error: error, onRetry: () => ref.invalidate(reportablePlotsProvider))]),
        data: (crops) {
          if (crops.isEmpty) {
            return const Padding(
              padding: EdgeInsets.all(24),
              child: Notice(message: 'Nothing is growing on your plots, so there is no harvest to plan.'),
            );
          }
          final crop = _crop ?? crops.first;
          return ListView(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 24),
            children: [
              DropdownButtonFormField<String>(
                initialValue: crop.cropCycleId,
                // Long farm names must shorten rather than overflow the row on a narrow phone.
                isExpanded: true,
                decoration: const InputDecoration(labelText: 'Crop', prefixIcon: Icon(Icons.grass)),
                items: [
                  for (final c in crops)
                    DropdownMenuItem(
                      value: c.cropCycleId,
                      child: Text('${c.cropName} · ${c.plotCode} · ${c.farmName}', overflow: TextOverflow.ellipsis),
                    ),
                ],
                onChanged: (id) => setState(() {
                  _crop = crops.firstWhere((c) => c.cropCycleId == id);
                  _date = null;
                  _bookingError = null;
                }),
              ),
              const SizedBox(height: 16),
              AgriCard(child: _HarvestDays(cropCycleId: crop.cropCycleId)),
              const SizedBox(height: 16),
              AgriCard(child: _SprayWeek(plotId: crop.plotId)),
              const SizedBox(height: 16),
              AgriCard(
                color: AgriColors.earth50,
                borderColor: AgriColors.earth200,
                child: _bookingForm(crop),
              ),
              const SizedBox(height: 24),
              const _MyBookings(),
            ],
          );
        },
      ),
    );
  }

  Widget _bookingForm(ReportablePlot crop) {
    final window = ref.watch(harvestWindowProvider(crop.cropCycleId)).value;
    return Form(
      key: _formKey,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const SectionTitle('Book collection', icon: Icons.local_shipping_outlined, earth: true),
          Text(
            'You get the nearest centre with room on your day, or up to two days later.',
            style: Theme.of(context).textTheme.bodySmall?.copyWith(color: AgriColors.earth800),
          ),
          const SizedBox(height: 14),
          FormField<DateTime>(
            validator: (_) => _date == null ? 'Choose the collection day.' : null,
            builder: (field) => InkWell(
              onTap: window == null ? null : () async {
                await _pickDate(window);
                field.didChange(_date);
              },
              borderRadius: BorderRadius.circular(10),
              child: InputDecorator(
                decoration: InputDecoration(
                  labelText: 'Collection day',
                  errorText: field.errorText,
                  prefixIcon: const Icon(Icons.event_outlined),
                  suffixIcon: const Icon(Icons.arrow_drop_down),
                ),
                child: Text(_date == null ? 'Choose a day' : dayLabel(_date!)),
              ),
            ),
          ),
          const SizedBox(height: 14),
          TextFormField(
            controller: _quantity,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            decoration: const InputDecoration(labelText: 'Quantity (kg)', suffixText: 'kg', prefixIcon: Icon(Icons.scale_outlined)),
            validator: (value) {
              final kg = double.tryParse(value?.trim() ?? '');
              if (kg == null || kg <= 0) return 'Enter how many kg you will bring.';
              if (kg > 20000) return 'That is more than one delivery; book it in parts.';
              return null;
            },
          ),
          if (_bookingError != null) ...[
            const SizedBox(height: 12),
            Notice(message: _bookingError!, tone: NoticeTone.error),
          ],
          const SizedBox(height: 16),
          FilledButton.icon(
            onPressed: _booking ? null : () => _book(crop),
            icon: _booking ? const InlineSpinner() : const Icon(Icons.local_shipping_outlined),
            label: const Text('Book a slot'),
          ),
        ],
      ),
    );
  }
}

/// The ranked harvest days, with the chemical-safety line first when a spray is still clearing.
class _HarvestDays extends ConsumerWidget {
  const _HarvestDays({required this.cropCycleId});

  final String cropCycleId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final window = ref.watch(harvestWindowProvider(cropCycleId));
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionTitle('When to harvest', icon: Icons.agriculture_outlined, earth: true),
        window.when(
          loading: () => const Padding(padding: EdgeInsets.all(16), child: Center(child: CircularProgressIndicator())),
          error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(harvestWindowProvider(cropCycleId))),
          data: (w) => Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text('Mature around ${dayLabel(w.maturityDate)}. ${w.summary}', style: theme.textTheme.bodyMedium),
              if (w.safetyReason != null) ...[
                const SizedBox(height: 10),
                Notice(message: w.safetyReason!, tone: NoticeTone.warning),
              ],
              const SizedBox(height: 6),
              for (final (i, d) in w.days.take(5).indexed) ...[
                if (i > 0) const Divider(height: 1),
                _HarvestDayRow(day: d),
              ],
            ],
          ),
        ),
      ],
    );
  }
}

/// One candidate day: its score as a filled bar with the number, the reasons, and "Best" on the
/// day the server recommends.
class _HarvestDayRow extends StatelessWidget {
  const _HarvestDayRow({required this.day});

  final HarvestDay day;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final d = day;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 10),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 52,
            child: Column(
              children: [
                Text('${d.score}', style: theme.textTheme.titleLarge?.copyWith(color: d.recommended ? AgriColors.brand700 : AgriColors.inkSoft)),
                const SizedBox(height: 4),
                ExcludeSemantics(
                  child: ClipRRect(
                    borderRadius: BorderRadius.circular(3),
                    child: LinearProgressIndicator(
                      value: (d.score.clamp(0, 100)) / 100,
                      minHeight: 5,
                      color: d.recommended ? AgriColors.brand600 : AgriColors.earth400,
                      backgroundColor: AgriColors.surfaceInset,
                    ),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Wrap(
                  spacing: 8,
                  runSpacing: 4,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  children: [
                    Text(dayLabel(d.date), style: theme.textTheme.titleSmall),
                    if (d.recommended) const _BestTag(),
                  ],
                ),
                const SizedBox(height: 2),
                Text(d.reasons.join(' · '), style: theme.textTheme.bodySmall),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _BestTag extends StatelessWidget {
  const _BestTag();

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.fromLTRB(6, 2, 8, 2),
        decoration: BoxDecoration(
          color: AgriColors.brand700,
          borderRadius: BorderRadius.circular(999),
        ),
        child: const Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.star_rounded, size: 15, color: Colors.white),
            SizedBox(width: 3),
            Text('Best', style: TextStyle(fontSize: 13, fontWeight: FontWeight.w700, color: Colors.white)),
          ],
        ),
      );
}

/// The spray-window widget (§8): seven days, each marked sprayable or not, judged exactly as the
/// safety rule V8 will judge a prescription.
class _SprayWeek extends ConsumerWidget {
  const _SprayWeek({required this.plotId});

  final String plotId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final window = ref.watch(sprayWindowProvider(plotId));
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionTitle('Spraying this week', icon: Icons.water_drop_outlined),
        window.when(
          loading: () => const LinearProgressIndicator(),
          error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(sprayWindowProvider(plotId))),
          data: (w) => !w.forecastAvailable
              ? Notice(message: w.summary)
              : Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Row(
                      children: [
                        for (final d in w.days)
                          Expanded(
                            child: Semantics(
                              label: '${dayLabel(d.date)}: ${d.suitable ? 'suits spraying' : d.problems.join('; ')}',
                              child: ExcludeSemantics(child: _SprayDayCell(day: d, today: DateUtils.isSameDay(d.date, DateTime.now()))),
                            ),
                          ),
                      ],
                    ),
                    const SizedBox(height: 10),
                    // The server's summary names each unsuitable day and why, in words.
                    Text(w.summary, style: theme.textTheme.bodySmall),
                  ],
                ),
        ),
      ],
    );
  }
}

/// One day of the spray strip: a tick or a cross (the shape says it, the colour repeats it), the
/// day, and the chance of rain.
class _SprayDayCell extends StatelessWidget {
  const _SprayDayCell({required this.day, required this.today});

  final SprayDay day;
  final bool today;

  @override
  Widget build(BuildContext context) {
    final tone = toneColors(day.suitable ? Tone.done : Tone.danger);
    return Container(
      margin: const EdgeInsets.symmetric(horizontal: 2),
      padding: const EdgeInsets.symmetric(vertical: 8),
      decoration: BoxDecoration(
        color: tone.fill,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: today ? tone.ink : tone.ring, width: today ? 2 : 1),
      ),
      child: Column(
        children: [
          Text(
            today ? 'Today' : _weekdays[day.date.weekday - 1],
            style: TextStyle(fontSize: 12, fontWeight: today ? FontWeight.w700 : FontWeight.w500, color: tone.ink),
          ),
          Text('${day.date.day}', style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700, color: tone.ink)),
          const SizedBox(height: 2),
          Icon(day.suitable ? Icons.check : Icons.close, size: 20, color: tone.ink),
          const SizedBox(height: 2),
          Text('${day.rainProbabilityPercent}%', style: TextStyle(fontSize: 12, color: tone.ink)),
        ],
      ),
    );
  }
}

class _MyBookings extends ConsumerWidget {
  const _MyBookings();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final bookings = ref.watch(myBookingsProvider);
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionTitle('My bookings', icon: Icons.event_available_outlined, earth: true),
        bookings.when(
          loading: () => const Padding(padding: EdgeInsets.all(16), child: Center(child: CircularProgressIndicator())),
          error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(myBookingsProvider)),
          data: (items) => items.isEmpty
              ? Padding(
                  padding: const EdgeInsets.symmetric(vertical: 8),
                  child: Text('No collection booked yet.', style: theme.textTheme.bodyMedium?.copyWith(color: AgriColors.inkMuted)),
                )
              : Column(
                  children: [
                    for (final b in items)
                      Padding(
                        padding: const EdgeInsets.only(bottom: 10),
                        child: AgriCard(
                          padding: const EdgeInsets.fromLTRB(14, 14, 8, 10),
                          child: Row(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              const GlyphTile(Icons.warehouse_outlined, earth: true, size: 40),
                              const SizedBox(width: 12),
                              Expanded(
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text('${b.bookingNo} · ${formatNumber(b.quantityKg)} kg ${b.cropName}', style: theme.textTheme.titleSmall),
                                    const SizedBox(height: 2),
                                    Text(
                                      '${b.centreName} · ${dayLabel(b.slotDate)} ${b.startTime}–${b.endTime} · ${b.distanceKm.toStringAsFixed(1)} km',
                                      style: theme.textTheme.bodySmall,
                                    ),
                                    const SizedBox(height: 4),
                                    Row(
                                      children: [
                                        StatusPill(label: _bookingLabel(b.status), tone: _bookingTone(b.status)),
                                        const Spacer(),
                                        // Kept apart from the details, so it is not pressed by accident.
                                        if (b.canCancel)
                                          TextButton.icon(
                                            style: TextButton.styleFrom(foregroundColor: AgriColors.danger800),
                                            onPressed: () async {
                                              try {
                                                await ref.read(harvestRepositoryProvider).cancel(b.id);
                                                ref.invalidate(myBookingsProvider);
                                              } on ApiException catch (e) {
                                                if (context.mounted) {
                                                  ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
                                                }
                                              }
                                            },
                                            icon: const Icon(Icons.event_busy_outlined, size: 20),
                                            label: const Text('Cancel'),
                                          ),
                                      ],
                                    ),
                                  ],
                                ),
                              ),
                            ],
                          ),
                        ),
                      ),
                  ],
                ),
        ),
      ],
    );
  }

  static String _bookingLabel(String status) => switch (status) {
        'CheckedIn' => 'Checked in',
        'NoShow' => 'Missed',
        _ => status,
      };

  /// BookingStatus on the server: Booked, CheckedIn, Completed, Cancelled, NoShow.
  static Tone _bookingTone(String status) => switch (status) {
        'Booked' || 'CheckedIn' => Tone.active,
        'Completed' => Tone.done,
        'NoShow' => Tone.warning,
        _ => Tone.neutral,
      };
}
