/// Plain dates ("2026-09-26") parse as local dates, never shifted by a time zone.
DateTime _date(Object? value) => DateTime.parse(value! as String);
DateTime? _optionalDate(Object? value) => value == null ? null : _date(value);
double _number(Object? value) => (value! as num).toDouble();

class District {
  const District({required this.id, required this.name});

  factory District.fromJson(Map<String, dynamic> json) => District(id: json['id'] as String, name: json['name'] as String);

  final String id;
  final String name;
}

class Crop {
  const Crop({required this.id, required this.name, required this.maturityDays});

  factory Crop.fromJson(Map<String, dynamic> json) =>
      Crop(id: json['id'] as String, name: json['name'] as String, maturityDays: json['maturityDays'] as int);

  final String id;
  final String name;
  final int maturityDays;
}

class Farm {
  const Farm({
    required this.id,
    required this.name,
    this.village,
    required this.districtName,
    required this.plotCount,
    required this.totalAreaHectares,
  });

  factory Farm.fromJson(Map<String, dynamic> json) => Farm(
        id: json['id'] as String,
        name: json['name'] as String,
        village: json['village'] as String?,
        districtName: json['districtName'] as String,
        plotCount: json['plotCount'] as int,
        totalAreaHectares: _number(json['totalAreaHectares']),
      );

  final String id;
  final String name;
  final String? village;
  final String districtName;
  final int plotCount;
  final double totalAreaHectares;
}

/// The crop stages in order (CropStageRules on the server); a cycle only ever moves one step on.
enum CropStage {
  sown('Sown', 'Sown'),
  vegetative('Vegetative', 'Vegetative'),
  flowering('Flowering', 'Flowering'),
  fruitSet('FruitSet', 'Fruit set'),
  preHarvest('PreHarvest', 'Pre-harvest'),
  harvested('Harvested', 'Harvested');

  const CropStage(this.wire, this.label);

  final String wire;
  final String label;

  static CropStage parse(String value) => values.firstWhere((s) => s.wire == value, orElse: () => sown);
}

enum SoilType {
  clay('Clay', 'Clay'),
  loam('Loam', 'Loam'),
  sandyLoam('SandyLoam', 'Sandy loam'),
  siltLoam('SiltLoam', 'Silt loam'),
  laterite('Laterite', 'Laterite'),
  peat('Peat', 'Peat');

  const SoilType(this.wire, this.label);

  final String wire;
  final String label;

  static SoilType parse(String value) => values.firstWhere((s) => s.wire == value, orElse: () => loam);
}

/// What is growing on a plot now, as the plot list shows it.
class ActiveCycle {
  const ActiveCycle({required this.id, required this.cropName, required this.stage, required this.expectedHarvestDate, this.plannedHarvestDate});

  factory ActiveCycle.fromJson(Map<String, dynamic> json) => ActiveCycle(
        id: json['id'] as String,
        cropName: json['cropName'] as String,
        stage: CropStage.parse(json['stage'] as String),
        expectedHarvestDate: _date(json['expectedHarvestDate']),
        plannedHarvestDate: _optionalDate(json['plannedHarvestDate']),
      );

  final String id;
  final String cropName;
  final CropStage stage;
  final DateTime expectedHarvestDate;
  final DateTime? plannedHarvestDate;

  DateTime get harvestDate => plannedHarvestDate ?? expectedHarvestDate;
}

class Plot {
  const Plot({
    required this.id,
    required this.farmId,
    required this.farmName,
    required this.plotCode,
    this.name,
    required this.areaHectares,
    required this.latitude,
    required this.longitude,
    required this.soilType,
    required this.status,
    this.activeCycle,
  });

  factory Plot.fromJson(Map<String, dynamic> json) => Plot(
        id: json['id'] as String,
        farmId: json['farmId'] as String,
        farmName: json['farmName'] as String,
        plotCode: json['plotCode'] as String,
        name: json['name'] as String?,
        areaHectares: _number(json['areaHectares']),
        latitude: _number(json['latitude']),
        longitude: _number(json['longitude']),
        soilType: SoilType.parse(json['soilType'] as String),
        status: json['status'] as String,
        activeCycle: json['activeCycle'] == null ? null : ActiveCycle.fromJson(json['activeCycle'] as Map<String, dynamic>),
      );

  final String id;
  final String farmId;
  final String farmName;
  final String plotCode;
  final String? name;
  final double areaHectares;
  final double latitude;
  final double longitude;
  final SoilType soilType;
  final String status;
  final ActiveCycle? activeCycle;

  String get title => name == null || name!.isEmpty ? plotCode : '$plotCode · $name';
}

class StageTransition {
  const StageTransition({required this.from, required this.to, required this.at, this.note});

  factory StageTransition.fromJson(Map<String, dynamic> json) => StageTransition(
        from: CropStage.parse(json['fromStage'] as String),
        to: CropStage.parse(json['toStage'] as String),
        at: DateTime.parse(json['transitionedAt'] as String),
        note: json['note'] as String?,
      );

