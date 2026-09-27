// Mirrors the API's case, plot and agent-run DTOs that the farmer's screens use.
// Enums arrive as names; unknown values fall back rather than crash an older app build.

enum CaseStatus {
  submitted('Submitted', 'Submitted'),
  agentProcessing('AgentProcessing', 'AI is analysing'),
  pendingApproval('PendingApproval', 'Awaiting agronomist review'),
  prescribed('Prescribed', 'Prescribed'),
  rejected('Rejected', 'Not prescribed'),
  awaitingManualReview('AwaitingManualReview', 'With an agronomist'),
  closed('Closed', 'Closed');

  const CaseStatus(this.wireName, this.label);

  final String wireName;

  /// Worded for the farmer: what is happening, not the workflow's internal name.
  final String label;

  static CaseStatus fromWire(String value) =>
      values.firstWhere((s) => s.wireName == value, orElse: () => CaseStatus.submitted);

  /// Someone (the agent or an agronomist) is working on it: worth refreshing on screen.
  bool get inProgress => this == agentProcessing || this == pendingApproval;

  /// The farmer may ask for AI advice from here (CaseStatusRules.CanStartAgentRun).
  bool get canRequestAdvice => this == submitted || this == awaitingManualReview;
}

enum CaseSeverity {
  low('Low'),
  medium('Medium'),
  high('High'),
  critical('Critical');

  const CaseSeverity(this.wireName);

  final String wireName;

  static CaseSeverity fromWire(String value) =>
      values.firstWhere((s) => s.wireName == value, orElse: () => CaseSeverity.medium);
}

class Symptom {
  const Symptom({required this.code, required this.label});

  factory Symptom.fromJson(Map<String, dynamic> json) =>
      Symptom(code: json['code'] as String, label: json['label'] as String);

  final String code;
  final String label;
}

/// A plot the farmer can report on: only ones with a crop growing now.
class ReportablePlot {
  const ReportablePlot({
    required this.plotId,
    required this.plotCode,
    required this.farmName,
    required this.cropCycleId,
    required this.cropName,
    required this.stage,
    required this.latitude,
    required this.longitude,
  });

  /// Null when the plot has no active crop cycle — there is nothing to report against.
  static ReportablePlot? fromPlotJson(Map<String, dynamic> json) {
    final cycle = json['activeCycle'] as Map<String, dynamic>?;
    if (cycle == null) return null;
    return ReportablePlot(
      plotId: json['id'] as String,
      plotCode: json['plotCode'] as String,
      farmName: json['farmName'] as String,
      cropCycleId: cycle['id'] as String,
      cropName: cycle['cropName'] as String,
      stage: cycle['stage'] as String,
      latitude: (json['latitude'] as num).toDouble(),
      longitude: (json['longitude'] as num).toDouble(),
    );
  }

  final String plotId;
  final String plotCode;
  final String farmName;
  final String cropCycleId;
  final String cropName;
  final String stage;
  final double latitude;
  final double longitude;
}

class CaseSummary {
  const CaseSummary({
    required this.id,
    required this.referenceNo,
    required this.status,
    required this.severity,
    required this.plotCode,
    required this.cropName,
    required this.createdAt,
  });

  factory CaseSummary.fromJson(Map<String, dynamic> json) => CaseSummary(
        id: json['id'] as String,
        referenceNo: json['referenceNo'] as String,
        status: CaseStatus.fromWire(json['status'] as String),
        severity: CaseSeverity.fromWire(json['severity'] as String),
        plotCode: json['plotCode'] as String,
        cropName: json['cropName'] as String,
        createdAt: DateTime.parse(json['createdAt'] as String),
      );

  final String id;
  final String referenceNo;
  final CaseStatus status;
  final CaseSeverity severity;
  final String plotCode;
  final String cropName;
  final DateTime createdAt;
}

class RunSummary {
  const RunSummary({required this.id, required this.status, this.failureReason});

