import { useState } from 'react'
import { getFirstTimeFix, type FirstTimeFixReport } from '../api/firstTimeFix'
import FtfTable from '../components/firsttimefix/FtfTable'
import {
  buildCsv,
  downloadCsv,
  formatRate,
  monthKey,
  validateRange,
  type SortMode,
} from '../components/firsttimefix/ftfUtils'
import { useLoad } from '../components/useLoad'
import '../components/firsttimefix/FirstTimeFix.css'

export default function FirstTimeFixPage() {
  const [from, setFrom] = useState(() => monthKey(5))
  const [to, setTo] = useState(() => monthKey(0))
  const [attempt, setAttempt] = useState(0)
  const [sortMode, setSortMode] = useState<SortMode>('rate')

  const rangeError = validateRange(from, to)
  const { data, loading, error } = useLoad<FirstTimeFixReport | null>(
    (): Promise<FirstTimeFixReport | null> =>
      rangeError ? Promise.resolve(null) : getFirstTimeFix(from, to),
    [from, to, attempt],
  )

  const isEmpty = data !== null && data.overall.jobsAttended === 0
  const overall = data?.overall

  function renderBody() {
    if (rangeError) return <p className="state error" role="alert">{rangeError}</p>
    if (loading) return <p className="state">Loading...</p>
    if (error) {
      return (
        <p className="state error" role="alert">
          Could not load the first-time-fix report: {error}
          <button className="ftf-retry" onClick={() => setAttempt((n) => n + 1)}>
            Retry
          </button>
        </p>
      )
    }
    if (!data || isEmpty) return <p className="state">No completed work orders in this range.</p>

    return (
      <>
        <div className="ftf-tiles">
          <div className="ftf-tile">
            <div className="ftf-tile-label">Overall first-time-fix rate</div>
            <div className="ftf-tile-value">{overall && formatRate(overall)}</div>
          </div>
          <div className="ftf-tile">
            <div className="ftf-tile-label">Jobs attended</div>
            <div className="ftf-tile-value">{overall?.jobsAttended}</div>
          </div>
          <div className="ftf-tile">
            <div className="ftf-tile-label">Fixed first time</div>
            <div className="ftf-tile-value">{overall?.fixedFirstTime}</div>
          </div>
        </div>

        <FtfTable caption="By region" nameHeader="Region" rows={data.byRegion} sortMode={sortMode} />
        <FtfTable caption="By job type" nameHeader="Job type" rows={data.byJobType} sortMode={sortMode} />

        <section className="ftf-note">
          <h2>How this is counted</h2>
          <p>
            A work order counts as fixed first time only if its first visit was fixed and it never needed a
            return visit. A first visit with no access counts as not fixed. Cancelled work orders and visits
            without an outcome are excluded. Each work order is counted in the month its first visit ended.
          </p>
        </section>
      </>
    )
  }

  return (
    <section>
      <h1>First-time fix</h1>
      <div className="ftf-controls">
        <label>
          From <input type="month" value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label>
          To <input type="month" value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
        <button onClick={() => setSortMode((m) => (m === 'rate' ? 'name' : 'rate'))}>
          {sortMode === 'rate' ? 'Sort by name' : 'Sort by rate'}
        </button>
        <button
          disabled={!data || isEmpty}
          onClick={() => {
            if (data) downloadCsv(`first-time-fix-${from}-to-${to}.csv`, buildCsv(data, sortMode))
          }}
        >
          Download CSV
        </button>
      </div>
      {renderBody()}
    </section>
  )
}
