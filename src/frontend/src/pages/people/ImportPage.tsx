import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, call, messageFor, sessionStore, type Schemas } from '../../api/client'
import { SelectField, type Option } from '../../components/Fields'
import { Pager } from '../../components/Pager'
import { loadEmployers } from './model'

type Batch = Schemas['ImportBatchDto']
const MAX_BYTES = 4_000_000

/** MIG-01 employee import wizard: upload, review row errors, second person commits. */
export function ImportPage() {
  const [employers, setEmployers] = useState<Option[]>([])
  const [legalEntityId, setLegalEntityId] = useState('')
  const [batch, setBatch] = useState<Batch | null>(null)
  const [batches, setBatches] = useState<Batch[]>([])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const loadBatches = useCallback(async () => {
    setBatches((await call(api.GET('/api/v1/people/imports', { params: { query: { pageSize: 20 } } }))).items)
  }, [])

  useEffect(() => {
    loadEmployers().then((e) => { setEmployers(e); if (e[0]) setLegalEntityId(e[0].value) }).catch((e) => setError(messageFor(e)))
    loadBatches().catch(() => {})
  }, [loadBatches])

  async function upload(file: File) {
    setError(null)
    if (file.size > MAX_BYTES) {
      setError('The file is larger than 4 MB. Split it into smaller files.')
      return
    }
    setBusy(true)
    try {
      const content = await file.text()
      setBatch(await call(api.POST('/api/v1/people/imports', { body: { legalEntityId, fileName: file.name, content } })))
      await loadBatches()
    } catch (err) {
      setError(messageFor(err))
    } finally {
      setBusy(false)
    }
  }

  async function act(kind: 'commit' | 'discard') {
    if (!batch) return
    setBusy(true)
    setError(null)
    try {
      const params = { params: { path: { id: batch.id } } }
      setBatch(await call(kind === 'commit' ? api.POST('/api/v1/people/imports/{id}/commit', params) : api.POST('/api/v1/people/imports/{id}/discard', params)))
      await loadBatches()
    } catch (err) {
      setError(messageFor(err))
      setBatch(await call(api.GET('/api/v1/people/imports/{id}', { params: { path: { id: batch.id } } })))
    } finally {
      setBusy(false)
    }
  }

  async function downloadTemplate() {
    const res = await fetch('/api/v1/people/imports/template', { headers: { Authorization: `Bearer ${sessionStore.get()?.token ?? ''}` } })
    const url = URL.createObjectURL(await res.blob())
    const a = document.createElement('a')
    a.href = url
    a.download = 'yeakazi-employees-template.csv'
    a.click()
    URL.revokeObjectURL(url)
  }

  const step = !batch ? 1 : batch.status === 'committed' ? 3 : 2
  return (
    <div className="stack">
      <p><Link to="/people">← Employees</Link></p>
      <h1>Import employees</h1>
      <ol className="steps" aria-label="Import steps">
        <li aria-current={step === 1 ? 'step' : undefined}>1. Upload file</li>
        <li aria-current={step === 2 ? 'step' : undefined}>2. Review errors</li>
        <li aria-current={step === 3 ? 'step' : undefined}>3. Second person commits</li>
      </ol>
      {error && <div role="alert" className="alert alert-error">{error}</div>}

      {step === 1 && (
        <section className="card stack">
          <p>Upload a CSV file using the template columns. Nothing is saved to employee records until a second authorised person reviews and commits the whole file.</p>
          <div className="row">
            <button type="button" className="btn-secondary" onClick={() => void downloadTemplate()}>Download template</button>
            <ColumnsHelp />
          </div>
          <div className="grid-2">
            <SelectField label="Legal employer" value={legalEntityId} onChange={setLegalEntityId} options={employers} required />
            <label className="field">CSV file
              <input type="file" accept=".csv,text/csv" disabled={busy || !legalEntityId} onChange={(e) => { const f = e.target.files?.[0]; if (f) void upload(f); e.target.value = '' }} />
              <span className="hint">Up to 2,000 employees. Dates as YYYY-MM-DD or DD/MM/YYYY.</span>
            </label>
          </div>
          {busy && <p role="status">Checking every row…</p>}
        </section>
      )}

      {batch && step >= 2 && <BatchReview batch={batch} busy={busy} onCommit={() => void act('commit')} onDiscard={() => void act('discard')} onNew={() => setBatch(null)} />}

      <section className="stack">
        <h2>Recent imports</h2>
        <div className="table-wrap">
          <table>
            <thead><tr><th>File</th><th>Status</th><th>Rows</th><th>Errors</th><th>Uploaded by</th><th><span className="sr-only">Open</span></th></tr></thead>
            <tbody>
              {batches.map((b) => (
                <tr key={b.id}>
                  <td>{b.fileName}</td><td><ImportStatus batch={b} /></td><td>{Number(b.totalRows)}</td><td>{Number(b.errorRows)}</td>
                  <td>{b.uploadedByDisplay ?? '—'}<div className="muted">{new Date(b.uploadedAt).toLocaleString('en-KE')}</div></td>
                  <td><button className="btn-link" onClick={() => setBatch(b)}>Open</button></td>
                </tr>
              ))}
              {batches.length === 0 && <tr><td colSpan={6} className="muted">No imports yet.</td></tr>}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}

function ImportStatus({ batch }: { batch: Batch }) {
  if (batch.status === 'committed') return <span className="badge badge-ok">Imported</span>
  if (batch.status === 'discarded') return <span className="badge badge-muted">Discarded</span>
  if (Number(batch.errorRows) > 0) return <span className="badge badge-warn">Has errors</span>
  return <span className="badge">Waiting for review</span>
}

function BatchReview({ batch, busy, onCommit, onDiscard, onNew }: { batch: Batch; busy: boolean; onCommit: () => void; onDiscard: () => void; onNew: () => void }) {
  const [rows, setRows] = useState<Schemas['PagedResultOfImportRowDto'] | null>(null)
  const [page, setPage] = useState(1)
  const hasErrors = Number(batch.errorRows) > 0
  useEffect(() => {
    call(api.GET('/api/v1/people/imports/{id}/rows', { params: { path: { id: batch.id }, query: { onlyErrors: hasErrors, page, pageSize: 50 } } }))
      .then(setRows).catch(() => setRows(null))
  }, [batch.id, batch.version, hasErrors, page])

  return (
    <section className="card stack">
      <div className="spread">
        <h2>{batch.fileName}</h2>
        <ImportStatus batch={batch} />
      </div>
      <p className="num">{Number(batch.totalRows)} rows · <strong>{Number(batch.validRows)}</strong> valid · <strong>{Number(batch.errorRows)}</strong> with errors</p>
      {batch.status === 'committed' && <div role="status" className="alert alert-success">{Number(batch.totalRows)} employees were created, with payment details approved by the person who committed. <Link to="/people">View employees</Link></div>}
      {batch.status === 'validated' && hasErrors && <div className="alert alert-info">Fix the rows below in your file and upload it again. Nothing has been saved.</div>}
      {batch.status === 'validated' && !hasErrors && batch.commitBlockedReason && <div className="alert alert-info">{batch.commitBlockedReason}</div>}

      <div className="table-wrap">
        <table>
          <thead><tr><th>Row</th><th>Employee no.</th><th>Name</th><th>{hasErrors ? 'Problems' : 'Result'}</th></tr></thead>
          <tbody>
            {rows?.items.map((r) => (
              <tr key={Number(r.rowNumber)}>
                <td>{Number(r.rowNumber)}</td><td className="mono">{r.employeeNumber ?? '—'}</td><td>{r.name ?? '—'}</td>
                <td>{r.errors.length > 0
                  ? <ul style={{ margin: 0, paddingLeft: 18 }}>{r.errors.map((e, i) => <li key={i}><code>{e.field}</code>: {e.message}</li>)}</ul>
                  : r.employeeId ? <Link to={`/people/${r.employeeId}`}>Created</Link> : <span className="badge badge-ok">Ready</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {rows && <Pager page={Number(rows.page)} pageSize={Number(rows.pageSize)} total={Number(rows.total)} onPage={setPage} />}
      <div className="row">
        {batch.status === 'validated' && batch.canCommit && <button className="btn-primary" disabled={busy} onClick={onCommit}>{busy ? 'Importing…' : `Commit ${Number(batch.totalRows)} employees`}</button>}
        {batch.status === 'validated' && <button className="btn-secondary" disabled={busy} onClick={onDiscard}>Discard</button>}
        <button className="btn-secondary" onClick={onNew}>Upload another file</button>
      </div>
    </section>
  )
}

function ColumnsHelp() {
  const [open, setOpen] = useState(false)
  const [cols, setCols] = useState<Schemas['ImportColumnDto'][]>([])
  useEffect(() => { if (open && cols.length === 0) call(api.GET('/api/v1/people/imports/columns')).then(setCols).catch(() => {}) }, [open, cols.length])
  return (
    <details onToggle={(e) => setOpen((e.target as HTMLDetailsElement).open)}>
      <summary>Column guide</summary>
      <table>
        <thead><tr><th>Column</th><th>Required</th><th>Notes</th></tr></thead>
        <tbody>{cols.map((c) => <tr key={c.name}><td><code>{c.name}</code></td><td>{c.required ? 'Yes' : ''}</td><td>{c.description}</td></tr>)}</tbody>
      </table>
    </details>
  )
}
