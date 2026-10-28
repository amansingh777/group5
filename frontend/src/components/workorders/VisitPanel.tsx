import { useRef, useState } from 'react'
import { completeVisit, startVisit } from '../../api/workOrders'
import type { VisitOutcome, WorkOrder } from '../../api/workOrders'
import { OUTCOME_OPTIONS } from './labels'
import type { PartRef } from './refData'
import { ErrorPanel, Panel, type Notice } from './WorkOrderUi'
import { useAction } from './useAction'

interface Props {
  wo: WorkOrder
  parts: PartRef[]
  userName: string
  disabled: boolean
  onChanged: (notice?: Notice) => Promise<void>
}

interface PartRow {
  id: number
  partId: string
  quantity: string
}

export default function VisitPanel({ wo, parts, userName, disabled, onChanged }: Props) {
  const start = useAction()
  const complete = useAction()
  const [outcome, setOutcome] = useState<VisitOutcome>('Fixed')
  const [notes, setNotes] = useState('')
  const [rows, setRows] = useState<PartRow[]>([])
  const nextRowId = useRef(1)

  if (wo.status !== 'Scheduled' && wo.status !== 'OnSite') return null

  const beginVisit = () =>
    start.run(async () => {
      await startVisit(wo.id)
      await onChanged({ title: `Visit started for ${wo.jobRef}.`, warnings: [] })
    })

  const rowsValid = rows.every((r) => r.partId !== '' && Number.isInteger(Number(r.quantity)) && Number(r.quantity) >= 1)

  const updateRow = (index: number, patch: Partial<PartRow>) =>
    setRows(rows.map((r, i) => (i === index ? { ...r, ...patch } : r)))

  const submitVisit = () =>
    complete.run(async () => {
      await completeVisit(wo.id, {
        outcome,
        userName,
        notes: notes.trim() || undefined,
        parts: rows.map((r) => ({ partId: Number(r.partId), quantity: Number(r.quantity) })),
      })
      const label = OUTCOME_OPTIONS.find((o) => o.value === outcome)?.label ?? outcome
      await onChanged({ title: `Visit recorded: ${label}.`, warnings: [] })
    })

  if (wo.status === 'Scheduled') {
    return (
      <Panel title="Visit">
        <div className="wo-row">
          <button disabled={disabled || start.busy} onClick={() => void beginVisit()}>
            Start visit
          </button>
        </div>
        {start.error && <ErrorPanel title="Visit not started" message={start.error} />}
      </Panel>
    )
  }

  return (
    <Panel title="Complete visit">
      <form
        className="wo-form"
        onSubmit={(e) => {
          e.preventDefault()
          void submitVisit()
        }}
      >
        <label>
          Outcome
          <select value={outcome} onChange={(e) => setOutcome(e.target.value as VisitOutcome)}>
            {OUTCOME_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </select>
        </label>
        {outcome !== 'Fixed' && (
          <p className="wo-hint">This outcome leaves the work order open and returns it to Triaged for a return visit.</p>
        )}
        <label>
          Notes (optional)
          <textarea rows={3} value={notes} onChange={(e) => setNotes(e.target.value)} />
        </label>
        <fieldset className="wo-parts">
          <legend>Parts used</legend>
          {rows.length === 0 && <p className="wo-hint">No parts recorded for this visit.</p>}
          {rows.map((row, i) => (
            <div className="wo-row" key={row.id}>
              <select
                aria-label={`Part for row ${i + 1}`}
                value={row.partId}
                onChange={(e) => updateRow(i, { partId: e.target.value })}
              >
                <option value="">Choose a part</option>
                {parts.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.sku} - {p.name} (stock {p.stockQty})
                  </option>
                ))}
              </select>
              <input
                aria-label={`Quantity for row ${i + 1}`}
                type="number"
                min={1}
                step={1}
                value={row.quantity}
                onChange={(e) => updateRow(i, { quantity: e.target.value })}
                style={{ width: '5rem' }}
              />
              <button type="button" onClick={() => setRows(rows.filter((_, j) => j !== i))}>
                Remove
              </button>
            </div>
          ))}
          <div className="wo-row">
            <button type="button" onClick={() => setRows([...rows, { id: nextRowId.current++, partId: '', quantity: '1' }])}>
              Add part
            </button>
          </div>
          {!rowsValid && <p className="wo-hint">Each part row needs a part and a whole quantity of 1 or more.</p>}
        </fieldset>
        <div className="wo-row">
          <button type="submit" disabled={disabled || complete.busy || !rowsValid}>
            Record visit
          </button>
        </div>
      </form>
      {complete.error && <ErrorPanel title="Visit not recorded" message={complete.error} />}
    </Panel>
  )
}
