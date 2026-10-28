import { useState, type FormEvent } from 'react'
import { markOrdered, markReceived, type ReorderRequest } from '../../api/reorderRequests'
import { useAction } from '../workorders/useAction'
import './reorder.css'

interface Props {
  item: ReorderRequest
  userName: string
  onDone: (message: string) => void
}

/** Today's date as yyyy-MM-dd in local time, for the date input min. */
function localToday(): string {
  const d = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

export default function ReorderActions({ item, userName, onDone }: Props) {
  if (item.status === 'Open') return <MarkOrderedForm item={item} userName={userName} onDone={onDone} />
  if (item.status === 'Ordered') return <MarkReceivedForm item={item} userName={userName} onDone={onDone} />
  return <span className="reorder-muted">-</span>
}

function MarkOrderedForm({ item, userName, onDone }: Props) {
  const action = useAction()
  const [expectedOn, setExpectedOn] = useState('')
  const today = localToday()
  const dateValid = expectedOn !== '' && expectedOn >= today
  const actingReady = userName.trim() !== ''

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (!dateValid || !actingReady) return
    void action.run(async () => {
      await markOrdered(item.id, { expectedOn, userName: userName.trim() })
      onDone(`${item.partSku} marked ordered, expected ${expectedOn}.`)
    })
  }

  return (
    <form className="reorder-form" onSubmit={submit}>
      <label>
        Expected{' '}
        <input
          type="date"
          required
          min={today}
          value={expectedOn}
          onChange={(e) => setExpectedOn(e.target.value)}
          aria-label={`Expected date for ${item.partSku}`}
        />
      </label>
      <button type="submit" disabled={!actingReady || !dateValid || action.busy}>
        Mark ordered
      </button>
      {!actingReady && <span className="reorder-hint">Enter your name to enable actions.</span>}
      {actingReady && expectedOn !== '' && !dateValid && (
        <span className="reorder-hint">Expected date cannot be in the past.</span>
      )}
      {action.error && (
        <div className="reorder-alert error reorder-row-error" role="alert">
          <strong>Action refused</strong>
          <p>{action.error}</p>
        </div>
      )}
    </form>
  )
}

function MarkReceivedForm({ item, userName, onDone }: Props) {
  const action = useAction()
  const [quantity, setQuantity] = useState('')
  const [confirming, setConfirming] = useState(false)
  const qty = Number(quantity)
  const qtyValid = /^\d+$/.test(quantity) && qty >= 1
  const actingReady = userName.trim() !== ''

  const review = (e: FormEvent) => {
    e.preventDefault()
    if (!qtyValid || !actingReady) return
    setConfirming(true)
  }

  const confirm = () => {
    void action.run(async () => {
      await markReceived(item.id, { quantity: qty, userName: userName.trim() })
      setConfirming(false)
      onDone(`${item.partSku} marked received, ${qty} added to stock.`)
    })
  }

  if (confirming) {
    return (
      <div className="reorder-confirm">
        <p>This adds {qty} to stock for {item.partSku}.</p>
        <div className="reorder-form">
          <button type="button" disabled={action.busy} onClick={confirm}>
            Confirm received
          </button>
          <button type="button" disabled={action.busy} onClick={() => setConfirming(false)}>
            Back
          </button>
        </div>
        {action.error && (
          <div className="reorder-alert error reorder-row-error" role="alert">
            <strong>Action refused</strong>
            <p>{action.error}</p>
          </div>
        )}
      </div>
    )
  }

  return (
    <form className="reorder-form" onSubmit={review}>
      <label>
        Quantity{' '}
        <input
          type="number"
          required
          min={1}
          step={1}
          value={quantity}
          onChange={(e) => setQuantity(e.target.value)}
          aria-label={`Quantity received for ${item.partSku}`}
        />
      </label>
      <button type="submit" disabled={!actingReady || !qtyValid || action.busy}>
        Mark received
      </button>
      {!actingReady && <span className="reorder-hint">Enter your name to enable actions.</span>}
    </form>
  )
}
