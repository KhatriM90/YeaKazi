import { useState } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import { useSession } from '../auth/session'
import { applyTheme, savedTheme, type ThemeChoice } from './theme'

/** Slot for the tenant's logo (portal header and payslip). Upload arrives with documents (DOC-01). */
export function TenantLogo({ name, src }: { name: string; src?: string | null }) {
  const initials = name.split(/\s+/).map((w) => w[0]).join('').slice(0, 2).toUpperCase()
  return (
    <div className="logo-slot" aria-label={`${name} logo`}>
      {src ? <img src={src} alt="" /> : <span aria-hidden>{initials}</span>}
    </div>
  )
}

export function ThemeToggle() {
  const [theme, setTheme] = useState<ThemeChoice>(savedTheme())
  return (
    <label className="row" style={{ gap: 6 }}>
      <span className="sr-only">Theme</span>
      <select value={theme} onChange={(e) => { const t = e.target.value as ThemeChoice; setTheme(t); applyTheme(t) }} aria-label="Theme">
        <option value="system">System theme</option>
        <option value="light">Light</option>
        <option value="dark">Dark</option>
      </select>
    </label>
  )
}

export function AppShell() {
  const { me, can, signOut } = useSession()
  if (!me) return null
  return (
    <div className="shell">
      <a href="#main" className="skip-link">Skip to content</a>
      <aside className="sidebar">
        <div className="brand">
          <img src="/favicon.svg" alt="" width={32} height={32} />
          <div>YeaKazi<small>by Yeasoft</small></div>
        </div>
        <nav aria-label="Main">
          <NavLink to="/" end>Home</NavLink>
          {can('people.read') && <NavLink to="/people" end>Employees</NavLink>}
          {can('people.write') && <NavLink to="/people/approvals">Approvals</NavLink>}
          {can('people.write') && <NavLink to="/people/import">Import</NavLink>}
          {can('people.read') && <NavLink to="/people/bank-branches">Bank branches</NavLink>}
          {can('org.read') && <NavLink to="/organization">Organization</NavLink>}
          {can('users.read') && <NavLink to="/users">Users</NavLink>}
          {can('users.read') && <NavLink to="/roles">Roles</NavLink>}
          {can('audit.read') && <NavLink to="/audit">Audit log</NavLink>}
          <NavLink to="/security">Sign-in security</NavLink>
        </nav>
      </aside>
      <div className="main">
        <header className="topbar">
          <div className="who">
            <TenantLogo name={me.tenantName} />
            <div>
              <div style={{ fontWeight: 600 }}>{me.tenantName}</div>
              <div className="muted" style={{ fontSize: '0.85rem' }}>{me.displayName}</div>
            </div>
          </div>
          <div className="row">
            <ThemeToggle />
            <button className="btn-secondary" onClick={signOut}>Sign out</button>
          </div>
        </header>
        <main id="main" className="content">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