  final CropStage from;
  final CropStage to;
  final DateTime at;
  final String? note;
}

/// A crop from sowing to harvest (GET /api/crop-cycles/{id}).
class CropCycle {
  const CropCycle({
    required this.id,
    required this.plotId,
    required this.cropName,
    required this.sownDate,
    required this.stage,
    required this.status,
    required this.harvestDate,
    required this.daysToHarvest,
    required this.allowedNextStages,
    required this.transitions,
  });

  factory CropCycle.fromJson(Map<String, dynamic> json) => CropCycle(
        id: json['id'] as String,
        plotId: json['plotId'] as String,
        cropName: json['cropName'] as String,
        sownDate: _date(json['sownDate']),
        stage: CropStage.parse(json['stage'] as String),
        status: json['status'] as String,
        harvestDate: _date(json['effectiveHarvestDate']),
        daysToHarvest: json['daysToHarvest'] as int,
        allowedNextStages: [for (final s in json['allowedNextStages'] as List) CropStage.parse(s as String)],
        transitions: [for (final t in json['transitions'] as List) StageTransition.fromJson(t as Map<String, dynamic>)],
      );

  final String id;
  final String plotId;
  final String cropName;
  final DateTime sownDate;
  final CropStage stage;
  final String status;

  /// The harvest date pre-harvest intervals are measured against: planned if set, else expected.
  final DateTime harvestDate;
  final int daysToHarvest;

  /// The one stage this crop may move to next; empty once harvested.
  final List<CropStage> allowedNextStages;
  final List<StageTransition> transitions;
}

/// Per product: can it be sprayed today, and if not, why (the V5–V7 view of the plot).
class ProductWindow {
  const ProductWindow({
    required this.productName,
    required this.activeIngredient,
    required this.canSprayToday,
    required this.lastSafeSprayDate,
    required this.applicationsUsed,
    required this.maxApplications,
    this.blockedExplanation,
  });

  factory ProductWindow.fromJson(Map<String, dynamic> json) => ProductWindow(
        productName: json['productName'] as String,
        activeIngredient: json['activeIngredientName'] as String,
        canSprayToday: json['canSprayToday'] as bool,
        lastSafeSprayDate: _date(json['lastSafeSprayDate']),
        applicationsUsed: json['applicationsUsed'] as int,
        maxApplications: json['maxApplicationsPerCycle'] as int,
        blockedExplanation: json['blockedExplanation'] as String?,
      );

  final String productName;
  final String activeIngredient;
  final bool canSprayToday;
  final DateTime lastSafeSprayDate;
  final int applicationsUsed;
  final int maxApplications;
  final String? blockedExplanation;
}

class AppliedTreatment {
  const AppliedTreatment({required this.productName, required this.date, required this.status});

  factory AppliedTreatment.fromJson(Map<String, dynamic> json) => AppliedTreatment(
        productName: json['productName'] as String,
        date: _date(json['applicationDate']),
        status: json['status'] as String,
      );

  final String productName;
  final DateTime date;
  final String status;
}

/// GET /api/plots/{id}/safety-profile: the plot's chemical-safety position. The crop fields are
/// null when nothing is growing.
class SafetyProfile {
  const SafetyProfile({
    this.cropName,
    this.harvestDate,
    this.daysToHarvest,
    required this.applications,
    required this.productWindows,
    this.reEntryClearAt,
  });

  factory SafetyProfile.fromJson(Map<String, dynamic> json) => SafetyProfile(
        cropName: json['cropName'] as String?,
        harvestDate: _optionalDate(json['harvestDate']),
        daysToHarvest: json['daysToHarvest'] as int?,
        applications: [for (final a in json['applications'] as List) AppliedTreatment.fromJson(a as Map<String, dynamic>)],
        productWindows: [for (final w in json['productWindows'] as List) ProductWindow.fromJson(w as Map<String, dynamic>)],
        reEntryClearAt: json['reEntryClearAtUtc'] == null ? null : DateTime.parse(json['reEntryClearAtUtc'] as String),
      );

  final String? cropName;
  final DateTime? harvestDate;
  final int? daysToHarvest;
  final List<AppliedTreatment> applications;
  final List<ProductWindow> productWindows;

  /// When people may go back into the field after the last spray; null when nothing restricts it.
  final DateTime? reEntryClearAt;
}

class NewPlot {
  const NewPlot({
    required this.farmId,
    required this.plotCode,
    this.name,
    required this.areaHectares,
    required this.latitude,
    required this.longitude,
    required this.soilType,
  });

  final String farmId;
  final String plotCode;
  final String? name;
  final double areaHectares;
  final double latitude;
  final double longitude;
  final SoilType soilType;

  Map<String, dynamic> toJson() => {
        'farmId': farmId,
        'plotCode': plotCode,
        'name': name,
        'areaHectares': areaHectares,
        'latitude': latitude,
        'longitude': longitude,
        'soilType': soilType.wire,
      };
}
