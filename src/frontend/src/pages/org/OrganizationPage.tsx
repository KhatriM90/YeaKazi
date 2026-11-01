import { useCallback, useEffect, useState } from 'react'
import { ApiError, api, call, messageFor, type Schemas } from '../../api/client'
import { Checkbox, SelectField, TextField, type Option } from '../../components/Fields'
import { Dialog } from '../../components/Dialog'
import { Pager } from '../../components/Pager'
import { useSession } from '../../auth/session'
import { ALL_SECTIONS, DAY_LABELS, type Lookups, type Paged, type Row, type SectionDef, type Values } from './sections'

const EMPTY: Lookups = { legalEntities: [], branches: [], departments: [], grades: [], costCenters: [], positions: [] }
const toOptions = (rows: { id: string; code: string; name: string }[]): Option[] => rows.map((r) => ({ value: r.id, label: `${r.code} · ${r.name}` }))

async function loadLookups(legalEntityId: string): Promise<Lookups> {
  const q = { legalEntityId, pageSize: 200 }
  const [les, br, dp, gr, cc, ps] = await Promise.all([
    call(api.GET('/api/v1/org/legal-entities', { params: { query: { pageSize: 200 } } })),
    legalEntityId ? call(api.GET('/api/v1/org/branches', { params: { query: q } })) : null,
    legalEntityId ? call(api.GET('/api/v1/org/departments', { params: { query: q } })) : null,
    legalEntityId ? call(api.GET('/api/v1/org/grades', { params: { query: q } })) : null,
    legalEntityId ? call(api.GET('/api/v1/org/cost-centers', { params: { query: q } })) : null,
    legalEntityId ? call(api.GET('/api/v1/org/positions', { params: { query: q } })) : null,
  ])
  return {
    legalEntities: toOptions(les.items),
    branches: toOptions(br?.items ?? []),
    departments: toOptions(dp?.items ?? []),
    grades: toOptions(gr?.items ?? []),
    costCenters: toOptions(cc?.items ?? []),
    positions: toOptions(ps?.items ?? []),
  }
}

/** ORG-01 organization setup screen. */
export function OrganizationPage() {
  const [active, setActive] = useState(ALL_SECTIONS[0].key)
  const [legalEntityId, setLegalEntityId] = useState('')
  const [lookups, setLookups] = useState<Lookups>(EMPTY)
  const [version, setVersion] = useState(0)

  const refreshLookups = useCallback(() => setVersion((v) => v + 1), [])

  useEffect(() => {
    loadLookups(legalEntityId).then((l) => {
      setLookups(l)
      if (!legalEntityId && l.legalEntities.length > 0) setLegalEntityId(l.legalEntities[0].value)
    }).catch(() => setLookups(EMPTY))
  }, [legalEntityId, version])

  const section = ALL_SECTIONS.find((s) => s.key === active)!
  return (
    <div className="stack">
      <div className="spread">
        <h1>Organization setup</h1>
        {section.employerScoped && (
          <div style={{ minWidth: 280 }}>
            <SelectField label="Legal employer" value={legalEntityId} onChange={setLegalEntityId} options={lookups.legalEntities} />
          </div>
        )}
      </div>
      <div className="tabs" role="tablist" aria-label="Organization sections">
        {ALL_SECTIONS.map((s) => (
          <button key={s.key} role="tab" aria-selected={s.key === active} onClick={() => setActive(s.key)}>{s.title}</button>
        ))}
      </div>
      <CrudSection key={`${section.key}:${legalEntityId}`} def={section} legalEntityId={legalEntityId} lookups={lookups} onChanged={refreshLookups} />
    </div>
  )
}

