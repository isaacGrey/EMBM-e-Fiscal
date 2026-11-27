import { useState } from 'react'
import { ArrowRight, Menu, X } from 'lucide-react'
import { Link, NavLink, Outlet } from 'react-router-dom'
import { Brand } from './brand'
import { Button } from './ui'

const navigation = [
  { label: 'Features', to: '/features' },
  { label: 'Integrations', to: '/integrations' },
  { label: 'How It Works', to: '/how-it-works' },
  { label: 'Pricing', to: '/pricing' },
  { label: 'Resources', to: '/resources' },
]

function PublicHeader() {
  const [open, setOpen] = useState(false)
  return <header className="public-header">
    <div className="container header-inner">
      <Brand />
      <nav className="desktop-nav" aria-label="Primary">{navigation.map(({ label, to }) =>
        <NavLink key={to} to={to}>{label}</NavLink>)}</nav>
      <div className="header-actions">
        <Button variant="ghost" asChild><Link to="/sign-in">Sign In</Link></Button>
        <Button asChild><Link to="/get-started">Get Started <ArrowRight size={15} /></Link></Button>
      </div>
      <button type="button" className="icon-button menu-toggle" aria-expanded={open} aria-controls="mobile-nav"
        aria-label={open ? 'Close navigation' : 'Open navigation'} onClick={() => setOpen(!open)}>
        {open ? <X size={23} /> : <Menu size={23} />}
      </button>
    </div>
    <nav className="mobile-nav" id="mobile-nav" hidden={!open} aria-label="Mobile navigation">
      {navigation.map(({ label, to }) => <NavLink key={to} to={to} onClick={() => setOpen(false)}>{label}</NavLink>)}
      <Link to="/faq" onClick={() => setOpen(false)}>FAQ</Link>
      <Link to="/contact" onClick={() => setOpen(false)}>Contact &amp; Support</Link>
      <Link to="/sign-in" onClick={() => setOpen(false)}>Sign In</Link>
      <Link to="/get-started" onClick={() => setOpen(false)}>Get Started <ArrowRight size={15} /></Link>
    </nav>
  </header>
}

export function PublicLayout() {
  return <div className="dark">
    <PublicHeader />
    <Outlet />
    <footer className="public-footer"><div className="container">
      <div className="footer-top">
        <div><Brand /><p>Invoice. Integrate. Comply.</p><p>A product of EMBM Services OPC.</p></div>
        <nav aria-label="Footer">
          {navigation.map(({ label, to }) => <Link key={to} to={to}>{label}</Link>)}
          <Link to="/faq">FAQ</Link><Link to="/contact">Contact &amp; Support</Link><Link to="/sign-in">Sign In</Link>
        </nav>
      </div>
      <div className="footer-bottom"><span>© {new Date().getFullYear()} EMBM Services OPC</span><span>Philippines</span></div>
    </div></footer>
  </div>
}
