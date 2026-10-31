import { useEffect, useState, type FormEvent } from 'react'
import QRCode from 'qrcode'
import { useNavigate } from 'react-router-dom'
import { api, call, messageFor, type Schemas } from '../api/client'
import { TextField } from '../components/Fields'
import { BrandMark } from '../layout/BrandMark'
import { useSession } from './session'

type Step =
  | { kind: 'password' }
  | { kind: 'mfa'; challengeToken: string }
  | { kind: 'enroll'; challengeToken: string; secret?: string; uri?: string; qr?: string }

/**
 * Password sign-in followed by TOTP. Privileged roles must enrol an authenticator app
 * before any access token is issued. Platform staff always use MFA.
 */
export function SignIn({ platform = false }: { platform?: boolean }) {
  const { signIn } = useSession()
  const navigate = useNavigate()
  const [tenant, setTenant] = useState(() => localStorage.getItem('yeakazi.lastTenant') ?? '')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [step, setStep] = useState<Step>({ kind: 'password' })
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function finish(res: Schemas['LoginResponse']) {
    if (res.status === 'authenticated' && res.accessToken && res.expiresAt) {
      await signIn(platform ? 'platform' : 'tenant', res.accessToken, res.expiresAt)
      navigate(platform ? '/platform' : '/', { replace: true })
    } else if (res.status === 'mfa_required' && res.challengeToken) {
      setStep({ kind: 'mfa', challengeToken: res.challengeToken })
    } else if (res.status === 'mfa_enrollment_required' && res.challengeToken) {
      setStep({ kind: 'enroll', challengeToken: res.challengeToken })
    }
  }

  async function submitPassword(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const res = platform
        ? await call(api.POST('/api/v1/platform/auth/login', { body: { email, password } }))
        : await call(api.POST('/api/v1/auth/login', { body: { tenant, email, password } }))
      if (!platform) localStorage.setItem('yeakazi.lastTenant', tenant)
      setPassword('')
      await finish(res)
    } catch (err) {
      setError(messageFor(err))
    } finally {
      setBusy(false)
    }
  }

  useEffect(() => {
    if (step.kind !== 'enroll' || step.secret) return
    const body = { challengeToken: step.challengeToken }
    const req = platform ? api.POST('/api/v1/platform/auth/mfa/enroll', { body }) : api.POST('/api/v1/auth/mfa/enroll', { body })
    call(req)
      .then(async (r) => setStep({ ...step, secret: r.secret, uri: r.otpAuthUri, qr: await QRCode.toDataURL(r.otpAuthUri, { margin: 1, width: 200 }) }))
      .catch((err) => setError(messageFor(err)))
  }, [step, platform])

  async function submitCode(e: FormEvent) {
    e.preventDefault()
    if (step.kind === 'password') return
    setBusy(true)
    setError(null)
    try {
      const body = { challengeToken: step.challengeToken, code: code.trim() }
      const res = platform
        ? await call(api.POST('/api/v1/platform/auth/mfa/verify', { body }))
        : await call(api.POST('/api/v1/auth/mfa/verify', { body }))
      await finish(res)
    } catch (err) {
      setError(messageFor(err))
      setCode('')
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="center-page">
      <div className="card auth-card stack">
        <BrandMark subtitle={platform ? 'Platform console' : 'Sign in to your organization'} />
        {error && <div role="alert" className="alert alert-error">{error}</div>}

        {step.kind === 'password' && (
          <form className="stack" onSubmit={submitPassword}>
            {!platform && (
              <TextField label="Organization" value={tenant} onChange={setTenant} required autoComplete="organization"
                hint="Your organization's YeaKazi address, for example acme-kenya" />
            )}
            <TextField label="Email" type="email" value={email} onChange={setEmail} required autoComplete="username" />
            <TextField label="Password" type="password" value={password} onChange={setPassword} required autoComplete="current-password" />
            <button className="btn-primary" disabled={busy}>{busy ? 'Signing in…' : 'Sign in'}</button>
          </form>
        )}

        {step.kind === 'enroll' && (
          <form className="stack" onSubmit={submitCode}>
            <h2>Set up multi-factor sign-in</h2>
            <p className="muted">Your role needs a second step. Scan this code with an authenticator app, then enter the 6-digit code it shows.</p>
            {step.qr ? <img className="qr" src={step.qr} alt="QR code for your authenticator app" /> : <p>Preparing…</p>}
            {step.secret && (
              <p className="hint">Can't scan? Enter this key: <code className="secret">{step.secret}</code></p>
            )}
            <TextField label="6-digit code" value={code} onChange={setCode} required inputMode="numeric" autoComplete="one-time-code" maxLength={6} />
            <button className="btn-primary" disabled={busy || !step.secret}>Verify and continue</button>
          </form>
        )}

        {step.kind === 'mfa' && (
          <form className="stack" onSubmit={submitCode}>
            <h2>Enter your verification code</h2>
            <TextField label="6-digit code" value={code} onChange={setCode} required inputMode="numeric" autoComplete="one-time-code" maxLength={6} autoFocus />
            <button className="btn-primary" disabled={busy}>Verify</button>
            <button type="button" className="btn-link" onClick={() => { setStep({ kind: 'password' }); setCode('') }}>Start again</button>
          </form>
        )}
      </div>
    </main>
  )
}
