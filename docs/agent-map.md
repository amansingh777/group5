# Agent map

Who does what when Claude Code works on this repository. Checked against `.claude/agents/`, `.claude/settings.json` and the work-order delivery (see `docs/specs/work-orders.md`).

## Project agents (`.claude/agents/`)

| Agent | Model | Tools | May edit | Use it for |
|---|---|---|---|---|
| `test-writer` | Haiku | Bash, Read, Edit, Write, Grep, Glob | `backend/Corrin.Tests` only | Writing and running xUnit tests (Moq service tests, `WebApplicationFactory` API tests, rolled-back DB repository tests) after a change is made |
| `e2e-browser-check` | Haiku | Bash, Read, Grep, Glob | Nothing | Starting the API and Vite, checking API responses, driving the pages in a real browser and comparing on-screen values with expected numbers |

Rules both agents follow:
- `test-writer` never touches `backend/Corrin.Api`. If a test fails because production code looks wrong, it stops and reports the failure instead of fixing it.
- `e2e-browser-check` never edits files and must say which checks were real browser checks and which were HTTP only.

## Built-in agent types used

| Agent type | Used for |
|---|---|
| `general-purpose` (run as Haiku) | All implementation work: backend services and repositories, migrations, frontend pages and API clients, spec writing and corrections, read-only code review |
| `Explore` | Read-only searching across many files (only when the question spans the codebase) |
| `Plan` | Designing an implementation plan before coding |
| `claude-code-guide` | Questions about Claude Code itself (hooks, settings, MCP) |

## Orchestration pattern

One orchestrating session (the main Claude session) delegates and verifies; it does not write the feature code itself.

1. **Brief.** Each agent gets a self-contained prompt: the files to read, what it may edit, what it must not touch, and what to report. Agents see nothing else.
2. **Parallel only when files are disjoint.** Agents that edit the shared files (`Program.cs`, `Dtos.cs`, `Mappers.cs`, `Validators.cs`, `App.tsx`) run one after another.
3. **Implement, then test, then verify.** A `general-purpose` agent builds a slice, `test-writer` covers it, `e2e-browser-check` verifies the live pages.
4. **Gate each phase.** A phase finishes only when the build is clean and the tests pass. Test runs are the repository owner's call: during the final rounds the owner ran `dotnet test` and agents only ran `dotnet build`.
5. **Treat agent reports as unverified.** The orchestrator checks the important claims itself (file contents, database state) before relying on them.
6. **Review as a separate agent.** A read-only reviewer checks the diff against `docs/review-checklist.md`, then the `code-review` skill gives an independent read.

## Phases of the work-order delivery

| Phase | Agents |
|---|---|
| 0 Spec | `general-purpose` (spec-writer) |
| 1 Foundations: schema, stock safety | `general-purpose` x2 in parallel, then `test-writer` |
| 2 Allocation and scheduling | `general-purpose` (allocation), then `general-purpose` (scheduling), then `test-writer` |
| 3 Visits and first-time-fix | `general-purpose` (visits), then `general-purpose` (report), then `test-writer` |
| 4 Backend read endpoints, then frontend | `general-purpose` (board and work-order endpoints), `general-purpose` (frontend foundation), three page agents in parallel, then `test-writer` and `e2e-browser-check` |
| 5 Manual parts reordering | `general-purpose` (backend), `general-purpose` (migration 008), `test-writer`, `general-purpose` (page), `e2e-browser-check` |
| 6 Review and cleanup | `general-purpose` (spec corrections), `general-purpose` (read-only review), three `general-purpose` fix rounds, `test-writer` after each, `e2e-browser-check` regression pass |

## Hooks and tools around the agents

- `.claude/settings.json`: after an edit or write, `journal.js` logs it to `docs/prompt-journal.md`; after a `git commit`, `journal.js` logs it and `update-claude-md.js` prompts a review of `CLAUDE.md`.
- `.mcp.json` registers the read-only `mysql` MCP server (`tools/mysql-mcp`), used to check database state without writing. See `docs/mcp-security.md`.
- Skills in use: `code-review` (independent diff review), `/reflect` (session history review, writes `docs/ai-usage-reflection.md`).

## Boundaries that apply to every agent

- Never edit an existing migration; add the next numbered file.
- Never leave test data in the seeded database; DB tests roll back.
- No call-recording code, tables, endpoints or screens (finding N2). No supplier calls (finding N1, deferred).
- Authentication does not exist: agents must not describe a recorded `userName` as verified.
- Commits and pushes happen only when the repository owner asks.
