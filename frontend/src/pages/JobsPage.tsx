import { useState } from 'react'
import { listJobs } from '../api/jobs'
import PageState from '../components/PageState'
import { useLoad } from '../components/useLoad'

const STATUSES = ['', 'Requested', 'Scheduled', 'InProgress', 'Completed', 'Cancelled']

export default function JobsPage() {
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)
  const { data, loading, error } = useLoad(() => listJobs({ status, page }), [status, page])

  return (
    <section>
      <h1>Jobs</h1>
      <label>
        Status{' '}
        <select value={status} onChange={(e) => { setStatus(e.target.value); setPage(1) }}>
          {STATUSES.map((s) => (
            <option key={s} value={s}>{s || 'All'}</option>
          ))}
        </select>
      </label>
      <PageState loading={loading} error={error} empty={data?.items.length === 0}>
        <table>
          <thead>
            <tr><th>Ref</th><th>Customer</th><th>Engineer</th><th>Priority</th><th>Status</th><th>Scheduled</th></tr>
          </thead>
          <tbody>
            {data?.items.map((j) => (
              <tr key={j.id}>
                <td>{j.jobRef}</td>
                <td>{j.customerName}</td>
                <td>{j.engineerName ?? 'Unassigned'}</td>
                <td>{j.priority}</td>
                <td>{j.status}</td>
                <td>{j.scheduledStart ? new Date(j.scheduledStart).toLocaleString() : '-'}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <div className="pager">
          <button disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
          <span>Page {page} of {data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1}</span>
          <button disabled={!data || page * data.pageSize >= data.total} onClick={() => setPage(page + 1)}>Next</button>
        </div>
      </PageState>
    </section>
  )
}
