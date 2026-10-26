import { listEngineers } from '../api/engineers'
import PageState from '../components/PageState'
import { useLoad } from '../components/useLoad'

export default function EngineersPage() {
  const { data, loading, error } = useLoad(() => listEngineers(), [])

  return (
    <section>
      <h1>Engineers</h1>
      <PageState loading={loading} error={error} empty={data?.length === 0}>
        <table>
          <thead>
            <tr><th>No.</th><th>Name</th><th>Grade</th><th>Region</th><th>Skills</th><th>Active</th></tr>
          </thead>
          <tbody>
            {data?.map((e) => (
              <tr key={e.id}>
                <td>{e.employeeNo}</td>
                <td>{e.fullName}</td>
                <td>{e.grade}</td>
                <td>{e.regionId}</td>
                <td>{e.skillIds.length}</td>
                <td>{e.active ? 'Yes' : 'No'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </PageState>
    </section>
  )
}
