import type { FirstTimeFixRow } from '../../api/firstTimeFix'
import { formatRate, hasJobs } from './ftfUtils'

interface Props {
  row: FirstTimeFixRow
}

/** Horizontal bar with the rate printed beside it, so the value never relies on colour alone. */
export default function RateBar({ row }: Props) {
  const width = hasJobs(row) ? Math.min(100, Math.max(0, row.ratePercent)) : 0
  return (
    <div className="ftf-rate">
      <div className="ftf-bar-track" aria-hidden="true">
        <div className="ftf-bar-fill" style={{ width: `${width}%` }} />
      </div>
      <span className="ftf-rate-value">{formatRate(row)}</span>
    </div>
  )
}
