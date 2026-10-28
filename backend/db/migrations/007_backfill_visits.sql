-- Backfill visit 1 for legacy completed jobs.
-- Legacy jobs had no return-visit history, so for them first-time-fix equals the old
-- outcome flag. Only visits recorded from now on carry real return-visit data.
-- Only jobs that reached Completed with an outcome are backfilled. Scheduled, Requested
-- and Cancelled jobs get no visit here; visit 1 is created when a job is actually started.
-- The NOT EXISTS guard makes re-running this script, or a partially backfilled table, safe.
INSERT INTO visits (job_id, engineer_id, visit_no, scheduled_start, scheduled_end, actual_start, actual_end, outcome, created_at)
SELECT j.id, j.engineer_id, 1, j.scheduled_start, j.scheduled_end, j.actual_start, j.actual_end, j.outcome, j.created_at
FROM jobs j
WHERE j.status = 'Completed'
  AND j.outcome IS NOT NULL
  AND j.actual_end IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM visits v WHERE v.job_id = j.id);
