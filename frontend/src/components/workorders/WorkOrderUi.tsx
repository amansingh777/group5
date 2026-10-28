import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import type { DisplacedJob, SlaState } from '../../api/workOrders'
import { SLA_LABELS, formatDateTime, statusLabel } from './labels'

export interface Notice {
  title: string
  warnings: string[]
  displaced?: DisplacedJob[]
}

export function StatusBadge({ status }: { status: string }) {
  return <span className="badge">{statusLabel(status)}</span>
}

export function SlaBadge({ state, dueAt }: { state: SlaState; dueAt?: string | null }) {
  return (
    <span className={`badge sla-${state}`} title={dueAt ? `SLA due ${formatDateTime(dueAt)}` : undefined}>
      {SLA_LABELS[state]}
    </span>
  )
}

export function Panel({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="wo-panel">
      <h2>{title}</h2>
      {children}
    </section>
  )
}

export function ErrorPanel({ title, message }: { title: string; message: string }) {
  return (
    <div className="wo-alert error" role="alert">
      <strong>{title}</strong>
      <p>{message}</p>
    </div>
  )
}

export function WarningPanel({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="wo-alert warning" role="status">
      <strong>{title}</strong>
      {children}
    </div>
  )
}

export function NoticeBanner({ notice }: { notice: Notice }) {
  const displaced = notice.displaced ?? []
  return (
    <>
      <div className="wo-alert success" role="status">
        <p>{notice.title}</p>
      </div>
      {notice.warnings.length > 0 && (
        <WarningPanel title="Warnings">
          <ul className="wo-warnings">
            {notice.warnings.map((w) => (
              <li key={w}>{w}</li>
            ))}
          </ul>
        </WarningPanel>
      )}
      {displaced.length > 0 && (
        <WarningPanel title="Moved to unscheduled">
          <p>
            Moved to unscheduled:{' '}
            {displaced.map((d, i) => (
              <span key={d.jobId}>
                {i > 0 && ', '}
                <Link to={`/work-orders/${d.jobId}`}>{d.jobRef}</Link>
              </span>
            ))}
          </p>
        </WarningPanel>
      )}
    </>
  )
}
