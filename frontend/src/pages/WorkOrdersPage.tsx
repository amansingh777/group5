import { useState } from 'react'
import { Link } from 'react-router-dom'
import { getRegions } from '../api/reference'
import { listWorkOrders } from '../api/workOrders'
import { useLoad } from '../components/useLoad'
import { ALL_STATUSES, PRIORITIES, STATUS_LABELS, formatDateTime } from '../components/workorders/labels'
import { SlaBadge, StatusBadge } from '../components/workorders/WorkOrderUi'
import '../components/workorders/workorders.css'

const PAGE_SIZE = 20

export default function WorkOrdersPage() {
  const [status, setStatus] = useState('')
  const [priority, setPriority] = useState('')
  const [regionId, setRegionId] = useState('')
  const [page, setPage] = useState(1)
  const [attempt, setAttempt] = useState(0)

  const regions = useLoad(getRegions, [])
  const { data, loading, error } = useLoad(
    () =>
      listWorkOrders({
        page,
        pageSize: PAGE_SIZE,
        status: status || undefined,
        priority: priority || undefined,
        regionId: regionId ? Number(regionId) : undefined,
      }),
    [status, priority, regionId, page, attempt],
  )

  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1
  const totalLabel = data ? `${data.total} ${data.total === 1 ? 'work order' : 'work orders'}` : ''

  return (
    <section>
      <h1>Work orders</h1>
      <div className="wo-filters">
        <label>
          Status{' '}
          <select value={status} onChange={(e) => { setStatus(e.target.value); setPage(1) }}>
            <option value="">All</option>
            {ALL_STATUSES.map((s) => (
              <option key={s} value={s}>{STATUS_LABELS[s]}</option>
            ))}
          </select>
        </label>
        <label>
          Priority{' '}
          <select value={priority} onChange={(e) => { setPriority(e.target.value); setPage(1) }}>
            <option value="">All</option>
            {PRIORITIES.map((p) => (
              <option key={p} value={p}>{p}</option>
            ))}
          </select>
        </label>
        <label>
          Region{' '}
          <select
            value={regionId}
            disabled={!regions.data}
            onChange={(e) => { setRegionId(e.target.value); setPage(1) }}
          >
            <option value="">All</option>
            {regions.data?.map((r) => (
              <option key={r.id} value={r.id}>{r.name}</option>
            ))}
          </select>
        </label>
        {regions.error && <span className="wo-hint">Regions could not be loaded.</span>}
      </div>

      {loading && <p className="state">Loading...</p>}

      {!loading && error && (
        <div className="wo-alert error" role="alert">
          <p>Something went wrong: {error}</p>
          <button onClick={() => setAttempt((a) => a + 1)}>Retry</button>
        </div>
      )}

      {!loading && !error && data && data.items.length === 0 && (
        <p className="state">No work orders match these filters.</p>
      )}

      {!loading && !error && data && data.items.length > 0 && (
        <>
          <div className="wo-table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Job ref</th>
                  <th>Customer</th>
                  <th>Site</th>
                  <th>Job type</th>
                  <th>Priority</th>
                  <th>Status</th>
                  <th>Engineer</th>
                  <th>Scheduled start</th>
                  <th>SLA</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((w) => (
                  <tr key={w.id}>
                    <td><Link to={`/work-orders/${w.id}`}>{w.jobRef}</Link></td>
                    <td>{w.customerName}</td>
                    <td>
                      {w.siteName}
                      <br />
                      <span className="wo-muted">{w.sitePostcode}</span>
                    </td>
                    <td>{w.jobType}</td>
                    <td>{w.priority}</td>
                    <td><StatusBadge status={w.status} /></td>
                    <td>{w.engineerName ?? 'Unassigned'}</td>
                    <td>{formatDateTime(w.scheduledStart)}</td>
                    <td><SlaBadge state={w.slaState} dueAt={w.slaDueAt} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="pager">
            <button disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
            <span>{totalLabel}</span>
            <span>Page {page} of {totalPages}</span>
            <button disabled={!data || page >= totalPages} onClick={() => setPage(page + 1)}>Next</button>
          </div>
        </>
      )}
    </section>
  )
}
