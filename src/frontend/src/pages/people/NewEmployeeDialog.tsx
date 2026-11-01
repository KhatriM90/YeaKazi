import { useEffect, useState } from 'react'
import { ApiError, api, call, messageFor } from '../../api/client'
import { Dialog } from '../../components/Dialog'
import { SelectField, TextField, type Option } from '../../components/Fields'
import { AssignmentFields } from './AssignmentFields'
import {
  ContactFields, EMPTY_IDENTIFIERS, EMPTY_PERSONAL, IdentifierFields, PaymentFields, PersonalFields,
  contactsInput, identifiersInput, personalInput, type ContactForm,
} from './EmployeeFields'
import { EMPTY_ASSIGNMENT, EMPTY_PAYMENT, NO_ORG, assignmentInput, loadOrgOptions, paymentInput, today, type OrgOptions } from './model'

/** EMP-01..04 new hire. Payment details become a pending change that a second person approves. */
export function NewEmployeeDialog({ employers, defaultEmployer, onClose, onCreated }: {
  employers: Option[]
  defaultEmployer: string
  onClose: () => void
  onCreated: (id: string) => void
}) {
  const [legalEntityId, setLegalEntityId] = useState(defaultEmployer)
  const [org, setOrg] = useState<OrgOptions>(NO_ORG)
  const [employeeNumber, setEmployeeNumber] = useState('')
  const [hireDate, setHireDate] = useState(today())
  const [personal, setPersonal] = useState(EMPTY_PERSONAL)
  const [identifiers, setIdentifiers] = useState(EMPTY_IDENTIFIERS)
  const [contacts, setContacts] = useState<ContactForm[]>([{ name: '', relationship: '', phone: '' }])
  const [assignment, setAssignment] = useState(EMPTY_ASSIGNMENT)
  const [payment, setPayment] = useState(EMPTY_PAYMENT)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => { loadOrgOptions(legalEntityId).then(setOrg).catch(() => setOrg(NO_ORG)) }, [legalEntityId])

  async function save() {
    setBusy(true)
    setError(null)
    setErrors({})
    try {
      const created = await call(api.POST('/api/v1/people/employees', { body: {
        legalEntityId, employeeNumber, hireDate,
        personal: personalInput(personal), identifiers: identifiersInput(identifiers), emergencyContacts: contactsInput(contacts),
        assignment: assignmentInput(assignment), paymentDetails: paymentInput(payment),
      } }))
      onCreated(created.id)
    } catch (err) {
      if (err instanceof ApiError) setErrors(err.fieldErrors())
      setError(messageFor(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open wide title="Add employee" submitLabel="Add employee" busy={busy} onClose={onClose} onSubmit={save}>
      {error && <div role="alert" className="alert alert-error">{error}</div>}
      <fieldset className="stack">
        <legend>Employment</legend>
        <div className="grid-2">
          <SelectField label="Legal employer" value={legalEntityId} onChange={setLegalEntityId} options={employers} required error={errors.legalEntityId} />
          <TextField label="Employee number" value={employeeNumber} onChange={setEmployeeNumber} required error={errors.employeeNumber} hint="Cannot be changed later." />
          <TextField label="Hire date" type="date" value={hireDate} onChange={setHireDate} required error={errors.hireDate} />
        </div>
        <AssignmentFields value={assignment} onChange={setAssignment} org={org} errors={errors} legalEntityId={legalEntityId} />
      </fieldset>
      <fieldset className="stack"><legend>Personal details</legend><PersonalFields value={personal} onChange={setPersonal} errors={errors} /></fieldset>
      <fieldset className="stack">
        <legend>Identifiers</legend>
        <p className="hint">Restricted. Only people with sensitive-data access see these in full; every view is logged.</p>
        <IdentifierFields value={identifiers} onChange={setIdentifiers} errors={errors} citizenship={personal.citizenship} />
      </fieldset>
      <fieldset className="stack"><legend>Emergency contacts</legend><ContactFields value={contacts} onChange={setContacts} errors={errors} /></fieldset>
      <fieldset className="stack">
        <legend>Payment details</legend>
        <p className="hint">Another authorised person must approve these before they are used for pay.</p>
        <PaymentFields value={payment} onChange={setPayment} errors={errors} />
      </fieldset>
    </Dialog>
  )
}
