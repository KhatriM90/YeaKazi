import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, call, messageFor, type Schemas } from '../../api/client'
import { Dialog } from '../../components/Dialog'
import { TextField } from '../../components/Fields'
import { useSession } from '../../auth/session'

type Change = Schemas['PaymentChangeDto']

/** Four-eyes approval of payment details: the requester cannot approve their own change. */
export function ApprovalsPage() {
  const { me } = useSession()
  const [rows, setRows] = useState<Change[]>([])
  const [deciding, setDeciding] = useState<{ change: Change; approve: boolean } | null>(null)
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(async () => {
    setRows((await call(api.GET('/api/v1/people/payment-changes', { params: { query: { status: 'pending', pageSize: 100 } } }))).items)
  }, [])
  useEffect(() => { load().catch((e) => setError(messageFor(e))) }, [load])

  async function decide() {
    if (!deciding) return
    setBusy(true)
    setError(null)
    try {
      const params = { params: { path: { id: deciding.change.id } }, body: { note: note || null, version: Number(deciding.change.version) } }
      await call(deciding.approve ? api.POST('/api/v1/people/payment-changes/{id}/approve', params) : api.POST('/api/v1/people/payment-changes/{id}/reject', params))
      setNotice(deciding.approve ? 'Approved. The new details apply from now.' : 'Rejected.')
      setDeciding(null)
      setNote('')
      await load()
    } catch (err) {
      setError(messageFor(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="stack">
      <h1>Payment detail approvals</h1>
      <p className="muted">Bank and M-Pesa details only take effect after a second authorised person approves them.</p>
      {notice && <div role="status" className="alert alert-success">{notice}</div>}
      {error && <div role="alert" className="alert alert-error">{error}</div>}
      <div className="table-wrap">
        <table>
          <thead><tr><th>Employee</th><th>New details</th><th>Requested</th><th>Checks</th><th><span className="sr-only">Actions</span></th></tr></thead>
          <tbody>
            {rows.map((c) => {
              const own = c.requestedByUserId === me?.userId
              return (
                <tr key={c.id}>
                  <td><Link to={`/people/${c.employeeId}`}>{c.employeeName}</Link> <span className="mono muted">{c.employeeNumber}</span></td>
                  <td className="mono">{c.paymentMethod === 'bank' ? `${c.bankCode}/${c.bankBranchCode} ${c.accountNo} · ${c.accountName}` : c.paymentMethod === 'mpesa' ? `M-Pesa ${c.mpesaPhone}` : 'Cash'}</td>
                  <td>{new Date(c.requestedAt).toLocaleString('en-KE')}{c.reason && <div className="muted">{c.reason}</div>}</td>
                  <td>{c.warnings.length === 0 ? <span className="badge badge-ok">No warnings</span> : c.warnings.map((w) => <div key={w} className="badge badge-warn">{w}</div>)}</td>
                  <td className="row">
                    {own ? <span className="muted">Your request. Someone else must approve.</span>
                      : <button className="btn-primary" onClick={() => setDeciding({ change: c, approve: true })}>Approve</button>}
                    <button className="btn-secondary" onClick={() => setDeciding({ change: c, approve: false })}>{own ? 'Withdraw' : 'Reject'}</button>
                  </td>
                </tr>
              )
            })}
            {rows.length === 0 && <tr><td colSpan={5} className="muted">Nothing waiting for approval.</td></tr>}
          </tbody>
        </table>
      </div>
      <Dialog open={!!deciding} title={deciding?.approve ? 'Approve payment details' : 'Reject payment details'} danger={!deciding?.approve}
        submitLabel={deciding?.approve ? 'Approve' : 'Reject'} busy={busy} onClose={() => setDeciding(null)} onSubmit={decide}>
        <TextField label="Note" value={note} onChange={setNote} hint={deciding?.approve ? 'For example: checked against the bank letter.' : 'Why it is rejected.'} />
      </Dialog>
    </div>
  )
}
