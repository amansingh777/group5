// PostToolUse hook: append an entry to docs/prompt-journal.md when an edit
// is approved (tool ran) or a git commit succeeds. Never blocks work.
const fs = require("fs");
const path = require("path");
const os = require("os");
const { execSync } = require("child_process");

const JOURNAL = path.join("docs", "prompt-journal.md");

// Fallback when the hook input has no transcript_path: newest transcript
// for this project under ~/.claude/projects (e.g. C:\Users\me\app -> c--Users-me-app)
function newestTranscript() {
  const slug = process.cwd().replace(/[:\\/]/g, "-");
  const dir = path.join(os.homedir(), ".claude", "projects", slug);
  const files = fs.readdirSync(dir).filter(f => f.endsWith(".jsonl"))
    .map(f => path.join(dir, f))
    .sort((a, b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs);
  return files[0];
}

function lastPrompt(transcript) {
  try {
    const lines = fs.readFileSync(transcript || newestTranscript(), "utf8").trim().split("\n");
    for (let i = lines.length - 1; i >= 0; i--) {
      const m = JSON.parse(lines[i]);
      if (m.type !== "user" || m.isMeta) continue;
      const c = m.message && m.message.content;
      const raw = typeof c === "string" ? c
        : Array.isArray(c) ? c.filter(p => p.type === "text").map(p => p.text).join("\n") : "";
      // drop injected blocks (IDE context, reminders, command output), keep what the user typed
      const text = raw.replace(/<(ide_\w+|system-reminder|local-command-\w+|command-\w+)>[\s\S]*?<\/\1>/g, "").trim();
      if (!text || text.startsWith("[Request interrupted")) continue;
      return text.split("\n")[0].slice(0, 200);
    }
  } catch (e) {}
  return "(unknown)";
}

function git(cmd) {
  try { return execSync(cmd, { encoding: "utf8" }).trim(); } catch (e) { return ""; }
}

let raw = "";
process.stdin.on("data", d => (raw += d));
process.stdin.on("end", () => {
  try {
    const ev = JSON.parse(raw);
    const input = ev.tool_input || {};
    let change, outcome, kind;

    if (ev.tool_name === "Bash") {
      if (!/\bgit\s+commit\b/.test(input.command || "")) return;
      const head = git("git log -1 --format=%h%x20%s");
      if (!head) return;
      change = `commit ${head}`;
      outcome = "committed";
      kind = "commit";
    } else {
      const file = input.file_path || input.notebook_path || "";
      const rel = path.relative(process.cwd(), path.resolve(file)).replace(/\\/g, "/");
      if (!file || rel === "docs/prompt-journal.md") return;
      change = `${ev.tool_name} ${rel}`;
      outcome = "approved";
      kind = "edit";
    }

    fs.mkdirSync("docs", { recursive: true });
    if (!fs.existsSync(JOURNAL)) fs.writeFileSync(JOURNAL, "# Prompt Journal\n");
    const n = (fs.readFileSync(JOURNAL, "utf8").match(/^## Entry /gm) || []).length + 1;
    const ts = new Date().toISOString().slice(0, 16).replace("T", " ");
    fs.appendFileSync(JOURNAL,
      `\n## Entry ${n} - ${ts} (${kind})\n` +
      `- Prompt: ${lastPrompt(ev.transcript_path)}\n` +
      `- Change: ${change}\n` +
      `- Outcome: ${outcome}\n`);
  } catch (e) {}
});
