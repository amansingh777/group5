# MySQL MCP server: security

The `mysql` MCP server (`tools/mysql-mcp`, registered in `.mcp.json`) lets Claude Code query the Corrin database. It is meant to be read-only. This page records what it enforces, what is tested, and what is not covered.

## Threat model

The caller is an LLM that may be steered by untrusted text (job notes, imported CSV rows, prompt injection) into running harmful SQL. The server must not allow data changes, schema changes, file access on the DB host, or bulk exposure of personal data.

## Layers

1. **SQL guard** (`tools/mysql-mcp/sql-guard.js`): comments and quoted literals are stripped in a single pass; the statement must be a single `SELECT`/`SHOW`/`DESCRIBE`/`EXPLAIN`/`WITH`; a keyword denylist rejects writes, DDL, locking, `INTO` (so `INTO OUTFILE`/`DUMPFILE`/`@var`), `LOAD`, `SET`, `CALL` and so on.
2. **Read-only transaction**: every query runs in `START TRANSACTION READ ONLY` and is rolled back.
3. **Driver**: `multipleStatements: false`.
4. **Database account** (recommended, not enforced here): connect as a user with `SELECT` only. Today the server falls back to the app credentials in `compose.yaml`.

## Tests

`cd tools/mysql-mcp && npm test` (Node built-in runner, no database needed). `tests/sql-guard.test.js`:

| Attack | Examples | Status |
|---|---|---|
| Writes | INSERT, UPDATE, DELETE, REPLACE, `FOR UPDATE`, `WITH ... DELETE`, mixed case, leading comments | refused |
| DDL / privileges | DROP, ALTER, CREATE, TRUNCATE, RENAME, GRANT, SET GLOBAL, CALL | refused |
| Multiple statements | `SELECT 1; DROP ...`, `;` after comments | refused |
| File export / load | `INTO OUTFILE`, `INTO DUMPFILE`, `INTO @v`, `LOAD DATA INFILE` | refused |
| Comment markers in literals | `SELECT '--'; DROP TABLE users` | refused |
| PII columns | `password_hash`, `email`, `phone`, `address_line`, `postcode`, `SELECT * FROM users` | **not refused (known gap, TODO tests)** |
| `LOAD_FILE()` | `SELECT LOAD_FILE('/etc/passwd')` | **not refused (known gap, TODO test)** |

## Bug found while writing the tests (fixed)

The guard used to strip comments before string literals, so `SELECT '--'; DROP TABLE users` had everything after `--` removed and passed the check. The driver's `multipleStatements: false` would still have stopped the second statement, but the guard alone was bypassable. Stripping is now one left-to-right pass, with regression tests.

## Known gaps

- **No PII protection.** Nothing stops `SELECT email, phone, address_line FROM customers` or reading `users.password_hash`. PII lives in `users`, `customers`, `sites`, `engineers` and the job-import tables. The PII tests are marked `todo` and will show as failing-expected until a guard exists; remove `todo` when it does.
- **`LOAD_FILE()` and other functions** (`SLEEP`, `BENCHMARK`) are not blocked; MySQL's `FILE` privilege and `secure_file_priv` are the only protection.
- **Denylist guard.** It is regex-based, not a SQL parser. It also over-blocks: any `SET`, `DO`, `INTO` or `LOAD` token (including as an identifier) is refused.
- **Credentials** are read from `.env`/`compose.yaml` and the account is the app user, not a read-only one. Passwords are committed to the repo (see CLAUDE.md).
- No audit log of queries run; results are capped by `MYSQL_MAX_ROWS` but not otherwise limited.

## Recommended fixes

1. Create a dedicated MySQL user with `SELECT` only and no `FILE` privilege; point `MYSQL_USER`/`MYSQL_PASSWORD` at it.
2. Close the PII gap at the database: grant column-level `SELECT` that omits PII columns, or expose a `corrin_ro` schema of views without them. That is stronger than a SQL-text denylist.
3. Optionally add a column denylist to the guard as defence in depth, then un-`todo` the PII tests.
