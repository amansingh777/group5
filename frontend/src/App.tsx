import { NavLink, Route, Routes } from 'react-router-dom'
import JobsPage from './pages/JobsPage'
import EngineersPage from './pages/EngineersPage'
import CustomersPage from './pages/CustomersPage'
import UtilisationPage from './pages/UtilisationPage'
import ImportPage from './pages/ImportPage'

export default function App() {
  return (
    <div className="shell">
      <header className="topbar">
        <strong>Corrin Field Service</strong>
        <nav>
          <NavLink to="/">Jobs</NavLink>
          <NavLink to="/engineers">Engineers</NavLink>
          <NavLink to="/customers">Customers</NavLink>
          <NavLink to="/reports/utilisation">Utilisation</NavLink>
          <NavLink to="/import">Import</NavLink>
        </nav>
      </header>
      <main>
        <Routes>
          <Route path="/" element={<JobsPage />} />
          <Route path="/engineers" element={<EngineersPage />} />
          <Route path="/customers" element={<CustomersPage />} />
          <Route path="/reports/utilisation" element={<UtilisationPage />} />
          <Route path="/import" element={<ImportPage />} />
        </Routes>
      </main>
    </div>
  )
}
