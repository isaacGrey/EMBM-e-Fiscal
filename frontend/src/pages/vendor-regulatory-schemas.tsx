import { useQuery } from '@tanstack/react-query'
import { Badge, Card, ErrorState, LoadingState } from '@/components/ui'
import { api, errorMessage } from '@/lib/account-api'

type Schema = {
  name: string; version: string; classification: string; environment: string; documentTypes: string[]
  content: string; sha256: string; source: string; verification: string; effectiveFrom: string; effectiveUntil?: string
}
type Registry = { versions: Schema[]; reserved: string[] }

const names: Record<string, string> = {
  InternalCanonical: 'Canonical schemas', DevelopmentSimulation: 'Development schemas',
  FutureOfficialCertification: 'Official certification', FutureOfficialProduction: 'Official production',
}

export function VendorRegulatorySchemas() {
  const query = useQuery({ queryKey: ['vendor-regulatory-schemas'], queryFn: () => api<Registry>('/vendor/regulatory-schemas') })
  if (query.isPending) return <LoadingState label="Loading regulatory schemas…" />
  if (query.isError) return <ErrorState>{errorMessage(query.error)}</ErrorState>
  return <div className="workspace-stack">
    <Card className="workspace-card"><h2>Regulatory Schema Registry</h2>
      <p>Internal canonical and development simulation schemas have separate version histories.</p>
    </Card>
    {Object.entries(names).map(([classification, heading]) => <Card key={classification} className="workspace-card">
      <h3>{heading}</h3>
      {query.data.reserved.includes(classification) && <p>Disabled. Verified official specifications are pending.</p>}
      {query.data.versions.filter(schema => schema.classification === classification).map(schema =>
        <section key={schema.version} className="reporting-evidence">
          <h4>{schema.name}</h4><Badge>{schema.verification === 'InternalOnly' ? 'Internal only' : 'Unverified'}</Badge>
          <dl className="workspace-definitions">
            <div><dt>Version</dt><dd>{schema.version}</dd></div>
            <div><dt>Environment</dt><dd>{schema.environment}</dd></div>
            <div><dt>Scope</dt><dd>{schema.documentTypes.map(type => type === 'SalesInvoice' ? 'Sales Invoice' : 'Credit Memo').join(', ')}</dd></div>
            <div><dt>Source</dt><dd>{schema.source}</dd></div>
            <div><dt>Effective dates</dt><dd>{schema.effectiveFrom}{schema.effectiveUntil && ' through ' + schema.effectiveUntil}</dd></div>
            <div><dt>Schema SHA-256</dt><dd>{schema.sha256}</dd></div>
          </dl>
          <details><summary>View version content</summary><pre>{JSON.stringify(JSON.parse(schema.content), null, 2)}</pre></details>
        </section>)}
    </Card>)}
  </div>
}
