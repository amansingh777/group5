import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { getWorkOrder, listVisits } from '../api/workOrders'
import type { Visit, WorkOrder } from '../api/workOrders'
import { listEngineers } from '../api/engineers'
import { useLoad } from '../components/useLoad'
import ActingAs, { useActingAs } from '../components/workorders/ActingAs'
import AllocatePanel from '../components/workorders/AllocatePanel'
import SchedulePanel from '../components/workorders/SchedulePanel'
import StatusActions from '../components/workorders/StatusActions'
import VisitPanel from '../components/workorders/VisitPanel'
import { FINAL_STATUSES, errorMessage, formatDateTime, outcomeLabel, statusLabel } from '../components/workorders/labels'
import { listParts } from '../components/workorders/refData'
import {
  ErrorPanel,
  NoticeBanner,
  Panel,
  SlaBadge,
  StatusBadge,
  type Notice,
} from '../components/workorders/WorkOrderUi'
import '../components/workorders/workorders.css'

export default function WorkOrderDetailPage() {
  const { id: idParam } = useParams()
  const id = Number(idParam)
  const [actingAs, setActingAs] = useActingAs()
  const [wo, setWo] = useState<WorkOrder | null>(null)
  const [visits, setVisits] = useState<Visit[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<Notice | null>(null)
  const engineers = useLoad(listEngineers, [])
  const parts = useLoad(listParts, [])

  const load = useCallback(async () => {
    try {
      const [w, v] = await Promise.all([getWorkOrder(id), listVisits(id)])
      setWo(w)
      setVisits(v)
      setError(null)
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [id])

  useEffect(() => {
    setLoading(true)
    setWo(null)
    setNotice(null)
    void load()
  }, [load])

  // Called by each action panel after a successful change. Reloads the work order and keeps the notice.
  const handleChanged = async (next?: Notice) => {
    setNotice(next ?? null)
    await load()
  }

  if (Number.isNaN(id)) {
    return (
      <section>
        <ErrorPanel title="Invalid work order" message="The work order id in the address is not valid." />
      </section>
    )
  }

  const userName = actingAs.trim()
  const actionsDisabled = userName === ''
  const partList = parts.data ?? []
  const engineerList = engineers.data ?? []
  const engineerName = (engineerId: number | null) =>
    engineerId === null ? '-' : engineerList.find((e) => e.id === engineerId)?.fullName ?? `Engineer #${engineerId}`
  const partName = (partId: number) => partList.find((p) => p.id === partId)?.name ?? `Part #${partId}`

  return (
    <section className="wo-detail">
      <p><Link to="/work-orders">Back to work orders</Link></p>
      <ActingAs value={actingAs} onChange={setActingAs} />

      {loading && (
        <div className="wo-grid" aria-busy="true">
          <div className="wo-panel wo-skeleton" />
          <div className="wo-panel wo-skeleton" />
          <div className="wo-panel wo-skeleton" />
        </div>
      )}

      {!loading && !wo && (
        <div className="wo-alert error" role="alert">
          <p>Something went wrong: {error}</p>
          <button onClick={() => { setLoading(true); void load() }}>Retry</button>
        </div>
      )}

      {wo && (
        <>
          {error && <ErrorPanel title="Could not refresh" message={error} />}

          <header className="wo-header">
            <h1>{wo.jobRef}</h1>
            <StatusBadge status={wo.status} />
            <span className="badge">Priority {wo.priority}</span>
            <SlaBadge state={wo.slaState} dueAt={wo.slaDueAt} />
            {wo.slaDueAt && <span className="wo-muted">SLA due {formatDateTime(wo.slaDueAt)}</span>}
          </header>

          <div className="wo-grid">
            <Panel title="Work order">
              <dl>
                <dt>Status</dt>
                <dd>{statusLabel(wo.status)}</dd>
                <dt>Customer</dt>
                <dd>{wo.customerName}</dd>
                <dt>Site</dt>
                <dd>{wo.siteName}, {wo.sitePostcode}</dd>
                <dt>Asset</dt>
                <dd>{wo.assetSummary ?? '-'}</dd>
                <dt>Job type</dt>
                <dd>{wo.jobType}</dd>
                <dt>Required skill</dt>
                <dd>{wo.requiredSkill}</dd>
                <dt>Engineer</dt>
                <dd>{wo.engineerName ?? 'Unassigned'}</dd>
                <dt>Scheduled</dt>
                <dd>
                  {wo.scheduledStart ? `${formatDateTime(wo.scheduledStart)} to ${formatDateTime(wo.scheduledEnd)}` : '-'}
                </dd>
                <dt>Outcome</dt>
                <dd>{outcomeLabel(wo.outcome)}</dd>
                <dt>Description</dt>
                <dd>{wo.description ?? '-'}</dd>
              </dl>
            </Panel>

            <Panel title="Actions">
              {actionsDisabled && <p className="wo-hint">Enter your name above to enable actions.</p>}
              {notice && <NoticeBanner notice={notice} />}
              {FINAL_STATUSES.includes(wo.status) ? (
                <p className="state">No actions: this work order is {statusLabel(wo.status)}.</p>
              ) : (
                <div key={wo.id}>
                  <StatusActions wo={wo} userName={userName} disabled={actionsDisabled} onChanged={handleChanged} />
                  <AllocatePanel wo={wo} userName={userName} disabled={actionsDisabled} onChanged={handleChanged} />
                  <SchedulePanel
                    wo={wo}
                    engineers={engineerList.filter((e) => e.active)}
                    userName={userName}
                    disabled={actionsDisabled}
                    onChanged={handleChanged}
                  />
                  <VisitPanel wo={wo} parts={partList} userName={userName} disabled={actionsDisabled} onChanged={handleChanged} />
                </div>
              )}
            </Panel>

            <Panel title="Notes">
              {wo.notes.length === 0 && <p className="state">No notes yet.</p>}
              <ul>
                {wo.notes.map((n) => (
                  <li key={n.id}>
                    <strong>{n.author}</strong> <span className="wo-muted">{formatDateTime(n.createdAt)}</span>
                    <br />
                    {n.note}
                  </li>
                ))}
              </ul>
            </Panel>

            <Panel title="Parts used">
              {wo.parts.length === 0 && <p className="state">No parts recorded.</p>}
              {wo.parts.length > 0 && (
                <table>
                  <thead>
                    <tr><th>Part</th><th>Quantity</th></tr>
                  </thead>
                  <tbody>
                    {wo.parts.map((p) => (
                      <tr key={p.id}>
                        <td>{partName(p.partId)}</td>
                        <td>{p.quantity}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </Panel>

            <Panel title="Visit history">
              {visits.length === 0 && <p className="state">No visits yet.</p>}
              {visits.length > 0 && (
                <table>
                  <thead>
                    <tr><th>Visit</th><th>Engineer</th><th>Scheduled</th><th>Actual</th><th>Outcome</th></tr>
                  </thead>
                  <tbody>
                    {visits.map((v) => (
                      <tr key={v.id}>
                        <td>{v.visitNo}</td>
                        <td>{engineerName(v.engineerId)}</td>
                        <td>{formatDateTime(v.scheduledStart)}</td>
                        <td>
                          {formatDateTime(v.actualStart)}
                          {v.actualEnd && <> to {formatDateTime(v.actualEnd)}</>}
                        </td>
                        <td>{outcomeLabel(v.outcome)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </Panel>
          </div>
        </>
      )}
    </section>
  )
}