function CrudSection({ def, legalEntityId, lookups, onChanged }: { def: SectionDef; legalEntityId: string; lookups: Lookups; onChanged: () => void }) {
  const { can } = useSession()
  const [data, setData] = useState<Paged<Row> | null>(null)
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<{ row: Row | null; values: Values } | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const [holidaysFor, setHolidaysFor] = useState<Row | null>(null)

  const load = useCallback(async () => {
    if (def.employerScoped && !legalEntityId) return
    try {
      setError(null)
      setData(await def.list({ legalEntityId: def.employerScoped ? legalEntityId : undefined, includeInactive, search: search || undefined, page, pageSize: 25 }))
    } catch (err) {
      setError(messageFor(err))
    }
  }, [def, legalEntityId, includeInactive, search, page])

  useEffect(() => { void load() }, [load])

  function openCreate() {
    const values: Values = { isActive: true }
    for (const f of def.fields) values[f.name] = f.kind === 'days' ? [1, 2, 3, 4, 5] : f.name === 'timezone' ? 'Africa/Nairobi' : f.name === 'legalEntityId' ? legalEntityId : ''
    setFieldErrors({})
    setEditing({ row: null, values })
  }

  async function save() {
    if (!editing) return
    setBusy(true)
    setFieldErrors({})
    try {
      if (editing.row) await def.update(editing.row, editing.values)
      else await def.create(editing.values, (editing.values.legalEntityId as string) || legalEntityId)
      setEditing(null)
      onChanged()
      await load()
    } catch (err) {
      if (err instanceof ApiError) setFieldErrors(err.fieldErrors())
      setFieldErrors((e) => ({ ...e, _form: messageFor(err) }))
    } finally {
      setBusy(false)
    }
  }

  const writable = can('org.write')
  const set = (name: string, value: string | boolean | number[]) => setEditing((e) => e && { ...e, values: { ...e.values, [name]: value } })

  return (
    <section className="card stack" aria-labelledby={`${def.key}-title`}>
      <div className="spread">
        <h2 id={`${def.key}-title`}>{def.title}</h2>
        {writable && <button className="btn-primary" onClick={openCreate}>Add {def.singular}</button>}
      </div>
      <div className="row">
        <div style={{ minWidth: 240 }}><TextField label="Search" value={search} onChange={(v) => { setSearch(v); setPage(1) }} placeholder="Code or name" /></div>
        <Checkbox label="Show inactive" checked={includeInactive} onChange={(v) => { setIncludeInactive(v); setPage(1) }} />
      </div>
      {error && <div className="alert alert-error" role="alert">{error}</div>}
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              {def.columns.map((c) => <th key={c.header} scope="col">{c.header}</th>)}
              <th scope="col">Status</th>
              <th scope="col"><span className="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            {data?.items.map((row) => (
              <tr key={row.id}>
                {def.columns.map((c) => <td key={c.header}>{c.render(row, lookups) ?? '—'}</td>)}
                <td><span className={`badge ${row.isActive ? 'badge-ok' : 'badge-muted'}`}>{row.isActive ? 'Active' : 'Inactive'}</span></td>
                <td className="row">
                  {writable && <button className="btn-link" onClick={() => { setFieldErrors({}); setEditing({ row, values: def.toValues(row) }) }}>Edit</button>}
                  {def.key === 'holiday-calendars' && <button className="btn-link" onClick={() => setHolidaysFor(row)}>Holidays</button>}
                </td>
              </tr>
            ))}
            {data && data.items.length === 0 && (
              <tr><td colSpan={def.columns.length + 2} className="muted">Nothing here yet.</td></tr>
            )}
          </tbody>
        </table>
      </div>
      {data && <Pager page={Number(data.page)} pageSize={Number(data.pageSize)} total={Number(data.total)} onPage={setPage} />}

      <Dialog open={!!editing} title={editing?.row ? `Edit ${def.singular}` : `Add ${def.singular}`} onClose={() => setEditing(null)} onSubmit={save} busy={busy}>
        {fieldErrors._form && <div className="alert alert-error" role="alert">{fieldErrors._form}</div>}
        {editing && def.fields.map((f) => {
          if (f.createOnly && editing.row) {
            return f.name === 'code' ? <p key={f.name} className="muted">Code: <strong>{editing.row.code}</strong></p> : null
          }
          const value = editing.values[f.name]
          if (f.kind === 'select') {
            const options = (f.options?.(lookups) ?? []).filter((o) => o.value !== editing.row?.id)
            return <SelectField key={f.name} label={f.label} value={(value as string) ?? ''} onChange={(v) => set(f.name, v)} options={options}
              placeholder={f.required ? undefined : 'None'} required={f.required} hint={f.hint} error={fieldErrors[f.name]} />
          }
          if (f.kind === 'days') {
            const days = (value as number[]) ?? []
            return (
              <fieldset key={f.name}>
                <legend>{f.label}</legend>
                <div className="row">
                  {[1, 2, 3, 4, 5, 6, 7].map((d) => (
                    <Checkbox key={d} label={DAY_LABELS[d]} checked={days.includes(d)}
                      onChange={(on) => set(f.name, on ? [...days, d].sort() : days.filter((x) => x !== d))} />
                  ))}
                </div>
                {fieldErrors[f.name] && <span className="field-error">{fieldErrors[f.name]}</span>}
              </fieldset>
            )
          }
          return <TextField key={f.name} label={f.label} type={f.kind === 'number' ? 'number' : f.kind === 'time' ? 'time' : 'text'}
            value={(value as string) ?? ''} onChange={(v) => set(f.name, v)} required={f.required} hint={f.hint} error={fieldErrors[f.name]} />
        })}
        {editing?.row && (
          <>
            <Checkbox label="Active" checked={editing.values.isActive === true} onChange={(v) => set('isActive', v)} />
            <TextField label="Reason for change (recorded in the audit log)" value={(editing.values.reason as string) ?? ''} onChange={(v) => set('reason', v)} />
          </>
        )}
      </Dialog>

      {holidaysFor && <HolidaysDialog calendar={holidaysFor} onClose={() => setHolidaysFor(null)} writable={writable} />}
    </section>
  )
}

