import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { getBoard, type DispatchJob } from '../api/dispatch'
import { getRegions, getSkills } from '../api/reference'
import DispatchGrid from '../components/dispatch/DispatchGrid'
import UnscheduledList from '../components/dispatch/UnscheduledList'
import { addDays, parseIsoDate, startOfToday, toIsoDate } from '../components/dispatch/dispatchUtils'
import { useLoad } from '../components/useLoad'
import '../components/dispatch/dispatch.css'

type DayCount = 7 | 14

/** Reference lookups. A failed call yields an empty list, so the page falls back to ids. */
function loadReference() {
  return Promise.all([
    getRegions().catch(() => []),
    getSkills().catch(() => []),
  ])
}

function SlaBadge({ state }: { state: DispatchJob['slaState'] }) {
  if (state === 'amber') return <span className="badge badge-amber">SLA near</span>
  if (state === 'breached') return <span className="badge badge-red">SLA breached</span>
  return null
}

function NeedsRebookStrip({ jobs }: { jobs: DispatchJob[] }) {
  return (
    <section className="needs-rebook" aria-labelledby="needs-rebook-heading">
      <h2 id="needs-rebook-heading">Needs rebooking ({jobs.length})</h2>
      <ul className="needs-rebook-list">
        {jobs.map((job) => (
          <li key={job.id}>
            <Link to={`/work-orders/${job.id}`}>{job.jobRef}</Link>{' '}
            <span className={job.priority === 'P1' ? 'priority priority-p1' : 'priority'}>
              Priority {job.priority}
            </span>{' '}
            <SlaBadge state={job.slaState} />
          </li>
        ))}
      </ul>
    </section>
  )
}

export default function DispatchBoardPage() {
  const [weekStart, setWeekStart] = useState<Date>(startOfToday)
  const [days, setDays] = useState<DayCount>(7)
  const [regionId, setRegionId] = useState<number | undefined>(undefined)
  const [reloadKey, setReloadKey] = useState(0)
  const from = toIsoDate(weekStart)

  const reference = useLoad(loadReference, [])
  const [regionList, skillList] = reference.data ?? [[], []]
  const regionNames = useMemo(() => new Map(regionList.map((r) => [r.id, r.name])), [regionList])
  const skillNames = useMemo(() => new Map(skillList.map((s) => [s.id, s.name])), [skillList])

  const { data, loading, error } = useLoad(
    () => getBoard(from, days, regionId),
    [from, days, regionId, reloadKey],
  )

  // Region ids of engineers seen on any board, so the filter still works when the reference call failed.
  const [seenRegionIds, setSeenRegionIds] = useState<number[]>([])
  useEffect(() => {
    if (!data) return
    const ids = data.engineers.map((e) => e.regionId)
    setSeenRegionIds((prev) => {
      const next = Array.from(new Set([...prev, ...ids])).sort((a, b) => a - b)
      return next.length === prev.length ? prev : next
    })
  }, [data])
  const regionOptions: { id: number; label: string }[] =
    regionList.length > 0
      ? regionList.map((r) => ({ id: r.id, label: r.name }))
      : seenRegionIds.map((id) => ({ id, label: `Region ${id}` }))

  const rebookJobs = data ? data.unscheduled.filter((j) => j.needsRebook) : []
  const otherUnscheduled = data ? data.unscheduled.filter((j) => !j.needsRebook) : []

  const dayIsos = data
    ? Array.from({ length: data.days }, (_, i) => toIsoDate(addDays(parseIsoDate(data.from), i)))
    : []

  return (
    <section>
      <h1>Dispatch board</h1>

      <div className="dispatch-controls">
        <button type="button" onClick={() => setWeekStart(addDays(weekStart, -7))}>
          Previous week
        </button>
        <button type="button" onClick={() => setWeekStart(startOfToday())}>
          Today
        </button>
        <button type="button" onClick={() => setWeekStart(addDays(weekStart, 7))}>
          Next week
        </button>
        <label>
          Region{' '}
          <select
            value={regionId ?? ''}
            onChange={(e) => setRegionId(e.target.value === '' ? undefined : Number(e.target.value))}
          >
            <option value="">All regions</option>
            {regionOptions.map((r) => (
              <option key={r.id} value={r.id}>
                {r.label}
              </option>
            ))}
          </select>
        </label>
        <label>
          Days{' '}
          <select value={days} onChange={(e) => setDays(Number(e.target.value) === 14 ? 14 : 7)}>
            <option value={7}>7 days</option>
            <option value={14}>14 days</option>
          </select>
        </label>
      </div>

      {loading && (
        <div className="dispatch-skeleton" role="status" aria-label="Loading dispatch board">
          {Array.from({ length: 24 }, (_, i) => (
            <div key={i} className="skeleton-block" />
          ))}
        </div>
      )}

      {!loading && error && (
        <div className="state-box error" role="alert">
          <p>Something went wrong: {error}</p>
          <button type="button" onClick={() => setReloadKey((k) => k + 1)}>
            Retry
          </button>
        </div>
      )}

      {!loading && !error && data && (
        <>
          {rebookJobs.length > 0 && <NeedsRebookStrip jobs={rebookJobs} />}
          {data.engineers.length === 0 ? (
            <p className="state">No engineers on shift in this window.</p>
          ) : (
            <DispatchGrid
              days={dayIsos}
              engineers={data.engineers}
              regionNames={regionNames}
              skillNames={skillNames}
            />
          )}
          <UnscheduledList jobs={otherUnscheduled} returned={data.unscheduled.length} total={data.unscheduledTotal} />
        </>
      )}
    </section>
  )
}
