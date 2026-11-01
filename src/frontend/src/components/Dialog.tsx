import { useEffect, useRef, type FormEvent, type ReactNode } from 'react'

/** Accessible modal built on the native <dialog> element (focus trap and Esc handled by the browser). */
export function Dialog({ open, title, onClose, onSubmit, submitLabel = 'Save', busy, children, danger }: {
  open: boolean
  title: string
  onClose: () => void
  onSubmit: () => unknown
  submitLabel?: string
  busy?: boolean
  danger?: boolean
  children: ReactNode
}) {
  const ref = useRef<HTMLDialogElement>(null)
  useEffect(() => {
    const d = ref.current
    if (!d) return
    if (open && !d.open) d.showModal()
    if (!open && d.open) d.close()
  }, [open])

  function submit(e: FormEvent) {
    e.preventDefault()
    void onSubmit()
  }

  return (
    <dialog ref={ref} onClose={onClose} aria-labelledby="dialog-title">
      <form onSubmit={submit}>
        <div className="dialog-body stack">
          <h2 id="dialog-title">{title}</h2>
          {children}
        </div>
        <div className="dialog-actions">
          <button type="button" className="btn-secondary" onClick={onClose}>Cancel</button>
          <button className={danger ? 'btn-danger' : 'btn-primary'} disabled={busy}>{busy ? 'Saving…' : submitLabel}</button>
        </div>
      </form>
    </dialog>
  )
}