function HolidaysDialog({ calendar, onClose, writable }: { calendar: Row; onClose: () => void; writable: boolean }) {
  const [year, setYear] = useState(String(new Date().getFullYear()))
  const [rows, setRows] = useState<Schemas['HolidayDto'][]>([])
  const [date, setDate] = useState('')
  const [name, setName] = useState('')
  const [kind, setKind] = useState('short_notice')
  const [sourceReference, setSourceReference] = useState('')
  const [allCalendars, setAllCalendars] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const load = useCallback(async () => {
    setRows(await call(api.GET('/api/v1/org/holiday-calendars/{id}/holidays', { params: { path: { id: calendar.id }, query: { year: Number(year) } } })))
  }, [calendar.id, year])
  useEffect(() => { void load().catch((e) => setError(messageFor(e))) }, [load])

  async function add() {
    setError(null)
    setNotice(null)
    try {
      const source = sourceReference.trim() || null
      if (kind !== 'company' && allCalendars) {
        // A government announcement applies to every calendar the user may change.
        const r = await call(api.POST('/api/v1/org/holidays/announce', { body: { date, name, kind, sourceReference: source ?? '', calendarIds: null } }))
        setNotice(`Added to ${r.added.length} calendar${r.added.length === 1 ? '' : 's'}${r.skippedCalendarIds.length ? `; ${r.skippedCalendarIds.length} already had this date` : ''}.`)
      } else {
        await call(api.POST('/api/v1/org/holiday-calendars/{id}/holidays', { params: { path: { id: calendar.id } }, body: { date, name, kind, sourceReference: source } }))
      }
      setDate('')
      setName('')
      setSourceReference('')
      await load()
    } catch (err) {
      setError(messageFor(err))
    }
  }

  async function loadStatutory() {
    setError(null)
    setNotice(null)
    try {
      const r = await call(api.POST('/api/v1/org/holiday-calendars/{id}/load-statutory', { params: { path: { id: calendar.id } }, body: { year: Number(year) } }))
      setNotice(`Added ${r.added.length} public holidays for ${year}${r.alreadyPresent.length ? ` (${r.alreadyPresent.length} were already there)` : ''}. `
        + `Add movable holidays yourself when gazetted: ${r.notGenerated.join(', ')}.`)
      await load()
    } catch (err) {
      setError(messageFor(err))
    }
  }

  async function deactivate(h: Schemas['HolidayDto']) {
    try {
      await call(api.PUT('/api/v1/org/holiday-calendars/{id}/holidays/{holidayId}', { params: { path: { id: calendar.id, holidayId: h.id } }, body: { name: h.name, kind: h.kind, sourceReference: h.sourceReference ?? null, isActive: false, version: Number(h.version), reason: 'Removed from calendar' } }))
      await load()
    } catch (err) {
      setError(messageFor(err))
    }
  }

  return (
    <Dialog open title={`Holidays · ${calendar.name}`} onClose={onClose} onSubmit={writable ? add : onClose} submitLabel={writable ? 'Add holiday' : 'Close'}>
      <p className="hint">Fixed-date Kenyan public holidays load from the approved rule version (Public Holidays Act, with the Sunday rule). Add Easter, Idd-ul-Fitr and any day the government announces below, with its Gazette notice.</p>
      {error && <div className="alert alert-error" role="alert">{error}</div>}
      {notice && <div className="alert alert-success" role="status">{notice}</div>}
      <div className="row">
        <TextField label="Year" type="number" value={year} onChange={setYear} />
        {writable && <button type="button" className="btn-secondary" onClick={() => void loadStatutory()}>Load {year} public holidays</button>}
      </div>
      <table>
        <thead><tr><th>Date</th><th>Name</th><th>Kind</th><th>Source</th><th><span className="sr-only">Actions</span></th></tr></thead>
        <tbody>
          {rows.map((h) => (
            <tr key={h.id}><td>{h.date}</td><td>{h.name}</td><td>{h.kind.replace('_', ' ')}</td>
              <td className="hint">{h.sourceReference ?? '—'}{h.ruleVersion && <div>Rule {h.ruleVersion}</div>}</td>
              <td>{writable && <button type="button" className="btn-link" onClick={() => deactivate(h)}>Remove</button>}</td></tr>
          ))}
          {rows.length === 0 && <tr><td colSpan={5} className="muted">No holidays for {year}.</td></tr>}
        </tbody>
      </table>
      {writable && (
        <fieldset className="stack">
          <legend>Add a holiday</legend>
          <div className="grid-2">
            <TextField label="Date" type="date" value={date} onChange={setDate} required />
            <TextField label="Name" value={name} onChange={setName} required />
            <SelectField label="Kind" value={kind} onChange={setKind} options={[
              { value: 'short_notice', label: 'Announced by government' }, { value: 'gazetted', label: 'Gazetted (movable)' }, { value: 'company', label: 'Company day off' },
            ]} />
            {kind !== 'company' && <TextField label="Gazette notice or legal source" value={sourceReference} onChange={setSourceReference} required hint="For example: Gazette Notice No. 1234 of 2026" />}
          </div>
          {kind !== 'company' && <Checkbox label="Add to all holiday calendars I manage" checked={allCalendars} onChange={setAllCalendars} />}
        </fieldset>
      )}
    </Dialog>
  )
}
