# Test fixtures
Test data shared by several test levels (testing strategy, chapter 6). Fixtures never contain personal data: chore names are synthetic and credentials are test-only.

## donetick/mappings
WireMock.Net mapping files that stand in for the Donetick external API (`/eapi/v1`). The integration tests load them with `DonetickStub.WithReferenceData()`, and the L4 stub container mounts the same folder.

| File | Request | Response |
|------|---------|----------|
| `list-chores.json` | `GET /eapi/v1/chore` | The six reference chores (`ReferenceData` in `DonetickCalDav.Testing`) |
| `create-chore.json` | `POST /eapi/v1/chore` | 201, chore 900 echoing name, description and due date |
| `update-chore.json` | `PUT /eapi/v1/chore/{id}` | 200, the chore in the path echoing the update |
| `complete-chore.json` | `POST /eapi/v1/chore/{id}/complete` | 200 |
| `delete-chore.json` | `DELETE /eapi/v1/chore/{id}` | 200 |

Every mapping requires the header `secretkey: test-api-key`. The list mapping has a fixed GUID, so a test can replace it with `DonetickStub.WithChores(...)`. The echo responses use WireMock's Handlebars templating; a due date `YYYY-MM-DD` comes back as `YYYY-MM-DDT09:00:00Z`. The real Donetick fills in the current UTC time of day instead (contract check CT-02).

The reference chores copy the field set of the recorded payloads below, with fixed dates around 15 June 2025, the base date of `TestChoreFactory`.

## donetick/recorded/&lt;version&gt;
Raw responses of a real Donetick, one file per eAPI call the bridge makes, each as `{"status": ..., "body": ...}`. Recorded with `tests/e2e/record_donetick_fixtures.py` against a fresh, disposable instance. Re-record after a Donetick upgrade and compare.

| File | Call |
|------|------|
| `list-chores.json` | `GET /eapi/v1/chore` after seeding the reference chores |
| `create-chore-date-only.json` | `POST /eapi/v1/chore` with a date-only due date |
| `update-chore-name-only.json` | `PUT /eapi/v1/chore/{id}` with only a name |
| `complete-recurring-chore.json` | `POST /eapi/v1/chore/{id}/complete` on a daily chore |
| `complete-one-off-chore.json` | The same on a one-off chore |
| `delete-chore.json` | `DELETE /eapi/v1/chore/{id}` |
| `list-chores-after-changes.json` | `GET /eapi/v1/chore` after the calls above |
| `wrong-api-key.json` | `GET /eapi/v1/chore` with an invalid key |

Observations from v0.1.82 that the bridge depends on: the list is a plain JSON array; `frequencyMetadata.time` is an RFC3339 timestamp (finding F-23); `updatedAt` carries the server's UTC offset (F-24); a completed one-off chore disappears from the list.
