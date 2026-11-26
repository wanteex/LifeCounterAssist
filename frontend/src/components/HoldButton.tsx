import { useEffect, useRef, type ButtonHTMLAttributes } from 'react'

const HOLD_DELAY_MS = 450
const REPEAT_MS = 350

/**
 * Button that fires `onStep(small)` on tap and `onStep(big)` repeatedly while held.
 */
export function HoldButton({
  small,
  big,
  onStep,
  className = '',
  children,
  ...rest
}: Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'onClick'> & { small: number; big: number; onStep: (delta: number) => void }) {
  const holdTimer = useRef<number | undefined>(undefined)
  const repeatTimer = useRef<number | undefined>(undefined)
  const held = useRef(false)
  const stepRef = useRef(onStep)
  stepRef.current = onStep

  const clear = () => {
    window.clearTimeout(holdTimer.current)
    window.clearInterval(repeatTimer.current)
  }
  useEffect(() => clear, [])

  return (
    <button
      type="button"
      {...rest}
      className={`touch-none select-none ${className}`}
      onContextMenu={(e) => e.preventDefault()}
      onPointerDown={(e) => {
        if (e.button !== 0) return
        held.current = false
        clear()
        holdTimer.current = window.setTimeout(() => {
          held.current = true
          stepRef.current(big)
          navigator.vibrate?.(15)
          repeatTimer.current = window.setInterval(() => stepRef.current(big), REPEAT_MS)
        }, HOLD_DELAY_MS)
      }}
      onPointerUp={() => {
        clear()
        if (!held.current) stepRef.current(small)
      }}
      onPointerLeave={clear}
      onPointerCancel={clear}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault()
          stepRef.current(e.shiftKey ? big : small)
        }
      }}
    >
      {children}
    </button>
  )
}
