# corrin-mysql-mcp

A read-only MySQL MCP server for querying the Corrin database from Claude Code.

## Tools

- `query` – run a single `SELECT`, `SHOW`, `DESCRIBE`, `EXPLAIN` or `WITH` statement. Results are capped at `MYSQL_MAX_ROWS` (default 1000).
- `list_tables` – list tables in the database.
- `describe_table` – show columns for a table (`table` must be a plain identifier).

## Read-only guarantees

1. Every query runs inside `START TRANSACTION READ ONLY` and is rolled back afterwards, so MySQL rejects writes.
2. The SQL is checked before execution: a single statement, an allowed leading keyword, and no write, locking or file keywords (comments and string literals are ignored during the check).
3. Multiple statements are disabled at the driver level.

For defence in depth, connect with a MySQL user that only has `SELECT` privileges.

## Credentials

Resolved in this order (first match wins):

1. `MYSQL_HOST`, `MYSQL_PORT`, `MYSQL_USER`, `MYSQL_PASSWORD`, `MYSQL_DATABASE`
2. Repo-root `.env` → `ConnectionStrings__Default`
3. Repo-root `compose.yaml` → `ConnectionStrings__Default` (host `mysql` is mapped to `127.0.0.1`)

Other settings: `MYSQL_MAX_ROWS` (default 1000), `MYSQL_QUERY_TIMEOUT_MS` (default 30000).

## Setup

```bash
cd tools/mysql-mcp
npm install
```

Register with Claude Code (run from the repo root):

```bash
claude mcp add mysql -- node tools/mysql-mcp/index.js
```

Or add to `.mcp.json`:

```json
{
  "mcpServers": {
    "mysql": {
      "command": "node",
      "args": ["tools/mysql-mcp/index.js"]
    }
  }
}
```

## Test with the MCP Inspector

```bash
npx @modelcontextprotocol/inspector node tools/mysql-mcp/index.js
```
