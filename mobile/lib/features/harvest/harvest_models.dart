/// Mirrors the API's Component D DTOs (HarvestContracts.cs, WeatherContracts.cs).
library;

DateTime _date(Object? value) => DateTime.parse(value! as String);

/// One candidate harvest day, scored from maturity, chemical safety and weather.
class HarvestDay {
  const HarvestDay({required this.date, required this.score, required this.recommended, required this.reasons, this.rainProbabilityPercent});

  factory HarvestDay.fromJson(Map<String, dynamic> json) => HarvestDay(
        date: _date(json['date']),
        score: json['score'] as int,
        recommended: json['recommended'] as bool,
        rainProbabilityPercent: json['rainProbabilityPercent'] as int?,
        reasons: [for (final r in json['reasons'] as List) r as String],
      );

  final DateTime date;
  final int score;
  final bool recommended;
  final int? rainProbabilityPercent;
  final List<String> reasons;
}

class HarvestWindow {
  const HarvestWindow({
    required this.cropCycleId,
    required this.maturityDate,
    required this.summary,
    required this.days,
    required this.forecastAvailable,
    this.safeFromDate,
    this.safetyReason,
  });

  factory HarvestWindow.fromJson(Map<String, dynamic> json) => HarvestWindow(
        cropCycleId: json['cropCycleId'] as String,
        maturityDate: _date(json['maturityDate']),
        safeFromDate: json['safeFromDate'] == null ? null : _date(json['safeFromDate']),
        safetyReason: json['safetyReason'] as String?,
        forecastAvailable: json['forecastAvailable'] as bool,
        summary: json['summary'] as String,
        days: [for (final d in json['days'] as List) HarvestDay.fromJson(d as Map<String, dynamic>)],
      );

  final String cropCycleId;
  final DateTime maturityDate;

  /// Collection before this day is refused: a spray's pre-harvest interval has not cleared.
  final DateTime? safeFromDate;
  final String? safetyReason;
  final bool forecastAvailable;
  final String summary;

  /// Best first.
  final List<HarvestDay> days;

  /// The day to suggest in the date picker: the best-ranked one.
  DateTime? get bestDay => days.isEmpty ? null : days.first.date;
}

/// One day of the spray-window widget: may the farmer spray, and if not, why.
class SprayDay {
  const SprayDay({required this.date, required this.suitable, required this.problems, required this.rainProbabilityPercent, required this.windSpeedKph});

  factory SprayDay.fromJson(Map<String, dynamic> json) => SprayDay(
        date: _date(json['date']),
        suitable: json['suitable'] as bool,
        rainProbabilityPercent: json['rainProbabilityPercent'] as int,
        windSpeedKph: (json['windSpeedKph'] as num).toDouble(),
        problems: [for (final p in json['problems'] as List) p as String],
      );

  final DateTime date;
  final bool suitable;
  final int rainProbabilityPercent;
  final double windSpeedKph;
  final List<String> problems;
}

class SprayWindow {
  const SprayWindow({required this.forecastAvailable, required this.summary, required this.days});

  factory SprayWindow.fromJson(Map<String, dynamic> json) => SprayWindow(
        forecastAvailable: json['forecastAvailable'] as bool,
        summary: json['summary'] as String,
        days: [for (final d in json['days'] as List) SprayDay.fromJson(d as Map<String, dynamic>)],
      );

  final bool forecastAvailable;
  final String summary;
  final List<SprayDay> days;
}

class CollectionBooking {
  const CollectionBooking({
    required this.id,
    required this.bookingNo,
    required this.status,
    required this.centreName,
    required this.slotDate,
    required this.startTime,
    required this.endTime,
    required this.plotCode,
    required this.cropName,
    required this.quantityKg,
    this.actualQuantityKg,
    required this.distanceKm,
  });

  factory CollectionBooking.fromJson(Map<String, dynamic> json) => CollectionBooking(
        id: json['id'] as String,
        bookingNo: json['bookingNo'] as String,
        status: json['status'] as String,
        centreName: json['centreName'] as String,
        slotDate: _date(json['slotDate']),
        startTime: (json['startTime'] as String).substring(0, 5),
        endTime: (json['endTime'] as String).substring(0, 5),
        plotCode: json['plotCode'] as String,
        cropName: json['cropName'] as String,
        quantityKg: (json['quantityKg'] as num).toDouble(),
        actualQuantityKg: (json['actualQuantityKg'] as num?)?.toDouble(),
        distanceKm: (json['distanceKm'] as num).toDouble(),
      );

  final String id;
  final String bookingNo;
  final String status;
  final String centreName;
  final DateTime slotDate;

  /// "07:00".
  final String startTime;
  final String endTime;
  final String plotCode;
  final String cropName;
  final double quantityKg;

  /// Weighed at the centre; null until the delivery is completed.
  final double? actualQuantityKg;
  final double distanceKm;

  /// The collection day is behind us. The server refuses to cancel it (SLOT_PASSED), so the
  /// phone does not offer to.
  bool get dayPassed {
    final now = DateTime.now();
    return slotDate.isBefore(DateTime(now.year, now.month, now.day));
  }

  bool get canCancel => status == 'Booked' && !dayPassed;

  /// "Booked", "Checked in", "Missed"…, or "Day passed" for a booking whose day went by unrecorded.
  String get statusLabel => switch (status) {
        'Booked' when dayPassed => 'Day passed',
        'CheckedIn' => 'Checked in',
        'NoShow' => 'Missed',
        _ => status,
      };
}
