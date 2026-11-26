import { useEffect, useRef, type ReactNode } from 'react'

/** Bottom sheet dialog, mobile friendly. */
export function Sheet({ open, onClose, title, children }: { open: boolean; onClose: () => void; title: ReactNode; children: ReactNode }) {
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) dialog.showModal()
    if (!open && dialog.open) dialog.close()
  }, [open])

  return (
    <dialog
      ref={ref}
      onClose={onClose}
      onClick={(e) => e.target === ref.current && onClose()}
      className="m-0 mt-auto max-h-[90dvh] w-full max-w-none rounded-t-3xl bg-slate-900 p-0 text-slate-100 backdrop:bg-black/70 sm:mx-auto sm:mb-auto sm:max-w-lg sm:rounded-3xl"
    >
      <div className="sticky top-0 flex items-center justify-between bg-slate-900 px-5 pt-4 pb-2">
        <h2 className="text-lg font-bold">{title}</h2>
        <button type="button" onClick={onClose} aria-label="close" className="rounded-full px-3 py-1 text-2xl text-slate-400">
          ×
        </button>
      </div>
      <div className="px-5 pb-6">{children}</div>
    </dialog>
  )
}
