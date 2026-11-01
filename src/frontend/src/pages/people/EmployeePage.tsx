import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ApiError, api, call, messageFor, type Schemas } from '../../api/client'
import { Dialog } from '../../components/Dialog'
import { SelectField, TextField } from '../../components/Fields'
import { useSession } from '../../auth/session'
import { AssignmentFields } from './AssignmentFields'
import {
  ContactFields, IdentifierFields, PaymentFields, PersonalFields, contactsInput, identifiersInput, personalInput,
  type ContactForm, type IdentifiersForm, type PersonalForm,
} from './EmployeeFields'
import { EmployeeStatus } from './EmployeeStatus'
import {
  CHANGE_LABEL, EMPTY_PAYMENT, GAP_LABEL, NO_ORG, PAY_BASIS_LABEL, PERMIT_ALERT_LABEL, TERMINATION_TYPES, WORKER_CATEGORIES,
  assignmentForm, assignmentInput, formatDate, label, loadOrgOptions, paymentInput, today, type Assignment, type Employee, type OrgOptions,
} from './model'

type Tab = 'overview' | 'identifiers' | 'history' | 'payment' | 'contacts'
type Action =
  | { kind: 'edit' }
  | { kind: 'change' }
  | { kind: 'correct'; version: Assignment }
  | { kind: 'cancel'; version: Assignment }
  | { kind: 'terminate' }
  | { kind: 'rescind' }
  | { kind: 'rehire' }
  | { kind: 'payment' }
  | { kind: 'contacts' }

