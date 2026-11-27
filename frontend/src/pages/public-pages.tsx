import { useState, type FormEvent } from 'react'
import { ArrowRight, BookOpen, CircleHelp, Clipboard, FileText, Link2 } from 'lucide-react'
import { Link } from 'react-router-dom'
import { FeatureCards, FrequentlyAskedQuestions, IntegrationCards, PageIntro, SectionIntro, WorkflowSteps } from '@/components/public-content'
import { Button, Card, Field, Select } from '@/components/ui'

export function Features() {
  return <main id="main-content">
    <PageIntro label="PHILIPPINE ELECTRONIC INVOICING" title="e-Invoicing Features">
      Issue, deliver, and keep secure electronic invoices while working alongside your existing accounting software.
    </PageIntro>
    <section className="section"><div className="container">
      <SectionIntro label="PRODUCT SCOPE" title="From issuance to authorized reporting" text="Explore the planned capabilities. The current workspace uses sample invoices, and live BIR transmission requires formal authorization." />
      <FeatureCards />
      <Link className="text-link section-link" to="/how-it-works">See the invoicing workflow <ArrowRight size={16} /></Link>
    </div></section>
  </main>
}

export function Integrations() {
  return <main id="main-content">
    <PageIntro label="CONNECT YOUR EXISTING SOFTWARE" title="Integration catalog">
      EMBM eFiscal complements the accounting software you use today. Each connector shows its current development status.
    </PageIntro>
    <section className="section"><div className="container">
      <SectionIntro label="ACCOUNTING SOFTWARE" title="External accounting systems" text="Connect electronic invoice data to the accounting records your business already maintains." />
      <IntegrationCards kind="accounting" />
    </div></section>
    <section className="section section-tint"><div className="container">
      <SectionIntro label="E-COMMERCE" title="Eligible source orders" text="These planned connectors will support electronic invoice issuance from eligible source orders. A source order by itself does not issue an invoice." />
      <IntegrationCards kind="commerce" />
      <Link className="text-link section-link" to="/pricing">Compare plan eligibility <ArrowRight size={16} /></Link>
    </div></section>
  </main>
}

export function HowItWorks() {
  return <main id="main-content">
    <PageIntro label="INVOICE WORKFLOW" title="How it works">
      Follow the planned path from subscription and company setup to issuing and reconciling electronic invoices.
    </PageIntro>
    <section className="section"><div className="container">
      <SectionIntro label="SUBSCRIPTION PREVIEW" title="Start with Save & Send Credits" text="Compare monthly and annual allowances, then try the registration preview. Company and taxpayer details belong to company onboarding after verified provisioning." />
      <Link className="text-link section-link" to="/pricing">Compare subscriptions <ArrowRight size={16} /></Link>
    </div></section>
    <section className="section section-tint"><div className="container">
      <SectionIntro label="COMPANY ONBOARDING" title="Set up and issue electronic invoices" text="Company onboarding, connector setup, and live invoice issuance will follow the verified account workflow." />
      <WorkflowSteps />
      <Link className="text-link section-link" to="/preview/client">View the sample invoice workspace <ArrowRight size={16} /></Link>
    </div></section>
  </main>
}

export function Faq() {
  return <main id="main-content">
    <PageIntro label="FREQUENTLY ASKED QUESTIONS" title="Questions about EMBM eFiscal">
      Find answers about invoice records, Save &amp; Send Credits, integrations, and BIR reporting readiness.
    </PageIntro>
    <section className="section"><div className="container reading-width">
      <FrequentlyAskedQuestions />
      <Link className="text-link section-link" to="/contact">Go to Contact &amp; Support <ArrowRight size={16} /></Link>
    </div></section>
  </main>
}

