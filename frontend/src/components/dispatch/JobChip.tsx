import { Link } from 'react-router-dom'
import type { DispatchJob } from '../../api/dispatch'
import { formatTimeRange } from './dispatchUtils'

export function JobBadges({ job }: { job: DispatchJob }) {
  return (
    <>
      {job.slaState === 'amber' && <span className="badge badge-amber">SLA near</span>}
      {job.slaState === 'breached' && <span className="badge badge-red">SLA breached</span>}
      {job.needsRebook && <span className="badge badge-rebook">Needs rebooking</span>}
    </>
  )
}

export default function JobChip({ job }: { job: DispatchJob }) {
  const p1 = job.priority === 'P1'
  return (
    <div className={p1 ? 'job-chip job-chip-p1' : 'job-chip'}>
      <div className="job-chip-top">
        <Link to={`/work-orders/${job.id}`}>{job.jobRef}</Link>
        <span className={p1 ? 'priority priority-p1' : 'priority'}>{job.priority}</span>
      </div>
      <div className="job-chip-time">{formatTimeRange(job.start, job.end)}</div>
      <div className="job-chip-badges">
        <JobBadges job={job} />
      </div>
    </div>
  )
}
