import { useState } from 'react'
import { nextSlot, schedule } from '../../api/workOrders'
import type { NextSlot, WorkOrder } from '../../api/workOrders'
import type { Engineer } from '../../api/types'
import { formatDateTime, toApiLocal, toLocalInput } from './labels'
import { ErrorPanel, Panel, type Notice } from './WorkOrderUi'
import { useAction } from './useAction'

interface Props {
  wo: WorkOrder
  engineers: Engineer[]
  userName: string
  disabled: boolean
  onChanged: (notice?: Notice) => Promise<void>
}

export default function SchedulePanel({ wo, engineers, userName, disabled, onChanged }: Props) {
  const finder = useAction()
  const saver = useAction()
  const [engineerId, setEngineerId] = useState(wo.engineerId ? String(wo.engineerId) : '')
  const [start, setStart] = useState('')
  const [slot, setSlot] = useState<NextSlot | null>(null)

  if (wo.status !== 'Allocated' && wo.status !== 'Scheduled') return null

  const findSlot = () =>
    finder.run(async () => {
      setSlot(null)
      setSlot(await nextSlot(wo.id))
    })

  const useSlot = (s: NextSlot) => {
    setEngineerId(String(s.engineerId))
    setStart(toLocalInput(s.start))
  }

  const save = () =>
    saver.run(async () => {
      const result = await schedule(wo.id, {
        engineerId: engineerId ? Number(engineerId) : undefined,
        start: toApiLocal(start),
        userName,
      })
      await onChanged({
        title: `Scheduled ${wo.jobRef} for ${formatDateTime(result.start)}.`,
        warnings: [],
        displaced: result.displaced ?? [],
      })
    })

  return (
    <Panel title="Schedule visit">
      <div className="wo-row">
        <button disabled={disabled || finder.busy} onClick={() => void findSlot()}>
          Find next slot
        </button>
      </div>
      {finder.error && <ErrorPanel title="No slot found" message={finder.error} />}
      {slot && (
        <div className="wo-alert success">
          <p>
            <strong>{slot.engineerName}</strong>, {formatDateTime(slot.start)} to {formatDateTime(slot.end)}
          </p>
          <button disabled={disabled} onClick={() => useSlot(slot)}>
            Use this slot
          </button>
        </div>
      )}
      <form
        className="wo-form"
        onSubmit={(e) => {
          e.preventDefault()
          void save()
        }}
      >
        <label>
          Engineer
          <select value={engineerId} onChange={(e) => setEngineerId(e.target.value)}>
            <option value="">Keep the work order's engineer</option>
            {engineers.map((en) => (
              <option key={en.id} value={en.id}>
                {en.fullName}
              </option>
            ))}
          </select>
        </label>
        <label>
          Start
          <input type="datetime-local" value={start} onChange={(e) => setStart(e.target.value)} required />
        </label>
        <div className="wo-row">
          <button type="submit" disabled={disabled || saver.busy || !start}>
            Schedule
          </button>
        </div>
      </form>
      {saver.error && <ErrorPanel title="Booking refused" message={saver.error} />}
    </Panel>
  )
}
