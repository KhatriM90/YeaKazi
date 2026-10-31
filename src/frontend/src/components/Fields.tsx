import { useId, type InputHTMLAttributes, type ReactNode } from 'react'

interface BaseProps {
  label: string
  hint?: string
  error?: string
}

type TextFieldProps = BaseProps &
  Omit<InputHTMLAttributes<HTMLInputElement>, 'onChange' | 'value'> & {
    value: string | number
    onChange: (value: string) => void
  }

export function TextField({ label, hint, error, value, onChange, ...rest }: TextFieldProps) {
  const id = useId()
  return (
    <label className="field" htmlFor={id}>
      {label}
      <input
        id={id}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        aria-invalid={error ? true : undefined}
        aria-describedby={hint || error ? `${id}-help` : undefined}
        {...rest}
      />
      {(hint || error) && (
        <span id={`${id}-help`} className={error ? 'field-error' : 'hint'}>{error ?? hint}</span>
      )}
    </label>
  )
}

export interface Option {
  value: string
  label: string
}

export function SelectField({ label, hint, error, value, onChange, options, placeholder, required }: BaseProps & {
  value: string
  onChange: (value: string) => void
  options: Option[]
  placeholder?: string
  required?: boolean
}) {
  const id = useId()
  return (
    <label className="field" htmlFor={id}>
      {label}
      <select id={id} value={value} onChange={(e) => onChange(e.target.value)} required={required}
        aria-invalid={error ? true : undefined} aria-describedby={hint || error ? `${id}-help` : undefined}>
        {placeholder !== undefined && <option value="">{placeholder}</option>}
        {options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
      </select>
      {(hint || error) && <span id={`${id}-help`} className={error ? 'field-error' : 'hint'}>{error ?? hint}</span>}
    </label>
  )
}

export function Checkbox({ label, checked, onChange }: { label: ReactNode; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <label className="checkbox">
      <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      {label}
    </label>
  )
}
