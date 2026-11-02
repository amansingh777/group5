# Debug report: CUS-19 engineer utilisation too low

## Issue (CUS-19)

`GET /api/reports/engineer-utilisation` reports every engineer below 12% utilisation for September 2026. The planner's spreadsheet gives roughly 22% to 47% for the same month. Part-time engineers look worst, which is wrong. Reported by Daniel Mensah; the figures went into the board pack. See `docs/bug-CUS-19.md`.

## Reproduction steps

1. Confirm MySQL is listening on port 3306 (`docker` is not installed on this machine, so `docker compose up -d mysql` was not used; MySQL was already running).
2. Start the API: `dotnet run --project backend/Corrin.Api` (http://localhost:5080).
3. Call:
   `curl "http://localhost:5080/api/reports/engineer-utilisation?from=2026-09-01&to=2026-09-30"`
4. Compare `utilisationPct` and `hoursAvailable` with the table below.

Reproduced on 2026-10-27. The response was HTTP 200.

## Observed output

Engineers are listed in the order returned by the API (sorted by `utilisationPct`, descending). Only the four engineers in the bug report are listed first.

| Engineer | engineerId | Shifts rostered | hoursWorked | hoursAvailable | utilisationPct |
|---|---|---|---|---|---|
| Owen Parry | 3 | 21 | 79.65 | 720 | 11.1 |
| Piotr Nowak | 7 | 20 | 64.25 | 720 | 8.9 |
| Priya Nair | 1 | 19 | 48.67 | 720 | 6.8 |
| Zainab Iqbal | 2 | 13 | 22.85 | 720 | 3.2 |

Remaining engineers from the same response:

| Engineer | engineerId | Shifts rostered | hoursWorked | hoursAvailable | utilisationPct |
|---|---|---|---|---|---|
| Abena Owusu | 8 | 25 | 57.65 | 720 | 8.0 |
| Mei Chen | 9 | 21 | 57.57 | 720 | 8.0 |
| Kwame Boateng | 4 | 25 | 56.6 | 720 | 7.9 |
| Dmitri Volkov | 5 | 21 | 56.18 | 720 | 7.8 |
| Cerys Williams | 10 | 21 | 55.37 | 720 | 7.7 |
| Niamh Doyle | 6 | 13 | 25.65 | 720 | 3.6 |

All ten engineers show `hoursAvailable` = 720. The mean of the ten `utilisationPct` values is 7.3%, which matches the bug report.

## Root cause

`backend/Corrin.Api/Services/ReportService.cs`, `GetEngineerUtilisationAsync`:

- Line 36: `var periodHours = (end - start).TotalHours;` gives calendar hours for the period, 30 days x 24 h = 720.
- Line 45: `var available = periodHours;` uses that calendar figure as each engineer's capacity.
- Line 46: `var pct = available > 0 ? worked / available * 100 : 0;` divides hours worked by calendar hours.

Two effects follow:

- Every engineer is measured against 720 hours, not their rostered hours, so all figures are roughly halved or worse. (The line numbers in `docs/bug-CUS-19.md` are one out; the calculation is at lines 36 and 45-46.)
- Part-time engineers are penalised for the days they do not work. Zainab Iqbal has 13 shifts, but her denominator is still 720.

The `shifts` list is fetched and `rostered` (`shiftsRostered`) is computed, but it is never used to derive available hours.

The existing unit test `Utilisation_ReturnsWorkedHoursAsPercentage` in `backend/Corrin.Tests/ReportServiceTests.cs` encodes the bug. It builds 22 shifts (08:00 to 16:30 with a 30-minute break, so 8 rostered hours each) and 90 hours worked, then asserts `UtilisationPct` = 12.5, which is 90 / 720 x 100. A correct calculation over rostered hours would give 90 / 176 = 51.1%, so this test needs to change along with the fix.

## Expected arithmetic

Rostered hours = shifts rostered x 8 h (the shift length used in the test fixture). `utilisationPct` = hoursWorked / rostered hours x 100.

| Engineer | hoursWorked | Shifts | Rostered hours | Expected % | Reported % |
|---|---|---|---|---|---|
| Owen Parry | 79.65 | 21 | 168 | 47.4 | 11.1 |
| Piotr Nowak | 64.25 | 20 | 160 | 40.2 | 8.9 |
| Priya Nair | 48.67 | 19 | 152 | 32.0 | 6.8 |
| Zainab Iqbal | 22.85 | 13 | 104 | 22.0 | 3.2 |

These match the planner's spreadsheet figures in `docs/bug-CUS-19.md` (47.4%, 40.2%, 32.0%, 22.0%).

## Fix

`ReportService.GetEngineerUtilisationAsync` now sets `available` to the sum of `Shift.RosteredHours` for the engineer's shifts in the period. `periodHours` was removed. See `docs/refactor-note.md`.

## Verification after the fix

Checked over HTTP against the seeded database for `from=2026-09-01&to=2026-09-30`.

| Engineer | hoursAvailable | utilisationPct | Planner |
|---|---|---|---|
| Owen Parry | 168 | 47.4 | 47.4 |
| Piotr Nowak | 160 | 40.2 | 40.2 |
| Priya Nair | 152 | 32.0 | 32.0 |
| Zainab Iqbal | 104 | 22.0 | 22.0 |

The other six engineers range from 24.7% to 34.3%. The mean of the ten is 32.9% (was 7.3%).

- Empty range (`2030-01`): HTTP 200, `hoursAvailable` 0 and `utilisationPct` 0 for all ten engineers, no divide-by-zero.
- Reversed dates: HTTP 422 with "'to' must not be before 'from'."
- Through the Vite proxy (`:5173`): same payload as the API.
- `dotnet test backend/Corrin.sln`: 26 passed, 0 failed (this includes the new tests).
- `npm run build`: passes.

## Not verified / open

- **No real browser check was done.** No browser automation tool was available, so the Utilisation page itself (date inputs, rendered table) was not looked at. The numbers it shows come from the endpoint checked above.
- **Region filter is unverified.** The parameter is `regionId`. `regionId=1` returned all ten engineers; this was not investigated further (it may simply be that every engineer is in region 1, or the filter may be ignored).
- **Utilisation can exceed 100%.** If an engineer completes jobs on days with no shift, hours worked can exceed rostered hours. Left as is.
- **Test that encoded the bug.** `Utilisation_ReturnsWorkedHoursAsPercentage` asserted 12.5 (90 / 720). It now asserts 51.1 (90 / 176).

## Status

Fixed and verified at API level. Not committed.
