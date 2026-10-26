# Deliverables: Track 5, Corrin Field Service

Tick every box before you submit. Paths are relative to this folder. Do not submit `node_modules`, `bin`, `obj` or `dist`.

## Common to every track

### Documents
- [ ] `docs/onboarding-brief.md` (architecture plus one request traced end to end)
- [ ] `docs/specs/work-orders-scheduling.md` (written from `docs/specs/_template.md`)
- [ ] `docs/prompt-journal.md` (at least 15 entries)
- [ ] `docs/mcp-security.md`
- [ ] `docs/agent-map.md`
- [ ] `docs/debug-report.md`
- [ ] `docs/refactor-note.md`
- [ ] `docs/perf-note.md` - Performance and concurrency note (before and after)
- [ ] `docs/review-report.md`
- [ ] `docs/ai-usage-reflection.md` (one page, written last)

### Claude Code setup
- [ ] `CLAUDE.md` (corrected)
- [ ] `.claude/rules/*` or a nested `CLAUDE.md`
- [ ] `.claude/skills/` (at least 2)
- [ ] `.claude/agents/` (at least 5: backend, frontend, test, reviewer, db-analyst)
- [ ] `.claude/settings.json` with hooks
- [ ] `.mcp.json`

### MCP server
- [ ] `mcp/` (the server) and `mcp/tests/` (negative tests)
- [ ] `db-setup/03-create-mcp-user.sql`
- [ ] `db-setup/02-mcp-views.sql`

### Pipeline and run files
- [ ] `.github/workflows/ci.yml` (green, plus a job for the MCP negative tests)
- [ ] `compose.yaml` (complete and working)
- [ ] `README.md` (updated)

## Specific to this track: work orders and engineer scheduling

- [ ] Work order lifecycle with rules, not just fields; invalid moves return a clear 4xx ProblemDetails
- [ ] Allocation by skill and region
- [ ] No double-booking of engineers
- [ ] Visit outcomes recorded, and parts used recorded against the work order
- [ ] Single call: `GET /api/workorders/{id}/next-slot`
- [ ] New SQL migration(s) under `backend/db/migrations/` for work orders and for any schema change you need (existing migrations untouched)
- [ ] Backend tests named after acceptance criteria in `backend/Corrin.Tests/` (at least 25, for example `AC_09_OverlapRejected`)
- [ ] Screen 1: dispatcher board in `frontend/src/pages/` (loading, empty and error states)
- [ ] Screen 2: first-time-fix dashboard in `frontend/src/pages/` (loading, empty and error states)
- [ ] MCP business tools: `first_time_fix_rate`, `engineer_utilisation`, `repeat_visits`, `sla_at_risk`, plus a guarded `run_readonly_query`
- [ ] Ticket `docs/bug-CUS-19.md` resolved, with a regression test that fails on the original code, explained in `docs/debug-report.md`
- [ ] Importer refactored behind characterisation tests, explained in `docs/refactor-note.md`

## Done means
- [ ] All tests pass, including at least 25 named after acceptance criteria
- [ ] The MCP negative tests pass
- [ ] CI is green when run locally with `act` or by running the same steps
- [ ] Every issue you found is fixed or explained in your docs
- [ ] Every item above is ticked
