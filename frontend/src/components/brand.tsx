import { Link } from 'react-router-dom'

export function Brand({ home = true }: { home?: boolean }) {
  const identity = <><img src="/mark.svg" alt="" width="36" height="36" /><span>EMBM <strong>eFiscal</strong></span></>
  return home ? <Link to="/" className="brand" aria-label="EMBM eFiscal home">{identity}</Link> : <span className="brand">{identity}</span>
}
