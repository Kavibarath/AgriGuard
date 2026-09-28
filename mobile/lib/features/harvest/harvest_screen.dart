import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

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
            padding: const EdgeInsets.all(16),
            children: [
              DropdownButtonFormField<String>(
                initialValue: crop.cropCycleId,
                // Long farm names must shorten rather than overflow the row on a narrow phone.
                isExpanded: true,
                decoration: const InputDecoration(labelText: 'Crop'),
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
              const SizedBox(height: 20),
              _HarvestDays(cropCycleId: crop.cropCycleId),
              const SizedBox(height: 20),
              _SprayWeek(plotId: crop.plotId),
              const SizedBox(height: 20),
              _bookingForm(crop),
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
          Text('Book collection', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          FormField<DateTime>(
            validator: (_) => _date == null ? 'Choose the collection day.' : null,
            builder: (field) => InkWell(
              onTap: window == null ? null : () async {
                await _pickDate(window);
                field.didChange(_date);
              },
              child: InputDecorator(
                decoration: InputDecoration(
                  labelText: 'Collection day',
                  errorText: field.errorText,
                  suffixIcon: const Icon(Icons.calendar_month),
                ),
                child: Text(_date == null ? 'Choose a day' : dayLabel(_date!)),
              ),
            ),
          ),
          const SizedBox(height: 12),
          TextFormField(
            controller: _quantity,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            decoration: const InputDecoration(labelText: 'Quantity (kg)'),
            validator: (value) {
              final kg = double.tryParse(value?.trim() ?? '');
              if (kg == null || kg <= 0) return 'Enter how many kg you will bring.';
              if (kg > 20000) return 'That is more than one delivery; book it in parts.';
              return null;
            },
          ),
          if (_bookingError != null) ...[
            const SizedBox(height: 12),
            Notice(message: _bookingError!, icon: Icons.error_outline, tone: NoticeTone.error),
          ],
          const SizedBox(height: 12),
          FilledButton.icon(
            onPressed: _booking ? null : () => _book(crop),
            icon: _booking
                ? const SizedBox.square(dimension: 18, child: CircularProgressIndicator(strokeWidth: 2))
                : const Icon(Icons.local_shipping),
            label: const Text('Book a slot'),
          ),
          const SizedBox(height: 4),
          const Text(
            'You get the nearest centre with room on your day, or up to two days later.',
            style: TextStyle(fontSize: 12),
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
        Text('When to harvest', style: theme.textTheme.titleMedium),
        const SizedBox(height: 8),
        window.when(
          loading: () => const Padding(padding: EdgeInsets.all(16), child: Center(child: CircularProgressIndicator())),
          error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(harvestWindowProvider(cropCycleId))),
          data: (w) => Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text('Mature around ${dayLabel(w.maturityDate)}. ${w.summary}'),
              if (w.safetyReason != null) ...[
                const SizedBox(height: 8),
                Notice(message: w.safetyReason!, icon: Icons.warning_amber, tone: NoticeTone.warning),
              ],
              for (final d in w.days.take(5))
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  leading: CircleAvatar(child: Text('${d.score}')),
                  title: Text(dayLabel(d.date)),
                  subtitle: Text(d.reasons.join(' · ')),
                  trailing: d.recommended ? const Chip(label: Text('Best')) : null,
                ),
            ],
          ),
        ),
      ],
    );
  }
}

/// The spray-window widget (§8): seven days, each marked sprayable or not, judged exactly as the
/// safety rule V8 will judge a prescription.
class _SprayWeek extends ConsumerWidget {
  const _SprayWeek({required this.plotId});

  final String plotId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final window = ref.watch(sprayWindowProvider(plotId));
    final colors = Theme.of(context).colorScheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text('Spraying this week', style: Theme.of(context).textTheme.titleMedium),
        const SizedBox(height: 8),
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
                              child: ExcludeSemantics(
                                child: Container(
                                  margin: const EdgeInsets.all(2),
                                  padding: const EdgeInsets.symmetric(vertical: 8),
                                  decoration: BoxDecoration(
                                    color: d.suitable ? colors.primaryContainer : colors.errorContainer,
                                    borderRadius: BorderRadius.circular(8),
                                  ),
                                  child: Column(
                                    children: [
                                      Text(_weekdays[d.date.weekday - 1], style: const TextStyle(fontSize: 12)),
                                      Text('${d.date.day}', style: const TextStyle(fontWeight: FontWeight.bold)),
                                      Icon(d.suitable ? Icons.check : Icons.close, size: 18),
                                      Text('${d.rainProbabilityPercent}%', style: const TextStyle(fontSize: 11)),
                                    ],
                                  ),
                                ),
                              ),
                            ),
                          ),
                      ],
                    ),
                    const SizedBox(height: 6),
                    Text(w.summary, style: const TextStyle(fontSize: 12)),
                  ],
                ),
        ),
      ],
    );
  }
}

class _MyBookings extends ConsumerWidget {
  const _MyBookings();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final bookings = ref.watch(myBookingsProvider);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text('My bookings', style: Theme.of(context).textTheme.titleMedium),
        bookings.when(
          loading: () => const Padding(padding: EdgeInsets.all(16), child: Center(child: CircularProgressIndicator())),
          error: (error, _) => ErrorRetry(error: error, onRetry: () => ref.invalidate(myBookingsProvider)),
          data: (items) => items.isEmpty
              ? const Padding(padding: EdgeInsets.symmetric(vertical: 12), child: Text('No collection booked yet.'))
              : Column(
                  children: [
                    for (final b in items)
                      ListTile(
                        contentPadding: EdgeInsets.zero,
                        title: Text('${b.bookingNo} · ${formatNumber(b.quantityKg)} kg ${b.cropName}'),
                        subtitle: Text(
                          '${b.centreName} · ${dayLabel(b.slotDate)} ${b.startTime}–${b.endTime} · ${b.distanceKm.toStringAsFixed(1)} km',
                        ),
                        trailing: b.canCancel
                            ? TextButton(
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
                                child: const Text('Cancel'),
                              )
                            : Text(b.status),
                      ),
                  ],
                ),
        ),
      ],
    );
  }
}
