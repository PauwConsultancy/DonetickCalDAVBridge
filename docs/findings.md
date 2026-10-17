# Donetick CalDAV Bridge - Findings
Review of version 0.7.1, based on the reverse engineering for the [functional design](functional-design.md) and [technical design](technical-design.md) on 2026-10-17. Those documents describe the bridge as it is; this document evaluates it: defects, risks, improvement proposals and project documentation that no longer matches the code.

## Summary
The design fits its stated scope (one user, a few devices, private network), and the riskiest logic, the VTODO mapping, is well covered by unit tests. The issues worth addressing first, in order of impact:

1. **Double completion risk.** A failed cache reload after a successful completion is reported as 502, and the HTTP retry policy also retries non-idempotent POSTs (create, complete). Both can make a client action count twice in Donetick (F-01, F-02).
2. **Change detection gaps.** The task ETag ignores description, labels and priority, and `sync-collection` never reports deletions while a `sync-token` is advertised (F-03, F-04).
3. **All-day dates use the UTC date**, so chores due shortly after local midnight show on the previous day (F-05).
4. **Smaller items:** unauthenticated status endpoints reveal configuration, request bodies for XML methods are read without an explicit limit, and the container runs as root without a health check (F-06, F-07, F-18).

Eight places in the existing project documentation and code comments no longer match the code (chapter 4).

## 1 Scope and method
All findings come from reading the code, tests, Dockerfile, templates and project notes; nothing was verified against a running Donetick instance or Apple client. Findings that depend on Donetick behaviour are marked **Assumption**.

Severity reflects the impact for the intended single-user, private-network use:

| Severity | Meaning |
|----------|---------|
| High | Can duplicate or corrupt data in Donetick |
| Medium | Shows wrong or stale information, or exposes information on the network |
| Low | Edge case, robustness or standards deviation without visible effect in normal use |
| Info | Housekeeping |

When a finding is resolved, remove its row and mention the fix in `CHANGELOG.md`. Findings that will be picked up can be moved to `backlog.md`.

## 2 Behaviour and robustness

