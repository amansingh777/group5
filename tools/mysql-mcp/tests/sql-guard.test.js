import { test, describe } from "node:test";
import assert from "node:assert/strict";
import { assertReadOnly } from "../sql-guard.js";

const refused = (label, sqls) =>
  describe(label, () => {
    for (const sql of sqls) {
      test(`refuses: ${sql}`, () => assert.throws(() => assertReadOnly(sql), Error));
    }
  });

refused("writes", [
  "INSERT INTO customers (name) VALUES ('x')",
  "UPDATE jobs SET status = 'done'",
  "DELETE FROM jobs",
  "REPLACE INTO parts (id) VALUES (1)",
  "SELECT * FROM jobs FOR UPDATE",
  "SELECT * FROM jobs LOCK IN SHARE MODE",
  "WITH x AS (SELECT 1) DELETE FROM jobs",
  "SELECT 1 /* hidden */ ; UPDATE jobs SET status = 'x'",
  "iNsErT INTO jobs VALUES (1)",
  "/* leading comment */ DELETE FROM jobs",
]);

refused("DDL and privileges", [
  "DROP TABLE users",
  "ALTER TABLE jobs ADD COLUMN x INT",
  "CREATE TABLE t (id INT)",
  "TRUNCATE TABLE jobs",
  "RENAME TABLE jobs TO jobs2",
  "GRANT ALL ON corrin.* TO 'x'@'%'",
  "SET GLOBAL read_only = 0",
  "CALL some_proc()",
]);

refused("multiple statements", [
  "SELECT 1; DROP TABLE users",
  "SELECT 1; SELECT 2",
  "SELECT 1;\n-- c\nDROP TABLE users",
  "SHOW TABLES; DELETE FROM jobs",
]);

refused("file export and loading", [
  "SELECT * FROM customers INTO OUTFILE '/tmp/c.csv'",
  "SELECT * FROM customers INTO DUMPFILE '/tmp/c.bin'",
  "SELECT 1 INTO @v",
  "LOAD DATA INFILE '/etc/passwd' INTO TABLE jobs",
]);

describe("positive controls", () => {
  for (const sql of [
    "SELECT * FROM jobs",
    "SELECT 1;",
    "SHOW TABLES",
    "DESCRIBE jobs",
    "EXPLAIN SELECT * FROM jobs",
    "WITH x AS (SELECT 1) SELECT * FROM x",
    "SELECT 'DROP TABLE users; --' AS s",
  ]) {
    test(`allows: ${sql}`, () => assert.doesNotThrow(() => assertReadOnly(sql)));
  }
});

// Known gap: the server has no PII column protection. Reported as TODO, not failures.
// Remove `todo` once a guard exists (see docs/mcp-security.md).
describe("PII columns", () => {
  for (const sql of [
    "SELECT password_hash FROM users",
    "SELECT email, phone FROM customers",
    "SELECT address_line, postcode FROM customers",
    "SELECT * FROM users",
    "SELECT full_name, email, phone FROM engineers",
  ]) {
    test(`refuses: ${sql}`, { todo: "no PII column guard yet" }, () =>
      assert.throws(() => assertReadOnly(sql), Error)
    );
  }
});

refused("comment markers inside string literals must not hide statements", [
  "SELECT '--'; DROP TABLE users",
  "SELECT '#'; DELETE FROM jobs",
  "SELECT '/*'; DELETE FROM jobs; SELECT '*/'",
]);

// Known gap: file-reading functions are not blocked by the guard (MySQL's FILE privilege and
// secure_file_priv are the only protection).
test("refuses: SELECT LOAD_FILE('/etc/passwd')", { todo: "LOAD_FILE not blocked by guard" }, () =>
  assert.throws(() => assertReadOnly("SELECT LOAD_FILE('/etc/passwd')"), Error)
);
