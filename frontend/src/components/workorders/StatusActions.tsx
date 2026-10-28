import { changeStatus } from '../../api/workOrders'
import type { WorkOrder } from '../../api/workOrders'
import { OPEN_STATUSES } from './labels'
import { ErrorPanel, Panel, type Notice } from './WorkOrderUi'
import { useAction } from './useAction'

interface Props {
  wo: WorkOrder
  userName: string
  disabled: boolean
  onChanged: (notice?: Notice) => Promise<void>
}

export default function StatusActions({ wo, userName, disabled, onChanged }: Props) {
  const action = useAction()
  const canTriage = wo.status === 'Requested'
  const canCancel = OPEN_STATUSES.includes(wo.status)
  if (!canTriage && !canCancel) return null

  const triage = () =>
    action.run(async () => {
      await changeStatus(wo.id, { status: 'Triaged', userName })
      await onChanged({ title: `${wo.jobRef} triaged.`, warnings: [] })
    })

  const cancel = () => {
    if (!window.confirm(`Cancel ${wo.jobRef}? A cancelled work order cannot be reopened.`)) return
    void action.run(async () => {
      await changeStatus(wo.id, { status: 'Cancelled', userName })
      await onChanged({ title: `${wo.jobRef} cancelled.`, warnings: [] })
    })
  }

  return (
    <Panel title="Status">
      <div className="wo-row">
        {canTriage && (
          <button disabled={disabled || action.busy} onClick={() => void triage()}>
            Triage
          </button>
        )}
        {canCancel && (
          <button disabled={disabled || action.busy} onClick={cancel}>
            Cancel work order
          </button>
        )}
      </div>
      {action.error && <ErrorPanel title="Action refused" message={action.error} />}
    </Panel>
  )
}
