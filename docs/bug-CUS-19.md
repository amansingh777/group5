# CUS-19: Engineer utilisation report shows implausibly low figures

**Reported by:** Daniel Mensah (Head of Field Operations)
**Area:** Reports, `GET /api/reports/engineer-utilisation`, also the Utilisation page in the web app
**Priority:** High, the figures go to the board pack

## Symptom

Every engineer shows a utilisation under 12% for September 2026. Nobody believes it. Our planner's spreadsheet gives figures between about 22% and 47% for the same month.

Report for `from=2026-09-01&to=2026-09-30`, all regions:

| Engineer | Hours worked | Shifts on the roster | Report says | Planner's spreadsheet says |
|---|---|---|---|---|
| Owen Parry (ENG-0103) | 79.65 | 21 | 11.1% | 47.4% |
| Piotr Nowak (ENG-0107) | 64.25 | 20 | 8.9% | 40.2% |
| Priya Nair (ENG-0101) | 48.67 | 19 | 6.8% | 32.0% |
| Zainab Iqbal (ENG-0102), part time | 22.85 | 13 | 3.2% | 22.0% |

"Hours worked" matches the job history in the system for those engineers (completed jobs only, actual start to actual end). Part-time engineers look worst of all, which makes no sense to the planners.

## Steps to reproduce

1. Start the API against the seeded database.
2. Call `GET /api/reports/engineer-utilisation?from=2026-09-01&to=2026-09-30`.
3. Compare `utilisationPct` for the engineers above with the table.

## Expected

Utilisation that agrees with the planner's spreadsheet, for example Owen Parry 47.4%, and a part-time engineer not being penalised for the days they are not working.

## Actual

Owen Parry 11.1%, Piotr Nowak 8.9%, Priya Nair 6.8%, Zainab Iqbal 3.2%. The average across the ten engineers is 7.3%.

## Notes

Reported first by the North West dispatcher after a board pack went out. Cancelled jobs are not in the hours-worked figure and that part is fine.