  factory RunSummary.fromJson(Map<String, dynamic> json) => RunSummary(
        id: json['id'] as String,
        status: json['status'] as String,
        failureReason: json['failureReason'] as String?,
      );

  final String id;
  final String status;
  final String? failureReason;
}

class CaseDetail {
  const CaseDetail({
    required this.id,
    required this.referenceNo,
    required this.status,
    required this.severity,
    required this.plotCode,
    required this.cropName,
    required this.stage,
    required this.symptoms,
    required this.farmerNote,
    required this.createdAt,
    required this.runs,
  });

  factory CaseDetail.fromJson(Map<String, dynamic> json) => CaseDetail(
        id: json['id'] as String,
        referenceNo: json['referenceNo'] as String,
        status: CaseStatus.fromWire(json['status'] as String),
        severity: CaseSeverity.fromWire(json['severity'] as String),
        plotCode: json['plotCode'] as String,
        cropName: json['cropName'] as String,
        stage: json['stage'] as String,
        symptoms: [
          for (final s in json['symptoms'] as List) Symptom.fromJson(s as Map<String, dynamic>),
        ],
        farmerNote: json['farmerNote'] as String?,
        createdAt: DateTime.parse(json['createdAt'] as String),
        runs: [
          for (final r in json['agentRuns'] as List) RunSummary.fromJson(r as Map<String, dynamic>),
        ],
      );

  final String id;
  final String referenceNo;
  final CaseStatus status;
  final CaseSeverity severity;
  final String plotCode;
  final String cropName;
  final String stage;
  final List<Symptom> symptoms;
  final String? farmerNote;
  final DateTime createdAt;

  /// Newest first, as the API returns them.
  final List<RunSummary> runs;

  RunSummary? get latestRun => runs.isEmpty ? null : runs.first;
}

/// The approved treatment, as the farmer acts on it.
class Prescription {
  const Prescription({
    required this.prescriptionNo,
    required this.productName,
    required this.dosePerHectare,
    required this.totalQuantity,
    required this.sprayDate,
    required this.earliestSafeHarvestDate,
    required this.instructions,
    required this.orderNo,
    required this.dealerName,
    required this.packs,
    required this.orderTotal,
  });

  factory Prescription.fromJson(Map<String, dynamic> json) => Prescription(
        prescriptionNo: json['prescriptionNo'] as String,
        productName: json['productName'] as String,
        dosePerHectare: (json['dosePerHectare'] as num).toDouble(),
        totalQuantity: (json['totalQuantity'] as num).toDouble(),
        sprayDate: DateTime.parse(json['sprayDate'] as String),
        earliestSafeHarvestDate: DateTime.parse(json['earliestSafeHarvestDate'] as String),
        instructions: json['instructions'] as String?,
        orderNo: json['orderNo'] as String,
        dealerName: json['dealerName'] as String,
        packs: json['packs'] as int,
        orderTotal: (json['orderTotal'] as num).toDouble(),
      );

  final String prescriptionNo;
  final String productName;
  final double dosePerHectare;
  final double totalQuantity;
  final DateTime sprayDate;
  final DateTime earliestSafeHarvestDate;
  final String? instructions;
  final String orderNo;
  final String dealerName;
  final int packs;
  final double orderTotal;
}

/// What the farmer sends to report a problem.
class NewCase {
  const NewCase({
    required this.plotId,
    required this.cropCycleId,
    required this.symptomCodes,
    required this.severity,
    required this.latitude,
    required this.longitude,
    this.farmerNote,
  });

  final String plotId;
  final String cropCycleId;
  final List<String> symptomCodes;
  final CaseSeverity severity;
  final double latitude;
  final double longitude;
  final String? farmerNote;

  Map<String, dynamic> toJson() => {
        'plotId': plotId,
        'cropCycleId': cropCycleId,
        'symptomCodes': symptomCodes,
        'severity': severity.wireName,
        'latitude': latitude,
        'longitude': longitude,
        'farmerNote': (farmerNote?.trim().isEmpty ?? true) ? null : farmerNote!.trim(),
      };
}