/** EMP-01..04 employee profile with effective-dated history. */
export function EmployeePage() {
  const { id = '' } = useParams()
  const { can } = useSession()
  const [employee, setEmployee] = useState<Employee | null>(null)
  const [history, setHistory] = useState<Schemas['EmployeeHistoryDto'] | null>(null)
  const [org, setOrg] = useState<OrgOptions>(NO_ORG)
  const [tab, setTab] = useState<Tab>('overview')
  const [action, setAction] = useState<Action | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const load = useCallback(async () => {
    const [e, h] = await Promise.all([
      call(api.GET('/api/v1/people/employees/{id}', { params: { path: { id } } })),
      call(api.GET('/api/v1/people/employees/{id}/history', { params: { path: { id } } })),
    ])
    setEmployee(e)
    setHistory(h)
  }, [id])

  useEffect(() => { load().catch((e) => setError(messageFor(e))) }, [load])
  useEffect(() => { if (employee) loadOrgOptions(employee.legalEntityId).then(setOrg).catch(() => setOrg(NO_ORG)) }, [employee?.legalEntityId]) // eslint-disable-line react-hooks/exhaustive-deps

  if (error && !employee) return <div role="alert" className="alert alert-error">{error}</div>
  if (!employee || !history) return <p>Loading…</p>

  const writable = can('people.write')
  const a = employee.currentAssignment
  const openEpisode = history.episodes.find((x) => !x.episode.terminationDate)
  const lastEpisode = history.episodes[history.episodes.length - 1]
  const done = async (message: string) => {
    setAction(null)
    setNotice(message)
    await load()
  }
  const orgName = (options: { value: string; label: string }[], v?: string | null) => (v ? label(options, v) : '—')

  return (
    <div className="stack">
      <p><Link to="/people">← Employees</Link></p>
      <div className="spread">
        <div>
          <h1>{employee.personal.fullName}</h1>
          <div className="row">
            <span className="mono">{employee.employeeNumber}</span>
            <EmployeeStatus status={employee.status} />
            {employee.workPermitAlert && <span className="badge badge-warn">{PERMIT_ALERT_LABEL[employee.workPermitAlert] ?? employee.workPermitAlert}</span>}
          </div>
        </div>
        {writable && (
          <div className="row">
            {openEpisode && <button className="btn-secondary" onClick={() => setAction({ kind: 'change' })}>Record a change</button>}
            {openEpisode && <button className="btn-danger" onClick={() => setAction({ kind: 'terminate' })}>End employment</button>}
            {!openEpisode && lastEpisode?.episode.terminationDate && lastEpisode.episode.terminationDate > history.today &&
              <button className="btn-secondary" onClick={() => setAction({ kind: 'rescind' })}>Withdraw termination</button>}
            {!openEpisode && lastEpisode?.episode.terminationDate && lastEpisode.episode.terminationDate < history.today &&
              <button className="btn-primary" onClick={() => setAction({ kind: 'rehire' })}>Rehire</button>}
          </div>
        )}
      </div>
      {notice && <div role="status" className="alert alert-success">{notice}</div>}
      {error && <div role="alert" className="alert alert-error">{error}</div>}
      {employee.statutoryGaps.length > 0 && (
        <div className="alert alert-info">
          <strong>Before this person can be paid:</strong> {employee.statutoryGaps.map((g) => GAP_LABEL[g] ?? g).join(' · ')}
        </div>
      )}

      <div className="tabs" role="tablist" aria-label="Employee sections">
        {(['overview', 'identifiers', 'history', 'payment', 'contacts'] as Tab[]).map((t) => (
          <button key={t} role="tab" aria-selected={t === tab} onClick={() => setTab(t)}>
            {{ overview: 'Overview', identifiers: 'Identifiers', history: 'Employment history', payment: 'Payment details', contacts: 'Emergency contacts' }[t]}
          </button>
        ))}
      </div>

      {tab === 'overview' && (
        <div className="grid-2">
          <section className="card stack">
            <div className="spread"><h2>Personal</h2>{writable && <button className="btn-link" onClick={() => setAction({ kind: 'edit' })}>Edit</button>}</div>
            <dl className="facts">
              <dt>Date of birth</dt><dd>{formatDate(employee.personal.dateOfBirth)}</dd>
              <dt>Gender</dt><dd>{employee.personal.gender}</dd>
              <dt>Citizenship</dt><dd>{employee.personal.citizenship === 'kenyan' ? 'Kenyan' : 'Non-Kenyan'}</dd>
              <dt>Tax residency</dt><dd>{employee.personal.residencyStatus === 'resident' ? 'Resident' : 'Non-resident'}</dd>
              <dt>Mobile</dt><dd className="mono">{employee.personal.phoneMobile}</dd>
              <dt>Work email</dt><dd>{employee.personal.emailWork ?? '—'}</dd>
              <dt>Personal email</dt><dd>{employee.personal.emailPersonal ?? '—'}</dd>
            </dl>
          </section>
          <section className="card stack">
            <h2>Current job</h2>
            {a ? (
              <dl className="facts">
                <dt>Job title</dt><dd>{a.jobTitle ?? '—'}</dd>
                <dt>Category</dt><dd>{label(WORKER_CATEGORIES, a.workerCategory)}</dd>
                <dt>Pay basis</dt><dd>{PAY_BASIS_LABEL[a.payBasis] ?? a.payBasis}</dd>
                {a.contractEndDate && <><dt>Contract ends</dt><dd>{formatDate(a.contractEndDate)}</dd></>}
                {a.probationEndDate && <><dt>Probation ends</dt><dd>{formatDate(a.probationEndDate)}</dd></>}
                <dt>Department</dt><dd>{orgName(org.departments, a.departmentId)}</dd>
                <dt>Position</dt><dd>{orgName(org.positions, a.positionId)}</dd>
                <dt>Location</dt><dd>{orgName(org.branches, a.branchId)}</dd>
                <dt>Cost center</dt><dd>{orgName(org.costCenters, a.costCenterId)}</dd>
                <dt>Grade</dt><dd>{orgName(org.grades, a.gradeId)}</dd>
                <dt>Reports to</dt><dd>{a.managerName ?? '—'}</dd>
                <dt>Hired</dt><dd>{formatDate(employee.currentEpisode?.hireDate)}</dd>
              </dl>
            ) : <p className="muted">No assignment.</p>}
          </section>
        </div>
      )}

      {tab === 'identifiers' && (
        <section className="card stack">
          {employee.sensitiveMasked && <p className="hint">Masked. You need sensitive-data access to see these in full.</p>}
          {!employee.sensitiveMasked && <p className="hint">Your viewing of these identifiers was recorded in the audit log.</p>}
          <dl className="facts">
            <dt>National ID</dt><dd className="mono">{employee.identifiers.nationalId ?? '—'}</dd>
            <dt>Passport</dt><dd className="mono">{employee.identifiers.passportNo ?? '—'}</dd>
            <dt>Work permit</dt><dd className="mono">{employee.identifiers.workPermitNo ?? '—'} {employee.identifiers.workPermitExpiry && `(expires ${formatDate(employee.identifiers.workPermitExpiry)})`}</dd>
            <dt>KRA PIN</dt><dd className="mono">{employee.identifiers.kraPin ?? '—'}</dd>
            <dt>NSSF</dt><dd className="mono">{employee.identifiers.nssfNo ?? 'Missing'}</dd>
            <dt>SHA</dt><dd className="mono">{employee.identifiers.shaNo ?? 'Missing'}</dd>
            <dt>HELB</dt><dd className="mono">{employee.identifiers.helbRef ?? '—'}</dd>
            <dt>Disability certificate</dt><dd className="mono">{employee.identifiers.disabilityCertNo ?? '—'}</dd>
          </dl>
        </section>
      )}

      {tab === 'history' && (
        <div className="stack">
          {[...history.episodes].reverse().map((ep) => (
            <section key={ep.episode.id} className="card stack">
              <h2>Employment {ep.episode.sequence}: {formatDate(ep.episode.hireDate)} – {ep.episode.terminationDate ? formatDate(ep.episode.terminationDate) : 'present'}</h2>
              {ep.episode.terminationType && <p className="muted">Ended: {label(TERMINATION_TYPES, ep.episode.terminationType)}. {ep.episode.terminationReason}</p>}
              <ol className="timeline">
                {[...ep.versions].reverse().map((v) => {
                  const future = v.effectiveFrom > history.today
                  return (
                    <li key={v.id} className={v.isCancelled ? 'cancelled' : future ? 'future' : undefined}>
                      <div className="spread">
                        <strong>{formatDate(v.effectiveFrom)}{v.effectiveTo ? ` – ${formatDate(v.effectiveTo)}` : ''} · {CHANGE_LABEL[v.changeType] ?? v.changeType}</strong>
                        <span className="row">
                          {v.isCancelled && <span className="badge badge-muted">Cancelled</span>}
                          {future && !v.isCancelled && <span className="badge badge-warn">Planned</span>}
                          {v.correctsVersionId && <span className="badge">Correction</span>}
                          {writable && !v.isCancelled && <button className="btn-link" onClick={() => setAction({ kind: 'correct', version: v })}>Correct</button>}
                          {writable && !v.isCancelled && future && <button className="btn-link" onClick={() => setAction({ kind: 'cancel', version: v })}>Cancel</button>}
                        </span>
                      </div>
                      <div>{v.jobTitle ?? 'No title'} · {label(WORKER_CATEGORIES, v.workerCategory)} · {PAY_BASIS_LABEL[v.payBasis]} · {orgName(org.departments, v.departmentId)}</div>
                      {v.reason && <div className="muted">Reason: {v.reason}</div>}
                      {v.isCancelled && v.cancelReason && <div className="muted">Cancelled: {v.cancelReason}</div>}
                    </li>
                  )
                })}
              </ol>
            </section>
          ))}
        </div>
      )}

      {tab === 'payment' && (
        <section className="card stack">
          <div className="spread"><h2>Payment details</h2>{writable && !employee.pendingPaymentChange && <button className="btn-secondary" onClick={() => setAction({ kind: 'payment' })}>Request a change</button>}</div>
          {employee.paymentDetails ? (
            <dl className="facts">
              <dt>Method</dt><dd>{employee.paymentDetails.paymentMethod}</dd>
              {employee.paymentDetails.bankCode && <><dt>Bank / branch</dt><dd className="mono">{employee.paymentDetails.bankCode} / {employee.paymentDetails.bankBranchCode}</dd></>}
              {employee.paymentDetails.accountNo && <><dt>Account</dt><dd className="mono">{employee.paymentDetails.accountNo} · {employee.paymentDetails.accountName}</dd></>}
              {employee.paymentDetails.mpesaPhone && <><dt>M-Pesa</dt><dd className="mono">{employee.paymentDetails.mpesaPhone}</dd></>}
            </dl>
          ) : <p className="muted">No approved payment details yet.</p>}
          {employee.pendingPaymentChange && (
            <div className="alert alert-info">
              A change to <strong>{employee.pendingPaymentChange.paymentMethod}</strong> is waiting for approval by another person.{' '}
              <Link to="/people/approvals">Open approvals</Link>
            </div>
          )}
        </section>
      )}

      {tab === 'contacts' && (
        <section className="card stack">
          <div className="spread"><h2>Emergency contacts</h2>{writable && <button className="btn-link" onClick={() => setAction({ kind: 'contacts' })}>Edit</button>}</div>
          <table>
            <thead><tr><th>Name</th><th>Relationship</th><th>Phone</th></tr></thead>
            <tbody>{employee.emergencyContacts.map((c) => <tr key={c.id}><td>{c.name}</td><td>{c.relationship}</td><td className="mono">{c.phone}</td></tr>)}</tbody>
          </table>
        </section>
      )}

      {action && <ActionDialog action={action} employee={employee} history={history} org={org} onClose={() => setAction(null)} onDone={done} />}
    </div>
  )
}

