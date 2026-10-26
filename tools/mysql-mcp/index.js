#!/usr/bin/env node
// Read-only MySQL MCP server. Speaks MCP over stdio, so nothing else may write to stdout.
import { readFileSync, existsSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import mysql from "mysql2/promise";
import { z } from "zod";

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, "..", "..");

// Parses "Key=Value;Key=Value;" connection strings from .env / compose.yaml.
function parseConnectionString(cs) {
  const parts = {};
  for (const segment of cs.split(";")) {
    const idx = segment.indexOf("=");
    if (idx > 0) parts[segment.slice(0, idx).trim().toLowerCase()] = segment.slice(idx + 1).trim();
  }
  return {
    host: parts["server"],
    port: Number(parts["port"] ?? 3306),
    user: parts["user id"],
    password: parts["password"],
    database: parts["database"],
  };
}

// Resolution order: explicit MYSQL_* env vars, then repo .env, then compose.yaml.
function loadConfig() {
  if (process.env.MYSQL_HOST && process.env.MYSQL_USER) {
    return {
      host: process.env.MYSQL_HOST,
      port: Number(process.env.MYSQL_PORT ?? 3306),
      user: process.env.MYSQL_USER,
      password: process.env.MYSQL_PASSWORD ?? "",
      database: process.env.MYSQL_DATABASE,
    };
  }

  const envFile = resolve(repoRoot, ".env");
  if (existsSync(envFile)) {
    const line = readFileSync(envFile, "utf8").match(/^ConnectionStrings__Default=(.+)$/m);
    if (line) return parseConnectionString(line[1]);
  }

  const compose = resolve(repoRoot, "compose.yaml");
  if (existsSync(compose)) {
    const m = readFileSync(compose, "utf8").match(/ConnectionStrings__Default:\s*"([^"]+)"/);
    if (m) {
      const cfg = parseConnectionString(m[1]);
      // compose hostnames only resolve inside the compose network; the server runs on the host.
      if (cfg.host === "mysql") cfg.host = "127.0.0.1";
      return cfg;
    }
  }

  throw new Error("No MySQL credentials found. Set MYSQL_* env vars or provide ConnectionStrings__Default in .env / compose.yaml.");
}

const config = loadConfig();
const maxRows = Number(process.env.MYSQL_MAX_ROWS ?? 1000);
const queryTimeoutMs = Number(process.env.MYSQL_QUERY_TIMEOUT_MS ?? 30000);

const pool = mysql.createPool({
  host: config.host,
  port: config.port,
  user: config.user,
  password: config.password,
  database: config.database,
  connectionLimit: 5,
  multipleStatements: false,
  dateStrings: true,
});

// Statements that may start a read-only query.
const ALLOWED_START = /^(SELECT|SHOW|DESCRIBE|DESC|EXPLAIN|WITH)\b/i;

// Any of these anywhere in the (comment- and string-stripped) SQL is rejected.
const FORBIDDEN = /\b(INSERT|UPDATE|DELETE|REPLACE|DROP|ALTER|CREATE|TRUNCATE|RENAME|GRANT|REVOKE|CALL|LOAD|DO|HANDLER|SET|LOCK|UNLOCK|INTO|PREPARE|EXECUTE|DEALLOCATE|FLUSH|KILL|RESET|SHUTDOWN|INSTALL|UNINSTALL|ANALYZE|OPTIMIZE|REPAIR)\b|\bFOR\s+UPDATE\b|\bFOR\s+SHARE\b|\bLOCK\s+IN\s+SHARE\s+MODE\b/i;

// Removes comments and quoted literals so keywords inside them don't trigger or hide anything.
function stripCommentsAndStrings(sql) {
  return sql
    .replace(/\/\*[\s\S]*?\*\//g, " ")
    .replace(/--[^\n]*/g, " ")
    .replace(/#[^\n]*/g, " ")
    .replace(/'(?:[^'\\]|\\.|'')*'/g, "''")
    .replace(/"(?:[^"\\]|\\.|"")*"/g, '""')
    .replace(/`[^`]*`/g, "``");
}

function assertReadOnly(sql) {
  const stripped = stripCommentsAndStrings(sql).trim().replace(/;+\s*$/, "");
  if (stripped.includes(";")) throw new Error("Multiple statements are not allowed.");
  if (!ALLOWED_START.test(stripped)) {
    throw new Error("Only SELECT, SHOW, DESCRIBE, EXPLAIN and WITH queries are allowed.");
  }
  const bad = stripped.match(FORBIDDEN);
  if (bad) throw new Error(`Statement contains a disallowed keyword: ${bad[0].toUpperCase()}`);
}

// Runs inside a READ ONLY transaction, so MySQL itself refuses writes even if the checks above miss one.
async function runReadOnly(sql, params = []) {
  const conn = await pool.getConnection();
  try {
    await conn.query("START TRANSACTION READ ONLY");
    const [rows, fields] = await conn.query({ sql, timeout: queryTimeoutMs }, params);
    await conn.query("ROLLBACK");
    return { rows, fields };
  } catch (err) {
    await conn.query("ROLLBACK").catch(() => {});
    throw err;
  } finally {
    conn.release();
  }
}

function textResult(payload) {
  return { content: [{ type: "text", text: JSON.stringify(payload, null, 2) }] };
}

function errorResult(err) {
  return { isError: true, content: [{ type: "text", text: `Error: ${err.message}` }] };
}

const server = new McpServer({ name: "corrin-mysql", version: "1.0.0" });

server.tool(
  "query",
  "Run a read-only SQL query (SELECT, SHOW, DESCRIBE, EXPLAIN, WITH). Results are capped at MYSQL_MAX_ROWS.",
  { sql: z.string().min(1).describe("A single read-only SQL statement") },
  async ({ sql }) => {
    try {
      assertReadOnly(sql);
      const { rows } = await runReadOnly(sql);
      const list = Array.isArray(rows) ? rows : [];
      const truncated = list.length > maxRows;
      return textResult({
        rowCount: list.length,
        truncated,
        rows: truncated ? list.slice(0, maxRows) : list,
      });
    } catch (err) {
      return errorResult(err);
    }
  }
);

server.tool("list_tables", "List tables in the connected database.", {}, async () => {
  try {
    const { rows } = await runReadOnly("SHOW TABLES");
    return textResult(rows.map((r) => Object.values(r)[0]));
  } catch (err) {
    return errorResult(err);
  }
});

server.tool(
  "describe_table",
  "Show the column definitions of a table.",
  { table: z.string().regex(/^[A-Za-z0-9_$]+$/, "Table name may only contain letters, digits, _ and $") },
  async ({ table }) => {
    try {
      // Identifier is validated above, so backtick-quoting is safe.
      const { rows } = await runReadOnly(`DESCRIBE \`${table}\``);
      return textResult(rows);
    } catch (err) {
      return errorResult(err);
    }
  }
);

const transport = new StdioServerTransport();
await server.connect(transport);

// Logs go to stderr; stdout is reserved for the MCP protocol.
console.error(`corrin-mysql MCP server ready (${config.user}@${config.host}:${config.port}/${config.database})`);

process.on("SIGINT", async () => {
  await pool.end();
  process.exit(0);
});
