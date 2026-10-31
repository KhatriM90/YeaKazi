import { useCallback, useEffect, useState } from 'react'
import { ApiError, api, call, messageFor, type Schemas } from '../../api/client'
import { useSession } from '../../auth/session'
import { ThemeToggle } from '../../layout/AppShell'
import { BrandMark } from '../../layout/BrandMark'
import { Dialog } from '../../components/Dialog'
import { SelectField, TextField } from '../../components/Fields'
import { Pager } from '../../components/Pager'

const blank = () => ({
  name: '', slug: '', hostingRegion: '',
  leCode: 'MAIN', leName: '', registrationNumber: '', kraPin: '', nssf: '', timezone: 'Africa/Nairobi',
  adminEmail: '', adminName: '',
  // One key per form: retries of the same submission are idempotent (SAAS-01).
  idempotencyKey: crypto.randomUUID(),
})

/** SaaS platform console (SAAS-01): tenant registry and provisioning. No tenant HR data is reachable here. */
export function PlatformConsole() {
  const { signOut } = useSession()
  const [data, setData] = useState<Schemas['PagedResultOfTenantSummaryDto'] | null>(null)
  const [regions, setRegions] = useState<string[]>([])
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [form, setForm] = useState<ReturnType<typeof blank> | null>(null)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(async () => {
    setData(await call(api.GET('/api/v1/platform/tenants', { params: { query: { search: search || undefined, page, pageSize: 25 } } })))
  }, [search, page])
  useEffect(() => { void load() }, [load])
  useEffect(() => { call(api.GET('/api/v1/platform/hosting-regions')).then(setRegions).catch(() => setRegions([])) }, [])

  async function provision() {
    if (!form) return
    setBusy(true)
    setErrors({})
    try {
      const res = await call(api.POST('/api/v1/platform/tenants', {
        params: { header: { 'Idempotency-Key': form.idempotencyKey } },
        body: {
          name: form.name, slug: form.slug, hostingRegion: form.hostingRegion,
          legalEntity: { code: form.leCode, name: form.leName, registrationNumber: form.registrationNumber || null, kraPin: form.kraPin || null, nssfEmployerNumber: form.nssf || null, timezone: form.timezone },
          administrator: { email: form.adminEmail, displayName: form.adminName },
        },
      }))
      setForm(null)
      setNotice(`Tenant ${res.slug} is ready. An invitation was emailed to the first administrator.`)
      await load()
    } catch (err) {
      setErrors({ ...(err instanceof ApiError ? err.fieldErrors() : {}), _form: messageFor(err) })
    } finally {
      setBusy(false)
    }
  }

  const f = form
  const set = (k: keyof ReturnType<typeof blank>) => (v: string) => setForm((x) => x && { ...x, [k]: v })

  return (
    <div className="stack" style={{ padding: 24, maxWidth: 1100, margin: '0 auto' }}>
      <div className="spread">
        <BrandMark subtitle="Platform console" />
        <div className="row"><ThemeToggle /><button className="btn-secondary" onClick={signOut}>Sign out</button></div>
      </div>
      <p className="alert alert-info">Platform staff manage tenants and subscriptions without access to tenant HR data.</p>
      {notice && <div className="alert alert-success" role="status">{notice}</div>}
      <div className="card stack">
        <div className="spread">
          <h1>Tenants</h1>
          <button className="btn-primary" onClick={() => { setErrors({}); setForm({ ...blank(), hostingRegion: regions[0] ?? '' }) }}>Provision tenant</button>
        </div>
        <div style={{ maxWidth: 320 }}><TextField label="Search" value={search} onChange={(v) => { setSearch(v); setPage(1) }} /></div>
        <table>
          <thead><tr><th>Name</th><th>Address</th><th>Region</th><th>Status</th><th>Created</th></tr></thead>
          <tbody>
            {data?.items.map((t) => (
              <tr key={t.tenantId}><td>{t.name}</td><td><code>{t.slug}</code></td><td>{t.hostingRegion}</td>
                <td><span className={`badge ${t.status === 'active' ? 'badge-ok' : 'badge-muted'}`}>{t.status}</span></td>
                <td>{new Date(t.createdAt).toLocaleDateString('en-KE')}</td></tr>
            ))}
          </tbody>
        </table>
        {data && <Pager page={Number(data.page)} pageSize={Number(data.pageSize)} total={Number(data.total)} onPage={setPage} />}
      </div>

      <Dialog open={!!f} title="Provision tenant" submitLabel="Provision" onClose={() => setForm(null)} onSubmit={provision} busy={busy}>
        {errors._form && <div className="alert alert-error" role="alert">{errors._form}</div>}
        {f && (
          <>
            <fieldset className="stack">
              <legend>Tenant</legend>
              <TextField label="Customer name" value={f.name} onChange={set('name')} required error={errors.name} />
              <TextField label="Address (slug)" value={f.slug} onChange={(v) => set('slug')(v.toLowerCase())} required hint="Used to sign in, for example acme-kenya" error={errors.slug} />
              <SelectField label="Hosting region" value={f.hostingRegion} onChange={set('hostingRegion')} options={regions.map((r) => ({ value: r, label: r }))} placeholder="Choose a region" required error={errors.hostingRegion} />
            </fieldset>
            <fieldset className="stack">
              <legend>First legal employer</legend>
              <TextField label="Code" value={f.leCode} onChange={set('leCode')} required error={errors['legalEntity.code'] ?? errors.code} />
              <TextField label="Registered name" value={f.leName} onChange={set('leName')} required error={errors['legalEntity.name']} />
              <TextField label="Registration number" value={f.registrationNumber} onChange={set('registrationNumber')} />
              <TextField label="KRA PIN" value={f.kraPin} onChange={(v) => set('kraPin')(v.toUpperCase())} error={errors.kraPin} />
              <TextField label="NSSF employer number" value={f.nssf} onChange={set('nssf')} />
            </fieldset>
            <fieldset className="stack">
              <legend>First tenant administrator</legend>
              <TextField label="Email" type="email" value={f.adminEmail} onChange={set('adminEmail')} required error={errors['administrator.email']} />
              <TextField label="Name" value={f.adminName} onChange={set('adminName')} required error={errors['administrator.displayName']} />
            </fieldset>
          </>
        )}
      </Dialog>
    </div>
  )
}
