import { StrictMode, useEffect, lazy, Suspense } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Link, Route, Routes, useLocation } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import '@fontsource-variable/inter'
import '@fontsource-variable/plus-jakarta-sans'
import './styles.css'
import './public-pages.css'
import { Home } from '@/pages/home'
import { PublicLayout } from '@/components/public-layout'
import { LoadingState } from '@/components/ui'

const GetStarted = lazy(() => import('@/pages/registration').then(module => ({ default: module.GetStarted })))
const Pricing = lazy(() => import('@/pages/pricing').then(module => ({ default: module.Pricing })))
const Features = lazy(() => import('@/pages/public-pages').then(module => ({ default: module.Features })))
const Integrations = lazy(() => import('@/pages/public-pages').then(module => ({ default: module.Integrations })))
const HowItWorks = lazy(() => import('@/pages/public-pages').then(module => ({ default: module.HowItWorks })))
const Faq = lazy(() => import('@/pages/public-pages').then(module => ({ default: module.Faq })))
const Resources = lazy(() => import('@/pages/public-pages').then(module => ({ default: module.Resources })))
const Contact = lazy(() => import('@/pages/public-pages').then(module => ({ default: module.Contact })))
const SignIn = lazy(() => import('@/pages/identity').then(module => ({ default: module.IdentitySignIn })))
const RecoverAccount = lazy(() => import('@/pages/identity').then(module => ({ default: module.RecoverAccount })))
const VerifyEmail = lazy(() => import('@/pages/identity').then(module => ({ default: module.VerifyEmail })))
const ResetPassword = lazy(() => import('@/pages/identity').then(module => ({ default: module.ResetPassword })))
const Invitation = lazy(() => import('@/pages/identity').then(module => ({ default: module.Invitation })))
const VendorWorkspace = lazy(() => import('@/pages/identity').then(module => ({ default: module.VendorWorkspace })))
const ClientWorkspace = lazy(() => import('@/pages/workspace').then(module => ({ default: module.ClientWorkspace })))
const BirSignIn = lazy(() => import('@/pages/portals').then(module => ({ default: module.BirSignIn })))
const PortalLayout = lazy(() => import('@/pages/portals').then(module => ({ default: module.PortalLayout })))
const queryClient = new QueryClient()

function ScrollToRoute() {
  const { pathname, hash } = useLocation()
  useEffect(() => {
    if (hash) {
      document.getElementById(hash.slice(1))?.scrollIntoView()
    } else {
      window.scrollTo(0, 0)
    }
  }, [pathname, hash])
  return null
}

function App() {
  return <><a className="skip-link" href="#main-content" onClick={event => {
    const main = document.getElementById('main-content')
    if (!main) return
    event.preventDefault()
    main.tabIndex = -1
    main.focus()
  }}>Skip to content</a>
    <Suspense fallback={<div className="container section"><LoadingState /></div>}>
      <ScrollToRoute />
      <Routes>
        <Route element={<PublicLayout />}>
          <Route path="/" element={<Home />} />
          <Route path="/features" element={<Features />} />
          <Route path="/integrations" element={<Integrations />} />
          <Route path="/how-it-works" element={<HowItWorks />} />
          <Route path="/pricing" element={<Pricing />} />
          <Route path="/faq" element={<Faq />} />
          <Route path="/resources" element={<Resources />} />
          <Route path="/contact" element={<Contact />} />
          <Route path="/get-started" element={<GetStarted />} />
        </Route>
        <Route path="/sign-in" element={<SignIn kind="client" />} />
        <Route path="/vendor/sign-in" element={<SignIn kind="vendor" />} />
        <Route path="/bir/sign-in" element={<BirSignIn />} />
        <Route path="/forgot-password" element={<RecoverAccount />} />
        <Route path="/resend-verification" element={<RecoverAccount resend />} />
        <Route path="/verify-email" element={<VerifyEmail />} />
        <Route path="/reset-password" element={<ResetPassword />} />
        <Route path="/invitation" element={<Invitation />} />
        <Route path="/portal/client/*" element={<ClientWorkspace />} />
        <Route path="/preview/client" element={<PortalLayout role="client" />} />
        <Route path="/portal/vendor" element={<VendorWorkspace />} />
        <Route path="/portal/vendor/accreditation" element={<VendorWorkspace />} />
        <Route path="/portal/vendor/regulatory-schemas" element={<VendorWorkspace />} />
        <Route path="/portal/vendor/connectors" element={<VendorWorkspace />} />
        <Route path="/portal/vendor/connectors/:connectorId" element={<VendorWorkspace />} />
        <Route path="/portal/bir" element={<PortalLayout role="bir" />} />
        <Route path="*" element={<main className="container section" id="main-content"><h1>Page not found</h1><Link className="text-link" to="/">Back to EMBM eFiscal</Link></main>} />
      </Routes>
    </Suspense>
  </>
}

createRoot(document.getElementById('root')!).render(<StrictMode><QueryClientProvider client={queryClient}><BrowserRouter><App /></BrowserRouter></QueryClientProvider></StrictMode>)
