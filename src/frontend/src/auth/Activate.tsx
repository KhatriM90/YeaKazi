import { useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { ApiError, api, call, messageFor } from '../api/client'
import { TextField } from '../components/Fields'
import { BrandMark } from '../layout/BrandMark'

/** Account activation from the emailed invitation link. */
export function Activate() {
  const [params] = useSearchParams()
  const tenant = params.get('tenant') ?? ''
  const token = params.get('token') ?? ''
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    if (password !== confirm) {
      setErrors({ confirm: 'Passwords do not match.' })
      return
    }
    try {
      await call(api.POST('/api/v1/auth/activate', { body: { tenant, token, password } }))
      localStorage.setItem('yeakazi.lastTenant', tenant)
      setDone(true)
    } catch (err) {
      if (err instanceof ApiError) setErrors(err.fieldErrors())
      setError(messageFor(err))
    }
  }

  return (
    <main className="center-page">
      <div className="card auth-card stack">
        <BrandMark subtitle="Activate your account" />
        {done ? (
          <div className="stack">
            <div className="alert alert-success" role="status">Your password is set.</div>
            <Link to="/login" className="btn btn-primary">Continue to sign in</Link>
          </div>
        ) : (
          <form className="stack" onSubmit={submit}>
            {error && <div role="alert" className="alert alert-error">{error}</div>}
            <p className="muted">Organization: <strong>{tenant || 'unknown'}</strong></p>
            <TextField label="New password" type="password" value={password} onChange={setPassword} required autoComplete="new-password"
              hint="At least 12 characters with letters and digits." error={errors.password} />
            <TextField label="Confirm password" type="password" value={confirm} onChange={setConfirm} required autoComplete="new-password" error={errors.confirm} />
            <button className="btn-primary" disabled={!token}>Set password</button>
          </form>
        )}
      </div>
    </main>
  )
}
