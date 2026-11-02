import type { FirstTimeFixRow } from '../../api/firstTimeFix'
import RateBar from './RateBar'
import { sortRows, type SortMode } from './ftfUtils'

interface Props {
  caption: string
  nameHeader: string
  rows: FirstTimeFixRow[]
  sortMode: SortMode
}

export default function FtfTable({ caption, nameHeader, rows, sortMode }: Props) {
  return (
    <div className="ftf-table-block">
      <h2>{caption}</h2>
      {rows.length === 0 ? (
        <p className="state">No rows for this range.</p>
      ) : (
        <table>
          <thead>
            <tr>
              <th>{nameHeader}</th>
              <th className="num">Jobs attended</th>
              <th className="num">Fixed first time</th>
              <th>Rate</th>
            </tr>
          </thead>
          <tbody>
            {sortRows(rows, sortMode).map((r) => (
              <tr key={r.name}>
                <td>{r.name}</td>
                <td className="num">{r.jobsAttended}</td>
                <td className="num">{r.fixedFirstTime}</td>
                <td>
                  <RateBar row={r} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
