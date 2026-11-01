import { fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import { RecoveryCodes } from '../auth/SignIn'
import { AssignmentFields } from '../pages/people/AssignmentFields'
import { EmployeeStatus } from '../pages/people/EmployeeStatus'
import { EMPTY_ASSIGNMENT, NO_ORG, PAY_BASES_FOR, assignmentInput, paymentInput, type AssignmentForm } from '../pages/people/model'

describe('EmployeeStatus', () => {
  it.each([['active', 'Active'], ['pending_start', 'Starts soon'], ['suspended', 'Suspended'], ['terminated', 'Left']])(
    '%s is always labelled in text', (status, text) => {
      render(<EmployeeStatus status={status} />)
      expect(screen.getByText(text)).toHaveAttribute('data-status', status)
    })
})

describe('worker category and pay basis', () => {
  it('casual workers cannot be paid monthly and piece-rate workers only by piece', () => {
    expect(PAY_BASES_FOR.casual).not.toContain('monthly')
    expect(PAY_BASES_FOR.piece_rate).toEqual(['piece_rate'])
  })

  it('switching category resets an incompatible pay basis', () => {
    function Harness() {
      const [v, setV] = useState<AssignmentForm>(EMPTY_ASSIGNMENT)
      return <><AssignmentFields value={v} onChange={setV} org={NO_ORG} errors={{}} legalEntityId="" /><output>{v.payBasis}</output></>
    }
    render(<Harness />)
    fireEvent.change(screen.getByLabelText('Worker category'), { target: { value: 'casual' } })
    expect(screen.getByRole('status').textContent).toBe('daily')
    fireEvent.change(screen.getByLabelText('Worker category'), { target: { value: 'fixed_term' } })
    expect(screen.getByLabelText('Contract end date')).toBeRequired()
  })

  it('sends blanks as null so the server can tell "not given" from empty', () => {
    const a = assignmentInput({ ...EMPTY_ASSIGNMENT, jobTitle: '  ' })
    expect(a.jobTitle).toBeNull()
    expect(paymentInput({ paymentMethod: 'mpesa', bankCode: '', bankBranchCode: '', accountNo: '', accountName: '', mpesaPhone: '0712345678' }).bankCode).toBeNull()
  })
})

describe('RecoveryCodes', () => {
  it('cannot continue until the user confirms the codes are saved', () => {
    const done = vi.fn()
    render(<RecoveryCodes codes={['AAAAA-BBBBB', 'CCCCC-DDDDD']} onDone={done} />)
    expect(screen.getByText('AAAAA-BBBBB')).toBeInTheDocument()
    const button = screen.getByRole('button', { name: 'Continue' })
    expect(button).toBeDisabled()
    fireEvent.click(screen.getByLabelText(/I have saved these codes/))
    fireEvent.click(button)
    expect(done).toHaveBeenCalled()
  })
})
