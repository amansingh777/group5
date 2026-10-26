import { useState } from 'react'
import { listCustomers } from '../api/customers'
import PageState from '../components/PageState'
import { useLoad } from '../components/useLoad'

export default function CustomersPage() {
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const { data, loading, error } = useLoad(() => listCustomers({ search, page }), [search, page])

  return (
    <section>
      <h1>Customers</h1>
      <input
        placeholder="Search name, account or postcode"
        value={search}
        onChange={(e) => { setSearch(e.target.value); setPage(1) }}
      />
      <PageState loading={loading} error={error} empty={data?.items.length === 0}>
        <table>
          <thead>
            <tr><th>Account</th><th>Name</th><th>Type</th><th>City</th><th>Postcode</th></tr>
          </thead>
          <tbody>
            {data?.items.map((c) => (
              <tr key={c.id}>
                <td>{c.accountRef}</td>
                <td>{c.name}</td>
                <td>{c.customerType}</td>
                <td>{c.city}</td>
                <td>{c.postcode}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <div className="pager">
          <button disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
          <span>Page {page} of {data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1}</span>
          <button disabled={!data || page * data.pageSize >= data.total} onClick={() => setPage(page + 1)}>Next</button>
        </div>
      </PageState>
    </section>
  )
}
