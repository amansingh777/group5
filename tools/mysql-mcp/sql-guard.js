// Pre-execution SQL checks for the read-only MCP server. Kept free of I/O so it can be unit tested.

// Statements that may start a read-only query.
const ALLOWED_START = /^(SELECT|SHOW|DESCRIBE|DESC|EXPLAIN|WITH)\b/i;

// Any of these anywhere in the (comment- and string-stripped) SQL is rejected.
const FORBIDDEN = /\b(INSERT|UPDATE|DELETE|REPLACE|DROP|ALTER|CREATE|TRUNCATE|RENAME|GRANT|REVOKE|CALL|LOAD|DO|HANDLER|SET|LOCK|UNLOCK|INTO|PREPARE|EXECUTE|DEALLOCATE|FLUSH|KILL|RESET|SHUTDOWN|INSTALL|UNINSTALL|ANALYZE|OPTIMIZE|REPAIR)\b|\bFOR\s+UPDATE\b|\bFOR\s+SHARE\b|\bLOCK\s+IN\s+SHARE\s+MODE\b/i;

// Removes comments and quoted literals so keywords inside them don't trigger or hide anything.
export function stripCommentsAndStrings(sql) {
  // Single left-to-right pass so a comment marker inside a literal (or a quote inside a comment) is
  // tokenised correctly; stripping comments first would let `'--'; DROP ...` hide the DROP.
  return sql.replace(
    /\/\*[\s\S]*?\*\/|--[^\n]*|#[^\n]*|'(?:[^'\\]|\\.|'')*'|"(?:[^"\\]|\\.|"")*"|`[^`]*`/g,
    (m) => (m[0] === "'" ? "''" : m[0] === '"' ? '""' : m[0] === "`" ? "``" : " ")
  );
}

export function assertReadOnly(sql) {
  const stripped = stripCommentsAndStrings(sql).trim().replace(/;+\s*$/, "");
  if (stripped.includes(";")) throw new Error("Multiple statements are not allowed.");
  if (!ALLOWED_START.test(stripped)) {
    throw new Error("Only SELECT, SHOW, DESCRIBE, EXPLAIN and WITH queries are allowed.");
  }
  const bad = stripped.match(FORBIDDEN);
  if (bad) throw new Error(`Statement contains a disallowed keyword: ${bad[0].toUpperCase()}`);
}
