import { useState } from 'react'

const STORAGE_KEY = 'corrin.workOrders.actingAs'

/** The API has no authentication, so the acting user's name is typed in and remembered locally. */
export function useActingAs() {
  const [name, setNameState] = useState(() => {
    try {
      return localStorage.getItem(STORAGE_KEY) ?? ''
    } catch {
      return ''
    }
  })

  const setName = (value: string) => {
    setNameState(value)
    try {
      localStorage.setItem(STORAGE_KEY, value)
    } catch {
      // storage unavailable; keep the value in memory only
    }
  }

  return [name, setName] as const
}

export default function ActingAs({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  return (
    <div className="wo-acting">
      <label>
        Acting as{' '}
        <input value={value} placeholder="Your name" onChange={(e) => onChange(e.target.value)} />
      </label>
      {!value.trim() && <span className="wo-hint">Enter your name to enable actions.</span>}
    </div>
  )
}
