# Component ownership

Which files make up each student's component (plan §1.2), for the report, the individual
sections and viva preparation. Paths are relative to the repository root. Migrations are named
without their timestamp prefix.

| Student | Component | Non-CRUD operations | Agent |
|---|---|---|---|
| A | Farm, Plot & Crop-Cycle Registry | Guarded crop-cycle stage transition; plot safety-profile computation; deterministic prescription validator (V1–V11) | Validation & Safety |
| B | Crop Health Case Management & Agentic Workflow | Agent-run initiation; approve / reject / revise decision (idempotent, concurrency-guarded) | Coordinator / Planner |
| C | Agro-Input Catalogue, Dealer Inventory & Orders | Transactional stock reservation with expiry and commit/release rollback; order fulfilment | Action / Prescription & Procurement |
| D | Harvest Windows, Collection Logistics & Regional Intelligence | Safe harvest window (maturity ∩ PHI ∩ weather); capacity-constrained slot allocation; regional outbreak signal | Diagnosis / Field-Intelligence |

The four agents share one LangGraph graph (`agent/app/graph.py`). Each agent is its own node
function and has its own system prompt in `agent/app/prompts.py` and its own tool allow-list in
`agent/app/tools.py`. Ownership below is by node, not by file.

---

## A — Registry, safety profile and the prescription validator

**Domain**
- `backend/src/AgriGuard.Domain/Registry/Registry.cs`
- `backend/src/AgriGuard.Domain/Registry/CropStageRules.cs`: the guarded stage transition
- `backend/src/AgriGuard.Domain/Registry/SafetyProfile.cs`: the safety-profile computation
- `backend/src/AgriGuard.Domain/Validation/PrescriptionSafetyValidator.cs`: V1–V11
- `backend/src/AgriGuard.Domain/Validation/PrescriptionValidationContracts.cs`

**Application, Infrastructure and API**
- `backend/src/AgriGuard.Application/Registry/RegistryContracts.cs`, `SafetyProfileContracts.cs`
- `backend/src/AgriGuard.Application/Validation/ValidationContracts.cs`
- `backend/src/AgriGuard.Infrastructure/Registry/` (CropCycleService, FarmService, PlotService, SafetyProfileService, RegistryScope, QueryableExtensions)
- `backend/src/AgriGuard.Infrastructure/Validation/PrescriptionValidationService.cs`
- `backend/src/AgriGuard.Infrastructure/Reports/RegistryReportService.cs`
- `backend/src/AgriGuard.Api/Controllers/FarmsController.cs`, `PlotsController.cs`, `CropCyclesController.cs`, `PrescriptionValidationController.cs`
- `backend/src/AgriGuard.Api/Validation/RegistryValidators.cs`

**Database**
- `backend/src/AgriGuard.Infrastructure/Persistence/Configurations/RegistryConfigurations.cs`

**Agent: the Validation & Safety node**
- `agent/app/graph.py`: `validation`, `review_verdict`, `record_review`, `route_after_validation`, `describe_limits`
- `agent/app/prompts.py`: `VALIDATION_SYSTEM`
- `agent/tests/test_validation_agent.py`

**React**
- `web/src/features/registry/` (FarmsPage, FarmDetailPage, PlotDetailPage, FarmFormModal, PlotFormModal, CropCyclePanel, SafetyProfilePanel, PhiCalendar, TreatmentHistory, RegistryTiles, safety.ts, api, queries, types and their tests)

**Flutter**
- `mobile/lib/features/registry/` (farms_screen, new_plot_screen, plot_screen, registry_models, registry_repository)
- `mobile/test/registry_screens_test.dart`

**Tests**
- Unit: `backend/tests/AgriGuard.UnitTests/Registry/`, `Validation/PrescriptionSafetyValidatorTests.cs`
- Integration: `FarmsEndpointsTests.cs`, `PlotsEndpointsTests.cs`, `CropCycleEndpointsTests.cs`, `SafetyProfileEndpointTests.cs`, `PrescriptionValidationTests.cs`, `Infrastructure/RegistryFixtures.cs`

**Study notes:** `docs/handover/prescription-validator.md`, `registry-mobile.md`, `plot-safety-web.md`

---

## B — Cases, agent runs and the approval decision

**Domain**
- `backend/src/AgriGuard.Domain/Cases/Cases.cs`, `AgentRuns.cs`, `CaseStatusRules.cs`

**Application, Infrastructure and API**
- `backend/src/AgriGuard.Application/Cases/CaseContracts.cs`, `ApprovalContracts.cs`
- `backend/src/AgriGuard.Application/Agent/AgentContracts.cs`
- `backend/src/AgriGuard.Infrastructure/Cases/` (CaseService, AgentRunService, ApprovalService (the approval transaction), IssuedPrescriptions, CasePhotoService, CaseScope)
- `backend/src/AgriGuard.Infrastructure/Agent/` (AgentRunLifecycle, AgentCallbackService, AgentPrescriptionGate, AgentRunTimeoutSweeper, AgentToolService, HttpAgentDispatcher, AgentPayloads, AgentServiceOptions)
- `backend/src/AgriGuard.Infrastructure/Reports/CaseReportService.cs`
- `backend/src/AgriGuard.Api/Controllers/CasesController.cs`, `AgentRunsController.cs`, `InternalAgentRunsController.cs`, `InternalToolsController.cs`
- `backend/src/AgriGuard.Api/Authentication/AgentKeyAuthentication.cs`: X-Agent-Key
- `backend/src/AgriGuard.Api/Validation/CaseValidators.cs`