| ID | Severity | Finding | Suggested direction |
|----|----------|---------|---------------------|
| F-01 | High | `PutHandler.HandleUpdate` completes the chore, then reloads all chores in the same `try`. If the reload fails, the client gets 502 although the completion succeeded, and a client retry completes the chore again. A `JsonException` from the reload is not caught at all and becomes a 500. | Treat the reload as best effort: catch its failure, invalidate the entry or trigger a background sync, and still return 204. |
| F-02 | High | The retry pipeline retries every method, including `POST /chore` and `POST /chore/{id}/complete`. A connection reset or 5xx after Donetick has applied the change leads to a duplicate chore or a double completion. | Restrict retries to idempotent methods (GET, PUT, DELETE), or disable retry for POST. |
| F-03 | Medium | `sync-collection` ignores the client token and never reports removed members, while collections advertise a `sync-token` property. The token formats also differ (`http://donetick-caldav/sync/{ctag}` in PROPFIND, the raw quoted CTag in REPORT). The report is not listed in `supported-report-set`, which probably keeps Apple on the CTag path. | Remove the `sync-token` property, or implement RFC 6578 properly (for example answer an unknown token with `valid-sync-token` so clients fall back to a full sync). |
| F-04 | Medium | The ETag covers `Id`, `UpdatedAt`, `Status`, `IsActive`, `Name` and `NextDueDate` only. **Assumption:** if Donetick does not bump `updatedAt` when description, labels or priority change, clients keep stale notes, categories and priority. | Add description, priority, label names and frequency metadata to the hash; the cost is negligible. |
| F-05 | Medium | With `AllDayEvents`, the date is taken from the UTC value. A chore due at 00:30 in Europe/Amsterdam (22:30 or 23:30 UTC the day before) is shown on the previous day. | Convert to the chore's `frequencyMetadata.timezone`, or a configurable time zone, before taking the date. |
| F-06 | Medium | `/`, `/health` and `/health/json` are anonymous and expose the Donetick URL, the CalDAV username, the calendar name and client types. | Reduce the anonymous output to status and version, or put details behind authentication or a setting. |
| F-07 | Low | XML request bodies are read with `ReadToEndAsync` without an explicit limit (Kestrel's 30 MB default applies), contrary to the project convention. Bodies without `Content-Length` (chunked) are treated as empty. | Apply the same bounded read as PUT, and read chunked bodies. |
| F-08 | Low | No precondition handling (`If-Match`, `If-None-Match: *`) on PUT and DELETE; concurrent edits are last writer wins. | Compare `If-Match` with the cached ETag and answer 412 on mismatch. |
| F-09 | Low | Null fields are omitted from `ChoreLiteRequest`, so clearing notes or the due date in a client is not propagated, and the time of day of `DUE` is dropped. | Confirm the eAPI semantics; send an explicit empty value where the API supports it. |
| F-10 | Low | `calendar-multiget` answers an unknown href with 404 propstats instead of a response-level 404 status (RFC 4791, 7.9). | Emit `<d:status>HTTP/1.1 404 Not Found</d:status>` for missing resources. |
| F-11 | Low | PROPFIND on a resource resolves only `donetick-{id}.ics`, while GET and DELETE also use the UID map. | Use `PathHelper.ResolveChoreIdFromPath` consistently. |
| F-12 | Low | `/` answers every method, including PROPFIND, with the HTML status page. A client configured with the bare host that skips `.well-known` gets HTML. | Redirect or answer PROPFIND on `/` as on `/caldav/`. |
| F-13 | Low | `/health/json` is unhealthy for an account without chores and healthy while Donetick has been down since the last successful poll. | Base health on the time and result of the last sync. |
| F-14 | Low | Certificates are loaded once; renewed Tailscale or Let's Encrypt certificates need a restart. | Document it, or use Kestrel's certificate reload support. |
| F-15 | Info | The UID map grows by one entry per created task for the lifetime of the process. | Acceptable; optionally prune entries older than a day. |
| F-16 | Info | Two full loads run back to back at startup (startup check, then the first loop iteration). | Delay the first loop iteration by one interval. |
| F-17 | Info | `ClientTracker.TotalRequests` sums only retained clients, so pruned clients disappear from the total. | Keep a separate counter. |
| F-22 | Info | Polly logs every Donetick call at Information ("Execution attempt"), so each poll adds a log line (about 2,900 a day at the default interval). | Add `builder.Logging.AddFilter("Polly", LogLevel.Warning)` next to the existing HttpClient filter. |

## 3 Build and deployment

| ID | Severity | Finding | Suggested direction |
|----|----------|---------|---------------------|
| F-18 | Low | The container runs as root and has no `HEALTHCHECK`; the `aspnet` image contains no curl or wget for a shell-based check. | Add `USER app` (the non-root user shipped in .NET 8 and later images; port 5232 needs no privileges) and a `HEALTHCHECK` on `/health/json`, which requires adding a small probe tool to the image. |
| F-20 | Info | The Unraid template still points to `https://github.com/your-username/donetick-caldav` for registry, support and project. | Replace with the real repository. |
| F-21 | Info | The default credentials `user` and `pass` in `appsettings.json` satisfy validation. | Remove the defaults so validation forces explicit values, or warn at startup. |

## 4 Documentation drift
Project notes and code comments that contradict the code.

| ID | Location | Drift |
|----|----------|-------|
| D-01 | `instructions.md`, "In-memory cache only" | Describes `ChoreCache` as a `ConcurrentDictionary`; it is a `Dictionary` guarded by a lock. |
| D-02 | `.claude/instructions.md` | States .NET 8 and a `net9.0` test project; both projects target `net10.0`. |
| D-03 | `README.md` | The Group by Label section says dragging is handled as delete plus create; the Limitations section and the code say MOVE is rejected with 403. |
| D-04 | `README.md`, Preserve Scheduled Time | Says non-recurring tasks are unchanged; the code applies the rule to every chore that has `frequencyMetadata.time`. |
| D-05 | `CalendarResolver.ToSlug` XML comment | Example "Work & Life" gives `work-life`, not `work--life`; the "groceries" / "boodschappen" example is inconsistent. |
| D-06 | `CalendarResolver.GetCalendars` comment | Says label calendars are sorted by display name; they are sorted by slug. |
| D-07 | `StatusPageHandler` summary | Mentions Docker HEALTHCHECK; the Dockerfile defines none. |
| D-08 | `PropPatchHandler` summary | Calls the 404 propstat "forbidden"; forbidden would be 403. |
