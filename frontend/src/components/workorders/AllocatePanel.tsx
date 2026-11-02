import { useState } from 'react'
import { allocate, getCandidates } from '../../api/workOrders'
import type { WorkOrder, WorkOrderCandidate } from '../../api/workOrders'
import { ErrorPanel, Panel, WarningPanel, type Notice } from './WorkOrderUi'
import { useAction } from './useAction'

interface Props {
  wo: WorkOrder
  userName: string
  disabled: boolean
  onChanged: (notice?: Notice) => Promise<void>
}

export default function AllocatePanel({ wo, userName, disabled, onChanged }: Props) {
  const lookup = useAction()
  const alloc = useAction()
  const [candidates, setCandidates] = useState<WorkOrderCandidate[] | null>(null)
  const [selected, setSelected] = useState<number | null>(null)

  if (wo.status !== 'Requested' && wo.status !== 'Triaged') return null

  const chosen = candidates?.find((c) => c.engineerId === selected) ?? null

  const findCandidates = () =>
    lookup.run(async () => {
      const list = await getCandidates(wo.id)
      setCandidates(list)
      setSelected(null)
    })

  const confirmAllocate = () => {
    if (!chosen) return
    const warnings = chosen.warnings.length ? `\n\nWarnings:\n- ${chosen.warnings.join('\n- ')}` : ''
    if (!window.confirm(`Allocate ${chosen.engineerName} to ${wo.jobRef}?${warnings}`)) return
    void alloc.run(async () => {
      const result = await allocate(wo.id, { engineerId: chosen.engineerId, userName })
      await onChanged({ title: `Allocated to ${chosen.engineerName}.`, warnings: result.warnings })
    })
  }

  return (
    <Panel title="Allocate engineer">
      <div className="wo-row">
        <button disabled={disabled || lookup.busy} onClick={() => void findCandidates()}>
          {candidates ? 'Refresh candidates' : 'Find candidates'}
        </button>
      </div>
      {lookup.error && <ErrorPanel title="Could not load candidates" message={lookup.error} />}
      {candidates && candidates.length === 0 && <p className="state">No suitable engineers found.</p>}
      {candidates && candidates.length > 0 && (
        <>
          <p className="wo-hint">Ranked best first. You may pick any engineer listed.</p>
          <ol className="wo-candidates">
            {candidates.map((c) => (
              <li key={c.engineerId}>
                <label>
                  <input
                    type="radio"
                    name={`candidate-${wo.id}`}
                    checked={selected === c.engineerId}
                    disabled={disabled}
                    onChange={() => setSelected(c.engineerId)}
                  />{' '}
                  <strong>
                    {c.rank}. {c.engineerName}
                  </strong>{' '}
                  <span className="wo-muted">{c.rankReason}</span>
                </label>
                {c.warnings.length > 0 && (
                  <ul className="wo-warnings">
                    {c.warnings.map((w) => (
                      <li key={w}>{w}</li>
                    ))}
                  </ul>
                )}
              </li>
            ))}
          </ol>
          <div className="wo-row">
            <button disabled={disabled || alloc.busy || !chosen} onClick={confirmAllocate}>
              Allocate selected engineer
            </button>
          </div>
        </>
      )}
      {alloc.error && <ErrorPanel title="Allocation refused" message={alloc.error} />}
      {chosen && chosen.warnings.length > 0 && (
        <WarningPanel title="This choice has warnings">
          <p>Allocation is still allowed. Check the warnings above before confirming.</p>
        </WarningPanel>
      )}
    </Panel>
  )
}
