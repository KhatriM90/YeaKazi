import { useCallback, useEffect, useState } from 'react'
import { ApiError, api, call, messageFor, type Schemas } from '../../api/client'
import { useSession } from '../../auth/session'
import { Dialog } from '../../components/Dialog'
import { SelectField, TextField, type Option } from '../../components/Fields'
import { Pager } from '../../components/Pager'

type User = Schemas['UserDto']

export function UsersPage() {
  const { can, me } = useSession()
  const [data, setData] = useState<Schemas['PagedResultOfUserDto'] | null>(null)
  const [roles, setRoles] = useState<Schemas['RoleDto'][]>([])
  const [employers, setEmployers] = useState<Option[]>([])
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [invite, setInvite] = useState<{ email: string; displayName: string; roleId: string; legalEntityId: string } | null>(null)
  const [grant, setGrant] = useState<{ user: User; roleId: string; legalEntityId: string } | null>(null)
  const [disable, setDisable] = useState<{ user: User; reason: string } | null>(null)
  const [resetMfa, setResetMfa] = useState<{ user: User; reason: string } | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const manage = can('users.manage')

  const load = useCallback(async () => {
    try {
      setData(await call(api.GET('/api/v1/users', { params: { query: { search: search || undefined, page, pageSize: 25 } } })))
    } catch (err) {
      setError(messageFor(err))
    }
  }, [search, page])

  useEffect(() => { void load() }, [load])
  useEffect(() => {
    call(api.GET('/api/v1/roles')).then(setRoles).catch(() => setRoles([]))
    call(api.GET('/api/v1/org/legal-entities', { params: { query: { pageSize: 200 } } }))
      .then((r) => setEmployers(r.items.map((x) => ({ value: x.id, label: `${x.code} · ${x.name}` })))).catch(() => setEmployers([]))
  }, [])

  const roleOptions = roles.map((r) => ({ value: r.id, label: r.name }))
  const employerName = (id?: string | null) => (id ? employers.find((e) => e.value === id)?.label ?? 'One employer' : 'All employers')

  async function run(action: () => Promise<unknown>, done: string, close: () => void) {
    setError(null)
    setFieldErrors({})
    try {
      await action()
      close()
      setNotice(done)
      await load()
    } catch (err) {
      if (err instanceof ApiError) setFieldErrors(err.fieldErrors())
      setFieldErrors((e) => ({ ...e, _form: messageFor(err) }))
    }
  }

  return (
    <div className="stack">
      <div className="spread">
        <h1>Users</h1>
        {manage && <button className="btn-primary" onClick={() => { setFieldErrors({}); setInvite({ email: '', displayName: '', roleId: '', legalEntityId: '' }) }}>Invite user</button>}
      </div>
      {notice && <div className="alert alert-success" role="status">{notice}</div>}
      {error && <div className="alert alert-error" role="alert">{error}</div>}
      <div className="card stack">
        <div style={{ maxWidth: 320 }}><TextField label="Search" value={search} onChange={(v) => { setSearch(v); setPage(1) }} placeholder="Name or email" /></div>
        <div className="table-wrap">
          <table>
            <thead><tr><th>Name</th><th>Email</th><th>Status</th><th>MFA</th><th>Roles</th><th><span className="sr-only">Actions</span></th></tr></thead>
            <tbody>
              {data?.items.map((u) => (
                <tr key={u.id}>
                  <td>{u.displayName}</td>
                  <td>{u.email}</td>
                  <td><span className={`badge ${u.status === 'active' ? 'badge-ok' : 'badge-muted'}`}>{u.status}</span></td>
                  <td>{u.mfaEnabled ? 'On' : 'Off'}</td>
                  <td>
                    <ul style={{ margin: 0, paddingLeft: 16 }}>
                      {u.roles.map((r) => (
                        <li key={r.id}>
                          {r.roleName} <span className="muted">({employerName(r.legalEntityId)})</span>{' '}
                          {manage && u.id !== me?.userId && (
                            <button className="btn-link" onClick={() => {
                              const reason = window.prompt('Reason for removing this role (recorded in the audit log)')
                              if (reason) void run(() => call(api.POST('/api/v1/users/{id}/roles/{assignmentId}/revoke', { params: { path: { id: u.id, assignmentId: r.id } }, body: { reason } })), 'Role removed.', () => {})
                            }}>Remove</button>
                          )}
                        </li>
                      ))}
                    </ul>
                  </td>
                  <td className="row">
                    {manage && u.id !== me?.userId && <button className="btn-link" onClick={() => { setFieldErrors({}); setGrant({ user: u, roleId: '', legalEntityId: '' }) }}>Add role</button>}
                    {manage && u.id !== me?.userId && u.status !== 'disabled' && <button className="btn-link" onClick={() => { setFieldErrors({}); setDisable({ user: u, reason: '' }) }}>Disable</button>}
                    {manage && u.id !== me?.userId && u.mfaEnabled && <button className="btn-link" onClick={() => { setFieldErrors({}); setResetMfa({ user: u, reason: '' }) }}>Reset MFA</button>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {data && <Pager page={Number(data.page)} pageSize={Number(data.pageSize)} total={Number(data.total)} onPage={setPage} />}
      </div>

      <Dialog open={!!invite} title="Invite user" submitLabel="Send invitation" onClose={() => setInvite(null)}
        onSubmit={() => invite && run(() => call(api.POST('/api/v1/users', { body: {
          email: invite.email, displayName: invite.displayName,
          roles: invite.roleId ? [{ roleId: invite.roleId, legalEntityId: invite.legalEntityId || null }] : [],
        } })), `Invitation sent to ${invite.email}.`, () => setInvite(null))}>
        {fieldErrors._form && <div className="alert alert-error" role="alert">{fieldErrors._form}</div>}
        {invite && (
          <>
            <TextField label="Email" type="email" value={invite.email} onChange={(v) => setInvite({ ...invite, email: v })} required error={fieldErrors.email} />
            <TextField label="Display name" value={invite.displayName} onChange={(v) => setInvite({ ...invite, displayName: v })} required error={fieldErrors.displayName} />
            <SelectField label="Role" value={invite.roleId} onChange={(v) => setInvite({ ...invite, roleId: v })} options={roleOptions} placeholder="No role yet" />
            <SelectField label="Applies to" value={invite.legalEntityId} onChange={(v) => setInvite({ ...invite, legalEntityId: v })} options={employers} placeholder="All employers" />
          </>
        )}
      </Dialog>

      <Dialog open={!!grant} title={`Add role for ${grant?.user.displayName ?? ''}`} onClose={() => setGrant(null)}
        onSubmit={() => grant && run(() => call(api.POST('/api/v1/users/{id}/roles', { params: { path: { id: grant.user.id } }, body: { roleId: grant.roleId, legalEntityId: grant.legalEntityId || null } })), 'Role added.', () => setGrant(null))}>
        {fieldErrors._form && <div className="alert alert-error" role="alert">{fieldErrors._form}</div>}
        {grant && (
          <>
            <SelectField label="Role" value={grant.roleId} onChange={(v) => setGrant({ ...grant, roleId: v })} options={roleOptions} placeholder="Choose a role" required />
            <SelectField label="Applies to" value={grant.legalEntityId} onChange={(v) => setGrant({ ...grant, legalEntityId: v })} options={employers} placeholder="All employers" />
            <p className="hint">Roles that change data or approve require the user to sign in with MFA.</p>
          </>
        )}
      </Dialog>

      <Dialog open={!!disable} danger title={`Disable ${disable?.user.displayName ?? ''}`} submitLabel="Disable user" onClose={() => setDisable(null)}
        onSubmit={() => disable && run(() => call(api.POST('/api/v1/users/{id}/disable', { params: { path: { id: disable.user.id } }, body: { version: Number(disable.user.version), reason: disable.reason } })), 'User disabled. Their sessions end immediately.', () => setDisable(null))}>
        {fieldErrors._form && <div className="alert alert-error" role="alert">{fieldErrors._form}</div>}
        {disable && <TextField label="Reason (recorded in the audit log)" value={disable.reason} onChange={(v) => setDisable({ ...disable, reason: v })} required error={fieldErrors.reason} />}
      </Dialog>

      <Dialog open={!!resetMfa} danger title={`Reset multi-factor sign-in for ${resetMfa?.user.displayName ?? ''}`} submitLabel="Reset MFA" onClose={() => setResetMfa(null)}
        onSubmit={() => resetMfa && run(() => call(api.POST('/api/v1/users/{id}/mfa/reset', { params: { path: { id: resetMfa.user.id } }, body: { version: Number(resetMfa.user.version), reason: resetMfa.reason } })), 'MFA reset. They are signed out and must set up an authenticator at their next sign-in. They were notified by email.', () => setResetMfa(null))}>
        {fieldErrors._form && <div className="alert alert-error" role="alert">{fieldErrors._form}</div>}
        <p className="muted">Use this only after confirming the person's identity, for example in person or by a call-back to a known number. Their authenticator and recovery codes stop working.</p>
        {resetMfa && <TextField label="How you verified their identity (recorded in the audit log)" value={resetMfa.reason} onChange={(v) => setResetMfa({ ...resetMfa, reason: v })} required error={fieldErrors.reason} />}
      </Dialog>
    </div>
  )
}
