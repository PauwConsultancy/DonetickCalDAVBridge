# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Integration test project (`tests/DonetickCalDav.IntegrationTests`): the bridge hosted in-process against a WireMock.Net Donetick stub with a fake clock; shared test helpers in `tests/DonetickCalDav.Testing`
- Integration scenarios IT-01 to IT-16 (discovery, authentication, calendars, reports, GET, create, edit, complete, delete, rejected operations, input limits, Donetick faults, startup, health, concurrency) and characterization tests for open findings; line coverage of hand-written code rises from 46.7 % to 91.5 %
- Test fixtures: WireMock mapping files for the Donetick eAPI and payloads recorded from Donetick v0.1.82, with the provisioning and recording scripts (`tests/e2e`)
- Coverage report and gate (`tests/coverage`): merged Cobertura from all test projects, 80 % floor plus a ratchet
- Snapshot tests with an in-house helper: the ICS matrix (48 approved files) and full HTTP responses for PROPFIND, REPORT, errors and health
- Replay tests (L3): client sessions under `tests/fixtures/sessions` replayed through the integration harness; synthetic Apple Reminders sessions RP-01 to RP-07, a recorded session of the Python `caldav` client, a capture converter and a recording guide (`docs/testing/apple-recording.md`)
- GitHub Actions CI (`.github/workflows/ci.yml`): Release build with warnings as errors, all tests with coverage gate and JUnit summary, vulnerable-package check, and on `main` and tags an image build with a Trivy scan
- Container end-to-end tests (L4, E2E-01 to E2E-11) and contract tests against a real Donetick (L5, CT-01 to CT-09) in `tests/e2e`: pytest with the Python `caldav` client, Docker Compose with a WireMock stub and Donetick built from its release binary, automatic provisioning, and a local backend for machines without Docker; nightly and weekly workflows
- Unit tests on payloads recorded from a real Donetick (U-01), property-based tests with FsCheck (U-02), request-body edge cases (U-03), client-tracker pruning (U-04), and a mutation-testing baseline with Stryker.NET (`tests/mutation`, weekly workflow)

### Changed
- Ical.Net 4.3.1 to 5.2.3 (v4 is out of support); code migrated to the v5 API
- Microsoft.Extensions.Http.Resilience 10.4.0 to 10.11.0
- Test packages: Microsoft.NET.Test.Sdk 18.11.0, xunit.runner.visualstudio 4.0.1, NSubstitute 6.2.0, FluentAssertions 7.2.2 (last Apache-2.0 release)
- Tests moved from xunit 2.9.3 to xunit.v3 4.0.2 on Microsoft.Testing.Platform (`global.json`); test package versions are managed centrally in `tests/Directory.Packages.props`
- `ChoreSyncService` takes its delays from the registered `TimeProvider` (no behaviour change), and `Program` is public for `WebApplicationFactory`
- All package versions are pinned instead of floating ranges (`4.*`, `2.*`)
- VTODO output: `PRODID` is now the bridge's own, the redundant `DURATION` next to `DUE` is no longer emitted (RFC 5545 does not allow both), and several labels are emitted as one `CATEGORIES` line. `DUE`, `DTSTART` and all other properties are unchanged

### Fixed
- `NullReferenceException` during ICS serialisation under concurrent requests: `VTodoMapper` shared one `CalendarSerializer`, whose context is not thread-safe. This was also the cause of the "known flaky" GetHandler test; the annotation is removed and a regression test added

### Documentation
- Added `docs/` with a functional design, a technical design, a findings report and PlantUML diagrams (sources and rendered SVGs), reverse engineered from 0.7.1
- README: link to the design documentation
- Added a testing strategy (`docs/testing-strategy.md`) and an Apple acceptance checklist (`docs/testing/apple-acceptance.md`)
- Findings F-23 to F-30 from the integration tests and a real Donetick: Preserve Scheduled Time cannot parse Donetick's timestamp format, a JSON object without `Res` empties the cache, timestamps outside UTC, DELETE of an already deleted chore, malformed base URL message, chunked PUT over 1 MB, unescaped backslashes in ICS text, scheduled times in the daylight-saving gap