const resources = [
  { icon: FileText, title: 'Electronic invoice workflow', text: 'See where company setup, issuance, delivery, and reconciliation fit.', path: '/how-it-works', action: 'View workflow' },
  { icon: BookOpen, title: 'Save & Send Credits guide', text: 'Estimate Save & Send Credits and review planned Additional Credits.', path: '/pricing#estimator', action: 'Estimate credits' },
  { icon: Link2, title: 'Connector availability', text: 'Find the status of accounting and e-commerce connectors.', path: '/integrations', action: 'View integrations' },
  { icon: CircleHelp, title: 'Common questions', text: 'Read about Sales Orders, immutable invoices, pricing, and regulator access.', path: '/faq', action: 'Read the FAQ' },
]

export function Resources() {
  return <main id="main-content">
    <PageIntro label="RESOURCES" title="e-Invoicing resources">
      Use these guides to evaluate EMBM eFiscal and try the development preview.
    </PageIntro>
    <section className="section"><div className="container">
      <div className="resource-grid">{resources.map(({ icon: Icon, title, text, path, action }) => <Card className="resource-card" key={path}>
        <span className="icon-tile"><Icon size={20} /></span><h2>{title}</h2><p>{text}</p>
        <Link className="text-link" to={path}>{action} <ArrowRight size={15} /></Link>
      </Card>)}</div>
      <Link className="text-link section-link" to="/contact">Contact &amp; Support <ArrowRight size={16} /></Link>
    </div></section>
  </main>
}

export function Contact() {
  const [request, setRequest] = useState('')
  const [copyStatus, setCopyStatus] = useState('')
  function prepareRequest(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const fields = new FormData(event.currentTarget)
    const details = String(fields.get('details') ?? '').trim()
    setRequest(details ? 'EMBM eFiscal support — ' + fields.get('topic') + '\n\n' + details : '')
    setCopyStatus(details ? 'Support request prepared.' : 'Enter a short description.')
  }

  async function copyRequest() {
    try {
      await navigator.clipboard.writeText(request)
      setCopyStatus('Support request copied.')
    } catch {
      setCopyStatus('Select and copy the prepared request from the text box.')
    }
  }

  return <main id="main-content">
    <PageIntro label="CONTACT & SUPPORT" title="How can we help?">
      Find answers about subscriptions, connectors, and the Philippine e-Invoicing preview.
    </PageIntro>
    <section className="section"><div className="container pricing-grid support-layout">
      <div>
        <SectionIntro label="SELF-SERVICE HELP" title="Find an answer" text="Check common questions and current availability before preparing an inquiry." />
        <div className="support-links">
          <Link className="text-link" to="/faq">Frequently asked questions <ArrowRight size={15} /></Link>
          <Link className="text-link" to="/resources">e-Invoicing resources <ArrowRight size={15} /></Link>
          <Link className="text-link" to="/integrations">Integration status <ArrowRight size={15} /></Link>
        </div>
      </div>
      <Card className="registration-card">
        <h2>Prepare a support request</h2>
        <p>Build a request to copy into your existing support conversation. This preview does not send messages. Omit credentials, tax IDs, and invoice data.</p>
        <form className="support-form" onSubmit={prepareRequest}>
          <Field id="support-topic" label="Topic"><Select id="support-topic" name="topic">
            <option>Subscription and Save &amp; Send Credits</option><option>Connectors</option><option>Electronic invoicing</option><option>Preview issue</option>
          </Select></Field>
          <Field id="support-details" label="Question or issue"><textarea className="control" id="support-details" name="details" rows={4} maxLength={1000} required /></Field>
          <Button type="submit">Prepare request</Button>
        </form>
        <p role="status">{copyStatus}</p>
        {request && <div className="support-form">
          <Field id="prepared-request" label="Prepared request"><textarea id="prepared-request" className="control" readOnly value={request} rows={4} /></Field>
          <Button variant="light" type="button" onClick={() => void copyRequest()}><Clipboard size={16} /> Copy request</Button>
        </div>}
      </Card>
    </div></section>
  </main>
}

