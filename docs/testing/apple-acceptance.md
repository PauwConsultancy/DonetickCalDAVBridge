# Donetick CalDAV Bridge - Apple Acceptance Checklist
Manual check (test level L6 in the [testing strategy](../testing-strategy.md)) before each release: all checks on macOS, and AT-10 on iOS. Record the result in the release entry of `CHANGELOG.md`, for example "Apple acceptance: macOS 26.1, iOS 26.1, all passed".

## Preparation
- A test Donetick with a known set of chores: at least one without labels, one with exactly one label, one with two labels, one daily recurring chore with a scheduled time, and one chore with a due date shortly after local midnight.
- The bridge under test reachable over HTTPS (Tailscale serve, built-in TLS or a reverse proxy), with `GroupByLabel=true` for AT-07 and `false` for the other checks, or two instances.
- A Mac and an iPhone or iPad without other accounts pointing at the same bridge.
- Debug logging on (`Logging__LogLevel__DonetickCalDav=Debug`) so failed checks can be traced.

## Checks
| ID | Check | Steps | Expected result |
|----|-------|-------|-----------------|
| AT-01 | Add account | System Settings, Internet Accounts, Add Account, Other, CalDAV, Manual; server URL, username, password; enable Reminders and Calendars | Account is added without certificate or authentication warnings |
| AT-02 | View tasks | Open Reminders and Calendar.app | The list (or lists) appear with the expected names; tasks with a due date also show in Calendar.app on the right day |
| AT-03 | Create task | Create a task with a due date in the default list; create one in a label list | Both appear in Donetick within one poll interval; the one from the label list moves to the default list after the next refresh |
| AT-04 | Edit task | Change title, notes and due date of an existing task | Changes appear in Donetick; Reminders keeps the new values after a refresh |
| AT-05 | Complete recurring task | Tick off the daily recurring task | Donetick records the completion; the task returns in Reminders with the next due date; no duplicate task appears in Donetick |
| AT-06 | Delete task | Delete a task in Reminders | The chore is gone in Donetick |
| AT-07 | Lists per label | With `GroupByLabel=true`: open Reminders | One list per label used as sole label; chores with two labels in the default list; colours stay the same after a restart of the bridge |
| AT-08 | Move between lists | Drag a task to another list | Reminders shows an error; the task stays in its original list |
| AT-09 | Change list colour | Change the colour of a list in Reminders | The bridge does not store it; after a refresh the configured colour may return |
| AT-10 | iOS | Repeat AT-01, AT-02, AT-03 and AT-05 on the iPhone or iPad | Same results as on macOS |

## Notes
- The moment Apple syncs is not deterministic; pull to refresh in Reminders, or wait for the client's own interval.
- Reminders supports AppleScript, so AT-03 to AT-06 can be driven from a script; judging the result stays manual.
- A failed check gets a backlog item, and once reproduced, a test at the lowest level that shows the defect.
