// PostToolUse hook: after a successful git commit, ask Claude to review whether
// CLAUDE.md needs updating. Skips commits that only touch CLAUDE.md. Never blocks work.
const { execSync } = require("child_process");

function git(cmd) {
  try { return execSync(cmd, { encoding: "utf8" }).trim(); } catch (e) { return ""; }
}

let raw = "";
process.stdin.on("data", d => (raw += d));
process.stdin.on("end", () => {
  try {
    const ev = JSON.parse(raw);
    if (ev.tool_name !== "Bash") return;
    if (!/\bgit\s+commit\b/.test((ev.tool_input || {}).command || "")) return;

    const head = git("git log -1 --format=%h%x20%s%n%n%b");
    if (!head) return;
    const files = git("git show --stat --format= HEAD");
    const names = git("git show --name-only --format= HEAD").split("\n").filter(Boolean);
    if (names.length && names.every(n => n === "CLAUDE.md")) return;

    const context =
      "A commit was just made. Review whether it changes the stack, commands, conventions, " +
      "or structure documented in CLAUDE.md. If it does, make a minimal edit to the relevant " +
      "section (no changelog entries; don't duplicate what git records). If not, do nothing. " +
      "Do not commit the CLAUDE.md edit; tell the user so they can commit it.\n\n" +
      `Commit:\n${head}\n\nFiles changed:\n${files}`;

    process.stdout.write(JSON.stringify({
      hookSpecificOutput: { hookEventName: "PostToolUse", additionalContext: context },
    }));
  } catch (e) {}
});
