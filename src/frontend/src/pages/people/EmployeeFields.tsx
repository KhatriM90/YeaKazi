import { useEffect, useState } from 'react'
import { api, call, type Schemas } from '../../api/client'
import { Checkbox, SelectField, TextField } from '../../components/Fields'
import type { PaymentForm } from './model'

type Errors = Record<string, string>

export interface PersonalForm {
  firstName: string; middleName: string; lastName: string; dateOfBirth: string; gender: string; citizenship: string
  residencyStatus: string; phoneMobile: string; emailWork: string; emailPersonal: string; internGuardianConsent: boolean
}

export const EMPTY_PERSONAL: PersonalForm = {
  firstName: '', middleName: '', lastName: '', dateOfBirth: '', gender: '', citizenship: 'kenyan', residencyStatus: 'resident',
  phoneMobile: '', emailWork: '', emailPersonal: '', internGuardianConsent: false,
}

export interface IdentifiersForm {
  nationalId: string; passportNo: string; workPermitNo: string; workPermitExpiry: string; kraPin: string; nssfNo: string
  shaNo: string; helbRef: string; disabilityCertNo: string; disabilityCertExpiry: string
}

export const EMPTY_IDENTIFIERS: IdentifiersForm = {
  nationalId: '', passportNo: '', workPermitNo: '', workPermitExpiry: '', kraPin: '', nssfNo: '', shaNo: '', helbRef: '', disabilityCertNo: '', disabilityCertExpiry: '',
}

export interface ContactForm { name: string; relationship: string; phone: string }

const n = (v: string) => (v.trim() === '' ? null : v.trim())

export const personalInput = (p: PersonalForm): Schemas['PersonalInput'] => ({
  firstName: n(p.firstName), middleName: n(p.middleName), lastName: n(p.lastName), dateOfBirth: n(p.dateOfBirth), gender: n(p.gender),
  citizenship: n(p.citizenship), residencyStatus: n(p.residencyStatus), phoneMobile: n(p.phoneMobile), emailWork: n(p.emailWork),
  emailPersonal: n(p.emailPersonal), internGuardianConsent: p.internGuardianConsent,
})

export const identifiersInput = (i: IdentifiersForm): Schemas['IdentifiersInput'] => ({
  nationalId: n(i.nationalId), passportNo: n(i.passportNo), workPermitNo: n(i.workPermitNo), workPermitExpiry: n(i.workPermitExpiry),
  kraPin: n(i.kraPin), nssfNo: n(i.nssfNo), shaNo: n(i.shaNo), helbRef: n(i.helbRef), disabilityCertNo: n(i.disabilityCertNo),
  disabilityCertExpiry: n(i.disabilityCertExpiry),
})

export const contactsInput = (c: ContactForm[]): Schemas['EmergencyContactInput'][] =>
  c.filter((x) => x.name.trim() || x.phone.trim()).map((x) => ({ name: n(x.name), relationship: n(x.relationship), phone: n(x.phone) }))

export function PersonalFields({ value, onChange, errors }: { value: PersonalForm; onChange: (v: PersonalForm) => void; errors: Errors }) {
  const set = (k: keyof PersonalForm) => (v: string) => onChange({ ...value, [k]: v })
  const e = (k: string) => errors[`personal.${k}`]
  return (
    <div className="grid-2">
      <TextField label="First name" value={value.firstName} onChange={set('firstName')} error={e('firstName')} required />
      <TextField label="Middle name" value={value.middleName} onChange={set('middleName')} error={e('middleName')} />
      <TextField label="Last name" value={value.lastName} onChange={set('lastName')} error={e('lastName')} required />
      <TextField label="Date of birth" type="date" value={value.dateOfBirth} onChange={set('dateOfBirth')} error={e('dateOfBirth')} required />
      <SelectField label="Gender" value={value.gender} onChange={set('gender')} placeholder="Choose" error={e('gender')} required
        hint="Used only for statutory leave eligibility."
        options={[{ value: 'female', label: 'Female' }, { value: 'male', label: 'Male' }, { value: 'intersex', label: 'Intersex' }]} />
      <SelectField label="Citizenship" value={value.citizenship} onChange={set('citizenship')} error={e('citizenship')}
        options={[{ value: 'kenyan', label: 'Kenyan' }, { value: 'non_kenyan', label: 'Non-Kenyan' }]} />
      <SelectField label="Tax residency" value={value.residencyStatus} onChange={set('residencyStatus')} error={e('residencyStatus')}
        options={[{ value: 'resident', label: 'Resident' }, { value: 'non_resident', label: 'Non-resident' }]} />
      <TextField label="Mobile phone" value={value.phoneMobile} onChange={set('phoneMobile')} error={e('phoneMobile')} required inputMode="tel" hint="07XX or +2547XX" />
      <TextField label="Work email" type="email" value={value.emailWork} onChange={set('emailWork')} error={e('emailWork')} />
      <TextField label="Personal email" type="email" value={value.emailPersonal} onChange={set('emailPersonal')} error={e('emailPersonal')} />
      <Checkbox label="Intern under 18 with guardian consent on file" checked={value.internGuardianConsent} onChange={(v) => onChange({ ...value, internGuardianConsent: v })} />
    </div>
  )
}

