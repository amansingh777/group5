import type { ReactNode } from 'react'

interface Props {
  loading: boolean
  error: string | null
  empty: boolean
  children: ReactNode
}

export default function PageState({ loading, error, empty, children }: Props) {
  if (loading) return <p className="state">Loading...</p>
  if (error) return <p className="state error">Something went wrong: {error}</p>
  if (empty) return <p className="state">Nothing to show.</p>
  return <>{children}</>
}
