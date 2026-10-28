import type { DispatchCell, DispatchEngineerRow } from '../../api/dispatch'
import JobChip from './JobChip'
import { dayHeading, formatShiftRange, sortByStart } from './dispatchUtils'

function DayCell({ cell }: { cell: DispatchCell | undefined }) {
  if (!cell || !cell.shiftStart || !cell.shiftEnd) {
    return (
      <td className="cell cell-no-shift">
        <span className="muted">No shift</span>
      </td>
    )
  }
  const jobs = sortByStart(cell.jobs)
  return (
    <td className="cell">
      <div className="shift-hours">
        {formatShiftRange(cell.shiftStart, cell.shiftEnd)}
        {cell.breakMinutes > 0 && <span className="muted"> ({cell.breakMinutes} min break)</span>}
      </div>
      {jobs.length === 0 ? (
        <span className="muted">No jobs</span>
      ) : (
        <ul className="cell-jobs">
          {jobs.map((job) => (
            <li key={job.id}>
              <JobChip job={job} />
            </li>
          ))}
        </ul>
      )}
    </td>
  )
}

interface Props {
  days: string[]
  engineers: DispatchEngineerRow[]
  /** Region id to name. Missing ids fall back to the id. */
  regionNames: Map<number, string>
  /** Skill id to name. Missing ids fall back to the id. */
  skillNames: Map<number, string>
}

export default function DispatchGrid({ days, engineers, regionNames, skillNames }: Props) {
  return (
    <div className="dispatch-scroll">
      <table className="dispatch-grid">
        <caption className="visually-hidden">Engineer schedule by day</caption>
        <thead>
          <tr>
            <th scope="col" className="sticky-col sticky-head">Engineer</th>
            {days.map((day) => (
              <th scope="col" key={day} className="day-head">{dayHeading(day)}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {engineers.map((row) => (
            <tr key={row.engineerId}>
              <th scope="row" className="sticky-col engineer-cell">
                <div className="engineer-name">{row.fullName}</div>
                <div className="muted">{regionNames.get(row.regionId) ?? `Region ${row.regionId}`}</div>
                <ul className="skill-tags" aria-label="Skills">
                  {row.skillIds.map((id) => (
                    <li key={id} className="skill-tag">{skillNames.get(id) ?? `Skill ${id}`}</li>
                  ))}
                </ul>
              </th>
              {days.map((day) => (
                <DayCell key={day} cell={row.cells.find((c) => c.date === day)} />
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
