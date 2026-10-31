import { api, call, type Schemas } from '../../api/client'
import type { Option } from '../../components/Fields'

export type Values = Record<string, string | boolean | number[]>

export interface Lookups {
  legalEntities: Option[]
  branches: Option[]
  departments: Option[]
  grades: Option[]
  costCenters: Option[]
  positions: Option[]
}

export interface FormField {
  name: string
  label: string
  kind: 'text' | 'number' | 'select' | 'time' | 'days'
  required?: boolean
  createOnly?: boolean
  hint?: string
  options?: (l: Lookups) => Option[]
}

export interface Row {
  id: string
  code: string
  name: string
  isActive: boolean
  version: number | string
  legalEntityId?: string
}

export interface Paged<T> {
  items: T[]
  page: number | string
  pageSize: number | string
  total: number | string
}

export interface ListQuery {
  legalEntityId?: string
  includeInactive?: boolean
  search?: string
  page?: number
  pageSize?: number
}

export interface SectionDef<T extends Row = Row> {
  key: string
  title: string
  singular: string
  employerScoped: boolean
  columns: { header: string; render: (row: T, l: Lookups) => string | number | null | undefined }[]
  fields: FormField[]
  list: (q: ListQuery) => Promise<Paged<T>>
  create: (v: Values, legalEntityId: string) => Promise<T>
  update: (row: T, v: Values) => Promise<T>
  toValues: (row: T) => Values
}

