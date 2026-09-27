import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:geolocator/geolocator.dart';

/// Where the phone is, and how sure it is (metres).
class GeoFix {
  const GeoFix({required this.latitude, required this.longitude, required this.accuracyMetres});

  final double latitude;
  final double longitude;
  final double accuracyMetres;
}

/// The phone's location, behind an interface so screens and tests do not depend on the GPS.
abstract class LocationService {
  /// A current fix, or null when location is off, refused, or too slow. Never throws: a farmer
  /// in a valley with no signal must still be able to report a problem.
  Future<GeoFix?> currentFix();
}

class GeolocatorLocationService implements LocationService {
  /// Long enough for a cold GPS start in the open, short enough not to hold up the report.
  static const _timeout = Duration(seconds: 15);

  @override
  Future<GeoFix?> currentFix() async {
    try {
      if (!await Geolocator.isLocationServiceEnabled()) return null;

      var permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied) {
        permission = await Geolocator.requestPermission();
      }
      if (permission == LocationPermission.denied || permission == LocationPermission.deniedForever) {
        return null;
      }

      final position = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(accuracy: LocationAccuracy.high, timeLimit: _timeout),
      );
      return GeoFix(
        latitude: position.latitude,
        longitude: position.longitude,
        accuracyMetres: position.accuracy,
      );
    } catch (_) {
      // Timeout, platform error: fall back to the plot's location rather than block the report.
      return null;
    }
  }
}

final locationServiceProvider = Provider<LocationService>((ref) => GeolocatorLocationService());
