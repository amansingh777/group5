// Digest every transcript for this project into a compact summary for /reflect.
// Usage: node .claude/skills/reflect/digest.js   (run from the repo root)
const fs = require("fs");
const path = require("path");
const os = require("os");

const MAX_PROMPT = 240;
const TOP = 15;
const CORRECTION = /\b(no|don't|dont|do not|instead|stop|actually|wrong|not what|undo|revert)\b/i;

const slug = process.cwd().replace(/[:\\/]/g, "-");
const dir = path.join(os.homedir(), ".claude", "projects", slug);

function textOf(m) {
  const c = m.message && m.message.content;
  const raw = typeof c === "string" ? c
    : Array.isArray(c) ? c.filter(p => p.type === "text").map(p => p.text).join("\n") : "";
  // same stripping as .claude/hooks/journal.js: keep only what the user typed
  return raw.replace(/<(ide_\w+|system-reminder|local-command-\w+|command-\w+|task-notification)>[\s\S]*?<\/\1>/g, "").trim();
}

function bump(map, key) { map[key] = (map[key] || 0) + 1; }
function top(map, n = TOP) { return Object.entries(map).sort((a, b) => b[1] - a[1]).slice(0, n); }

let files = [];
try {
  files = fs.readdirSync(dir).filter(f => f.endsWith(".jsonl")).sort();
} catch (e) {
  console.log(`No transcripts found at ${dir}`);
  process.exit(0);
}

const tools = {}, bashPrefixes = {}, edited = {}, hooks = {}, promptCounts = {};
const sessions = [];

for (const f of files) {
  const s = { id: f.replace(".jsonl", ""), start: "", prompts: [], corrections: [] };
  let lines = [];
  try { lines = fs.readFileSync(path.join(dir, f), "utf8").split("\n"); } catch (e) { continue; }
  for (const line of lines) {
    let m;
    try { m = JSON.parse(line); } catch (e) { continue; }
    if (!s.start && m.timestamp) s.start = m.timestamp.slice(0, 16).replace("T", " ");

    if (m.type === "user" && !m.isMeta) {
      const human = (m.origin && m.origin.kind === "human") || m.turnOrigin === "human";
      const t = textOf(m);
      if (human && t && !t.startsWith("[Request interrupted")) {
        const first = t.replace(/\s+/g, " ").slice(0, MAX_PROMPT);
        s.prompts.push(first);
        bump(promptCounts, first.toLowerCase().slice(0, 80));
        if (CORRECTION.test(first)) s.corrections.push(first);
      }
    } else if (m.type === "assistant" && m.message && Array.isArray(m.message.content)) {
      for (const p of m.message.content) {
        if (p.type !== "tool_use") continue;
        bump(tools, p.name);
        const inp = p.input || {};
        if (p.name === "Bash" && inp.command) bump(bashPrefixes, inp.command.trim().split(/\s+/).slice(0, 2).join(" "));
        const file = inp.file_path || inp.notebook_path;
        if (file && /^(Edit|Write|NotebookEdit)$/.test(p.name)) bump(edited, path.relative(process.cwd(), file).replace(/\\/g, "/"));
      }
    } else if (m.type === "attachment" && m.attachment && /^hook_/.test(m.attachment.type || "")) {
      bump(hooks, m.attachment.hookName || m.attachment.type);
    }
  }
  sessions.push(s);
}

const out = [];
out.push(`# Transcript digest (${sessions.length} sessions in ${dir})`);
for (const s of sessions) {
  out.push(`\n## Session ${s.id.slice(0, 8)} (${s.start}) - ${s.prompts.length} user prompts`);
  s.prompts.forEach((p, i) => out.push(`${i + 1}. ${p}`));
  if (s.corrections.length) {
    out.push(`Possible corrections (${s.corrections.length}):`);
    s.corrections.forEach(c => out.push(`- ${c}`));
  }
}
const repeated = top(promptCounts).filter(([, n]) => n > 1);
out.push("\n## Repeated prompts (across all sessions)");
out.push(repeated.length ? repeated.map(([p, n]) => `- x${n}: ${p}`).join("\n") : "- none");
out.push("\n## Tool usage\n" + top(tools).map(([k, n]) => `- ${k}: ${n}`).join("\n"));
out.push("\n## Most common Bash commands\n" + top(bashPrefixes).map(([k, n]) => `- ${k}: ${n}`).join("\n"));
out.push("\n## Most edited files\n" + top(edited).map(([k, n]) => `- ${k}: ${n}`).join("\n"));
out.push("\n## Hook events\n" + (Object.keys(hooks).length ? top(hooks).map(([k, n]) => `- ${k}: ${n}`).join("\n") : "- none"));
console.log(out.join("\n"));
