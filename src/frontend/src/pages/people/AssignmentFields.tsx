import { useEffect, useState } from 'react'
import { api, call } from '../../api/client'
import { SelectField, TextField, type Option } from '../../components/Fields'
import { PAY_BASES_FOR, PAY_BASIS_LABEL, WORKER_CATEGORIES, type AssignmentForm, type OrgOptions } from './model'

/** Job, organization placement and pay basis for one assignment version (EMP-02, EMP-04). */
export function AssignmentFields({ value, onChange, org, errors, prefix = 'assignment.', legalEntityId, excludeEmployeeId }: {
  value: AssignmentForm
  onChange: (v: AssignmentForm) => void
  org: OrgOptions
  errors: Record<string, string>
  prefix?: string
  legalEntityId: string
  excludeEmployeeId?: string
}) {
  const set = (k: keyof AssignmentForm) => (v: string) => {
    const next = { ...value, [k]: v }
    if (k === 'workerCategory') {
      const allowed = PAY_BASES_FOR[v] ?? []
      if (!allowed.includes(next.payBasis)) next.payBasis = allowed[0] ?? ''
    }
    onChange(next)
  }
  const err = (k: string) => errors[prefix + k]
  const bases = (PAY_BASES_FOR[value.workerCategory] ?? []).map((b) => ({ value: b, label: PAY_BASIS_LABEL[b] }))
  const managers = useManagers(legalEntityId, excludeEmployeeId)

  return (
    <div className="grid-2">
      <SelectField label="Worker category" value={value.workerCategory} onChange={set('workerCategory')} options={WORKER_CATEGORIES} error={err('workerCategory')} required />
      <SelectField label="Pay basis" value={value.payBasis} onChange={set('payBasis')} options={bases} error={err('payBasis')} required
        hint={value.workerCategory === 'contractor' ? 'Contractors are paid outside payroll statutory deductions.' : undefined} />
      <TextField label="Job title" value={value.jobTitle} onChange={set('jobTitle')} error={err('jobTitle')} />
      {value.workerCategory === 'fixed_term' && <TextField label="Contract end date" type="date" value={value.contractEndDate} onChange={set('contractEndDate')} error={err('contractEndDate')} required />}
      {value.workerCategory === 'probation' && <TextField label="Probation end date" type="date" value={value.probationEndDate} onChange={set('probationEndDate')} error={err('probationEndDate')} required />}
      <SelectField label="Department" value={value.departmentId} onChange={set('departmentId')} options={org.departments} placeholder="None" error={err('departmentId')} />
      <SelectField label="Position" value={value.positionId} onChange={set('positionId')} options={org.positions} placeholder="None" error={err('positionId')} />
      <SelectField label="Location (branch)" value={value.branchId} onChange={set('branchId')} options={org.branches} placeholder="None" error={err('branchId')} />
      <SelectField label="Cost center" value={value.costCenterId} onChange={set('costCenterId')} options={org.costCenters} placeholder="None" error={err('costCenterId')} />
      <SelectField label="Grade" value={value.gradeId} onChange={set('gradeId')} options={org.grades} placeholder="None" error={err('gradeId')} />
      <SelectField label="Working schedule" value={value.workScheduleId} onChange={set('workScheduleId')} options={org.workSchedules} placeholder="None" error={err('workScheduleId')} />
      <SelectField label="Reports to" value={value.managerEmployeeId} onChange={set('managerEmployeeId')} options={managers} placeholder="Nobody" error={err('managerEmployeeId')} />
    </div>
  )
}

function useManagers(legalEntityId: string, exclude?: string): Option[] {
  const [options, setOptions] = useState<Option[]>([])
  useEffect(() => {
    if (!legalEntityId) return
    call(api.GET('/api/v1/people/employees', { params: { query: { legalEntityId, status: 'active', pageSize: 200 } } }))
      .then((d) => setOptions(d.items.filter((e) => e.id !== exclude).map((e) => ({ value: e.id, label: `${e.fullName} (${e.employeeNumber})` }))))
      .catch(() => setOptions([]))
  }, [legalEntityId, exclude])
  return options
}
