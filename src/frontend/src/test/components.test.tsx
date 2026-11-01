import { render, screen } from '@testing-library/react'
import { ApiError, messageFor } from '../api/client'
import { Money, StatusBadge, type PayrollStatus } from '../components/StatusBadge'

describe('StatusBadge', () => {
  const cases: [PayrollStatus, string, string][] = [
    ['draft', 'Draft', '--status-draft'],
    ['calculated', 'Calculated', '--status-calculated'],
    ['in_review', 'In review', '--status-in-review'],
    ['approved', 'Approved', '--status-approved'],
    ['locked', 'Locked', '--status-locked'],
    ['rejected', 'Rejected', '--status-rejected'],
    ['failed', 'Failed', '--status-rejected'],
    ['outcome_unknown', 'Outcome unknown', '--status-outcome-unknown'],
  ]
  it.each(cases)('%s always shows a text label and its token colour', (status, label, token) => {
    render(<StatusBadge status={status} />)
    const badge = screen.getByText(label)
    expect(badge).toBeInTheDocument()
    expect(badge.getAttribute('style')).toContain(token)
  })
})

describe('Money', () => {
  it('uses tabular figures and two decimals', () => {
    render(<Money value="53800" />)
    const el = screen.getByText(/53,800\.00/)
    expect(el).toHaveClass('money')
    expect(el.textContent).toContain('KES')
  })
})

describe('messageFor', () => {
  it('adds a short correlation reference to API errors', () => {
    const err = new ApiError({ status: 409, title: 'Conflict', code: 'duplicate', correlationId: 'abcdef1234567890' })
    expect(messageFor(err)).toBe('Conflict (reference abcdef12)')
  })
  it('maps forbidden responses to a clear message', () => {
    expect(messageFor(new ApiError({ status: 403 }))).toMatch(/permission/)
  })
  it('collects field errors', () => {
    const err = new ApiError({ status: 400, errors: { name: ['Required.'], code: ['Too long.', 'Bad.'] } })
    expect(err.fieldErrors()).toEqual({ name: 'Required.', code: 'Too long. Bad.' })
  })
})
