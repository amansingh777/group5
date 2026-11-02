# Live demo script (about 6 minutes, inside the 15-minute slot)

Written for: the presenter, who runs these commands live.

**Read this first.** None of the commands below were run while preparing this pack. Expected output is taken from the docs named on each step, not observed. Run the pre-flight check the day before and record what you see.

Working directory for every command: the repo root, `C:\Users\labuser\group5`.

## Pre-flight (before the session, not shown on stage)

```
mysql -h 127.0.0.1 -P 3306 -u root -p -e "SELECT 1"
```
Expected: a row with `1`. Source: [README.md](../README.md) (Run it).

If MySQL is not up, do the fallback at the end of this script before the session starts.

## Timed run

| Time | Step | Command or action | Expected output (source) |
|---|---|---|---|
| 0:00 to 0:30 | 1. Show the slide deck | Open [slides.md](slides.md) | n/a |
| 0:30 to 1:30 | 2. Start the API | `dotnet run --project backend/Corrin.Api` | Listens on http://localhost:5080. Swagger at `/swagger` ([README.md](../README.md)) |
| 1:30 to 2:30 | 3. Reproduce the CUS-19 numbers | `curl "http://localhost:5080/api/reports/engineer-utilisation?from=2026-09-01&to=2026-09-30"` | After the fix, Owen Parry `utilisationPct` 47.4 with `hoursAvailable` 168; Zainab Iqbal 22.0 with 104 ([docs/debug-report.md](../docs/debug-report.md), Verification after the fix) |
| 2:30 to 3:00 | 4. Show the regression test in code | Open [backend/Corrin.Tests/ReportServiceTests.cs](../backend/Corrin.Tests/ReportServiceTests.cs), line 26 | Asserts `51.1`, the 90 / 176 figure ([docs/refactor-note.md](../docs/refactor-note.md)) |
| 3:00 to 3:30 | 5. Show the fix commit | `git show --stat 547f29f` | Commit "Fix CUS-19: utilisation uses rostered hours, add e2e and test agents" (`git log`, commit `547f29f`; [docs/debug-report.md](../docs/debug-report.md) says "Not committed", which is stale) |
| 3:30 to 4:30 | 6. Show the MCP guard refusing writes | `cd tools/mysql-mcp` then `npm test`, and point at the DROP and INTO OUTFILE cases in `tests/sql-guard.test.js` | Writes and file-export cases refused, as listed in [docs/mcp-security.md](../docs/mcp-security.md). **Do not quote a pass count:** the run was not captured. Known gaps (PII, `LOAD_FILE`) show as todo or failing-expected, as the doc says |
| 4:30 to 5:30 | 7. Show the work order rules in the spec | Open [docs/specs/work-orders.md](../docs/specs/work-orders.md) at R5, R10 and R11 | Rules as written; "Reality" lines show what is built |
| 5:30 to 6:00 | 8. Close on the index | Open [index.md](index.md) | Every DELIVERABLES item with status |

## Optional extra (only if there is time and the API is up)

```
curl "http://localhost:5080/api/reports/first-time-fix?from=2026-09&to=2026-09"
```
Expected: JSON with `overall`, `byRegion`, `byJobType` ([spec API table](../docs/specs/work-orders.md)). The values were not observed in this pack.

## Fallbacks

| If this fails | Do this | Say this |
|---|---|---|
| MySQL is not on port 3306 | Skip steps 2 and 3. Show the numbers from [docs/debug-report.md](../docs/debug-report.md) (Verification after the fix) | "I am showing the recorded figures from the debug report, not a live call." |
| The API will not start | Same as above. Show Swagger from a screenshot if you have one; otherwise skip | "The API did not start in time, so these are the documented results." |
| `curl` is missing on Windows | Use `Invoke-RestMethod`, or paste the URL into a browser | n/a |
| `npm test` in `tools/mysql-mcp` fails | Do not claim a pass. Show the guard cases in `tests/sql-guard.test.js` and [docs/mcp-security.md](../docs/mcp-security.md). The LOAD_FILE and PII failures are documented gaps | "The guard test run is not green. Two cases are known gaps, documented in the security note." |
| Anything on the frontend | Do not demo the pages. The dispatcher board and first-time-fix pages have not been checked in a browser | "The pages are built, but I have not checked them in a browser for this pack." |
| You are asked for the CI result | Show [docs/ci-failing-run.log](../docs/ci-failing-run.log) | "CI is not green. The workflow file is missing, and the log shows the .NET 6 SDK problem." |

## Things not to demo

- `dotnet test backend/Corrin.sln`: needs MySQL and has no recorded result for this pack.
- `docker compose up`: Docker is not installed on the demo machine ([docs/debug-report.md](../docs/debug-report.md)), and the `api` service has no Dockerfile.
- The Utilisation page in the browser: not checked ([docs/debug-report.md](../docs/debug-report.md), Not verified).
