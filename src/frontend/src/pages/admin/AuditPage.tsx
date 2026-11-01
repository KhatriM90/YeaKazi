import { Fragment, useCallback, useEffect, useState, type FormEvent } from 'react'
import { api, call, messageFor, type Schemas } from '../../api/client'
import { TextField } from '../../components/Fields'
import { Pager } from '../../components/Pager'

/** Audit search. Every search is itself recorded in the audit log. */
export function AuditPage() {
  const [filters, setFilters] = useState({ action: '', entityType: '', entityId: '', from: '', to: '' })
  const [applied, setApplied] = useState(filters)
  const [page, setPage] = useState(1)
  const [data, setData] = useState<Schemas['PagedResultOfAuditEventDto'] | null>(null)
  const [open, setOpen] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [chain, setChain] = useState<Schemas['AuditChainStatus'] | null>(null)

  const load = useCallback(async () => {
    try {
      setError(null)
      setData(await call(api.GET('/api/v1/audit-events', { params: { query: {
        action: applied.action || undefined,
        entityType: applied.entityType || undefined,
        entityId: applied.entityId || undefined,
        from: applied.from ? new Date(applied.from).toISOString() : undefined,
        to: applied.to ? new Date(applied.to).toISOString() : undefined,
        page,
        pageSize: 50,
      } } })))
    } catch (err) {
      setError(messageFor(err))
    }
  }, [applied, page])

  useEffect(() => { void load() }, [load])

  function search(e: FormEvent) {
    e.preventDefault()
    setPage(1)
    setApplied(filters)
  }

  async function verify() {
    try {
      setChain(await call(api.POST('/api/v1/audit-events/verify-chain')))
    } catch (err) {
      setError(messageFor(err))
    }
  }

  return (
    <div className="stack">
      <div className="spread">
        <h1>Audit log</h1>
        <button className="btn-secondary" onClick={verify}>Verify integrity</button>
      </div>
      {chain && (
        <div className={`alert ${chain.valid ? 'alert-success' : 'alert-error'}`} role="status">
          {chain.valid
            ? `Hash chain intact across ${chain.eventsChecked} events.`
            : `Hash chain broken at event #${chain.firstBrokenSeq}. Escalate to your security officer.`}
        </div>
      )}
      <form className="card grid-2" onSubmit={search}>
        <TextField label="Action starts with" value={filters.action} onChange={(v) => setFilters({ ...filters, action: v })} placeholder="org.branch" />
        <TextField label="Entity type" value={filters.entityType} onChange={(v) => setFilters({ ...filters, entityType: v })} placeholder="branch" />
        <TextField label="Entity id" value={filters.entityId} onChange={(v) => setFilters({ ...filters, entityId: v })} />
        <TextField label="From" type="datetime-local" value={filters.from} onChange={(v) => setFilters({ ...filters, from: v })} />
        <TextField label="To" type="datetime-local" value={filters.to} onChange={(v) => setFilters({ ...filters, to: v })} />
        <div className="row" style={{ alignSelf: 'end' }}><button className="btn-primary">Search</button></div>
      </form>
      {error && <div className="alert alert-error" role="alert">{error}</div>}
      <div className="card table-wrap">
        <table>
          <thead><tr><th>#</th><th>When (UTC)</th><th>Action</th><th>Entity</th><th>Actor</th><th>Reason</th><th><span className="sr-only">Details</span></th></tr></thead>
          <tbody>
            {data?.items.map((e) => (
              <Fragment key={e.id}>
                <tr>
                  <td className="money">{String(e.seq)}</td>
                  <td>{new Date(e.occurredAt).toISOString().replace('T', ' ').slice(0, 19)}</td>
                  <td><code>{e.action}</code></td>
                  <td>{e.entityType}{e.entityId ? <div className="hint mono">{e.entityId}</div> : null}</td>
                  <td>{e.actorDisplay ?? e.actorKind}</td>
                  <td>{e.reason ?? '—'}</td>
                  <td><button className="btn-link" aria-expanded={open === e.id} onClick={() => setOpen(open === e.id ? null : e.id)}>{open === e.id ? 'Hide' : 'Details'}</button></td>
                </tr>
                {open === e.id && (
                  <tr>
                    <td colSpan={7}>
                      <div className="grid-2">
                        <div><strong>Before</strong><pre className="json">{JSON.stringify(e.before ?? null, null, 2)}</pre></div>
                        <div><strong>After</strong><pre className="json">{JSON.stringify(e.after ?? null, null, 2)}</pre></div>
                      </div>
                      <p className="hint">Correlation id <code>{e.correlationId}</code> · hash <code>{e.hash.slice(0, 16)}…</code></p>
                    </td>
                  </tr>
                )}
              </Fragment>
            ))}
          </tbody>
        </table>
        {data && <Pager page={Number(data.page)} pageSize={Number(data.pageSize)} total={Number(data.total)} onPage={setPage} />}
      </div>
    </div>
  )
}
