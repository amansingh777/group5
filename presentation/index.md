# Deliverables index

Written for: reviewers checking Track 5 against [DELIVERABLES.md](../DELIVERABLES.md).

Status key: **done** = present and checked against the code; **partial** = present but incomplete or contradicted, reason given; **MISSING** = not in the repo (shown as plain text, not a link).

Audit date: 2026-10-27. Checks were by reading files and grep. No test suite, build or app run was completed for this index (see [slides.md](slides.md) slide 12).

## Common to every track

### Documents

| Item | Status | Description |
|---|---|---|
| docs/onboarding-brief.md | MISSING | Architecture plus one request traced end to end |
| [docs/specs/work-orders.md](../docs/specs/work-orders.md) (deliverable named `work-orders-scheduling.md`) | partial | Work order spec from `_template.md`; file name differs; contains "Not built" and "Reality differs" notes |
| [docs/prompt-journal.md](../docs/prompt-journal.md) | done | Prompt journal, 288 entries, written by hooks |
| [docs/mcp-security.md](../docs/mcp-security.md) | done | MCP security model, layers, tests and known gaps (PII, LOAD_FILE) |
| MISSING: docs/agent-map.md | MISSING | Agent map |
| [docs/debug-report.md](../docs/debug-report.md) | partial | CUS-19 root cause and verification; says "Not committed" but fix is in `547f29f`; test count "26" not re-verified |
| [docs/refactor-note.md](../docs/refactor-note.md) | partial | Explains the CUS-19 change, not the importer refactor |
| MISSING: docs/perf-note.md | MISSING | Performance and concurrency note, before and after |
| MISSING: docs/review-report.md | MISSING | Review report (the checklist exists, see below) |
| MISSING: docs/ai-usage-reflection.md | MISSING | One-page AI usage reflection |

### Claude Code setup

