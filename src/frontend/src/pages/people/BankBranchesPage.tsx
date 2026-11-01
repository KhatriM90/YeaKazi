import { useCallback, useEffect, useState } from 'react'
import { api, call, messageFor, type Schemas } from '../../api/client'
import { Dialog } from '../../components/Dialog'
import { Checkbox, TextField } from '../../components/Fields'
import { useSession } from '../../auth/session'

type Branch = Schemas['BankBranchDto']

/** Bank and branch codes used to validate employee bank details. Maintained per tenant (docs/assumptions.md). */
export function BankBranchesPage() {
  const { can } = useSession()
  const [rows, setRows] = useState<Branch[]>([])
  const [editing, setEditing] = useState<{ row: Branch | null; bankCode: string; bankName: string; branchCode: string; branchName: string; isActive: boolean; reason: string } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(async () => setRows(await call(api.GET('/api/v1/people/bank-branches', { params: { query: { includeInactive: true } } }))), [])
  useEffect(() => { load().catch((e) => setError(messageFor(e))) }, [load])

  async function save() {
    if (!editing) return
    setBusy(true)
    setError(null)
    try {
      if (editing.row) {
        await call(api.PUT('/api/v1/people/bank-branches/{id}', { params: { path: { id: editing.row.id } }, body: {
          bankName: editing.bankName, branchName: editing.branchName, isActive: editing.isActive, version: Number(editing.row.version), reason: editing.reason || null,
        } }))
      } else {
        await call(api.POST('/api/v1/people/bank-branches', { body: { bankCode: editing.bankCode, bankName: editing.bankName, branchCode: editing.branchCode, branchName: editing.branchName } }))
      }
      setEditing(null)
      await load()
    } catch (err) {
      setError(messageFor(err))
    } finally {
      setBusy(false)
    }
  }

  const set = (k: 'bankCode' | 'bankName' | 'branchCode' | 'branchName' | 'reason') => (v: string) => setEditing((e) => (e ? { ...e, [k]: v } : e))
  return (
    <div className="stack">
      <div className="spread">
        <h1>Bank branches</h1>
        {can('people.write') && <button className="btn-primary" onClick={() => setEditing({ row: null, bankCode: '', bankName: '', branchCode: '', branchName: '', isActive: true, reason: '' })}>Add branch</button>}
      </div>
      <p className="muted">Enter codes exactly as your bank publishes them. Employee bank details and imports are checked against this list.</p>
      {error && <div role="alert" className="alert alert-error">{error}</div>}
      <div className="table-wrap">
        <table>
          <thead><tr><th>Bank code</th><th>Bank</th><th>Branch code</th><th>Branch</th><th>Status</th><th><span className="sr-only">Actions</span></th></tr></thead>
          <tbody>
            {rows.map((b) => (
              <tr key={b.id}>
                <td className="mono">{b.bankCode}</td><td>{b.bankName}</td><td className="mono">{b.branchCode}</td><td>{b.branchName}</td>
                <td>{b.isActive ? <span className="badge badge-ok">Active</span> : <span className="badge badge-muted">Inactive</span>}</td>
                <td>{can('people.write') && <button className="btn-link" onClick={() => setEditing({ row: b, bankCode: b.bankCode, bankName: b.bankName, branchCode: b.branchCode, branchName: b.branchName, isActive: b.isActive, reason: '' })}>Edit</button>}</td>
              </tr>
            ))}
            {rows.length === 0 && <tr><td colSpan={6} className="muted">No bank branches yet.</td></tr>}
          </tbody>
        </table>
      </div>
      <Dialog open={!!editing} title={editing?.row ? 'Edit bank branch' : 'Add bank branch'} busy={busy} onClose={() => setEditing(null)} onSubmit={save}>
        {editing && <div className="grid-2">
          <TextField label="Bank code" value={editing.bankCode} onChange={set('bankCode')} required disabled={!!editing.row} />
          <TextField label="Bank name" value={editing.bankName} onChange={set('bankName')} required />
          <TextField label="Branch code" value={editing.branchCode} onChange={set('branchCode')} required disabled={!!editing.row} />
          <TextField label="Branch name" value={editing.branchName} onChange={set('branchName')} required />
          {editing.row && <Checkbox label="Active" checked={editing.isActive} onChange={(v) => setEditing({ ...editing, isActive: v })} />}
          {editing.row && <TextField label="Reason" value={editing.reason} onChange={set('reason')} />}
        </div>}
      </Dialog>
    </div>
  )
}
