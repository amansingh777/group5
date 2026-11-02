-- Reorder audit columns and backfill of missing reorder requests.
-- (a) Records who placed the order (Open -> Ordered) and who marked it received (Ordered -> Received).
--     VARCHAR(80) matches the MaximumLength(80) the userName validators allow. Transition times are
--     already held in updated_at (set on every transition), so no separate ordered_at/received_at columns.
--     Both columns are NULL for requests that have not reached those states, including all existing rows.
-- (b) Backfill: parts already at or below their reorder level before the reorder feature existed get one
--     Open request, so they are not silently skipped. Parts with an Open or Ordered request are left alone.
--     The NOT EXISTS guard makes re-running this script add nothing.
ALTER TABLE reorder_requests
  ADD COLUMN ordered_by VARCHAR(80) NULL,
  ADD COLUMN received_by VARCHAR(80) NULL;

INSERT INTO reorder_requests (part_id, status, expected_on, created_at, updated_at)
SELECT p.id, 'Open', NULL, UTC_TIMESTAMP(), UTC_TIMESTAMP()
FROM parts p
WHERE p.stock_qty <= p.reorder_level
  AND NOT EXISTS (
    SELECT 1 FROM reorder_requests r
    WHERE r.part_id = p.id AND r.status IN ('Open', 'Ordered'));