## [0.7.1] — 2026-04-20

### Added
- HTTP resilience via `Microsoft.Extensions.Http.Resilience` (Polly) — retries on transient Donetick API failures with delays of 5s, 15s, 40s, fitting within Apple Reminders' ~60s timeout
- `Allow` header on 403 responses for MOVE, MKCALENDAR, and MKCOL — more RFC-compliant

### Fixed
- `CalendarName`, `BaseUrl`, and `Username` now HTML-encoded on the status page — prevents XSS from config values containing special characters
- `CalendarName` default corrected to `DonetickTasks` (no space) — consistent across code, config, Dockerfile, Unraid template, and docker-compose example

### Documentation
- README: corrected Apple Calendar claim — tasks with a due date also appear in Calendar.app
- README: corrected MOVE description — bridge rejects with 403, task stays in place
- README: removed stale backlog section, added tested-with-Apple disclaimer
- README: replaced ambiguous "server URL" with "Donetick CalDAV Bridge URL"
- Added comments for `CachedChore` immutability requirement, concurrent completion behaviour, slug collision edge case
- Added known-flaky test annotation on `GetHandlerTests`

## [0.7.0] — 2026-04-19

### Changed
- **Credentials are now compared in constant time.** `CalDavAuthenticationHandler` uses
  `CryptographicOperations.FixedTimeEquals` on UTF-8 bytes instead of `string.Equals`, closing
  a prefix-length timing side channel. Length is still observable (documented trade-off).
- **Donetick deserialization fails loudly.** `DonetickApiClient.DeserializeChoreResponse` now
  throws `JsonException` when the upstream response matches neither the direct-array nor the
  wrapper-object shape, instead of silently returning an empty list. A silent empty list made
  clients believe the user had no tasks; the throw surfaces API contract changes immediately.
- **Options validation is strict and runs at startup.** `AppSettings` uses a source-generated
  `[OptionsValidator]` with recursive `[ValidateObjectMembers]` and `ValidateOnStart()`.
  Misconfiguration now fails the host at boot instead of producing silent runtime errors.
  Removed misleading defaults (empty `Username` / `Password` / `BaseUrl`) from the model.
- **VTODO mapping is unified.** `ToCreateRequest` and `ToUpdateRequest` merged into a single
  `VTodoMapper.ToChoreLiteRequest` — both produced identical output.
- **Version is single-sourced.** `Program.cs` reads `AssemblyInformationalVersion` via
  reflection; `<Version>` in the csproj is the only place to edit at release time.
- **VTODO parser exception handling tightened.** `PutHandler.ParseVTodo` catches broadly
  (Ical.Net's exception surface is undocumented) but deliberately re-raises
  `OperationCanceledException` so host shutdown stays responsive.
- **Upgraded to .NET 10 LTS** (from .NET 8). Supported until November 2028.

### Added
- **Request-body size limit on PUT.** `PutHandler` rejects bodies above 1 MB with 413, both via
  `Content-Length` pre-check and a post-read guard for missing/incorrect headers.
- **Bounded `ClientTracker` growth.** Caps at 100 entries and prunes least-recently-seen when
  exceeded, preventing unbounded memory growth from rotating or hostile User-Agent headers.
- **`instructions.md`** — internal project-memory document capturing the *why* behind the
  architectural decisions (in-memory cache, no RRULE, GroupByLabel, MOVE-403, etc.) so future
  changes do not inadvertently undo deliberate choices.
- **This changelog.**

### Documentation
- Added rationale comments on non-obvious code paths: constant-time comparison tradeoff,
  bounded-cache pruning semantics, unknown-response-shape failure mode, ephemeral UID mapping.

## [0.6.0] and earlier

No changelog was maintained before 0.7.0. See `git log` for history.
