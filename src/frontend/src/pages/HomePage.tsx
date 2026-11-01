import { Link } from 'react-router-dom'
import { useSession } from '../auth/session'

/** Role-aware home. Payroll, leave and people widgets join with their slices. */
export function HomePage() {
  const { me, can } = useSession()
  if (!me) return null
  const tiles = [
    can('org.read') && { to: '/organization', title: 'Organization setup', text: 'Legal employers, branches, departments, positions, grades, cost centers, calendars and schedules.' },
    can('users.read') && { to: '/users', title: 'Users', text: 'Invite people, assign roles per employer and disable access.' },
    can('users.read') && { to: '/roles', title: 'Roles', text: 'System roles and custom roles built from the permission catalogue.' },
    can('audit.read') && { to: '/audit', title: 'Audit log', text: 'Search every change and verify the tamper-evident hash chain.' },
  ].filter(Boolean) as { to: string; title: string; text: string }[]

  return (
    <div className="stack">
      <h1>Karibu, {me.displayName.split(' ')[0]}</h1>
      <p className="muted">You are signed in to {me.tenantName}{me.mfaEnabled ? ' with multi-factor sign-in' : ''}.</p>
      {tiles.length === 0 ? (
        <div className="card">Your employee portal (profile, leave and payslips) arrives with the next releases.</div>
      ) : (
        <div className="grid-2">
          {tiles.map((t) => (
            <Link key={t.to} to={t.to} className="card" style={{ textDecoration: 'none', color: 'inherit' }}>
              <h2>{t.title}</h2>
              <p className="muted">{t.text}</p>
            </Link>
          ))}
        </div>
      )}
    </div>
  )
}
