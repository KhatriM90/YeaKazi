import { api, call, type Schemas } from '../../api/client'
import type { Option } from '../../components/Fields'

export type Employee = Schemas['EmployeeDto']
export type EmployeeSummary = Schemas['EmployeeSummaryDto']
export type Assignment = Schemas['AssignmentDto']
export type AssignmentInput = Schemas['AssignmentInput']
export type PaymentInput = Schemas['PaymentDetailsInput']

export const WORKER_CATEGORIES: Option[] = [
  { value: 'permanent', label: 'Permanent' },
  { value: 'fixed_term', label: 'Fixed-term contract' },
  { value: 'probation', label: 'On probation' },
  { value: 'casual', label: 'Casual' },
  { value: 'piece_rate', label: 'Piece-rate' },
  { value: 'intern', label: 'Intern / attaché' },
  { value: 'contractor', label: 'Contractor (non-employee)' },
]

/** Mirrors WorkerCategories.AllowedPayBases on the server; the server re-checks. */
export const PAY_BASES_FOR: Record<string, string[]> = {
  permanent: ['monthly', 'daily', 'hourly'],
  fixed_term: ['monthly', 'daily', 'hourly'],
  probation: ['monthly', 'daily', 'hourly'],
  intern: ['monthly', 'daily', 'hourly'],
  casual: ['daily', 'hourly'],
  piece_rate: ['piece_rate'],
  contractor: ['monthly', 'daily', 'hourly', 'piece_rate'],
}

export const PAY_BASIS_LABEL: Record<string, string> = { monthly: 'Monthly', daily: 'Daily', hourly: 'Hourly', piece_rate: 'Piece-rate' }
export const CHANGE_LABEL: Record<string, string> = {
  hire: 'Hired', rehire: 'Rehired', promotion: 'Promotion', transfer: 'Transfer', change: 'Change', suspension: 'Suspension', reinstatement: 'Reinstatement',
}
export const TERMINATION_TYPES: Option[] = [
  { value: 'resignation', label: 'Resignation' },
  { value: 'end_of_contract', label: 'End of contract' },
  { value: 'dismissal', label: 'Dismissal' },
  { value: 'redundancy', label: 'Redundancy' },
  { value: 'retirement', label: 'Retirement' },
  { value: 'death', label: 'Death' },
  { value: 'mutual_agreement', label: 'Mutual agreement' },
  { value: 'other', label: 'Other' },
]
export const GAP_LABEL: Record<string, string> = {
  nssf_no_missing: 'NSSF number missing',
  sha_no_missing: 'SHA number missing',
  payment_details_not_approved: 'Payment details not approved',
  work_permit_expired: 'Work permit expired',
  disability_certificate_expired: 'Disability certificate expired',
}
export const PERMIT_ALERT_LABEL: Record<string, string> = {
  expired: 'Permit expired', '7_days': 'Permit expires within 7 days', '30_days': 'Permit expires within 30 days', '60_days': 'Permit expires within 60 days',
}
export const label = (options: Option[], value?: string | null) => options.find((o) => o.value === value)?.label ?? value ?? '—'

export interface OrgOptions {
  departments: Option[]
  positions: Option[]
  branches: Option[]
  costCenters: Option[]
  grades: Option[]
  workSchedules: Option[]
}

export const NO_ORG: OrgOptions = { departments: [], positions: [], branches: [], costCenters: [], grades: [], workSchedules: [] }

const toOptions = (rows: { id: string; code: string; name: string }[]): Option[] => rows.map((r) => ({ value: r.id, label: `${r.code} · ${r.name}` }))

