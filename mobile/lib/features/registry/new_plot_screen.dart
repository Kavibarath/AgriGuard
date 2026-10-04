import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_exception.dart';
import '../../core/location/location_service.dart';
import '../cases/case_widgets.dart';
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
    return Scaffold(
      appBar: AppBar(title: const Text('Add a plot')),
      body: Form(
        key: _formKey,
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            if (_error != null) ...[
              Notice(message: _error!, icon: Icons.error_outline, tone: NoticeTone.error),
              const SizedBox(height: 16),
            ],
            TextFormField(
              controller: _code,
              maxLength: 20,
              textCapitalization: TextCapitalization.characters,
              decoration: InputDecoration(labelText: 'Plot code', hintText: 'e.g. P-07', errorText: _serverError('plotCode')),
              validator: (v) => (v == null || v.trim().isEmpty) ? 'Give the plot a short code, e.g. P-07' : null,
            ),
            TextFormField(
              controller: _name,
              maxLength: 100,
              decoration: const InputDecoration(labelText: 'Name (optional)', hintText: 'e.g. Tank field'),
            ),
            TextFormField(
              controller: _area,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              decoration: InputDecoration(labelText: 'Area (hectares)', helperText: '1 acre is about 0.4 ha', errorText: _serverError('areaHectares')),
              // The same bounds as the API and the database CHECK constraint.
              validator: (v) => _decimal(v, min: 0.001, max: 10000, message: 'Enter the area in hectares, more than 0'),
            ),
            const SizedBox(height: 12),
            DropdownButtonFormField<SoilType>(
              initialValue: _soil,
              decoration: const InputDecoration(labelText: 'Soil'),
              items: [for (final s in SoilType.values) DropdownMenuItem(value: s, child: Text(s.label))],
              onChanged: (s) => setState(() => _soil = s ?? _soil),
            ),
            const SizedBox(height: 20),
            Text('Location', style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 4),
            _LocationLine(locating: _locating, fix: _fix, onRetry: _locate),
            Row(
              children: [
                Expanded(
                  child: TextFormField(
                    controller: _latitude,
                    keyboardType: const TextInputType.numberWithOptions(decimal: true, signed: true),
                    decoration: InputDecoration(labelText: 'Latitude', errorText: _serverError('latitude')),
                    validator: (v) => _decimal(v, min: -90, max: 90, message: 'Between -90 and 90'),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: TextFormField(
                    controller: _longitude,
                    keyboardType: const TextInputType.numberWithOptions(decimal: true, signed: true),
                    decoration: InputDecoration(labelText: 'Longitude', errorText: _serverError('longitude')),
                    validator: (v) => _decimal(v, min: -180, max: 180, message: 'Between -180 and 180'),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 24),
            FilledButton.icon(
              onPressed: _saving ? null : _save,
              icon: _saving
                  ? const SizedBox.square(dimension: 18, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Icon(Icons.check),
              label: const Text('Add plot'),
            ),
          ],
        ),
      ),
    );
  }
}

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
    return Row(
      children: [
        Icon(fix == null ? Icons.location_off_outlined : Icons.my_location, size: 18),
        const SizedBox(width: 8),
        Expanded(
          child: Text(fix == null
              ? 'Location unavailable. Type the coordinates, or try again.'
              : 'From your phone (within ${fix!.accuracyMetres.round()} m). Stand in the plot for the best result.'),
        ),
        TextButton(onPressed: onRetry, child: Text(fix == null ? 'Try again' : 'Update')),
      ],
    );
  }
}
