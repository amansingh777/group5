import { useState } from 'react'
import { listReorderRequests, type ReorderStatus } from '../api/reorderRequests'
import ActingAs, { useActingAs } from '../components/workorders/ActingAs'
import '../components/workorders/workorders.css'
import ReorderActions from '../components/reorder/ReorderActions'
import '../components/reorder/reorder.css'
import { useLoad } from '../components/useLoad'

const PAGE_SIZE = 20

type StatusFilter = ReorderStatus | 'All'

const FILTER_OPTIONS: StatusFilter[] = ['Open', 'Ordered', 'Received', 'All']

export default function ReorderRequestsPage() {
  const [actingAs, setActingAs] = useActingAs()
  const [status, setStatus] = useState<StatusFilter>('Open')
  const [page, setPage] = useState(1)
  const [attempt, setAttempt] = useState(0)
  const [notice, setNotice] = useState<string | null>(null)

  const { data, loading, error } = useLoad(
    () =>
      listReorderRequests({
        page,
        pageSize: PAGE_SIZE,
        status: status === 'All' ? undefined : status,
      }),
    [status, page, attempt],
  )

  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1
  const totalLabel = data ? `${data.total} ${data.total === 1 ? 'request' : 'requests'}` : ''

  const done = (message: string) => {
    setNotice(message)
    setAttempt((a) => a + 1)
  }

  return (
    <section>
      <h1>Reorder requests</h1>
      <p className="reorder-note">Orders are recorded manually here. Nothing is sent to a supplier.</p>
      <ActingAs value={actingAs} onChange={setActingAs} />

      <div className="reorder-filters">
        <label>
          Status{' '}
          <select
            value={status}
            onChange={(e) => {
              setStatus(e.target.value as StatusFilter)
              setPage(1)
              setNotice(null)
            }}
          >
            {FILTER_OPTIONS.map((s) => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
        </label>
      </div>

      {notice && (
        <div className="reorder-alert success" role="status">
          <p>{notice}</p>
        </div>
      )}

      {loading && <p className="state">Loading...</p>}

      {!loading && error && (
        <div className="reorder-alert error" role="alert">
          <p>Something went wrong: {error}</p>
          <button onClick={() => setAttempt((a) => a + 1)}>Retry</button>
        </div>
      )}

      {!loading && !error && data && data.items.length === 0 && (
        <p className="state">No reorder requests match this filter.</p>
      )}

      {!loading && !error && data && data.items.length > 0 && (
        <>
          <div className="wo-table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Part</th>
                  <th>Stock</th>
                  <th>Status</th>
                  <th>Expected</th>
                  <th>Ordered by</th>
                  <th>Received by</th>
                  <th>Created</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((r) => (
                  <tr key={r.id}>
                    <td>
                      {r.partSku}
                      <br />
                      <span className="reorder-muted">{r.partName}</span>
                    </td>
                    <td className="reorder-stock">
                      {r.stockQty} / reorder at {r.reorderLevel}{' '}
                      {r.belowReorderLevel && <span className="reorder-badge below">Below level</span>}
                    </td>
                    <td>{r.status}</td>
                    <td>{r.expectedOn ?? '-'}</td>
                    <td>{r.orderedBy ?? '-'}</td>
                    <td>{r.receivedBy ?? '-'}</td>
                    <td>{new Date(r.createdAt).toLocaleString()}</td>
                    <td>
                      <ReorderActions
                        item={r}
                        userName={actingAs}
                        onDone={done}
                      />
                    </td>
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
