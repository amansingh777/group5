# Refactor note: CUS-19 utilisation report

## What changed

- `backend/Corrin.Api/Services/ReportService.cs`, `GetEngineerUtilisationAsync`: `available` is now `shifts.Where(s => s.EngineerId == engineer.Id).Sum(s => s.RosteredHours)` instead of the calendar hours of the period (30 days x 24 h = 720). The unused `periodHours` variable was removed. `Shift.RosteredHours` already existed in `Domain/Entities.cs` (end minus start minus break).
- `backend/Corrin.Tests/ReportServiceTests.cs`:
  - `Utilisation_ReturnsWorkedHoursAsPercentage` now expects 51.1 (90 / 176) instead of 12.5 (90 / 720), because the old value encoded the bug.
  - New: `Utilisation_PartTimerIsNotPenalised` (13 shifts, 22.85 h worked, 22.0%), `Utilisation_DeductsBreakMinutesFromAvailableHours`, `Utilisation_JobsWithoutShifts_ReturnsZeroWithoutException`.
- `backend/Corrin.Tests/ApiTests.cs`: new `Utilisation_PartTimersAndFullTimers_UseRosteredHours` checks Owen Parry 47.4 and Zainab Iqbal 22.0 against the seeded database.
- `docs/debug-report.md`: new, holds the reproduction, root cause and verification.

## What did not change

- No change to the API contract. `hoursAvailable` and `utilisationPct` keep their names and types, but `hoursAvailable` now means rostered hours.
- No change to DTOs, validators, SQL migrations or the frontend. The "Hours available" column on the Utilisation page shows the new value as is.
- Empty-shift engineers still return 0% (the existing `available > 0` guard).

## Effect

September 2026: Owen Parry 11.1% to 47.4%, Piotr Nowak 8.9% to 40.2%, Priya Nair 6.8% to 32.0%, Zainab Iqbal 3.2% to 22.0%. The mean of the ten engineers went from 7.3% to 32.9%.

## Follow-ups

- Report figures already sent in board packs used the old calculation; the owner of those should decide whether to reissue them.
- Utilisation can exceed 100% when jobs are completed outside rostered shifts. Not handled.
- The region filter (`regionId`) and the rendered Utilisation page were not checked; see `docs/debug-report.md`.
