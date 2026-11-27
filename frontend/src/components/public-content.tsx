import { Check } from 'lucide-react'
import { Badge, Card } from './ui'
import { faqs, features, integrations, steps } from '@/lib/marketing-content'
import type { ReactNode } from 'react'

export function SectionIntro({ label, title, text }: { label: string; title: string; text: string }) {
  return <div className="section-intro"><span className="eyebrow">{label}</span><h2>{title}</h2><p>{text}</p></div>
}

export function PageIntro({ label, title, children }: { label: string; title: string; children: ReactNode }) {
  return <header className="page-intro"><div className="container">
    <span className="eyebrow">{label}</span><h1>{title}</h1><p>{children}</p>
  </div></header>
}

export function FeatureCards() {
  return <div className="feature-grid">{features.map(({ title, text, points, icon: Icon }) => <Card className="feature-card" key={title}>
    <span className="icon-tile"><Icon size={23} /></span><h3>{title}</h3><p>{text}</p>
    <ul>{points.map(point => <li key={point}><Check size={15} />{point}</li>)}</ul>
  </Card>)}</div>
}

export function IntegrationCards({ kind }: { kind?: 'accounting' | 'commerce' }) {
  return <div className="integration-grid">{integrations.filter(item => !kind || kind === item.kind).map(({ id, name, mark, group, tint, status }) =>
    <Card className="integration-card" key={id}>
      <span className={'integration-mark mark-' + tint} aria-hidden>{mark}</span><div><h3>{name}</h3><p>{group}</p></div>
      <Badge tone={status === 'Available' ? 'success' : status === 'Setup Required' ? 'warning' : 'neutral'}>{status}</Badge>
    </Card>)}</div>
}

export function WorkflowSteps() {
  return <ol className="step-grid">{steps.map(({ title, text, icon: Icon }, index) => <li key={title}>
    <div className="step-top"><span className="icon-tile"><Icon size={22} /></span><span>0{index + 1}</span></div><h3>{title}</h3><p>{text}</p>
  </li>)}</ol>
}

export function FrequentlyAskedQuestions() {
  return <div className="faqs">{faqs.map(([question, answer]) => <details key={question}>
    <summary>{question}</summary><p>{answer}</p>
  </details>)}</div>
}
