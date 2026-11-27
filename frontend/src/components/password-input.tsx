import { useState, type ComponentProps } from 'react'
import { Eye, EyeOff } from 'lucide-react'
import { Input } from './ui'

export function PasswordInput({ nameForToggle, ...props }: Omit<ComponentProps<typeof Input>, 'type'> & { nameForToggle: string }) {
  const [visible, setVisible] = useState(false)
  return <div className="password-control">
    <Input {...props} type={visible ? 'text' : 'password'} />
    <button type="button" className="icon-button" aria-label={(visible ? 'Hide ' : 'Show ') + nameForToggle}
      aria-controls={props.id} onClick={() => setVisible(!visible)}>
      {visible ? <EyeOff size={17} /> : <Eye size={17} />}
    </button>
  </div>
}
