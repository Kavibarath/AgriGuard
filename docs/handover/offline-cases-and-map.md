# Study note: offline case reports and the case map (Component B, slice 6)

Three pieces, one idea: a farmer's report must never be lost, and never be stored twice.

1. **API.** `POST /api/cases` takes an optional `clientReference` (a UUID) and `capturedAt`.
2. **Phone.** An outbox (`mobile/lib/features/cases/case_outbox.dart`) keeps reports made
   without a signal and sends them later.
3. **Web.** `/cases` (the list, with a map of the page's cases) and `/cases/:id` (one case, the
   phone's position beside the plot's centre).

## 1. Idempotent reporting (backend and DB)

**The problem.** A phone on a weak signal sends a report. The server stores it, but the reply is
lost. The phone cannot tell "never arrived" from "arrived, reply lost". If it sends again, the
farmer gets two cases, two agent runs, and possibly two prescriptions.

**The fix.** The phone makes a UUID **before the first attempt** and sends it every time. The
server stores it in `crop_cases.client_reference` (migration `CaseClientReferenceAndCapturedAt`),
under a unique index on `(farmer_id, client_reference)`, filtered `WHERE client_reference IS NOT
NULL`. `CaseService.CreateAsync` works in this order:

1. The usual plot and ownership checks.
2. `FindReplayAsync`: if this farmer already used the reference, it returns that case. The
   controller answers **200** with `Idempotent-Replayed: true`, instead of 201.
3. Only then the crop rules, so a report accepted yesterday stays accepted even if the crop was
   harvested since.
4. Insert. If two copies race, both pass step 2, and the unique index rejects the second insert.
   The `DbUpdateException` is caught (`PostgresErrors.IsUniqueViolation(ex, "client_reference")`),
   and the stored case is returned. The database is the real guard; step 2 is only the fast path.
   The test `A_queued_report_and_its_retry_arriving_together_still_make_one_case` fires 4 at once.

Two further rules:

- **Why per farmer?** One account can never collide with, or probe for, another account's
  reference (`Another_farmer_s_reference_is_invisible`).
- **Same reference, different plot?** That is a client bug, not a retry. It gets **422
  `CLIENT_REFERENCE_REUSED`** rather than an unrelated case back.

This is the same pattern as the approval decision's `Idempotency-Key` (see
`approval-transaction.md`). There it is a header; here it is a body field, because the phone stores
the whole request in its queue anyway.

**`capturedAt`.** This is when the farmer pressed "Report problem". `CreatedAt` stays the time the
report arrived. The server keeps `capturedAt` only when the report actually waited (2 minutes or
more), and refuses two cases:

- a time more than 5 minutes in the future, which means a wrong phone clock;
- a time more than 14 days old (`MaxQueuedReportAge`), because symptoms that old would mislead the
  diagnosis.

Both give a 400 on `capturedAt`, so the farmer is told, rather than the data being quietly bent.

## 2. The phone's outbox

- **What is saved.** The `NewCase` JSON (with its reference and capture time), the owner's user id,
  a label ("P-01 · Tomato"), and the photo bytes as separate files. Everything goes in
  `LocalStore`, in the app-private support directory from `path_provider`. It is not a cache
  directory, so Android does not clear it. The queue file is written to a temporary file and then
  renamed, so a phone dying mid-write keeps the old queue. Tokens stay in the Keystore-backed
  `TokenStorage`; the queue holds no secrets.
- **When a report is queued.** Only when `reportCase` fails with `isNetworkError`. A 422 is shown
  on the form as before. Photos cut off after the case was made are queued with the known `caseId`,
  so only the photos are retried and the case is never re-reported.
- **When it is sent** (`CaseOutbox.sync`):
  - when the cases screen opens;
  - when the app comes back to the foreground (`AppLifecycleListener`);
  - on pull-to-refresh;
  - on "Send now";
  - after any successful online report.

  Reports go oldest first. The first transient failure stops the pass, because the rest would fail
  the same way. Transient means no network, a timeout, 401, 429 or 5xx. A second call during a
  pass does nothing.
