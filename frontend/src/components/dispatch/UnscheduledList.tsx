import type { DispatchJob } from '../../api/dispatch'
import JobChip from './JobChip'

// returned is how many rows the board sent (the capped list); total is the uncapped count.
export default function UnscheduledList({ jobs, returned, total }: { jobs: DispatchJob[]; returned: number; total: number }) {
  const truncated = total > returned
  return (
    <section className="unscheduled" aria-labelledby="unscheduled-heading">
      <h2 id="unscheduled-heading">Unscheduled</h2>
      {truncated && <p className="muted">Showing {returned} of {total}</p>}
      {jobs.length === 0 ? (
        <p className="muted">No unscheduled work orders.</p>
      ) : (
        <ul className="unscheduled-list">
          {jobs.map((job) => (
            <li key={job.id}>
              <JobChip job={job} />
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
