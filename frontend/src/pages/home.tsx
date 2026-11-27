import { lazy, Suspense } from 'react'
import { ArrowDown, ArrowRight, Building2, CircleCheck, FileText, Fingerprint, GitBranch, Layers3, LockKeyhole, ShieldCheck } from 'lucide-react'
import { Link } from 'react-router-dom'
import { WorkspacePreview } from '@/components/workspace-preview'
import { Button, Card, LoadingState } from '@/components/ui'
import { FeatureCards, FrequentlyAskedQuestions, IntegrationCards, SectionIntro, WorkflowSteps } from '@/components/public-content'

const CreditEstimator = lazy(() => import('@/components/credit-estimator').then(module => ({ default: module.CreditEstimator })))
export function Home() {
  return <main id="main-content">
    <section className="hero" aria-labelledby="hero-title">
      <div className="container hero-grid">
        <div className="hero-copy">
          <span className="hero-kicker"><i /> Invoice. Integrate. Comply.</span>
          <h1 id="hero-title">Electronic Invoicing, <em>Built for the Philippines.</em></h1>
          <p>Simplify electronic invoicing, connect your existing accounting software, and prepare your business for Philippine BIR e-Invoicing requirements.</p>
          <div className="hero-actions"><Button asChild><Link to="/get-started">Get Started <ArrowRight size={18} /></Link></Button>
            <Button variant="secondary" asChild><a href="#product">Explore e-Invoicing <ArrowDown size={15} /></a></Button></div>
          <div className="hero-note"><Building2 size={16} /> A product of EMBM Services OPC.</div>
        </div>
        <WorkspacePreview />
      </div>
    </section>
    <div className="benefit-bar"><div className="container benefit-inner">
      <span><FileText size={18} /> Issue electronic invoices</span>
      <span><Layers3 size={18} /> Connect accounting software</span>
      <span><GitBranch size={18} /> Reconcile invoice records</span>
    </div></div>
    <section className="section" id="product"><div className="container">
      <SectionIntro label="E-INVOICING CAPABILITIES" title="Issue and manage Philippine electronic invoices." text="These planned capabilities cover invoice issuance, electronic delivery, and the supporting workflows that follow." />
      <FeatureCards />
      <Link className="text-link section-link" to="/features">Explore all e-Invoicing features <ArrowRight size={16} /></Link>
    </div></section>

    <section className="section section-tint" id="integrations"><div className="container">
      <SectionIntro label="INTEGRATIONS" title="Keep the accounting software you already use." text="Planned connectors bring electronic invoices into your existing accounting workflow. Shopee and TikTok Shop will support invoice issuance from eligible source orders." />
      <IntegrationCards />
      <Link className="text-link section-link" to="/integrations">View the integration catalog <ArrowRight size={16} /></Link>
    </div></section>

    <section className="section" id="how-it-works"><div className="container">
      <SectionIntro label="HOW IT WORKS" title="From invoice setup to reconciliation." text="The planned workflow takes you from company and tax setup to issuing invoices and checking them against connected accounting records." />
      <WorkflowSteps />
      <Link className="text-link section-link" to="/how-it-works">See how it works <ArrowRight size={16} /></Link>
    </div></section>

    <section className="section section-tint" id="pricing"><div className="container pricing-grid">
      <div>
        <SectionIntro label="SUBSCRIPTIONS & SAVE & SEND CREDITS" title="Budget for your invoicing volume." text="Compare illustrative subscriptions with Included Credits for Save & Send. Try subscription checkout and credit top-ups in development." />
        <ul className="pricing-points"><li><CircleCheck size={18} /> A successful Save &amp; Send uses credits for Sales Invoices and applicable Credit Memos.</li>
          <li><CircleCheck size={18} /> Sales Order saving and accounting synchronization use zero credits.</li>
          <li><CircleCheck size={18} /> Estimate your monthly volume before choosing a plan.</li></ul>
        <Link className="text-link section-link" to="/pricing">Compare subscription plans <ArrowRight size={16} /></Link>
      </div>
      <Suspense fallback={<Card className="estimator-placeholder"><LoadingState label="Loading Save & Send credit estimator" /></Card>}><CreditEstimator /></Suspense>
    </div></section>

    <section className="section" id="compliance"><div className="container compliance">
      <div><SectionIntro label="BIR E-INVOICING READINESS" title="Prepare invoice records for authorized reporting." text="Keep invoice and company details available for officially supported reporting methods when authorized." />
        <p className="compliance-note"><LockKeyhole size={18} /> Live BIR transmission is disabled pending formal authorization.</p></div>
      <div className="compliance-points">
        <div><span className="icon-tile"><Fingerprint size={22} /></span><div><h3>Permanent invoice records</h3><p>Save &amp; Send commits the electronic invoice. Authorized corrections create a separate linked record or lifecycle event.</p></div></div>
        <div><span className="icon-tile"><ShieldCheck size={22} /></span><div><h3>Scoped read-only access</h3><p>Authorized BIR users may only view invoice records within their formal grants.</p></div></div>
      </div>
    </div></section>

    <section className="section section-tint" id="resources"><div className="container faq-grid">
      <SectionIntro label="RESOURCES" title="Frequently asked questions" text="Answers about invoice issuance, Save & Send Credits, integrations, and BIR reporting." />
      <div><FrequentlyAskedQuestions />
        <Link className="text-link section-link" to="/faq">View all questions <ArrowRight size={16} /></Link></div>
    </div></section>

    <section className="closing"><div className="container closing-inner">
      <div><h2>Explore Philippine electronic invoicing.</h2><p>See a sample invoice workspace and estimate your Save &amp; Send Credits.</p></div>
      <Button asChild><Link to="/get-started">Get Started <ArrowRight size={17} /></Link></Button>
    </div></section>
  </main>
}
