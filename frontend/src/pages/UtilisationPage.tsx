import { useState } from 'react'
import { getUtilisation } from '../api/reports'
import PageState from '../components/PageState'
import { useLoad } from '../components/useLoad'

export default function UtilisationPage() {
  const [from, setFrom] = useState('2026-09-01')
  const [to, setTo] = useState('2026-09-30')
  const { data, loading, error } = useLoad(() => getUtilisation(from, to), [from, to])

  return (
    <section>
      <h1>Engineer utilisation</h1>
      <label>From <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label>{' '}
      <label>To <input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
      <PageState loading={loading} error={error} empty={data?.length === 0}>
        <table>
          <thead>
            <tr><th>Engineer</th><th>Shifts</th><th>Hours worked</th><th>Hours available</th><th>Utilisation</th></tr>
          </thead>
          <tbody>
            {data?.map((r) => (
              <tr key={r.engineerId}>
                <td>{r.engineerName}</td>
                <td>{r.shiftsRostered}</td>
                <td>{r.hoursWorked}</td>
                <td>{r.hoursAvailable}</td>
                <td>{r.utilisationPct}%</td>
              </tr>
            ))}
          </tbody>
        </table>
      </PageState>
    </section>
  )
}
