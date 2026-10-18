# Project memory — Donetick CalDAV Bridge

Living document of **why** this codebase looks the way it does. Read before changing architecture,
removing code that looks redundant, or adding dependencies. User-facing documentation lives in
[README.md](README.md); this file is the internal "don't improve this away by mistake" reference.

## What this project is (and isn't)

A narrow, single-user bridge between Donetick's external API (`/eapi/v1/chore`) and the CalDAV
VTODO protocol. It translates tasks so Apple Reminders, Calendar.app, Thunderbird, and friends
can read/write them.

**Not a general-purpose CalDAV server.** It serves exactly one user, exactly one principal, and
only the subset of CalDAV that Apple's clients exercise in practice.

**Not a database.** State lives in memory only. Restart = rebuild from the upstream API.
This is deliberate — see "In-memory cache only" below.

## Stack — non-negotiable

- **.NET 10 LTS** (C#), ASP.NET Core minimal hosting — chosen for LTS window to Nov 2028
- **No ORM.** No database. No persistent store of any kind inside this service.
- **Ical.Net 5** for VTODO parsing and serialisation (undocumented exception surface — wrap broadly; create a `CalendarSerializer` per call, a shared one is not thread-safe)
- **Microsoft.Extensions.Http.Resilience** (Polly) for retry — not a custom retry loop
- **xUnit v3 + FluentAssertions + NSubstitute** for tests, on Microsoft.Testing.Platform (`global.json`); integration tests add WebApplicationFactory, WireMock.Net and a fake `TimeProvider` (see [docs/testing-strategy.md](docs/testing-strategy.md))

If a change requires adding persistence, an ORM, or a second user, stop and ask first —
it is out of scope for this project's stated purpose.

## Architectural decisions — the "why"s that are easy to erase

### In-memory cache only
`ChoreCache` is a `ConcurrentDictionary` seeded by `ChoreSyncService` polling the Donetick API
every `PollIntervalSeconds`. There is intentionally no SQLite, no Redis, no file-backed store.

**Why:** Donetick is the source of truth. Persisting a second copy invites divergence bugs and
adds operational surface (migrations, backups) for a homelab tool. A cold start costs one HTTP
request. That is the right trade.

**Consequence:** UID-to-ID mappings created during a client's session are lost on restart —
see "UID mapping is ephemeral" below.

### UID mapping is ephemeral
When Apple Reminders creates a task, it sends a PUT with a client-generated UUID. We map that
UUID to the Donetick-assigned integer ID so subsequent PUT/DELETE requests on the same URL
resolve correctly. The map lives in `ChoreCache` only.

**Why:** Same as above — no persistence layer. The map rebuilds naturally as Apple re-discovers
tasks via PROPFIND. In practice clients cache the Donetick-issued `donetick-{id}.ics` URL after
the 201 Created response, so the UUID-based path is only hit in the narrow window before that
first sync completes.

**Consequence:** Do NOT add a second, on-disk UID map "for safety". It creates a second source
of truth that will drift from the in-memory one.

### No RRULE emission
Donetick stores recurrence as its own frequency model; we never emit iCalendar `RRULE` in the
VTODO. Completion triggers a server-side advance of `NextDueDate`, and the client re-reads the
resource.

**Why:** Apple's recurring-task handling over CalDAV is quirky — completing a recurring VTODO
sends a *new* VTODO with a new UID and `COMPLETED` status, not a mutation of the existing one
(see `PutHandler.HandleAsync`, the "unknown UID with COMPLETED" branch). Letting the server
own recurrence and refetching after completion is simpler and correct. Translating RRULE both
ways would add a large, fragile surface area for marginal benefit.

**Consequence:** If a VTODO arrives with `RRULE`, it is ignored. Recurrence is set in Donetick.

### GroupByLabel = virtual calendars
When enabled, each Donetick label becomes its own CalDAV calendar collection. Tasks with exactly
one label land in that collection; tasks with zero or multiple labels land in the default
collection.

**Why:** Apple Reminders ignores `CATEGORIES` and `X-APPLE-HASHTAGS` from third-party CalDAV
accounts (iCloud-only). Separate collections is the only mechanism Apple honours for visual
grouping. Two labels on one task would require duplicating it across collections — which Apple
treats as two independent tasks and the user would then complete twice.

**Consequence:** Do not "fix" the 2+-label case by emitting duplicates. The current behaviour
(falls back to default list) is the only sane option given Apple's constraints.

### MOVE returns 403
Dragging a task between lists in Apple Reminders triggers a WebDAV MOVE. The Donetick eAPI
has no endpoint for assigning/removing labels, so MOVE cannot be honoured faithfully.

**Why:** Silently creating + deleting (the obvious "make it work" alternative) strips priority,
recurrence metadata, and history. Rejecting with 403 and surfacing the error is the honest
response. README tells users to manage labels in Donetick.

### Throw, don't swallow, on unknown Donetick response shape
`DonetickApiClient.DeserializeChoreResponse` throws `JsonException` when the response matches
neither the direct array nor the wrapper-object format.

**Why:** Returning `[]` silently would make clients believe the user has no tasks — worst
possible failure mode (data appears deleted). Throwing surfaces the upstream contract change
loudly. Honest failure > silent corruption.

### Constant-time credential comparison
`CalDavAuthenticationHandler` uses `CryptographicOperations.FixedTimeEquals` on UTF-8 bytes,
not `string.Equals`. `!=` short-circuits on the first mismatching byte and leaks the matching
prefix length through timing.

**Caveat (documented in code):** `FixedTimeEquals` still returns early on length mismatch, so
credential length is not hidden. The guarantee is per-byte probing resistance, which is what
matters against an online attacker.

### Bounded ClientTracker
`ClientTracker` caps at 100 entries and prunes least-recently-seen when exceeded.

**Why:** Unknown User-Agents fall into `other-{hash}` buckets. Without a cap, a rotating or
hostile User-Agent header causes unbounded memory growth in a long-running process. 100 is
well above any realistic homelab client count (1–5 devices typical).

### Single-source version
The `<Version>` tag in `DonetickCalDav.csproj` is the only place the version is declared. At
runtime, `Program.cs` reads `AssemblyInformationalVersionAttribute` via reflection. Status page
and structured logs read from that single source.

**Consequence:** Bumping the version means editing **one** line in the csproj and adding an
entry to [CHANGELOG.md](CHANGELOG.md). Do not duplicate the version string elsewhere.

### Options validation at startup
`AppSettings` uses `[OptionsValidator]` (source-generated) + `[ValidateObjectMembers]` for
recursive `DataAnnotations` validation + `ValidateOnStart()`. Misconfiguration fails the host
at boot, not on the first request.

**Why:** A bridge that silently runs with `Donetick__ApiKey=""` returns 401s forever and is
invisible to monitoring. Fail loudly, fail early.

## Domain glossary

| Term | Meaning |
|------|---------|
| **Chore** | A Donetick task (has `Id`, `Name`, `NextDueDate`, `Labels`, `Priority`, `FrequencyMetadata`). Canonical unit. |
| **VTODO** | The iCalendar representation of a Chore served over CalDAV. Not persisted; generated on demand. |
| **CTag** | Per-collection change tag. Bumped when any chore in the collection changes. Lets clients skip full resyncs. |
| **ETag** | Per-resource version tag. Bumped when that specific chore changes. Lets clients detect stale copies. |
| **Virtual calendar** | A CalDAV collection that doesn't correspond to any separate storage — derived from a Donetick label under GroupByLabel mode. |
| **UID mapping** | In-memory map from client-generated UUID (Apple-style) to Donetick integer ID, maintained in `ChoreCache`. Ephemeral by design. |
| **Slug** | URL-safe form of a label name, used in the CalDAV path. E.g. "Private tasks" → `private-tasks`. |
| **eAPI** | Donetick's external REST API at `/eapi/v1/chore`, authenticated via `secretkey` header. |
| **PROPFIND / REPORT / PUT / DELETE** | CalDAV HTTP methods. One handler per method in `CalDav/Handlers/`. |

## Stop conditions — when to ask before acting

Always pause and confirm when a change would:

- Introduce any form of persistence (database, file cache, second UID store)
- Add a dependency not already in the csproj
- Change the Donetick eAPI contract assumptions (what endpoints exist, what they return)
- Alter the VTODO shape in a way that might break already-synced Apple clients
- Expose a second user, multi-tenant behaviour, or role/permission concept
- Add a new public CalDAV method beyond the current minimal set
- Change how credentials are compared or stored
- Weaken startup validation in `AppSettings`
- Touch the MOVE-returns-403 behaviour

Also confirm before: force-pushing, rewriting git history, running destructive DB/file commands,
or making the project accessible from the public internet by default.

## Versioning

- [SemVer 2.0.0](https://semver.org/spec/v2.0.0.html)
- Single source: `<Version>` in [src/DonetickCalDav/DonetickCalDav.csproj](src/DonetickCalDav/DonetickCalDav.csproj)
- Every user-visible change gets a line in [CHANGELOG.md](CHANGELOG.md) under `## [Unreleased]`
- At release: move Unreleased entries under the new version heading and bump the csproj

## Testing

- Approach: [docs/testing-strategy.md](docs/testing-strategy.md) (accepted 2026-10-17). It defines the
  test levels (unit, snapshot, in-process integration, Apple traffic replay, container end-to-end,
  contract tests against a real Donetick, manual Apple acceptance), the CI gates and the roadmap
- Projects: `tests/DonetickCalDav.Tests` (unit and snapshot), `tests/DonetickCalDav.IntegrationTests`
  (the real app in-process against a WireMock.Net Donetick stub, with fake time) and
  `tests/DonetickCalDav.Testing` (shared helpers: test data, `TestTimeProvider`, integration harness).
  Test package versions live in `tests/Directory.Packages.props`
- Run: `dotnet test --solution donetick_caldav.sln`; one level: `dotnet test --project <project>
  --filter-trait "Level=Integration"` (Microsoft.Testing.Platform syntax, not `--filter`)
- Coverage (Release, as in CI): `dotnet test --solution donetick_caldav.sln -c Release --coverage
  --coverage-output-format cobertura --coverage-settings $PWD/tests/coverage/coverage.settings.xml
  --results-directory $PWD/artifacts/test-results`, then `python3 tests/coverage/coverage_report.py
  artifacts/test-results/*.cobertura.xml`. The gate is an 80 % floor plus a ratchet on
  `tests/coverage/baseline.json`; raise the baseline (`--update-baseline`) when coverage rises
- CI: `.github/workflows/ci.yml` runs all of the above on every push; keep it green
- `dotnet test` must pass and compile with zero warnings before any commit
- Production code that waits or schedules uses the registered `TimeProvider` (`Task.Delay(delay,
  timeProvider, ct)`), never a bare `Task.Delay`, so tests can control time
- Mapping logic (VTODO ↔ ChoreLiteRequest, status, priority, recurrence-time preservation) is
  fully unit-tested — that is the riskiest code surface and the easiest to regress
- Integration tests are no longer "intentionally minimal": the Ical.Net 5 upgrade showed that
  wiring, wire-format and concurrency defects slip past unit tests and manual checks
- Approved test dependencies (no need to ask again): xunit.v3, Microsoft.AspNetCore.Mvc.Testing,
  WireMock.Net, Microsoft.Extensions.TimeProvider.Testing
- Snapshot tests use a small in-house helper (`tests/DonetickCalDav.Testing/Snapshots`), not a library.
  Verify was rejected because of its maintenance fee for revenue-generating use (strategy chapter 13);
  do not add a snapshot library without revisiting that decision
- Approve new or changed snapshots with `SNAPSHOT_ACCEPT=1 dotnet test ...` and review the changed
  `.verified` files like code: a snapshot change is a wire-format change. Recorded client sessions go
  into `tests/fixtures/sessions` (docs/testing/apple-recording.md)
- Every defect gets a test before the fix; open findings get characterization tests tagged `KnownIssue`

## MCP servers

None currently required for this project.

## Known intentional absences

These are **choices**, not oversights. Do not "fix" them without a conversation:

- No persistent storage
- No ORM / Entity Framework
- No `RRULE` emission
- No `CATEGORIES` rendering workaround beyond GroupByLabel
- No background job framework (Hangfire, Quartz) — `IHostedService` is sufficient
- No authentication beyond HTTP Basic — single user, expected to run behind HTTPS/Tailscale
- No metrics/OpenTelemetry — structured ILogger is sufficient for the scope
- No multi-user support
- No write path for labels, priority, or recurrence (Donetick eAPI does not expose them)
