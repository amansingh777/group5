// PostToolUse hook: append an entry to docs/prompt-journal.md when an edit
// is approved (tool ran) or a git commit succeeds. Never blocks work.
const fs = require("fs");
const path = require("path");
const { execSync } = require("child_process");

const JOURNAL = path.join("docs", "prompt-journal.md");

function lastPrompt(transcript) {
  try {
    const lines = fs.readFileSync(transcript, "utf8").trim().split("\n");
    for (let i = lines.length - 1; i >= 0; i--) {
      const m = JSON.parse(lines[i]);
      if (m.type !== "user" || m.isMeta) continue;
      const c = m.message && m.message.content;
      const text = typeof c === "string" ? c
        : Array.isArray(c) ? c.filter(p => p.type === "text").map(p => p.text).join(" ") : "";
      if (text && !text.startsWith("<")) return text.split("\n")[0].slice(0, 200);
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