const s = (v: Values, k: string) => (typeof v[k] === 'string' ? (v[k] as string).trim() : '')
const opt = (v: Values, k: string) => s(v, k) || null
const reason = (v: Values) => opt(v, 'reason')
const labelOf = (options: Option[], id?: string | null) => (id ? options.find((o) => o.value === id)?.label ?? '—' : '—')
const ver = (r: Row) => Number(r.version)
const DAYS = ['', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']

const leField: FormField = { name: 'legalEntityId', label: 'Legal employer', kind: 'select', required: true, createOnly: true, options: (l) => l.legalEntities }
const codeField: FormField = { name: 'code', label: 'Code', kind: 'text', required: true, createOnly: true, hint: 'Letters, digits, - or _. Cannot be changed later.' }
const nameField: FormField = { name: 'name', label: 'Name', kind: 'text', required: true }

export const legalEntities: SectionDef<Schemas['LegalEntityDto']> = {
  key: 'legal-entities',
  title: 'Legal employers',
  singular: 'legal employer',
  employerScoped: false,
  columns: [
    { header: 'Code', render: (r) => r.code },
    { header: 'Name', render: (r) => r.name },
    { header: 'KRA PIN', render: (r) => r.kraPin ?? '—' },
    { header: 'NSSF employer no.', render: (r) => r.nssfEmployerNumber ?? '—' },
    { header: 'Time zone', render: (r) => r.timezone },
  ],
  fields: [
    codeField,
    nameField,
    { name: 'registrationNumber', label: 'Registration number', kind: 'text' },
    { name: 'kraPin', label: 'KRA PIN', kind: 'text', hint: 'For example P051234567X' },
    { name: 'nssfEmployerNumber', label: 'NSSF employer number', kind: 'text' },
    { name: 'timezone', label: 'Time zone', kind: 'text', required: true, hint: 'IANA name, for example Africa/Nairobi' },
  ],
  list: (q) => call(api.GET('/api/v1/org/legal-entities', { params: { query: { includeInactive: q.includeInactive, search: q.search, page: q.page, pageSize: q.pageSize } } })),
  create: (v) => call(api.POST('/api/v1/org/legal-entities', { body: { code: s(v, 'code'), name: s(v, 'name'), registrationNumber: opt(v, 'registrationNumber'), kraPin: opt(v, 'kraPin'), nssfEmployerNumber: opt(v, 'nssfEmployerNumber'), timezone: opt(v, 'timezone') } })),
  update: (r, v) => call(api.PUT('/api/v1/org/legal-entities/{id}', { params: { path: { id: r.id } }, body: { name: s(v, 'name'), registrationNumber: opt(v, 'registrationNumber'), kraPin: opt(v, 'kraPin'), nssfEmployerNumber: opt(v, 'nssfEmployerNumber'), timezone: s(v, 'timezone'), isActive: v.isActive === true, version: ver(r), reason: reason(v) } })),
  toValues: (r) => ({ code: r.code, name: r.name, registrationNumber: r.registrationNumber ?? '', kraPin: r.kraPin ?? '', nssfEmployerNumber: r.nssfEmployerNumber ?? '', timezone: r.timezone, isActive: r.isActive }),
}

export const branches: SectionDef<Schemas['BranchDto']> = {
  key: 'branches',
  title: 'Branches',
  singular: 'branch',
  employerScoped: true,
  columns: [
    { header: 'Code', render: (r) => r.code },
    { header: 'Name', render: (r) => r.name },
    { header: 'Address', render: (r) => r.address ?? '—' },
  ],
  fields: [leField, codeField, nameField, { name: 'address', label: 'Address', kind: 'text' }],
  list: (q) => call(api.GET('/api/v1/org/branches', { params: { query: q } })),
  create: (v, le) => call(api.POST('/api/v1/org/branches', { body: { legalEntityId: le, code: s(v, 'code'), name: s(v, 'name'), address: opt(v, 'address') } })),
  update: (r, v) => call(api.PUT('/api/v1/org/branches/{id}', { params: { path: { id: r.id } }, body: { name: s(v, 'name'), address: opt(v, 'address'), isActive: v.isActive === true, version: ver(r), reason: reason(v) } })),
  toValues: (r) => ({ code: r.code, name: r.name, address: r.address ?? '', isActive: r.isActive }),
}

export const costCenters: SectionDef<Schemas['CostCenterDto']> = {
  key: 'cost-centers',
  title: 'Cost centers',
  singular: 'cost center',
  employerScoped: true,
  columns: [{ header: 'Code', render: (r) => r.code }, { header: 'Name', render: (r) => r.name }],
  fields: [leField, codeField, nameField],
  list: (q) => call(api.GET('/api/v1/org/cost-centers', { params: { query: q } })),
  create: (v, le) => call(api.POST('/api/v1/org/cost-centers', { body: { legalEntityId: le, code: s(v, 'code'), name: s(v, 'name') } })),
  update: (r, v) => call(api.PUT('/api/v1/org/cost-centers/{id}', { params: { path: { id: r.id } }, body: { name: s(v, 'name'), isActive: v.isActive === true, version: ver(r), reason: reason(v) } })),
  toValues: (r) => ({ code: r.code, name: r.name, isActive: r.isActive }),
}

export const grades: SectionDef<Schemas['GradeDto']> = {
  key: 'grades',
  title: 'Grades',
  singular: 'grade',
  employerScoped: true,
  columns: [{ header: 'Code', render: (r) => r.code }, { header: 'Name', render: (r) => r.name }, { header: 'Rank', render: (r) => Number(r.rank) }],
  fields: [leField, codeField, nameField, { name: 'rank', label: 'Rank', kind: 'number', required: true, hint: '1 is the most junior grade' }],
  list: (q) => call(api.GET('/api/v1/org/grades', { params: { query: q } })),
  create: (v, le) => call(api.POST('/api/v1/org/grades', { body: { legalEntityId: le, code: s(v, 'code'), name: s(v, 'name'), rank: Number(s(v, 'rank')) } })),
  update: (r, v) => call(api.PUT('/api/v1/org/grades/{id}', { params: { path: { id: r.id } }, body: { name: s(v, 'name'), rank: Number(s(v, 'rank')), isActive: v.isActive === true, version: ver(r), reason: reason(v) } })),
  toValues: (r) => ({ code: r.code, name: r.name, rank: String(r.rank), isActive: r.isActive }),
}

export const departments: SectionDef<Schemas['DepartmentDto']> = {
  key: 'departments',
  title: 'Departments',
  singular: 'department',
  employerScoped: true,
  columns: [
    { header: 'Code', render: (r) => r.code },
    { header: 'Name', render: (r) => r.name },
    { header: 'Parent', render: (r, l) => labelOf(l.departments, r.parentDepartmentId) },
    { header: 'Cost center', render: (r, l) => labelOf(l.costCenters, r.costCenterId) },
  ],
  fields: [
    leField, codeField, nameField,
    { name: 'parentDepartmentId', label: 'Parent department', kind: 'select', options: (l) => l.departments },
    { name: 'costCenterId', label: 'Cost center', kind: 'select', options: (l) => l.costCenters },
  ],
  list: (q) => call(api.GET('/api/v1/org/departments', { params: { query: q } })),
  create: (v, le) => call(api.POST('/api/v1/org/departments', { body: { legalEntityId: le, code: s(v, 'code'), name: s(v, 'name'), parentDepartmentId: opt(v, 'parentDepartmentId'), costCenterId: opt(v, 'costCenterId') } })),
  update: (r, v) => call(api.PUT('/api/v1/org/departments/{id}', { params: { path: { id: r.id } }, body: { name: s(v, 'name'), parentDepartmentId: opt(v, 'parentDepartmentId'), costCenterId: opt(v, 'costCenterId'), isActive: v.isActive === true, version: ver(r), reason: reason(v) } })),
  toValues: (r) => ({ code: r.code, name: r.name, parentDepartmentId: r.parentDepartmentId ?? '', costCenterId: r.costCenterId ?? '', isActive: r.isActive }),
}

export const positions: SectionDef<Schemas['PositionDto']> = {
  key: 'positions',
  title: 'Positions and reporting lines',
  singular: 'position',
  employerScoped: true,
  columns: [
    { header: 'Code', render: (r) => r.code },
    { header: 'Title', render: (r) => r.name },
    { header: 'Reports to', render: (r, l) => labelOf(l.positions, r.reportsToPositionId) },
    { header: 'Department', render: (r, l) => labelOf(l.departments, r.departmentId) },
    { header: 'Grade', render: (r, l) => labelOf(l.grades, r.gradeId) },
    { header: 'Branch', render: (r, l) => labelOf(l.branches, r.branchId) },
  ],
  fields: [
    leField, codeField, { ...nameField, label: 'Title' },
    { name: 'reportsToPositionId', label: 'Reports to', kind: 'select', options: (l) => l.positions, hint: 'Circular reporting lines are rejected.' },
    { name: 'departmentId', label: 'Department', kind: 'select', options: (l) => l.departments },
    { name: 'gradeId', label: 'Grade', kind: 'select', options: (l) => l.grades },
    { name: 'branchId', label: 'Branch', kind: 'select', options: (l) => l.branches },
  ],
  list: (q) => call(api.GET('/api/v1/org/positions', { params: { query: q } })),
  create: (v, le) => call(api.POST('/api/v1/org/positions', { body: { legalEntityId: le, code: s(v, 'code'), name: s(v, 'name'), departmentId: opt(v, 'departmentId'), gradeId: opt(v, 'gradeId'), branchId: opt(v, 'branchId'), reportsToPositionId: opt(v, 'reportsToPositionId') } })),
  update: (r, v) => call(api.PUT('/api/v1/org/positions/{id}', { params: { path: { id: r.id } }, body: { name: s(v, 'name'), departmentId: opt(v, 'departmentId'), gradeId: opt(v, 'gradeId'), branchId: opt(v, 'branchId'), reportsToPositionId: opt(v, 'reportsToPositionId'), isActive: v.isActive === true, version: ver(r), reason: reason(v) } })),
  toValues: (r) => ({ code: r.code, name: r.name, departmentId: r.departmentId ?? '', gradeId: r.gradeId ?? '', branchId: r.branchId ?? '', reportsToPositionId: r.reportsToPositionId ?? '', isActive: r.isActive }),
}

export const holidayCalendars: SectionDef<Schemas['HolidayCalendarDto']> = {
  key: 'holiday-calendars',
  title: 'Holiday calendars',
  singular: 'holiday calendar',
  employerScoped: true,
  columns: [{ header: 'Code', render: (r) => r.code }, { header: 'Name', render: (r) => r.name }],
  fields: [leField, codeField, nameField],
  list: (q) => call(api.GET('/api/v1/org/holiday-calendars', { params: { query: q } })),
  create: (v, le) => call(api.POST('/api/v1/org/holiday-calendars', { body: { legalEntityId: le, code: s(v, 'code'), name: s(v, 'name') } })),
  update: (r, v) => call(api.PUT('/api/v1/org/holiday-calendars/{id}', { params: { path: { id: r.id } }, body: { name: s(v, 'name'), isActive: v.isActive === true, version: ver(r), reason: reason(v) } })),
  toValues: (r) => ({ code: r.code, name: r.name, isActive: r.isActive }),
}

export const workSchedules: SectionDef<Schemas['WorkScheduleDto']> = {
  key: 'work-schedules',
  title: 'Working schedules',
  singular: 'working schedule',
  employerScoped: true,
  columns: [
    { header: 'Code', render: (r) => r.code },
    { header: 'Name', render: (r) => r.name },
    { header: 'Hours', render: (r) => `${r.startTime}–${r.endTime}${r.isOvernight ? ' (overnight)' : ''}` },
    { header: 'Break (min)', render: (r) => Number(r.breakMinutes) },
    { header: 'Days', render: (r) => r.workingDays.map((d) => DAYS[Number(d)]).join(', ') },
  ],
  fields: [
    leField, codeField, nameField,
    { name: 'startTime', label: 'Start time', kind: 'time', required: true },
    { name: 'endTime', label: 'End time', kind: 'time', required: true, hint: 'An end before the start means an overnight shift.' },
    { name: 'breakMinutes', label: 'Break (minutes)', kind: 'number', required: true },
    { name: 'workingDays', label: 'Working days', kind: 'days', required: true },
  ],
  list: (q) => call(api.GET('/api/v1/org/work-schedules', { params: { query: q } })),
  create: (v, le) => call(api.POST('/api/v1/org/work-schedules', { body: { legalEntityId: le, code: s(v, 'code'), name: s(v, 'name'), startTime: s(v, 'startTime'), endTime: s(v, 'endTime'), breakMinutes: Number(s(v, 'breakMinutes')), workingDays: (v.workingDays as number[]) ?? [] } })),
  update: (r, v) => call(api.PUT('/api/v1/org/work-schedules/{id}', { params: { path: { id: r.id } }, body: { name: s(v, 'name'), startTime: s(v, 'startTime'), endTime: s(v, 'endTime'), breakMinutes: Number(s(v, 'breakMinutes')), workingDays: (v.workingDays as number[]) ?? [], isActive: v.isActive === true, version: ver(r), reason: reason(v) } })),
  toValues: (r) => ({ code: r.code, name: r.name, startTime: r.startTime, endTime: r.endTime, breakMinutes: String(r.breakMinutes), workingDays: r.workingDays.map(Number), isActive: r.isActive }),
}

export const ALL_SECTIONS = [legalEntities, branches, departments, positions, grades, costCenters, holidayCalendars, workSchedules] as unknown as SectionDef[]

export const DAY_LABELS = DAYS