**Database**
- `backend/src/AgriGuard.Infrastructure/Persistence/Configurations/CaseConfigurations.cs`
- Migrations: `CaseReferenceSequence`, `PrescriptionAndOrderSequences`, `CasePhotoUniqueHash`, `AgentRunFarmerAdvice`, `CaseClientReferenceAndCapturedAt`
- `backend/src/AgriGuard.Infrastructure/Persistence/SerializableTransaction.cs`, `Sequences.cs`

**Agent: the Coordinator / Planner node, and the graph**
- `agent/app/graph.py`: `coordinator`, `triage`, `route_after_triage`, `escalated`, the revise loop (`increment_revision`, `approved`, `rejected`, `exhausted`), `build_graph`
- `agent/app/runner.py`, `agent/app/main.py`, `agent/app/contracts.py`
- `agent/app/prompts.py`: `COORDINATOR_SYSTEM`, `TRIAGE_SYSTEM`, plus the injection guard (`sanitise_farmer_note`, `fence`)
- `agent/tests/test_coordinator_triage.py`, `test_graph.py`, `test_prompt_injection.py`

**React**
- `web/src/features/agent-runs/` (AgentRunsPage, AgentRunPage, DecisionPanel, ProposalCard, VerdictCard, TriageCard, RunSteps, RunTimeline, CasePhotos, agents, agent-identity, api, queries, types, format and their tests)
- `web/src/features/cases/` (CasesPage, CaseDetailPage, CaseHistory, format and tests)
- `web/src/components/map/TileMap.tsx`, `web/src/lib/geo.ts` (+ test)

**Flutter**
- `mobile/lib/features/cases/` (cases_screen, new_case_screen, case_detail_screen, case_widgets, case_photos, case_outbox, outbox_banner, gps_card, case_models, case_providers, case_repository)
- `mobile/lib/core/location/location_service.dart`, `core/photos/photo_source.dart`, `core/storage/local_store.dart`
- `mobile/test/case_screens_test.dart`, `case_outbox_test.dart`

**Tests**
- Unit: `backend/tests/AgriGuard.UnitTests/Cases/`
- Integration: `CasesEndpointsTests.cs`, `CasePhotoTests.cs`, `OfflineCaseReportTests.cs`, `AgentRunEndpointsTests.cs`, `InternalAgentApiTests.cs`, `ApprovalDecisionTests.cs`, `Infrastructure/CaseFixtures.cs`, `Infrastructure/FakeAgentDispatcher.cs`

**Study notes:** `docs/handover/approval-transaction.md`, `coordinator-triage.md`, `offline-cases-and-map.md`

---

## C — Catalogue, dealer inventory, reservations and orders

**Domain**
- `backend/src/AgriGuard.Domain/Inventory/` (Catalogue, Stock, StockLevel, StockAllocation, BatchExpiry, OrderStatusRules, PickupCodes)

**Application, Infrastructure and API**
- `backend/src/AgriGuard.Application/Inventory/InventoryContracts.cs`
- `backend/src/AgriGuard.Infrastructure/Inventory/` (ProductService, ProductCropApprovalService, InventoryService, ReservationService, ReservationExpirySweeper, ProposalStockHolds, StockLedger, OrderService, DealerScope)
- `backend/src/AgriGuard.Infrastructure/Reports/StockReportService.cs`
- `backend/src/AgriGuard.Api/Controllers/ProductsController.cs`, `ProductCropApprovalsController.cs`, `InventoryController.cs`, `OrdersController.cs`, `FarmerOrdersController.cs`
- `backend/src/AgriGuard.Api/Validation/InventoryValidators.cs`

**Database**
- `backend/src/AgriGuard.Infrastructure/Persistence/Configurations/InventoryConfigurations.cs`
- Migrations: `OrderPackedAtAndReservationNote`, `InputOrderPickupCode`
- `backend/src/AgriGuard.Infrastructure/Persistence/Seed/DemoInventorySeeder.cs`

**Agent: the Action / Prescription & Procurement node**
- `agent/app/graph.py`: `action`
- `agent/app/prompts.py`: `ACTION_SYSTEM`

**React**
- `web/src/features/inventory/` (InventoryPage, OrdersPage, BatchFormModal, HoldStockModal, HoldsPanel, HandOverModal, DealerPanels, api, queries, types, format and tests)
- `web/src/features/rules/` (RulesPage, RuleFormModal and test): the product–crop approval table that V2–V7 read

**Flutter**
- `mobile/lib/features/orders/` (orders, orders_screen)
- `mobile/test/orders_screen_test.dart`

