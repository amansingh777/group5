# Corrin Field Service: Track 5

Written for: the Track 5 review panel, about 15 minutes. Status of every item is in [index.md](index.md). Items marked MISSING were not found in the repo and are not claimed.

---

## 1. Corrin Field Service

- Field-service system for Corrin Utility Services: boiler and meter install, service and repair across six English regions.
- Scope of this track: work orders and engineer scheduling, the first-time-fix dashboard, the CUS-19 fix, and the MySQL MCP server.
- Source: [README.md](../README.md), [CLAUDE.md](../CLAUDE.md)

---

## 2. Problem and stakeholders

- Planning lives in a spreadsheet. Daniel Mensah (Head of Field Operations) wants it in the system as a **work order**.
- Hard requirements: a boiler job never goes to an engineer without gas skill; no double-booking; P1 emergencies always go straight on.
- Board-pack figures had to be right: CUS-19 showed every engineer under 12% for September 2026. The planner's spreadsheet showed 22% to 47%.
- Stakeholders: Daniel Mensah (sponsor), dispatchers, the North West dispatcher who reported CUS-19, ten engineers across six regions.
- Source: [docs/stakeholder-brief.md](../docs/stakeholder-brief.md), [docs/bug-CUS-19.md](../docs/bug-CUS-19.md)

---

## 3. Architecture

- ASP.NET Core Web API on .NET 8, EF Core 8 with Pomelo MySQL, DbUp migrations at startup. React 19 and Vite frontend with a plain `fetch` wrapper.
- Flow: Controllers, then Services, then Repositories. DTOs and mappers at the edge. Controllers never see entities.
- Errors are ProblemDetails: 404, 409, 422, with 400 from FluentValidation.
- Source: [CLAUDE.md](../CLAUDE.md) (Stack, Layout, Conventions), [backend/Corrin.Api/](../backend/Corrin.Api/)
- Gap: the onboarding brief that was meant to trace one request end to end is MISSING ([docs/onboarding-brief.md](../docs/onboarding-brief.md)). The request trace is not shown here.

---

## 4. Work order lifecycle and rules

- Statuses: Raised (stored as `Requested`), Triaged, Allocated, Scheduled, OnSite, Completed, NotCompleted, Cancelled.
- Allowed moves are rules, not fields. Invalid moves return 409. Completed, NotCompleted and Cancelled are final (R3).
- Allocation is a hard block for a missing required skill and for an inactive engineer (422). A different region is a warning only (R5, R6, R8).
- A non-Fixed visit outcome returns the work order to Triaged and creates an unscheduled follow-up visit (R18).
- Source: [docs/specs/work-orders.md](../docs/specs/work-orders.md), sections Business rules R1 to R9

---

## 5. Scheduling: no double-booking, P1, next slot

- Non-P1 overlap is refused with 409, checked in one transaction with an engineer row lock (R10, spec Concurrency note).
- A P1 may displace a non-P1 booking that has not started. The displaced job goes back to Allocated, with its booking cleared and a note (R11, R13).
- A P1 never displaces an OnSite job or another P1 (R12, AC-06).
- One call, `GET /api/work-orders/{id}/next-slot`, returns engineer, start and end within 14 days, using shift time less the break (R14, R15).
- Source: [docs/specs/work-orders.md](../docs/specs/work-orders.md), R10 to R16, API table

---

## 6. Two brief items we did not build

- **Call recording (N2): not built.** Recording calls without telling callers is unlawful in the UK and needs a DPIA. The spec has no recording code, schema or endpoint. Alternative: call notes in the existing job notes.
- **Automatic supplier ordering (N1): manual version only.** There is no supplier record or API. The system creates a reorder request at the reorder level; a person marks it ordered with a date and received with a quantity (R27, R28).
- Source: [docs/specs/work-orders.md](../docs/specs/work-orders.md), N1, N2, Not built list; [backend/Corrin.Api/Controllers/ReorderRequestsController.cs](../backend/Corrin.Api/Controllers/ReorderRequestsController.cs)

---

## 7. Claude Code setup

- Rules: [CLAUDE.md](../CLAUDE.md) is the project memory. The `.claude/rules/` folder is MISSING.
- Skills: 1 of 2 required. Only `reflect` exists ([.claude/skills/reflect/SKILL.md](../.claude/skills/reflect/SKILL.md)).
- Agents: 2 of 5 required. Present: `e2e-browser-check` and `test-writer` ([.claude/agents/](../.claude/agents/)). Missing: backend, frontend, reviewer, db-analyst.
- Hooks: PostToolUse on Edit, Write and NotebookEdit, and on `git commit`, write to the prompt journal and prompt a CLAUDE.md review ([.claude/settings.json](../.claude/settings.json)).
- `.mcp.json` registers the `mysql` MCP server ([.mcp.json](../.mcp.json)).

---

## 8. MySQL MCP server and its security model

- Server at `tools/mysql-mcp` (the deliverable named `mcp/`; not present). Tools: `query`, `list_tables`, `describe_table` ([tools/mysql-mcp/index.js](../tools/mysql-mcp/index.js)).
- Layers: a SQL guard that allows one SELECT, SHOW, DESCRIBE, EXPLAIN or WITH and denies writes, DDL and file access; a `START TRANSACTION READ ONLY` for every query; `multipleStatements: false` ([docs/mcp-security.md](../docs/mcp-security.md)).
- Known gaps, stated in the doc: no PII protection (`users.password_hash`, customer email and phone); `LOAD_FILE()` not blocked; the guard is a denylist, not a parser; it connects with the app user, not a read-only one.
- Business tools (`first_time_fix_rate`, `engineer_utilisation`, `repeat_visits`, `sla_at_risk`) are MISSING. The guard tests are in `tools/mysql-mcp/tests/sql-guard.test.js`, not `mcp/tests/`. Pass and fail counts are not captured in this pack.