- **Refused reports.** A 4xx (the crop was harvested, the capture time is too old) is final. The
  item is marked `refused` with the API's message, and it is not retried. The farmer reads why and
  discards it.
- **Owner scoping.** Two farmers can share a phone. Only the owner's items are sent, shown
  (`myPendingCasesProvider`) or discarded. Another farmer who signs in never submits someone
  else's report under their own account.
- **The form works offline.** The plot list and the symptom checklist are saved after each
  successful load (`_cachedOnline` in `case_providers.dart`, with plots saved per user). Only a
  *network* failure falls back to the saved copy. A 403 or 500 is still shown, so an old copy never
  hides a real problem. GPS works without a signal.

## 3. The web pages and the map

- `/cases` has filters (status, severity, crop, search), sorting and the page number, all in the
  URL. The default is newest first. The map shows the page's cases, coloured by severity, and each
  pin links to its case.
- `/cases/:id` shows:
  - the offline banner ("Sent from the phone's offline queue … after waiting 2 h");
  - the map, with the phone as a filled dot and the plot's registered centre as a ring;
  - the haversine distance between them. Over 1 km gives a warning to check that it is the right
    plot before approving a spray for it;
  - the symptoms, and the note as plain text (the prompt-injection surface);
  - photos, runs and advice;
  - the two hand changes: "Take into manual review" for an agronomist or administrator, and "Close
    case" behind a confirmation, because a closed case cannot be reopened. The server's
    `CaseStatusRules` still decides.
- For this, the API added `reportedLatitude/Longitude` to the case list and
  `plotLatitude/Longitude` and `capturedAt` to the detail.

**Why no Leaflet?** It was the plan, but `npm install` failed on this network. A Fortinet firewall
re-signs TLS, and npm rightly refused the certificate. Turning off certificate checks was not an
option. The browser loads OpenStreetMap tiles fine, so the map is hand-rolled in about 150 lines
(`web/src/components/map/TileMap.tsx`, with `web/src/lib/geo.ts`):

- **Web Mercator.** At zoom z the world is 256·2^z pixels square.
  x = (lng+180)/360 · size, and y = (½ − ln((1+sin φ)/(1−sin φ))/4π) · size.
  Tile (x, y) covers pixels [256x, 256x+256).
- **`fitZoom`.** Try zoom levels from the cap downwards until every pin fits inside the box less a
  margin. The cap is 13: closer in, hill-country tiles are often blank.
- **Drawing.** The tiles that overlap the viewport are drawn as absolutely positioned `<img>`s,
  with pins on top. There are + and − buttons, and drag to pan. The pan is scaled by 2^Δ on zoom,
  so the same spot stays centred.
- **Tile use.** OSM attribution is shown, as their tile policy requires. Only tile requests leave
  the browser.

A unit test pins the projection to a real tile: Nuwara Eliya is tile 12/2967/1968.

## Questions to expect

- *"What if the first POST did arrive?"* The retry carries the same reference, so the server
  returns the stored case with 200 and `Idempotent-Replayed`. One case exists.
- *"Two retries at the same instant?"* Both miss the lookup. The unique index lets one insert win.
  The other catches the violation and returns the winner's case.
- *"Why not let the server make the id?"* The server's id comes back in the reply, which is exactly
  what was lost. Only an id made before sending survives a lost reply.
- *"Why keep a refused report instead of dropping it?"* The farmer must learn it did not go. Silent
  loss is the one outcome the queue exists to prevent.
- *"Is the map a security risk?"* Tiles are public images. The page sends OSM only tile
  coordinates, never case data.

## Practice changes

- Show the number of waiting reports as a badge on the home screen's "My crop problems" tile
  (`myPendingCasesProvider.length`).
- Add a `Retry-After`-aware backoff: on 429, skip syncing until the given time.
- Add a "Needs attention" filter on `/cases` for reports more than 1 km from their plot. This
  needs the distance in SQL, or a filter on the client for the current page.
