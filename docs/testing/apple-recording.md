# Donetick CalDAV Bridge - Recording Apple Sessions
How to capture a session of Apple Reminders or Calendar.app and add it to the replay tests (test level L3 in the [testing strategy](../testing-strategy.md)). A recorded session turns one observation of a real client into a regression test that runs on every build. Record once per major macOS or iOS version, and when a client behaves differently than the replay expects.

## What you need
- A Mac (and an iPhone or iPad for iOS sessions) that you can point at a test account. Do not use an account that also syncs with your real Donetick.
- mitmproxy 12 or later (`pip install mitmproxy` or `brew install mitmproxy`) and Python 3.
- The bridge running against the reference data, so chore ids in the recording match the replay (101 to 106):
  - simplest: the L4 compose setup with the stub profile (`tests/e2e`), which starts the bridge with the WireMock stub and the reference mappings;
  - or `dotnet-wiremock` with `tests/fixtures/donetick/mappings` and the bridge pointed at it with `Donetick__ApiKey=test-api-key`.
- A test user name and password for the bridge, for example `recorder` / `recorder-pass`. They are removed during conversion, but never record with real credentials.

## 1 Put mitmproxy in front of the bridge
Apple clients only send Basic credentials over HTTPS, so mitmproxy terminates TLS and forwards to the bridge:

```bash
mitmdump --mode reverse:http://localhost:5232 --listen-port 8443 -w rp-05.flows
```

On the Mac or iPhone, install and trust the mitmproxy certificate (`~/.mitmproxy/mitmproxy-ca-cert.pem`; on iOS also enable it under Settings, General, About, Certificate Trust Settings). Remove the trust again when you are done.

## 2 Add the account and perform the scenario
Add a CalDAV account (Other, CalDAV account, manual) with server `https://<mac-hostname>:8443` and the test credentials, then perform exactly one scenario from the table below. Start a new flow file for each scenario, so each session stays small and focused. Wait for the client to settle (pull to refresh in Reminders) before stopping mitmdump with Ctrl+C.

| Scenario | Steps | GroupByLabel |
|----------|-------|--------------|
| RP-01 Account setup | Add the account, open Reminders | off |
| RP-02 Initial sync and refresh | Open Reminders, wait, refresh once without changes | off |
| RP-03 Create | Create a task in the default list and one in the Home list | on |
| RP-04 Edit | Change title, notes and due date of "Buy groceries" | off |
| RP-05 Complete recurring | Tick off "Water the plants" | off |
| RP-06 Delete | Delete "Call the dentist" | off |
| RP-07 Move | Drag "Buy groceries" from Home to Finance | on |

## 3 Convert and sanitise

```bash
python tests/fixtures/sessions/tools/capture_to_session.py rp-05.flows \
  --user recorder --id RP-05 --title "Complete a recurring task" \
  --client "Reminders, macOS 26.1" \
  --output tests/fixtures/sessions/apple-reminders-macos/rp-05-complete-recurring.json
```

Add `--group-by-label` when the bridge ran with `GroupByLabel=true`. The converter keeps only CalDAV requests, removes credentials, host names and cookies, replaces the user name with `{user}` and client UUIDs with stable fake ones.

Then edit the file by hand:

1. Add `expectDonetick` entries for the Donetick calls the scenario must cause, for example one `POST /eapi/v1/chore/101/complete` for RP-05.
2. Read every request body. Remove anything personal that came from the device (other lists, real task names, email addresses in principal properties).
3. Put useful observations in `notes`, for example "Reminders sends the completed copy first, then the original".

## 4 Run and approve

```bash
SNAPSHOT_ACCEPT=1 dotnet test --project tests/DonetickCalDav.IntegrationTests --filter-class "*SessionReplayTests"
```

The first run writes the transcript snapshot. Review it together with the session file and commit both. If a status differs from the recording, the replay fails with the exchange number: that is either a regression in the bridge or a difference between the recording setup and the reference data, and either way worth understanding before committing.

Once a recorded session covers a scenario, delete the synthetic file for that scenario in `apple-reminders-macos-synthetic/` together with its snapshot.

## Notes
- mitmproxy's HAR export drops the bodies of PROPFIND and REPORT requests; always convert the flow file. The converter refuses a HAR file with missing bodies.
- The moment Apple syncs is not deterministic, and clients repeat requests. Keep repeated requests in the session; they are part of how the client behaves.
- Flow files contain the raw traffic including credentials. Do not commit them; delete them after converting.
