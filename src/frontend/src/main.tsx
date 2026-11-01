import '@fontsource-variable/inter'
import './styles/tokens.css'
import './styles/app.css'
import { StrictMode, type ReactNode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { Activate } from './auth/Activate'
import { SessionProvider, useSession } from './auth/session'
import { SignIn } from './auth/SignIn'
import { AppShell } from './layout/AppShell'
import { applyTheme, savedTheme } from './layout/theme'
import { AuditPage } from './pages/admin/AuditPage'
import { RolesPage } from './pages/admin/RolesPage'
import { UsersPage } from './pages/admin/UsersPage'
import { HomePage } from './pages/HomePage'
import { OrganizationPage } from './pages/org/OrganizationPage'
import { PlatformConsole } from './pages/platform/PlatformConsole'
import { SecurityPage } from './pages/account/SecurityPage'
import { ApprovalsPage } from './pages/people/ApprovalsPage'
import { BankBranchesPage } from './pages/people/BankBranchesPage'
import { EmployeePage } from './pages/people/EmployeePage'
import { EmployeesPage } from './pages/people/EmployeesPage'
import { ImportPage } from './pages/people/ImportPage'

applyTheme(savedTheme())

function RequireTenant({ children }: { children: ReactNode }) {
  const { scope, me, loading } = useSession()
  if (loading) return <p className="center-page">Loading…</p>
  if (scope !== 'tenant' || !me) return <Navigate to="/login" replace />
  return children
}

function RequirePlatform({ children }: { children: ReactNode }) {
  const { scope } = useSession()
  return scope === 'platform' ? children : <Navigate to="/platform/login" replace />
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <SessionProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<SignIn />} />
          <Route path="/activate" element={<Activate />} />
          <Route path="/platform/login" element={<SignIn platform />} />
          <Route path="/platform" element={<RequirePlatform><PlatformConsole /></RequirePlatform>} />
          <Route element={<RequireTenant><AppShell /></RequireTenant>}>
            <Route index element={<HomePage />} />
            <Route path="/organization" element={<OrganizationPage />} />
            <Route path="/users" element={<UsersPage />} />
            <Route path="/roles" element={<RolesPage />} />
            <Route path="/audit" element={<AuditPage />} />
            <Route path="/security" element={<SecurityPage />} />
            <Route path="/people" element={<EmployeesPage />} />
            <Route path="/people/approvals" element={<ApprovalsPage />} />
            <Route path="/people/import" element={<ImportPage />} />
            <Route path="/people/bank-branches" element={<BankBranchesPage />} />
            <Route path="/people/:id" element={<EmployeePage />} />
          </Route>
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </SessionProvider>
  </StrictMode>,
)