---

## 9. Bug CUS-19: before and after

- **Symptom:** September 2026 utilisation under 12% for every engineer. Owen Parry 11.1% reported, 47.4% in the planner's sheet. Zainab Iqbal 3.2% reported, 22.0%.
- **Root cause:** `available` was calendar hours (720 for 30 days), not rostered hours. Part-timers were penalised for days off. Source: [docs/debug-report.md](../docs/debug-report.md) (Root cause).
- **Fix:** `available` = sum of `Shift.RosteredHours`. Code at [backend/Corrin.Api/Services/ReportService.cs:44-45](../backend/Corrin.Api/Services/ReportService.cs#L44-L45). Committed in `547f29f`.
- **After:** Owen Parry 47.4, Zainab Iqbal 22.0, mean of ten 32.9% (was 7.3%). Figures from [docs/debug-report.md](../docs/debug-report.md) (Verification after the fix).
- **Regression test:** `Utilisation_ReturnsWorkedHoursAsPercentage` now asserts 51.1 (90 / 176), where the old code gives 12.5 (90 / 720). Source: [backend/Corrin.Tests/ReportServiceTests.cs:26](../backend/Corrin.Tests/ReportServiceTests.cs#L26). Also `Utilisation_PartTimerIsNotPenalised`, and `Utilisation_PartTimersAndFullTimers_UseRosteredHours` in [backend/Corrin.Tests/ApiTests.cs:86](../backend/Corrin.Tests/ApiTests.cs#L86).
- Not yet verified: the test failing on the original code was not re-run in this pack. The debug report's "not committed" line is stale (see slide 12).

---

## 10. Importer refactor

- **Status: MISSING.** Deliverable: "Importer refactored behind characterisation tests, explained in `docs/refactor-note.md`".
- [docs/refactor-note.md](../docs/refactor-note.md) explains the CUS-19 change, not the importer.
- `ImportService.cs` has no change since the first commit. The only importer test is `Import_UnknownCustomer_RejectsTheRow` in [backend/Corrin.Tests/ApiTests.cs:108](../backend/Corrin.Tests/ApiTests.cs#L108). No characterisation tests exist.
- We say so plainly rather than present a refactor that is not in the repo.

---

## 11. Performance: before and after

- **Status: MISSING.** [docs/perf-note.md](../docs/perf-note.md) does not exist, so there are no before and after numbers to show.
- Known issues, from [CLAUDE.md](../CLAUDE.md) (Known gaps): `JobService.ToDtoAsync` runs per-row lookups (N+1 on list); `AddPartAsync` checks and decrements stock without concurrency protection; `JobRef` has no collision handling.
- The work-order schedule path does take row locks (engineer, then job) per the spec; no concurrent test exists (spec AC-11).
- No timing was measured for this pack.

---

## 12. Review findings

- **Status: MISSING.** [docs/review-report.md](../docs/review-report.md) does not exist. The review checklist exists: [docs/review-checklist.md](../docs/review-checklist.md), 20 points.
- Findings we can show from the repo:
  - [docs/debug-report.md](../docs/debug-report.md) says the CUS-19 fix is "Not committed". Git history shows it committed in `547f29f`. The debug report is stale.
  - [docs/debug-report.md](../docs/debug-report.md) reports `dotnet test` as 26 passed. That count is not re-verified here, and the test suite now has 365 `[Fact]` and `[Theory]` attributes.
  - The spec traceability names `GetAsync_KnownId_ReturnsRowWithAmberSla`; the real method is `GetAsync_KnownId_ReturnsRowWithNotesPartsAndAmberSla` ([backend/Corrin.Tests/WorkOrderReadServiceTests.cs:149](../backend/Corrin.Tests/WorkOrderReadServiceTests.cs#L149)).
  - Secrets in `compose.yaml` and `db-setup/01-create-users.sql` (checklist point 9; CLAUDE.md known gaps).
  - `UserDto` exposes `PasswordHash` (CLAUDE.md known gaps).
- CI: [docs/ci-failing-run.log](../docs/ci-failing-run.log) shows a failing run. It uses .NET 6 SDK against a .NET 8 project, and a working directory `web` that does not exist. `.github/workflows/ci.yml` is MISSING, so CI is not green.

---

## 13. AI usage reflection and lessons

- **Status: MISSING.** [docs/ai-usage-reflection.md](../docs/ai-usage-reflection.md) does not exist. The journal is present: [docs/prompt-journal.md](../docs/prompt-journal.md) has 288 entries, written by the hooks.
- Lessons the repo supports:
  - Hooks wrote the journal automatically, so the record exists without anyone remembering to write it.
  - Docs drifted from code: the debug report's commit status, the spec's test name, and the refactor note that covers the wrong ticket. Check claims against `git log` and grep before presenting them.
  - Name the gaps: the spec's "Not built" and "Reality differs" lines are more useful than a clean-looking table.
  - A test run with a known-failing guard test (LOAD_FILE, PII) is a documented gap, not a surprise.

---

## 14. Where to find everything

- Full list of every DELIVERABLES.md item, with paths and status: [index.md](index.md)
- Presenter's timed demo: [demo-script.md](demo-script.md)
- Talking points and Q&A: [speaker-notes.md](speaker-notes.md)