export async function loadOrgOptions(legalEntityId: string): Promise<OrgOptions> {
  if (!legalEntityId) return NO_ORG
  const query = { legalEntityId, pageSize: 200 }
  const [dp, ps, br, cc, gr, ws] = await Promise.all([
    call(api.GET('/api/v1/org/departments', { params: { query } })),
    call(api.GET('/api/v1/org/positions', { params: { query } })),
    call(api.GET('/api/v1/org/branches', { params: { query } })),
    call(api.GET('/api/v1/org/cost-centers', { params: { query } })),
    call(api.GET('/api/v1/org/grades', { params: { query } })),
    call(api.GET('/api/v1/org/work-schedules', { params: { query } })),
  ])
  return {
    departments: toOptions(dp.items), positions: toOptions(ps.items), branches: toOptions(br.items),
    costCenters: toOptions(cc.items), grades: toOptions(gr.items), workSchedules: toOptions(ws.items),
  }
}

export async function loadEmployers(): Promise<Option[]> {
  const les = await call(api.GET('/api/v1/org/legal-entities', { params: { query: { pageSize: 200 } } }))
  return les.items.map((l) => ({ value: l.id, label: `${l.code} · ${l.name}` }))
}

export interface AssignmentForm {
  workerCategory: string
  payBasis: string
  jobTitle: string
  contractEndDate: string
  probationEndDate: string
  departmentId: string
  positionId: string
  branchId: string
  costCenterId: string
  gradeId: string
  workScheduleId: string
  managerEmployeeId: string
}

export const EMPTY_ASSIGNMENT: AssignmentForm = {
  workerCategory: 'permanent', payBasis: 'monthly', jobTitle: '', contractEndDate: '', probationEndDate: '', departmentId: '', positionId: '',
  branchId: '', costCenterId: '', gradeId: '', workScheduleId: '', managerEmployeeId: '',
}

const n = (v: string) => (v.trim() === '' ? null : v.trim())

export function assignmentInput(f: AssignmentForm): AssignmentInput {
  return {
    workerCategory: n(f.workerCategory), payBasis: n(f.payBasis), jobTitle: n(f.jobTitle), contractEndDate: n(f.contractEndDate),
    probationEndDate: n(f.probationEndDate), departmentId: n(f.departmentId), positionId: n(f.positionId), branchId: n(f.branchId),
    costCenterId: n(f.costCenterId), gradeId: n(f.gradeId), workScheduleId: n(f.workScheduleId), managerEmployeeId: n(f.managerEmployeeId),
  }
}

export function assignmentForm(a: Assignment | null | undefined): AssignmentForm {
  if (!a) return EMPTY_ASSIGNMENT
  return {
    workerCategory: a.workerCategory, payBasis: a.payBasis, jobTitle: a.jobTitle ?? '', contractEndDate: a.contractEndDate ?? '',
    probationEndDate: a.probationEndDate ?? '', departmentId: a.departmentId ?? '', positionId: a.positionId ?? '', branchId: a.branchId ?? '',
    costCenterId: a.costCenterId ?? '', gradeId: a.gradeId ?? '', workScheduleId: a.workScheduleId ?? '', managerEmployeeId: a.managerEmployeeId ?? '',
  }
}

export interface PaymentForm {
  paymentMethod: string
  bankCode: string
  bankBranchCode: string
  accountNo: string
  accountName: string
  mpesaPhone: string
}

export const EMPTY_PAYMENT: PaymentForm = { paymentMethod: 'bank', bankCode: '', bankBranchCode: '', accountNo: '', accountName: '', mpesaPhone: '' }

export function paymentInput(f: PaymentForm): PaymentInput {
  return {
    paymentMethod: n(f.paymentMethod), bankCode: n(f.bankCode), bankBranchCode: n(f.bankBranchCode), accountNo: n(f.accountNo),
    accountName: n(f.accountName), mpesaPhone: n(f.mpesaPhone),
  }
}

export const today = () => new Date().toLocaleDateString('en-CA', { timeZone: 'Africa/Nairobi' })
export const formatDate = (d?: string | null) => (d ? new Date(`${d}T00:00:00`).toLocaleDateString('en-KE', { day: 'numeric', month: 'short', year: 'numeric' }) : '—')