| Item | Status | Description |
|---|---|---|
| [CLAUDE.md](../CLAUDE.md) | done | Project instructions; modified in working tree, not re-audited line by line |
| MISSING: .claude/rules/* or nested CLAUDE.md | MISSING | Rules folder not found in the paths checked |
| [.claude/skills/reflect/SKILL.md](../.claude/skills/reflect/SKILL.md) | partial | 1 of 2 required skills (`reflect`) |
| [.claude/agents/e2e-browser-check.md](../.claude/agents/e2e-browser-check.md) | partial | Agent 1 of 5 (no edit, runs API and web) |
| [.claude/agents/test-writer.md](../.claude/agents/test-writer.md) | partial | Agent 2 of 5 (edits only `backend/Corrin.Tests`) |
| MISSING: .claude/agents: backend, frontend, reviewer, db-analyst | MISSING | Four of the five required agents are absent; the required `test` agent is covered by `test-writer` above |
| [.claude/settings.json](../.claude/settings.json) | done | Hooks for journal and CLAUDE.md review |
| [.mcp.json](../.mcp.json) | done | Registers the `mysql` MCP server |

### MCP server

| Item | Status | Description |
|---|---|---|
| MISSING: mcp/ (the server) | MISSING | Server is at [tools/mysql-mcp/](../tools/mysql-mcp/) instead; tools are `query`, `list_tables`, `describe_table`; business tools not found |
| [tools/mysql-mcp/tests/sql-guard.test.js](../tools/mysql-mcp/tests/sql-guard.test.js) (deliverable named `mcp/tests/`) | partial | Negative tests for the SQL guard; run result not captured; PII and LOAD_FILE cases are known gaps |
| MISSING: db-setup/03-create-mcp-user.sql | MISSING | Read-only MySQL user for MCP |
| MISSING: db-setup/02-mcp-views.sql | MISSING | Views for MCP |

### Pipeline and run files

| Item | Status | Description |
|---|---|---|
| MISSING: .github/workflows/ci.yml | MISSING | CI pipeline with MCP negative-test job |
| [docs/ci-failing-run.log](../docs/ci-failing-run.log) | partial | A failing CI log (.NET 6 SDK for a .NET 8 project; missing `web` directory). Shows the problem; does not fix it |
| [compose.yaml](../compose.yaml) | partial | MySQL service defined; `api` service builds `./backend` with no Dockerfile, so it is not complete or working |
| [README.md](../README.md) | partial | Run instructions present; no mention of work orders, dispatch pages or the MCP server |

## Specific to this track: work orders and engineer scheduling

| Item | Status | Description |
|---|---|---|
| Work order lifecycle with rules; invalid moves return 4xx ProblemDetails | partial | Rules R1 to R3 in [docs/specs/work-orders.md](../docs/specs/work-orders.md); status endpoint at [backend/Corrin.Api/Controllers/WorkOrdersController.cs](../backend/Corrin.Api/Controllers/WorkOrdersController.cs). Status endpoint accepts fewer moves than R2 (see spec reconciliation notes) |
| Allocation by skill and region | done | Hard skill block (422), region as warning. Spec R5 to R9 |
| No double-booking of engineers | done | Overlap rule 409 with row lock. Spec R10 |
| Visit outcomes recorded; parts used recorded against the work order | done | `visits/complete` with outcome and parts. Spec R17 to R19 |
| Single call `GET /api/workorders/{id}/next-slot` | partial | Built as `GET /api/work-orders/{id}/next-slot` (hyphenated path in the code and spec) |
| New SQL migration(s) under `backend/db/migrations/` | done | [006_work_orders.sql](../backend/db/migrations/006_work_orders.sql), [007_backfill_visits.sql](../backend/db/migrations/007_backfill_visits.sql), [008_reorder_audit_and_backfill.sql](../backend/db/migrations/008_reorder_audit_and_backfill.sql). Existing migrations not edited in this audit (not diffed) |
| Backend tests named after acceptance criteria (at least 25, for example `AC_09_OverlapRejected`) | partial | 365 test attributes in [backend/Corrin.Tests/](../backend/Corrin.Tests/); none named `AC_*`. Traceability maps names to AC-01 to AC-17 in the spec. Not run in this audit |
| Screen 1: dispatcher board with loading, empty and error states | done | [frontend/src/pages/DispatchBoardPage.tsx](../frontend/src/pages/DispatchBoardPage.tsx) has error and empty states; loading state present. Not run in a browser |
| Screen 2: first-time-fix dashboard with loading, empty and error states | done | [frontend/src/pages/FirstTimeFixPage.tsx](../frontend/src/pages/FirstTimeFixPage.tsx). Not run in a browser |
| MCP business tools: `first_time_fix_rate`, `engineer_utilisation`, `repeat_visits`, `sla_at_risk`, plus `run_readonly_query` | MISSING | Server exposes `query`, `list_tables`, `describe_table` in [tools/mysql-mcp/index.js](../tools/mysql-mcp/index.js) |
| Ticket `docs/bug-CUS-19.md` resolved, with a regression test | done | Ticket: [docs/bug-CUS-19.md](../docs/bug-CUS-19.md). Fix: [backend/Corrin.Api/Services/ReportService.cs:44-45](../backend/Corrin.Api/Services/ReportService.cs#L44-L45), commit `547f29f`. Regression test: [backend/Corrin.Tests/ReportServiceTests.cs:26](../backend/Corrin.Tests/ReportServiceTests.cs#L26). Failing-on-original not re-run here |
| Importer refactored behind characterisation tests, explained in `docs/refactor-note.md` | MISSING | `ImportService.cs` unchanged since first commit; no characterisation tests; refactor note covers CUS-19 instead |

## Done means

| Item | Status | Description |
|---|---|---|
| All tests pass, including at least 25 named after acceptance criteria | partial | Not run in this audit; no AC-named tests |
| The MCP negative tests pass | partial | Run was started and then stopped by the user; counts not captured |
| CI is green when run locally with `act` or the same steps | MISSING | No `ci.yml`; `act` not run |
| Every issue found is fixed or explained in the docs | partial | Several issues are explained, some docs are stale (see [slides.md](slides.md) slide 12) |
| Every item above is ticked | MISSING | See the statuses above |
