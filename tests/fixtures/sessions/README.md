# Replay sessions
Client sessions for the L3 replay tests (testing strategy, 5.4). `SessionReplayTests` replays every file below this folder through the integration harness, against the reference data in `../donetick/mappings`, and checks three things: each request gets the status the client got when the session was recorded, the Donetick calls match `expectDonetick`, and the full transcript of responses matches an approved snapshot in `tests/DonetickCalDav.IntegrationTests/Replay/Snapshots`.

## Folders
| Folder | Source | Content |
|--------|--------|---------|
| `apple-reminders-macos-synthetic/` | Synthetic | RP-01 to RP-07, written by hand from the request patterns Apple Reminders is known to use. Not captured from a device |
| `python-caldav/` | Recorded | A session of the Python `caldav` library through mitmproxy, made to prove the recording toolchain end to end. Not an Apple client |

Recorded Apple sessions go into folders named after the client, for example `apple-reminders-macos/` or `apple-ios/`, made with the [recording guide](../../../docs/testing/apple-recording.md). A synthetic session stays until a recording covers the same scenario.

## Format
```json
{
  "id": "RP-05",
  "title": "Complete a recurring task (the double PUT)",
  "source": "recorded",
  "client": "Reminders, macOS 26.1",
  "recorded": "2026-11-02",
  "notes": "Free text",
  "bridge": { "groupByLabel": false },
  "exchanges": [
    {
      "request": {
        "method": "PUT",
        "path": "/caldav/calendars/{user}/tasks/donetick-101.ics",
        "headers": { "Content-Type": "text/calendar", "User-Agent": "..." },
        "authenticated": true,
        "body": "BEGIN:VCALENDAR\r\n..."
      },
      "response": { "status": 204 }
    }
  ],
  "expectDonetick": [
    { "method": "POST", "path": "/eapi/v1/chore/101/complete", "count": 1 }
  ]
}
```

- `{user}` stands for the CalDAV user name; the replay substitutes the test user.
- `authenticated` says whether the client sent credentials; the replay adds the test credentials. Recorded `Authorization` headers are never stored.
- Chore ids refer to the reference data (101 to 106, new chores get 900). Record against the reference stub, or map ids with `--id-map` when converting.
- Client-generated UUIDs are replaced by stable fake ones (`00000000-0000-4000-8000-000000000001` and up).
- `expectDonetick` is written by hand: the capture only contains the client side.

## Tools
`tools/capture_to_session.py` converts a mitmproxy flow file (or a HAR file with complete bodies) into this format and sanitises it. See the recording guide for the whole procedure.
