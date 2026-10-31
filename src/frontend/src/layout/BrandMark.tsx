export function BrandMark({ subtitle }: { subtitle?: string }) {
  return (
    <div className="row" style={{ gap: 12 }}>
      <img src="/favicon.svg" alt="" width={40} height={40} />
      <div>
        <div style={{ fontWeight: 700, fontSize: '1.3rem' }}>YeaKazi</div>
        <div className="muted" style={{ fontSize: '0.85rem' }}>{subtitle ?? 'by Yeasoft'}</div>
      </div>
    </div>
  )
}
