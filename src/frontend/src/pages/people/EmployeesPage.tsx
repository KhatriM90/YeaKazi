import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api, call, messageFor, type Schemas } from '../../api/client'
import { SelectField, TextField, type Option } from '../../components/Fields'
import { Pager } from '../../components/Pager'
import { useSession } from '../../auth/session'
import { EmployeeStatus } from './EmployeeStatus'
import { GAP_LABEL, WORKER_CATEGORIES, formatDate, label, loadEmployers } from './model'
import { NewEmployeeDialog } from './NewEmployeeDialog'

const STATUSES: Option[] = [
  { value: 'active', label: 'Active' }, { value: 'pending_start', label: 'Starts soon' },
  { value: 'suspended', label: 'Suspended' }, { value: 'terminated', label: 'Left' },
]

/** EMP-01 employee directory. */
export function EmployeesPage() {
  const { can } = useSession()
  const navigate = useNavigate()
  const [employers, setEmployers] = useState<Option[]>([])
  const [legalEntityId, setLegalEntityId] = useState('')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<Schemas['PagedResultOfEmployeeSummaryDto'] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [hiring, setHiring] = useState(false)

  useEffect(() => { loadEmployers().then(setEmployers).catch((e) => setError(messageFor(e))) }, [])

  useEffect(() => {
    const t = setTimeout(() => {
      call(api.GET('/api/v1/people/employees', { params: { query: {
        legalEntityId: legalEntityId || undefined, search: search || undefined, status: status || undefined, page, pageSize: 25,
      } } }))
        .then((d) => { setData(d); setError(null) })
        .catch((e) => setError(messageFor(e)))
    }, 200)
    return () => clearTimeout(t)
  }, [legalEntityId, search, status, page])

  return (
    <div className="stack">
      <div className="spread">
        <h1>Employees</h1>
        <div className="row">
          {can('people.write') && <Link to="/people/import" className="btn btn-secondary">Import from file</Link>}
          {can('people.write') && <button className="btn-primary" onClick={() => setHiring(true)}>Add employee</button>}
        </div>
      </div>
      <div className="grid-2">
        <SelectField label="Legal employer" value={legalEntityId} onChange={(v) => { setLegalEntityId(v); setPage(1) }} options={employers} placeholder="All employers" />
        <TextField label="Search" value={search} onChange={(v) => { setSearch(v); setPage(1) }} placeholder="Name or employee number" />
        <SelectField label="Status" value={status} onChange={(v) => { setStatus(v); setPage(1) }} options={STATUSES} placeholder="Any status" />
      </div>
      {error && <div role="alert" className="alert alert-error">{error}</div>}
      <div className="table-wrap">
        <table>
          <thead><tr><th>No.</th><th>Name</th><th>Job title</th><th>Category</th><th>Hired</th><th>Status</th><th>Before payroll</th></tr></thead>
          <tbody>
            {data?.items.map((e) => (
              <tr key={e.id}>
                <td className="mono">{e.employeeNumber}</td>
                <td><Link to={`/people/${e.id}`}>{e.fullName}</Link></td>
                <td>{e.jobTitle ?? '—'}</td>
                <td>{label(WORKER_CATEGORIES, e.workerCategory)}</td>
                <td>{formatDate(e.hireDate)}</td>
                <td><EmployeeStatus status={e.status} /></td>
                <td>{e.statutoryGaps.length === 0 ? <span className="badge badge-ok">Complete</span>
                  : <span className="badge badge-warn" title={e.statutoryGaps.map((g) => GAP_LABEL[g] ?? g).join(', ')}>{e.statutoryGaps.length} to fix</span>}</td>
              </tr>
            ))}
            {data && data.items.length === 0 && <tr><td colSpan={7} className="muted">No employees match.</td></tr>}
          </tbody>
        </table>
      </div>
      {data && <Pager page={Number(data.page)} pageSize={Number(data.pageSize)} total={Number(data.total)} onPage={setPage} />}
      {hiring && <NewEmployeeDialog employers={employers} defaultEmployer={legalEntityId || employers[0]?.value || ''}
        onClose={() => setHiring(false)} onCreated={(id) => navigate(`/people/${id}`)} />}
    </div>
  )
}
