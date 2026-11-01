import { useState } from 'react'
import { api, call, messageFor } from '../../api/client'
import { RecoveryCodes } from '../../auth/SignIn'
import { useSession } from '../../auth/session'
import { TextField } from '../../components/Fields'

/** A-04 account security: MFA status and recovery codes. */
export function SecurityPage() {
  const { me } = useSession()
  const [code, setCode] = useState('')
  const [codes, setCodes] = useState<string[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  if (!me) return null

  async function regenerate() {
    setBusy(true)
    setError(null)
    try {
      setCodes((await call(api.POST('/api/v1/me/mfa/recovery-codes', { body: { code: code.trim() } }))).recoveryCodes)
      setCode('')
    } catch (err) {
      setError(messageFor(err))
    } finally {
      setBusy(false)
    }
  }

  const remaining = Number(me.recoveryCodesRemaining)
  return (
    <div className="stack">
      <h1>Sign-in security</h1>
      <section className="card stack">
        <h2>Multi-factor sign-in</h2>
        <p>{me.mfaEnabled ? <span className="badge badge-ok">On</span> : <span className="badge badge-muted">Off</span>}</p>
        {me.mfaEnabled && (
          <p className="num">
            Recovery codes left: <strong>{codes ? codes.length : remaining}</strong>
            {!codes && remaining <= 3 && <span className="badge badge-warn" style={{ marginLeft: 8 }}>Running low</span>}
          </p>
        )}
      </section>
      {me.mfaEnabled && (
        <section className="card stack">
          <h2>New recovery codes</h2>
          {codes ? <RecoveryCodes codes={codes} doneLabel="Done" onDone={() => setCodes(null)} /> : (
            <form className="stack" onSubmit={(e) => { e.preventDefault(); void regenerate() }}>
              <p className="muted">Generating new codes cancels all your old ones. Confirm with the code from your authenticator app.</p>
              {error && <div role="alert" className="alert alert-error">{error}</div>}
              <TextField label="6-digit code" value={code} onChange={setCode} required inputMode="numeric" autoComplete="one-time-code" maxLength={6} />
              <button className="btn-primary" disabled={busy}>Generate new codes</button>
            </form>
          )}
        </section>
      )}
    </div>
  )
}
