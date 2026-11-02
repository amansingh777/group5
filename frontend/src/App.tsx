import { NavLink, Route, Routes } from 'react-router-dom'
import JobsPage from './pages/JobsPage'
import EngineersPage from './pages/EngineersPage'
import CustomersPage from './pages/CustomersPage'
import UtilisationPage from './pages/UtilisationPage'
import ImportPage from './pages/ImportPage'
import DispatchBoardPage from './pages/DispatchBoardPage'
import WorkOrdersPage from './pages/WorkOrdersPage'
import WorkOrderDetailPage from './pages/WorkOrderDetailPage'
import FirstTimeFixPage from './pages/FirstTimeFixPage'
import ReorderRequestsPage from './pages/ReorderRequestsPage'

export default function App() {
  return (
    <div className="shell">
      <header className="topbar">
        <strong>Corrin Field Service</strong>
        <nav>
          <NavLink to="/">Jobs</NavLink>
          <NavLink to="/engineers">Engineers</NavLink>
          <NavLink to="/customers">Customers</NavLink>
          <NavLink to="/work-orders">Work orders</NavLink>
          <NavLink to="/dispatch">Dispatch</NavLink>
          <NavLink to="/reorder">Reorder</NavLink>
          <NavLink to="/reports/utilisation">Utilisation</NavLink>
          <NavLink to="/reports/first-time-fix">First-time fix</NavLink>
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
          <Route path="/dispatch" element={<DispatchBoardPage />} />
          <Route path="/work-orders" element={<WorkOrdersPage />} />
          <Route path="/work-orders/:id" element={<WorkOrderDetailPage />} />
          <Route path="/reports/first-time-fix" element={<FirstTimeFixPage />} />
          <Route path="/reorder" element={<ReorderRequestsPage />} />
        </Routes>
      </main>
    </div>
  )
}
