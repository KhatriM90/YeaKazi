import { useEffect, useState } from 'react'
import { ApiError, api, call, messageFor, type Schemas } from '../../api/client'
import { useSession } from '../../auth/session'
import { Dialog } from '../../components/Dialog'
import { Checkbox, TextField } from '../../components/Fields'

export function RolesPage() {
  const { can } = useSession()
  const [roles, setRoles] = useState<Schemas['RoleDto'][]>([])
  const [catalogue, setCatalogue] = useState<Schemas['PermissionDto'][]>([])
  const [draft, setDraft] = useState<{ code: string; name: string; description: string; permissions: string[] } | null>(null)
  const [errors, setErrors] = useState<Record<string, string>>({})

  const load = () => call(api.GET('/api/v1/roles')).then(setRoles)
  useEffect(() => {
    void load()
    call(api.GET('/api/v1/roles/permissions')).then(setCatalogue).catch(() => setCatalogue([]))
  }, [])

  async function create() {
    if (!draft) return
    setErrors({})
    try {
      await call(api.POST('/api/v1/roles', { body: { code: draft.code, name: draft.name, description: draft.description || null, permissions: draft.permissions } }))
      setDraft(null)
      await load()
    } catch (err) {
      setErrors({ ...(err instanceof ApiError ? err.fieldErrors() : {}), _form: messageFor(err) })
    }
  }

  const describe = (p: string) => catalogue.find((c) => c.code === p)?.description ?? p

  return (
    <div className="stack">
      <div className="spread">
        <h1>Roles</h1>
        {can('roles.manage') && <button className="btn-primary" onClick={() => setDraft({ code: '', name: '', description: '', permissions: [] })}>New custom role</button>}
      </div>
      <p className="muted">System roles follow the separation of duties in the specification and cannot be edited. Build custom roles from the permission catalogue.</p>
      <div className="grid-2">
        {roles.map((r) => (
          <article key={r.id} className="card stack">
            <div className="spread">
              <h2>{r.name}</h2>
              <span className={`badge ${r.isSystem ? 'badge-muted' : 'badge-ok'}`}>{r.isSystem ? 'System' : 'Custom'}</span>
            </div>
            {r.description && <p className="muted">{r.description}</p>}
            <ul style={{ margin: 0, paddingLeft: 18 }}>
              {r.permissions.map((p) => <li key={p}>{describe(p)}</li>)}
            </ul>
          </article>
        ))}
      </div>
      <Dialog open={!!draft} title="New custom role" onClose={() => setDraft(null)} onSubmit={create} submitLabel="Create role">
        {errors._form && <div className="alert alert-error" role="alert">{errors._form}</div>}
        {draft && (
          <>
            <TextField label="Code" value={draft.code} onChange={(v) => setDraft({ ...draft, code: v })} required hint="Lowercase letters, digits and underscores" error={errors.code} />
            <TextField label="Name" value={draft.name} onChange={(v) => setDraft({ ...draft, name: v })} required error={errors.name} />
            <TextField label="Description" value={draft.description} onChange={(v) => setDraft({ ...draft, description: v })} />
            <fieldset>
              <legend>Permissions</legend>
              <div className="stack" style={{ gap: 6 }}>
                {catalogue.map((c) => (
                  <Checkbox key={c.code} checked={draft.permissions.includes(c.code)}
                    onChange={(on) => setDraft({ ...draft, permissions: on ? [...draft.permissions, c.code] : draft.permissions.filter((p) => p !== c.code) })}
                    label={<span>{c.description} {c.requiresMfa && <span className="hint">(requires MFA)</span>}</span>} />
                ))}
              </div>
              {errors.permissions && <span className="field-error">{errors.permissions}</span>}
            </fieldset>
          </>
        )}
      </Dialog>
    </div>
  )
}
