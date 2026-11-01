/** Employee status with a text label; colour is never the only signal (WCAG 1.4.1). */
const LABELS: Record<string, string> = { active: 'Active', pending_start: 'Starts soon', suspended: 'Suspended', terminated: 'Left' }
const CLASSES: Record<string, string> = { active: 'badge badge-ok', pending_start: 'badge', suspended: 'badge badge-warn', terminated: 'badge badge-muted' }

export function EmployeeStatus({ status }: { status: string }) {
  return <span className={CLASSES[status] ?? 'badge'} data-status={status}>{LABELS[status] ?? status}</span>
}
