# Mutation testing
Stryker.NET changes the core logic in small ways (mutants) and runs the unit tests against each change; a mutant that no test notices shows a gap in the assertions, not in the coverage. Testing strategy, 5.8.

```bash
dotnet tool install --global dotnet-stryker --version 5.0.0
cd tests/mutation
dotnet stryker --config-file stryker-config.json --output ../../artifacts/mutation
```

The report is written to `artifacts/mutation/reports` (HTML, JSON, Markdown). `.github/workflows/mutation.yml` runs it weekly. The configuration mutates `CalDav/VTodo`, `CalendarResolver`, `PathHelper` and `ChoreCache` and runs `DonetickCalDav.Tests` through the Microsoft.Testing.Platform runner (`"test-runner": "mtp"`); with the default VSTest runner no xUnit v3 test runs and every mutant survives.

## Baseline (2026-10-18)
Score 67.3 % (140 killed, 59 survived, 9 without coverage), in under two minutes.

| File | Score | Where mutants survive |
|------|-------|-----------------------|
| `PathHelper` | 100 % | |
| `PriorityMapper` | 100 % | |
| `StatusMapper` | 93 % | One equality boundary |
| `VTodoMapper` | 92 % | Fallback strings and two boundary checks |
| `ChoreCache` | 68 % | Log messages, and the ETag composition: a test only checks that the ETag changes, not what it includes (compare finding F-04) |
| `CalendarResolver` | 42 % | The HSL-to-RGB colour arithmetic; unit tests check the colour format, not the values. The SN-02 snapshots in the integration project do pin the colours, but Stryker only runs the unit tests |

The break threshold is 60 %, so the weekly run fails on a clear drop; raise it as tests improve.

The ICS snapshot tests kill many mapper mutants, and on each kill the snapshot helper writes a `.received` file next to the approved one. Those files are ignored by git; the next normal test run deletes them.