export function IdentifierFields({ value, onChange, errors, citizenship }: { value: IdentifiersForm; onChange: (v: IdentifiersForm) => void; errors: Errors; citizenship: string }) {
  const set = (k: keyof IdentifiersForm) => (v: string) => onChange({ ...value, [k]: v })
  const e = (k: string) => errors[`identifiers.${k}`]
  const foreign = citizenship === 'non_kenyan'
  return (
    <div className="grid-2">
      <TextField label="National ID" value={value.nationalId} onChange={set('nationalId')} error={e('nationalId')} inputMode="numeric" hint={foreign ? undefined : 'Or a passport number'} />
      <TextField label="Passport number" value={value.passportNo} onChange={set('passportNo')} error={e('passportNo')} required={foreign} />
      {foreign && <TextField label="Work permit number" value={value.workPermitNo} onChange={set('workPermitNo')} error={e('workPermitNo')} required />}
      {foreign && <TextField label="Work permit expiry" type="date" value={value.workPermitExpiry} onChange={set('workPermitExpiry')} error={e('workPermitExpiry')} required />}
      <TextField label="KRA PIN" value={value.kraPin} onChange={set('kraPin')} error={e('kraPin')} required hint="For example A123456789Z" />
      <TextField label="NSSF number" value={value.nssfNo} onChange={set('nssfNo')} error={e('nssfNo')} hint="Needed before the first payroll" />
      <TextField label="SHA number" value={value.shaNo} onChange={set('shaNo')} error={e('shaNo')} hint="Needed before the first payroll" />
      <TextField label="HELB reference" value={value.helbRef} onChange={set('helbRef')} error={e('helbRef')} />
      <TextField label="Disability certificate no." value={value.disabilityCertNo} onChange={set('disabilityCertNo')} error={e('disabilityCertNo')} />
      <TextField label="Certificate expiry" type="date" value={value.disabilityCertExpiry} onChange={set('disabilityCertExpiry')} error={e('disabilityCertExpiry')} />
    </div>
  )
}

export function ContactFields({ value, onChange, errors, prefix = 'emergencyContacts' }: { value: ContactForm[]; onChange: (v: ContactForm[]) => void; errors: Errors; prefix?: string }) {
  const set = (i: number, k: keyof ContactForm) => (v: string) => onChange(value.map((c, n) => (n === i ? { ...c, [k]: v } : c)))
  return (
    <div className="stack">
      {errors[prefix] && <span className="field-error">{errors[prefix]}</span>}
      {value.map((c, i) => (
        <div className="grid-2" key={i}>
          <TextField label={`Contact ${i + 1} name`} value={c.name} onChange={set(i, 'name')} error={errors[`${prefix}[${i}].name`]} required={i === 0} />
          <TextField label="Relationship" value={c.relationship} onChange={set(i, 'relationship')} error={errors[`${prefix}[${i}].relationship`]} required={i === 0} />
          <TextField label="Phone" value={c.phone} onChange={set(i, 'phone')} error={errors[`${prefix}[${i}].phone`]} required={i === 0} inputMode="tel" />
        </div>
      ))}
      {value.length < 5 && <button type="button" className="btn-link" onClick={() => onChange([...value, { name: '', relationship: '', phone: '' }])}>Add another contact</button>}
    </div>
  )
}

export function PaymentFields({ value, onChange, errors, prefix = 'paymentDetails.' }: { value: PaymentForm; onChange: (v: PaymentForm) => void; errors: Errors; prefix?: string }) {
  const [branches, setBranches] = useState<Schemas['BankBranchDto'][]>([])
  useEffect(() => { call(api.GET('/api/v1/people/bank-branches')).then(setBranches).catch(() => setBranches([])) }, [])
  const set = (k: keyof PaymentForm) => (v: string) => onChange({ ...value, [k]: v })
  const e = (k: string) => errors[prefix + k]
  const banks = [...new Map(branches.map((b) => [b.bankCode, b.bankName])).entries()].map(([code, name]) => ({ value: code, label: `${code} · ${name}` }))
  const branchOptions = branches.filter((b) => b.bankCode === value.bankCode).map((b) => ({ value: b.branchCode, label: `${b.branchCode} · ${b.branchName}` }))
  return (
    <div className="grid-2">
      <SelectField label="Payment method" value={value.paymentMethod} onChange={set('paymentMethod')} error={e('paymentMethod')}
        options={[{ value: 'bank', label: 'Bank transfer' }, { value: 'mpesa', label: 'M-Pesa' }, { value: 'cash', label: 'Cash' }]} />
      {value.paymentMethod === 'bank' && <>
        <SelectField label="Bank" value={value.bankCode} onChange={(v) => onChange({ ...value, bankCode: v, bankBranchCode: '' })} options={banks} placeholder="Choose" error={e('bankCode')}
          hint={banks.length === 0 ? 'Add banks under People › Bank branches first.' : undefined} />
        <SelectField label="Branch" value={value.bankBranchCode} onChange={set('bankBranchCode')} options={branchOptions} placeholder="Choose" error={e('bankBranchCode')} />
        <TextField label="Account number" value={value.accountNo} onChange={set('accountNo')} error={e('accountNo')} />
        <TextField label="Account name" value={value.accountName} onChange={set('accountName')} error={e('accountName')} />
      </>}
      {value.paymentMethod === 'mpesa' && <TextField label="M-Pesa number" value={value.mpesaPhone} onChange={set('mpesaPhone')} error={e('mpesaPhone')} inputMode="tel" />}
    </div>
  )
}
