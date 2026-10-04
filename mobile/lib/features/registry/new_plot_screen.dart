import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../app/agri_widgets.dart';
import '../../core/api/api_exception.dart';
import '../../core/location/location_service.dart';
import '../cases/case_widgets.dart';
import '../cases/gps_card.dart';
import 'registry_models.dart';
import 'registry_repository.dart';

/// Registers a plot. The farmer is asked to stand in it: the phone's GPS gives its location, which
/// the weather, the harvest windows and the nearest collection centre are all worked out from.
/// Without a fix the coordinates can be typed in.
class NewPlotScreen extends ConsumerStatefulWidget {
  const NewPlotScreen({required this.farmId, super.key});

  final String farmId;

  @override
  ConsumerState<NewPlotScreen> createState() => _NewPlotScreenState();
}

class _NewPlotScreenState extends ConsumerState<NewPlotScreen> {
  final _formKey = GlobalKey<FormState>();
  final _code = TextEditingController();
  final _name = TextEditingController();
  final _area = TextEditingController();
  final _latitude = TextEditingController();
  final _longitude = TextEditingController();
  SoilType _soil = SoilType.loam;

  bool _locating = false;
  GeoFix? _fix;
  bool _saving = false;
  String? _error;
  Map<String, List<String>> _fieldErrors = const {};

  @override
  void initState() {
    super.initState();
    _locate();
  }

  @override
  void dispose() {
    for (final c in [_code, _name, _area, _latitude, _longitude]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> _locate() async {
    setState(() => _locating = true);
    final fix = await ref.read(locationServiceProvider).currentFix();
    if (!mounted) return;
    setState(() {
      _locating = false;
      _fix = fix;
      if (fix != null) {
        _latitude.text = fix.latitude.toStringAsFixed(6);
        _longitude.text = fix.longitude.toStringAsFixed(6);
      }
    });
  }

  Future<void> _save() async {
    setState(() => _fieldErrors = const {});
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final plot = await ref.read(registryRepositoryProvider).createPlot(NewPlot(
            farmId: widget.farmId,
            plotCode: _code.text.trim(),
            name: _name.text.trim().isEmpty ? null : _name.text.trim(),
            areaHectares: double.parse(_area.text.trim()),
            latitude: double.parse(_latitude.text.trim()),
            longitude: double.parse(_longitude.text.trim()),
            soilType: _soil,
          ));
      ref.invalidate(farmPlotsProvider(widget.farmId));
      ref.invalidate(farmProvider(widget.farmId));
      ref.invalidate(myFarmsProvider);
      if (mounted) context.pushReplacement('/plots/${plot.id}');
    } on ApiException catch (e) {
      setState(() {
        _error = e.message;
        _fieldErrors = e.fieldErrors;
      });
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  String? _serverError(String field) => _fieldErrors[field]?.first;

  static String? _decimal(String? value, {required double min, required double max, required String message}) {
    final number = double.tryParse(value?.trim() ?? '');
    if (number == null || number < min || number > max) return message;
    return null;
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Add a plot')),
      body: Form(
        key: _formKey,
        child: Column(
          children: [
            Expanded(
              child: ListView(
                padding: const EdgeInsets.fromLTRB(16, 12, 16, 24),
                children: [
                  if (_error != null) ...[
                    Notice(message: _error!, tone: NoticeTone.error),
                    const SizedBox(height: 16),
                  ],
                  const SectionTitle('About the plot', icon: Icons.grass),
                  TextFormField(
                    controller: _code,
                    maxLength: 20,
                    textCapitalization: TextCapitalization.characters,
                    decoration: InputDecoration(labelText: 'Plot code', hintText: 'e.g. P-07', errorText: _serverError('plotCode')),
                    validator: (v) => (v == null || v.trim().isEmpty) ? 'Give the plot a short code, e.g. P-07' : null,
                  ),
                  const SizedBox(height: 4),
                  TextFormField(
                    controller: _name,
                    maxLength: 100,
                    decoration: const InputDecoration(labelText: 'Name (optional)', hintText: 'e.g. Tank field'),
                  ),
                  const SizedBox(height: 4),
                  TextFormField(
                    controller: _area,
                    keyboardType: const TextInputType.numberWithOptions(decimal: true),
                    decoration: InputDecoration(
                      labelText: 'Area (hectares)',
                      suffixText: 'ha',
                      helperText: '1 acre is about 0.4 ha',
                      errorText: _serverError('areaHectares'),
                    ),
                    // The same bounds as the API and the database CHECK constraint.
                    validator: (v) => _decimal(v, min: 0.001, max: 10000, message: 'Enter the area in hectares, more than 0'),
                  ),
                  const SizedBox(height: 16),
                  DropdownButtonFormField<SoilType>(
                    initialValue: _soil,
                    decoration: const InputDecoration(labelText: 'Soil', prefixIcon: Icon(Icons.layers_outlined)),
                    items: [for (final s in SoilType.values) DropdownMenuItem(value: s, child: Text(s.label))],
                    onChanged: (s) => setState(() => _soil = s ?? _soil),
                  ),
                  const SizedBox(height: 28),
                  const SectionTitle('Location', icon: Icons.my_location),
                  Text(
                    'The weather, the harvest days and the nearest collection centre are all worked out from here.',
                    style: theme.textTheme.bodySmall,
                  ),
                  const SizedBox(height: 12),
                  GpsCard(
                    locating: _locating,
                    fix: _fix,
                    onRetry: _locate,
                    message: _fix == null
                        ? 'Location unavailable. Type the coordinates, or try again.'
                        : 'From your phone (within ${_fix!.accuracyMetres.round()} m). Stand in the plot for the best result.',
                  ),
                  const SizedBox(height: 16),
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Expanded(
                        child: TextFormField(
                          controller: _latitude,
                          keyboardType: const TextInputType.numberWithOptions(decimal: true, signed: true),
                          decoration: InputDecoration(labelText: 'Latitude', suffixText: '°', errorText: _serverError('latitude')),
                          validator: (v) => _decimal(v, min: -90, max: 90, message: 'Between -90 and 90'),
                        ),
                      ),
                      const SizedBox(width: 12),
                      Expanded(
                        child: TextFormField(
                          controller: _longitude,
                          keyboardType: const TextInputType.numberWithOptions(decimal: true, signed: true),
                          decoration: InputDecoration(labelText: 'Longitude', suffixText: '°', errorText: _serverError('longitude')),
                          validator: (v) => _decimal(v, min: -180, max: 180, message: 'Between -180 and 180'),
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
            BottomActionBar(
              children: [
                Expanded(
                  child: FilledButton.icon(
                    onPressed: _saving ? null : _save,
                    icon: _saving ? const InlineSpinner() : const Icon(Icons.check),
                    label: const Text('Add plot'),
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
