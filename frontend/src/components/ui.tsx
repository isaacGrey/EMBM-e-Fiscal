import type { ComponentProps, ReactNode } from 'react'
import { Slot, Dialog as DialogPrimitive } from 'radix-ui'
import { cva, type VariantProps } from 'class-variance-authority'
import { AlertCircle, Inbox, LoaderCircle, X } from 'lucide-react'
import { cn } from '@/lib/utils'

// Adapted from the shadcn/ui New York button registry component.
const buttonVariants = cva('button', {
  variants: { variant: { primary: 'button-primary', secondary: 'button-secondary', ghost: 'button-ghost', light: 'button-light' } },
  defaultVariants: { variant: 'primary' },
})

export function Button({ asChild = false, className, variant = 'primary', ...props }: ComponentProps<'button'> &
  VariantProps<typeof buttonVariants> & { asChild?: boolean }) {
  const Component = asChild ? Slot.Root : 'button'
  return <Component data-slot="button" data-variant={variant} className={cn(buttonVariants({ variant }), className)} {...props} />
}

export function Input({ className, ...props }: ComponentProps<'input'>) {
  return <input className={cn('control', className)} {...props} />
}

export function Select({ className, ...props }: ComponentProps<'select'>) {
  return <select className={cn('control', className)} {...props} />
}

export function Field({ id, label, children, error }: { id: string; label: string; children: ReactNode; error?: string }) {
  return <div className="field"><label htmlFor={id}>{label}</label>{children}
    {error && <p id={id + '-error'} className="field-error" role="alert">{error}</p>}
  </div>
}

export function Card({ className, ...props }: ComponentProps<'div'>) {
  return <div className={cn('card', className)} {...props} />
}

export function Badge({ className, tone = 'neutral', ...props }: ComponentProps<'span'> & { tone?: 'neutral' | 'success' | 'warning' }) {
  return <span className={cn('badge', 'badge-' + tone, className)} {...props} />
}

export function Table(props: ComponentProps<'table'>) {
  return <div className="table-scroll"><table {...props} /></div>
}

export const Dialog = DialogPrimitive.Root
export const DialogTrigger = DialogPrimitive.Trigger

export function DialogContent({ title, description, children }: { title: string; description: string; children: ReactNode }) {
  return <DialogPrimitive.Portal>
    <DialogPrimitive.Overlay className="dialog-overlay" />
    <DialogPrimitive.Content className="dialog-content">
      <DialogPrimitive.Title>{title}</DialogPrimitive.Title>
      <DialogPrimitive.Description>{description}</DialogPrimitive.Description>
      {children}
      <DialogPrimitive.Close className="icon-button close-dialog" aria-label="Close"><X size={20} /></DialogPrimitive.Close>
    </DialogPrimitive.Content>
  </DialogPrimitive.Portal>
}

export function LoadingState({ label = 'Loading' }: { label?: string }) {
  return <p className="inline-state" role="status"><LoaderCircle className="spinner" size={16} />{label}</p>
}

export function EmptyState({ title, children }: { title: string; children: ReactNode }) {
  return <div className="empty-state"><Inbox size={25} aria-hidden /><h2>{title}</h2><p>{children}</p></div>
}

export function ErrorState({ children }: { children: ReactNode }) {
  return <p className="inline-state field-error" role="alert"><AlertCircle size={16} />{children}</p>
}

const pesos = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP' })
export function Money({ amount }: { amount: number }) {
  return <span className="money">{pesos.format(amount)}</span>
}