**Tests**
- Unit: `backend/tests/AgriGuard.UnitTests/Inventory/`
- Integration: `CatalogueAndRulesTests.cs`, `InventoryAndOrderTests.cs`, `ReservationTests.cs`, `Infrastructure/InventoryFixtures.cs`

**Study note:** `docs/handover/rules-and-stock.md`

---

## D — Weather, harvest windows, collection and regional intelligence

**Domain**
- `backend/src/AgriGuard.Domain/Harvest/` (Harvest, HarvestWindows, SlotAllocation, SprayWeather, ForecastAccuracy)
- `backend/src/AgriGuard.Domain/Intelligence/OutbreakSignal.cs`

**Application, Infrastructure and API**
- `backend/src/AgriGuard.Application/Harvest/HarvestContracts.cs`, `Intelligence/IntelligenceContracts.cs`, `Weather/WeatherContracts.cs`
- `backend/src/AgriGuard.Infrastructure/Harvest/` (HarvestWindowService, HarvestForecastService, CollectionService, HarvestScope)
- `backend/src/AgriGuard.Infrastructure/Weather/` (OpenMeteoClient, WeatherService, SprayWindowService)
- `backend/src/AgriGuard.Infrastructure/Intelligence/OutbreakSignalService.cs`
- `backend/src/AgriGuard.Infrastructure/Reports/HarvestReportService.cs`
- `backend/src/AgriGuard.Api/Controllers/HarvestController.cs`, `CollectionController.cs`, `IntelligenceController.cs`, `WeatherController.cs`, `ReportsController.cs`
- `backend/src/AgriGuard.Api/Validation/HarvestValidators.cs`

**Database**
- `backend/src/AgriGuard.Infrastructure/Persistence/Configurations/HarvestConfigurations.cs`
- Migration: `CollectionBookingSequence`
- `backend/src/AgriGuard.Infrastructure/Persistence/Seed/DemoCollectionSeeder.cs`, `DemoDryZoneSeeder.cs`, `DemoHistorySeeder.cs`

**Agent: the Diagnosis / Field-Intelligence node**
- `agent/app/graph.py`: `diagnosis`, `primary_confidence`, `describe_recent_weather`
- `agent/app/prompts.py`: `DIAGNOSIS_SYSTEM`

**React**
- `web/src/features/harvest/` (HarvestPage, CollectionPlannerPage, SlotFormModal, RecordActualModal, SprayWindowPanel, api, queries, types, format and tests)
- `web/src/features/intelligence/` (IntelligencePage, api, queries, types and test)
- `web/src/components/charts/`

**Flutter**
- `mobile/lib/features/harvest/` (harvest_screen, harvest_models, harvest_repository)
- `mobile/test/harvest_screen_test.dart`

**Tests**
- Unit: `backend/tests/AgriGuard.UnitTests/Harvest/`, `Intelligence/`
- Integration: `HarvestAndCollectionTests.cs`, `WeatherTests.cs`, `IntelligenceAndReportsTests.cs`, `Infrastructure/FakeWeatherProvider.cs`, `Infrastructure/WeatherFixtures.cs`

**Study notes:** `docs/handover/weather-and-v8.md`, `harvest-and-collection.md`, `outbreak-and-reports.md`

---

## Shared: group work, not one student's

- **Identity and security:** `Domain/Identity`, `Infrastructure/Identity` (JWT, PBKDF2), `AuthController`, `Api/Authentication/AuthenticationSetup.cs`, `Api/Authorization`, `Application/Auth`. Tests: `AuthenticationTests`, `AuthorizationPolicyTests`, `TokenClaimsTests`, `UnitTests/Identity`. Study note: `docs/handover/jwt-auth.md`.
- **API foundation:** `Program.cs`, `Api/Infrastructure` (exception handler, correlation id, rate limits, health), `ValidationFilter.cs`, `Application/Common` (paging, `FarmCalendar`, exceptions), `Domain/Common`, `Domain/Reference`, `ReferenceController`, `DependencyInjection.cs`. Tests: `ApiFoundationTests`, `ReferenceEndpointsTests`, `UnitTests/Common`.
- **Database base:** `AgriGuardDbContext.cs`, `IdentityConfigurations.cs`, `InitialSchema`, `PostgresErrors.cs`, `ReferenceDataSeeder.cs`, `DemoUserSeeder.cs`.
- **Agent service base:** `agent/app/config.py`, `llm.py`, `tools.py` (the client and allow-lists), `agent/tests/conftest.py`.
- **Web shell and design:** `web/src/app`, `components/` (ui, layout, icons, motion, spotlight, carousel), `features/auth`, `features/dashboard`, `features/home`, `features/design-system`, `lib/` (api, dates, form, motion, use-hide-on-scroll, utils), `index.css`, `test/`.
- **Phone shell and design:** `mobile/lib/app`, `core/api`, `core/config.dart`, `core/storage/token_storage.dart`, `features/auth`, `features/home`, `main.dart`, and the auth, login and formatter tests.
- **Delivery:** `.github/workflows`, `docker-compose.yml`, `docs/design`, `docs/PROJECT-PLAN.md`.
