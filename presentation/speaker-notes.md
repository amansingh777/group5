# Speaker notes

Written for: the presenter. Talking points per slide, then likely Q&A. Every number is from a doc or the code, and the source is named. Slides are in [slides.md](slides.md); the index is [index.md](index.md).

## Slide 1: Corrin Field Service

- Field service for boilers and meters across six regions.
- Our track: work orders and scheduling, first-time-fix, the CUS-19 fix, and the MCP server.
- Be upfront early: several deliverables are missing. Say so once here and again on the index slide.

## Slide 2: Problem and stakeholders

- Daniel's brief is in [stakeholder-brief.md](../docs/stakeholder-brief.md). Planning is in a spreadsheet today.
- Three hard rules: gas skill is absolute, no double-booking, P1 goes straight on.
- CUS-19 shows why numbers matter: the board pack got figures under 12%, the planner's sheet said 22% to 47%.

## Slide 3: Architecture

- Controllers, then Services, then Repositories. Controllers never see entities. Source: [CLAUDE.md](../CLAUDE.md).
- Migrations run through DbUp at startup. New schema goes in a new numbered file.
- The onboarding brief that was supposed to trace a request is missing. Do not claim a trace.

## Slide 4: Work order lifecycle

- Statuses and allowed moves are rules in [work-orders.md](../docs/specs/work-orders.md), R1 to R3.
- Invalid moves get a 409, not a silent accept.
- Honest note: the status endpoint accepts fewer moves than R2. Allocate, schedule and visits carry the rest. Point to the "Reality differs" lines.

## Slide 5: Scheduling

- Overlap is refused with 409 for non-P1 bookings (R10). The check and save happen in one transaction with an engineer row lock.
- A P1 may displace a booking that has not started. The displaced job goes to Allocated with its booking cleared, and a note is written (R11, R13).
- A P1 never displaces an OnSite job or another P1 (R12).
- Next slot is one call, 14-day search, shift time less break (R14, R15).
- Limit: the "no engineer free" warning is not built (Q16).

## Slide 6: Two items not built

- Call recording (N2) is not built. Say why in one sentence: recording calls without telling callers is unlawful in the UK and needs a DPIA. Source: [spec N2](../docs/specs/work-orders.md).
- Supplier ordering (N1) is manual. Staff mark a reorder request ordered and received.
- This is a strength, not an apology: we turned down a rule that would have broken the law.

## Slide 7: Claude Code setup

- CLAUDE.md is the project memory. [settings.json](../.claude/settings.json) has hooks that write the journal and prompt a CLAUDE.md review after commits.
- Two agents exist: `e2e-browser-check` and `test-writer`. Both are Haiku. Four required agents are missing.
- One skill exists (`reflect`). The rules folder is missing.

## Slide 8: MCP server and security

- The server is in `tools/mysql-mcp`, not `mcp/`. Three tools: `query`, `list_tables`, `describe_table`.
- Three layers: SQL guard, read-only transaction, `multipleStatements: false`. Source: [mcp-security.md](../docs/mcp-security.md).
- Say the gaps first, then the layers. PII is not protected, `LOAD_FILE()` is not blocked, and the account is the app user.
- The four business tools and `run_readonly_query` are not in the code.

## Slide 9: Bug CUS-19

- Root cause in one line: available hours were calendar hours (720), not rostered hours.
- Fix is in [ReportService.cs:44-45](../backend/Corrin.Api/Services/ReportService.cs#L44-L45), committed in `547f29f`.
- Numbers: Owen Parry 11.1 to 47.4; mean of ten 7.3 to 32.9. Source: [debug-report.md](../docs/debug-report.md).
- Regression test: [ReportServiceTests.cs:26](../backend/Corrin.Tests/ReportServiceTests.cs#L26) asserts 51.1; the old code gives 12.5. The "fails on original code" claim was not re-run here. Say so if asked.

## Slide 10: Importer refactor

- MISSING. The deliverable asks for a refactor behind characterisation tests. None is in the repo.
- Say it directly: "I cannot show an importer refactor. The refactor note covers CUS-19."

## Slide 11: Performance

- MISSING. There is no perf note and no before and after numbers.
- Known issues from CLAUDE.md: N+1 in `JobService.ToDtoAsync`, unprotected stock decrement in `AddPartAsync`.
- Do not invent a speed-up figure.

## Slide 12: Review findings

- The review report is missing. The checklist is [review-checklist.md](../docs/review-checklist.md).
- Findings we can show: stale "Not committed" in the debug report; a traceability test name that does not match the real method; secrets in compose and SQL; `PasswordHash` exposed.
- CI is red: [ci-failing-run.log](../docs/ci-failing-run.log) shows .NET 6 SDK and a missing `web` folder. There is no `ci.yml`.

## Slide 13: AI usage reflection

- The reflection is missing. The journal exists with 288 entries.
- Strongest lesson: docs drift. Three examples in this pack alone. Check claims against git and grep before you present.
- Second lesson: write down the gap. The spec's "Not built" list is the most trusted part of the spec.

## Slide 14: Where to find everything

- Point at [index.md](index.md) and say how to read the status column.

---

## Likely Q&A

**1. Why is there no CI?**
There is no `ci.yml`. The only CI evidence is [ci-failing-run.log](../docs/ci-failing-run.log), which shows the workflow used a .NET 6 SDK for a .NET 8 project and pointed at a `web` directory that does not exist. Fixing it is a listed deliverable and is not done.

**2. Did you verify the CUS-19 fix?**
Partly. The fix is in code and committed (`547f29f`). The debug report records HTTP results against the seeded database. The "test fails on the original code" claim was not re-run for this pack.

**3. Why is the importer refactor missing?**
It is not in the repo. `ImportService.cs` has not changed since the first commit, and there are no characterisation tests. The refactor note covers CUS-19 and should not be cited for the importer.

**4. Is the MCP server really read-only?**
Two layers work: the SQL guard and a `START TRANSACTION READ ONLY` per query. It is not enforced at the database account, which is the app user. Writes and file export are refused by the guard tests; PII and `LOAD_FILE()` are documented gaps.

**5. Can a dispatcher put a gas-unqualified engineer on a boiler job?**
No. The skill check is a hard 422 with no override (R5, AC-02). Region and shift are warnings only.

**6. What happens to a job that a P1 displaces?**
It goes back to Allocated with its booking cleared, and a note records which P1 moved it and when. It is not deleted (R13). A displaced OnSite job is never moved (R12).

**7. How does the next-slot call pick an engineer?**
Earliest free slot in 14 days, shift time less the break, job length from the job type's default minutes. Ties go to same region, then name, then id. The full ranking in R7 is not applied there (spec reconciliation notes).

**8. Is the first-time-fix rate right for historical data?**
Only for new work. Migration 007 gave every historical completed job one visit, so the historical rate equals the old flag, 152 of 207 (73.4%). The spec asks Daniel to acknowledge this before publishing (Q23).

**9. Why is call recording not built?**
Recording without telling callers is unlawful in the UK and breaks UK GDPR transparency. The spec says it must not be built under this work. An alternative is call notes in the job notes.

**10. Are the secrets a problem?**
Yes. Passwords are committed in `compose.yaml` and `db-setup/01-create-users.sql` (CLAUDE.md known gaps). `UserDto` also exposes `PasswordHash`. Both are in the review checklist and are not fixed in this pack.
