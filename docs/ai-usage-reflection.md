# AI Usage Reflection

## Reflection - 2026-10-28 05:28

Source: 15 session transcripts for this project (2026-10-26 to 2026-10-28), the prompt journal, and the current `.claude/` setup. Summaries and short quotes only.

### Learnings

1. **Commit requests are repeated in almost every session.** "commit the changes" appears 5 times word for word, plus "yes commit them", "commit everything", "commit it", "commit all the untracked and modified changes", "commit the prompt-journal.md file and other untracked files". About 10 requests across 9 sessions. Each time the same steps are needed: look at status, make sure nothing sensitive or generated is staged, write a message, add the attribution line, do not push.
2. **Delegation briefs repeat the same boilerplate.** In the work-order session the orchestrator made about 25 implementation and review calls to a generic agent. Each brief restated: read `CLAUDE.md`, stay in a named folder, do not edit the tests or existing migrations, run the build, reply in a fixed number of lines. The two project agents cover only testing and browser checks. There is no implementer or reviewer agent.
3. **Review and checklist work is done by hand.** One long review prompt was written for the work-order diff (layering, migrations, locks, security, spec rules, frontend). `docs/review-checklist.md` exists, but no agent is bound to it. The built-in `code-review` skill covers the diff-level read.
4. **Stated preference about test runs, twice in two days.** "continue where we left and one more thing don't include dotnet test run in your scope, i will run it manually later" (work-order session) and "continue without running test cases" (presentation session). `.claude/agents/test-writer.md` step 3 still says to run `dotnet test`, and `CLAUDE.md` Commands describes it as a normal step, so the written setup contradicts what the user asked for.
5. **Corrections that are now fixed in the setup.** "The current CLAUDE.md is wrong for this repo", "i created mcp server but /mcp is not showing any servers", "hook should store only user given prompts" and "the entry is showing unknown". Each led to a change that now exists.
6. **Starting the app** was asked for twice ("read the readme file and do the setup...", "start this application for testing manually").
7. **Read-only database questions** ("run a query to count the number of customers") recur and are served by the MySQL MCP tool.
8. **Not mined:** session `f4692d05` (the Track 5 presentation) is outside this repository's engineering work; only its prompt about test runs was used.

### Comparison with what exists

| Learning | Status | Existing item |
|---|---|---|
| 1 Commit routine | Partially covered | `.claude/hooks/journal.js` and `update-claude-md.js` run after a commit; nothing runs before it (staging hygiene, secrets scan, attribution, no push) |
| 2 Implementer agents | Gap | `.claude/agents/` has only `test-writer` and `e2e-browser-check`; `docs/agent-map.md` documents the pattern but the general agent is used with ad hoc briefs |
| 3 Reviewer agent | Gap (diff review itself covered) | `docs/review-checklist.md`, built-in `code-review` skill |
| 4 Test runs are the owner's call | Contradicted by the setup | `test-writer.md` line 19, `CLAUDE.md` line 22 |
| 5 Corrections | Covered | `CLAUDE.md` rewrite, `.mcp.json`, `journal.js`, `update-claude-md.js` |
| 6 Start the app | Covered | `CLAUDE.md` Commands, built-in `run` skill, `e2e-browser-check` |
| 7 Read-only queries | Covered | `mysql` MCP server (`tools/mysql-mcp`, `docs/mcp-security.md`) |

### Recommendations (ranked)

| # | Learning | Evidence | Proposed form | Why that form | Overlap with existing |
|---|---|---|---|---|---|
| 1 | Repeated implementation and review briefs | About 25 delegation calls with the same boilerplate in one session; `docs/agent-map.md` lists a "general-purpose" agent used for everything | Three project agents: `backend-implementer` (edits `backend/Corrin.Api` and `backend/db/migrations` additions only, builds, never runs tests), `frontend-implementer` (edits `frontend/src` only, runs `npm run build`), `reviewer` (read-only, bound to `docs/review-checklist.md`, ranked findings with file and line) | Isolated, scoped work with fixed boundaries; the boundaries become enforceable tool and folder rules instead of text pasted into each prompt | Extends `.claude/agents/` and `docs/agent-map.md`; `test-writer` and `e2e-browser-check` unchanged |
| 2 | Commit routine | About 10 commit requests across 9 sessions | `/commit` skill: show status, refuse to stage environment files, credentials or build output, stage what the user named (default: everything the user asked for), write the message from the diff, add the attribution line, never push, then point out any `CLAUDE.md` edit the post-commit hook asks for | Repeatable procedure the user triggers; the post-commit hooks already exist, this adds the missing pre-commit half | Complements both hooks; does not replace them |
| 3 | Test runs are the owner's call | Two prompts in two sessions | One `CLAUDE.md` rule ("agents build with `dotnet build`; `dotnet test` only when the owner asks") and change `test-writer.md` step 3 to match | Standing convention; the current agent file says the opposite | Edits two existing files, no new file. Note: this fixes today's wording, but the preference may be about time pressure rather than permanent, so confirm before making it a rule |

Not recommended: a start-the-app skill (covered by `CLAUDE.md`, the built-in `run` skill and `e2e-browser-check`), more hooks (nothing else here is event-driven and repeated), a memory entry (the test-run preference belongs in the shared files if it is kept).

Decision: recommendation 1 (implementer and reviewer agents) built: `.claude/agents/backend-implementer.md`, `frontend-implementer.md`, `reviewer.md`, and `docs/agent-map.md` updated. Recommendation 2 (`/commit` skill) skipped. Recommendation 3 (test-run rule and `test-writer.md` wording) skipped; the agent map notes the preference instead.
