---
name: reflect
description: Review all Claude sessions for this project, extract learnings, compare them with existing skills/agents/hooks/commands/CLAUDE.md, and suggest what to convert into a new skill, agent, hook, command or rule. Builds and commits only after the user approves.
disable-model-invocation: true
allowed-tools: Bash, Read, Glob, Grep, Write, Edit, AskUserQuestion
---

# /reflect

Mine this project's session history for reusable learnings and recommend automation. Do not build anything until the user approves.

## 1. Gather
- Run `node .claude/skills/reflect/digest.js` from the repo root. It summarizes every session transcript for this project (user prompts, possible corrections, repeated prompts, tool and Bash usage, most edited files, hook events).
- Read `docs/prompt-journal.md` for what was changed and committed.

## 2. Inventory what already exists
- `.claude/skills/*/SKILL.md`, `.claude/agents/*`, `.claude/commands/*`
- Hooks in `.claude/settings.json` and `.claude/hooks/*`
- `CLAUDE.md` and the memory directory under `~/.claude/projects/<project>/memory/`
- User-level `~/.claude/settings.json` and installed plugins

## 3. Extract learnings
Look for:
- Procedures the user asked for 2 or more times
- Corrections and stated preferences ("don't", "instead", "always")
- Manual steps that could run automatically
- Repeated commands or file edits that suggest a missing shortcut

## 4. Compare
Mark each learning as **covered** (name the file), **partially covered**, or a **gap**. Never propose something that duplicates an existing item; propose extending it instead.

## 5. Recommend
Produce a table: Learning | Evidence (session and prompt) | Proposed form | Why that form | Overlap with existing.

Choose the form this way:
- Deterministic, event-driven behavior -> **hook**
- Repeatable procedure the user triggers -> **skill** or **command**
- Isolated, read-heavy or parallel work -> **agent**
- Standing convention or fact -> **CLAUDE.md rule** (or memory)

If nothing is worth converting, say so plainly. Keep the list short and ranked by value.

## 6. Save the report (before asking anything)
Append the full output of steps 3-5 to `docs/ai-usage-reflection.md`:
- Create the file with a `# AI Usage Reflection` heading if it does not exist.
- Add a section `## Reflection - YYYY-MM-DD HH:MM` (UTC, same as the prompt journal) containing the learnings, the comparison and the recommendation table.
- Use summaries and short quotes only. Never paste raw transcript text, secrets, tokens or credentials.

## 7. Ask
Present the same report to the user, then use AskUserQuestion to ask which candidates to build (multi-select, include "none"). Build nothing without a yes.

## 8. Build
For each approved item, follow the conventions of the existing files found in step 2. Register hooks in `.claude/settings.json` using relative paths like the existing ones. Then add a `Decision:` line (built / skipped, per item) to this run's section in `docs/ai-usage-reflection.md`.

## 9. Commit
Stage only the files created or changed in this run (including `docs/ai-usage-reflection.md`), commit with a clear message, and do not push. If the user declined everything, commit only the reflection file if they want it; otherwise leave it uncommitted.
