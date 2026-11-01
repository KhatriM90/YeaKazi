/**
 * Payroll run status badge. Colour is never the only signal: the text label is always
 * shown (WCAG 1.4.1). Used from S3; defined in S1 as part of the design system.
 */
export type PayrollStatus = 'draft' | 'calculated' | 'in_review' | 'approved' | 'locked' | 'rejected' | 'failed' | 'outcome_unknown'

const LABELS: Record<PayrollStatus, string> = {
  draft: 'Draft',
  calculated: 'Calculated',
  in_review: 'In review',
  approved: 'Approved',
  locked: 'Locked',
  rejected: 'Rejected',
  failed: 'Failed',
  outcome_unknown: 'Outcome unknown',
}

const COLOURS: Record<PayrollStatus, string> = {
  draft: 'var(--status-draft)',
  calculated: 'var(--status-calculated)',
  in_review: 'var(--status-in-review)',
  approved: 'var(--status-approved)',
  locked: 'var(--status-locked)',
  rejected: 'var(--status-rejected)',
  failed: 'var(--status-rejected)',
  outcome_unknown: 'var(--status-outcome-unknown)',
}

export function StatusBadge({ status }: { status: PayrollStatus }) {
  return (
    <span className="badge" style={{ color: COLOURS[status] }} data-status={status}>
      {LABELS[status]}
    </span>
  )
}

/** Formats a KES amount with tabular figures. Amounts arrive from the API as decimal strings or numbers. */
export function Money({ value, currency = 'KES' }: { value: number | string; currency?: string }) {
  const n = typeof value === 'string' ? Number(value) : value
  const text = new Intl.NumberFormat('en-KE', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(n)
  return <span className="money" data-money>{currency} {text}</span>
}
