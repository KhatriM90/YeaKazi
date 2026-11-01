import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, call, type Schemas } from '../api/client'
import { useSession } from '../auth/session'
import { PERMIT_ALERT_LABEL, formatDate } from './people/model'

/** Role-aware home. Payroll and leave widgets join with their slices. */
export function HomePage() {
  const { me, can } = useSession()
  if (!me) return null
  const tiles = [
    can('people.read') && { to: '/people', title: 'Employees', text: 'Employee records, Kenyan identifiers, job history and payment details.' },
    can('people.write') && { to: '/people/import', title: 'Import employees', text: 'Upload a CSV, fix row errors, then a colleague commits it.' },
    can('org.read') && { to: '/organization', title: 'Organization setup', text: 'Legal employers, branches, departments, positions, grades, cost centers, calendars and schedules.' },
    can('users.read') && { to: '/users', title: 'Users', text: 'Invite people, assign roles per employer and disable access.' },
    can('users.read') && { to: '/roles', title: 'Roles', text: 'System roles and custom roles built from the permission catalogue.' },
    can('audit.read') && { to: '/audit', title: 'Audit log', text: 'Search every change and verify the tamper-evident hash chain.' },
  ].filter(Boolean) as { to: string; title: string; text: string }[]
  const lowCodes = me.mfaEnabled && Number(me.recoveryCodesRemaining) <= 3

  return (
    <div className="stack">
      <h1>Karibu, {me.displayName.split(' ')[0]}</h1>
      <p className="muted">You are signed in to {me.tenantName}{me.mfaEnabled ? ' with multi-factor sign-in' : ''}.</p>
      {lowCodes && <div className="alert alert-info">You have {Number(me.recoveryCodesRemaining)} recovery codes left. <Link to="/security">Generate new ones</Link>.</div>}
      {can('people.read') && <PermitAlerts />}
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

/** EMP-03 work permit expiry alerts (60/30/7 days and expired). */
function PermitAlerts() {
  const [alerts, setAlerts] = useState<Schemas['WorkPermitAlertDto'][]>([])
  useEffect(() => { call(api.GET('/api/v1/people/work-permit-alerts')).then(setAlerts).catch(() => setAlerts([])) }, [])
  if (alerts.length === 0) return null
  return (
    <section className="card stack">
      <h2>Work permits needing attention</h2>
      <table>
        <thead><tr><th>Employee</th><th>Expiry</th><th>Status</th></tr></thead>
        <tbody>
          {alerts.map((a) => (
            <tr key={a.employeeId}>
              <td><Link to={`/people/${a.employeeId}`}>{a.fullName}</Link> <span className="mono muted">{a.employeeNumber}</span></td>
              <td>{formatDate(a.workPermitExpiry)}</td>
              <td><span className={a.alert === 'expired' ? 'badge badge-warn' : 'badge'}>{PERMIT_ALERT_LABEL[a.alert] ?? a.alert}</span></td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  )
}