function ActionDialog({ action, employee, history, org, onClose, onDone }: {
  action: Action
  employee: Employee
  history: Schemas['EmployeeHistoryDto']
  org: OrgOptions
  onClose: () => void
  onDone: (message: string) => Promise<void>
}) {
  const id = employee.id
  const openEpisode = history.episodes.find((x) => !x.episode.terminationDate)?.episode
  const lastEpisode = history.episodes[history.episodes.length - 1]?.episode
  const [date, setDate] = useState(today())
  const [changeType, setChangeType] = useState('promotion')
  const [reason, setReason] = useState('')
  const [terminationType, setTerminationType] = useState('resignation')
  const [assignment, setAssignment] = useState(assignmentForm(action.kind === 'correct' ? action.version : employee.currentAssignment))
  const [payment, setPayment] = useState(EMPTY_PAYMENT)
  const [personal, setPersonal] = useState<PersonalForm>(() => ({
    ...employee.personal, middleName: employee.personal.middleName ?? '', emailWork: employee.personal.emailWork ?? '', emailPersonal: employee.personal.emailPersonal ?? '',
  }))
  const [identifiers, setIdentifiers] = useState<IdentifiersForm>(() => {
    const i = employee.identifiers
    return {
      nationalId: i.nationalId ?? '', passportNo: i.passportNo ?? '', workPermitNo: i.workPermitNo ?? '', workPermitExpiry: i.workPermitExpiry ?? '',
      kraPin: i.kraPin ?? '', nssfNo: i.nssfNo ?? '', shaNo: i.shaNo ?? '', helbRef: i.helbRef ?? '', disabilityCertNo: i.disabilityCertNo ?? '',
      disabilityCertExpiry: i.disabilityCertExpiry ?? '',
    }
  })
  const [contacts, setContacts] = useState<ContactForm[]>(employee.emergencyContacts.map((c) => ({ name: c.name, relationship: c.relationship, phone: c.phone })))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function run(fn: () => Promise<unknown>, message: string) {
    setBusy(true)
    setError(null)
    setErrors({})
    try {
      await fn()
      await onDone(message)
    } catch (err) {
      if (err instanceof ApiError) setErrors(err.fieldErrors())
      setError(messageFor(err))
    } finally {
      setBusy(false)
    }
  }

  const path = { params: { path: { id } } }
  const reasonField = (required: boolean) => <TextField label="Reason" value={reason} onChange={setReason} required={required} error={errors.reason} />
  let title = ''
  let submit = () => {}
  let body = null
  let danger = false
  switch (action.kind) {
    case 'edit':
      title = 'Edit personal details'
      submit = () => void run(() => call(api.PUT('/api/v1/people/employees/{id}', { ...path, body: {
        personal: personalInput(personal), identifiers: employee.sensitiveMasked ? null : identifiersInput(identifiers), version: Number(employee.version), reason: reason || null,
      } })), 'Details updated.')
      body = <>
        <PersonalFields value={personal} onChange={setPersonal} errors={errors} />
        {!employee.sensitiveMasked && <IdentifierFields value={identifiers} onChange={setIdentifiers} errors={errors} citizenship={personal.citizenship} />}
        {reasonField(false)}
      </>
      break
    case 'change':
      title = 'Record a change'
      submit = () => void run(() => call(api.POST('/api/v1/people/employees/{id}/assignments', { ...path, body: {
        effectiveFrom: date, changeType, assignment: assignmentInput(assignment), reason: reason || null,
      } })), 'Change recorded. Earlier history is kept.')
      body = <>
        <div className="grid-2">
          <TextField label="Effective from" type="date" value={date} onChange={setDate} required error={errors.effectiveFrom} hint="Back-dated and future-dated changes are allowed." />
          <SelectField label="Type of change" value={changeType} onChange={setChangeType} error={errors.changeType} options={[
            { value: 'promotion', label: 'Promotion' }, { value: 'transfer', label: 'Transfer' }, { value: 'change', label: 'Other change' },
            { value: 'suspension', label: 'Suspension' }, { value: 'reinstatement', label: 'Reinstatement' },
          ]} />
        </div>
        <AssignmentFields value={assignment} onChange={setAssignment} org={org} errors={errors} legalEntityId={employee.legalEntityId} excludeEmployeeId={id} />
        {reasonField(changeType === 'suspension')}
      </>
      break
    case 'correct':
      title = `Correct the ${formatDate(action.version.effectiveFrom)} record`
      submit = () => void run(() => call(api.POST('/api/v1/people/employees/{id}/assignments/{versionId}/correct', { params: { path: { id, versionId: action.version.id } }, body: {
        assignment: assignmentInput(assignment), reason, version: Number(action.version.version),
      } })), 'Corrected. The original record is kept and marked as replaced.')
      body = <>
        <p className="hint">Use this for data-entry mistakes. The original stays in history, marked cancelled, with your reason.</p>
        <AssignmentFields value={assignment} onChange={setAssignment} org={org} errors={errors} legalEntityId={employee.legalEntityId} excludeEmployeeId={id} />
        {reasonField(true)}
      </>
      break
    case 'cancel':
      title = 'Cancel planned change'
      danger = true
      submit = () => void run(() => call(api.POST('/api/v1/people/employees/{id}/assignments/{versionId}/cancel', { params: { path: { id, versionId: action.version.id } }, body: {
        reason, version: Number(action.version.version),
      } })), 'Planned change cancelled.')
      body = <>{reasonField(true)}</>
      break
    case 'terminate':
      title = 'End employment'
      danger = true
      submit = () => void run(() => call(api.POST('/api/v1/people/employees/{id}/terminate', { ...path, body: {
        terminationDate: date, terminationType, reason, version: Number(openEpisode?.version ?? 0),
      } })), 'Employment ended. History is kept and the person can be rehired.')
      body = <>
        <div className="grid-2">
          <TextField label="Last working day" type="date" value={date} onChange={setDate} required error={errors.terminationDate} />
          <SelectField label="Reason type" value={terminationType} onChange={setTerminationType} options={TERMINATION_TYPES} error={errors.terminationType} />
        </div>
        {reasonField(true)}
        <p className="hint">Final dues and certificates of service arrive with the exits slice.</p>
      </>
      break
    case 'rescind':
      title = 'Withdraw termination'
      submit = () => void run(() => call(api.POST('/api/v1/people/employees/{id}/rescind-termination', { ...path, body: { reason, version: Number(lastEpisode?.version ?? 0) } })), 'Termination withdrawn.')
      body = <>{reasonField(true)}</>
      break
    case 'rehire':
      title = 'Rehire'
      submit = () => void run(() => call(api.POST('/api/v1/people/employees/{id}/rehire', { ...path, body: { hireDate: date, assignment: assignmentInput(assignment), reason: reason || null } })), 'Rehired. The earlier employment is kept in history.')
      body = <>
        <TextField label="New hire date" type="date" value={date} onChange={setDate} required error={errors.hireDate} />
        <AssignmentFields value={assignment} onChange={setAssignment} org={org} errors={errors} legalEntityId={employee.legalEntityId} excludeEmployeeId={id} />
        {reasonField(false)}
      </>
      break
    case 'payment':
      title = 'Request a payment details change'
      submit = () => void run(() => call(api.POST('/api/v1/people/employees/{id}/payment-changes', { ...path, body: { details: paymentInput(payment), reason } })), 'Change requested. Another authorised person must approve it.')
      body = <>
        <PaymentFields value={payment} onChange={setPayment} errors={errors} prefix="details." />
        {reasonField(true)}
      </>
      break
    case 'contacts':
      title = 'Emergency contacts'
      submit = () => void run(() => call(api.PUT('/api/v1/people/employees/{id}/emergency-contacts', { ...path, body: { contacts: contactsInput(contacts), reason: reason || null } })), 'Contacts saved.')
      body = <><ContactFields value={contacts} onChange={setContacts} errors={errors} prefix="contacts" />{reasonField(false)}</>
      break
  }

  return (
    <Dialog open wide={action.kind !== 'cancel' && action.kind !== 'rescind'} danger={danger} title={title} busy={busy} onClose={onClose} onSubmit={submit}
      submitLabel={danger ? 'Confirm' : 'Save'}>
      {error && <div role="alert" className="alert alert-error">{error}</div>}
      {body}
    </Dialog>
  )
}
