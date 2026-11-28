import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button, Card, ErrorState, LoadingState } from '@/components/ui'
import { api, errorMessage, manilaTimestamp } from '@/lib/account-api'
import { ScrollTable } from './connect'

type Register = { provider: { registeredName: string }; subscriber: { billingName: string }
  total: number; items: { id: string; purpose: string; amount: number; currency: string; refundedAmount: number
    confirmedAt: string; providerTransactionReference?: string; paymentStatus: string; preparationStatus: string }[] }

export function ProviderBilling({ subscriber }: { subscriber: string }) {
  const [page, setPage] = useState(1)
  const base = '/billing/subscribers/' + subscriber + '/provider-records'
  const query = useQuery({ queryKey: [base, page], queryFn: () => api<Register>(base + '?page=' + page) })
  if (query.isPending) return <LoadingState label="Loading provider billing records…" />
  if (query.isError) return <ErrorState>{errorMessage(query.error)}</ErrorState>
  const money = (amount: number, currency: string) => new Intl.NumberFormat('en-PH', { style: 'currency', currency }).format(amount)
  const { provider, subscriber: billed, items, total } = query.data
  return <Card className="workspace-card connect-stack">
    <h2>{provider.registeredName} billing records</h2>
    <p>Billing identity · {billed.billingName}</p>
    <p>These records show confirmed provider charges and refunds. Tax treatment and provider issuance require verified authorization.</p>
    {items.length === 0 ? <p>No confirmed payments in this register.</p> : <ScrollTable name="Provider billing records">
      <thead><tr><th>Charge</th><th>Paid</th><th>Refunded</th><th>Confirmed</th><th>Payment reference</th><th>Provider preparation</th></tr></thead>
      <tbody>{items.map(row => <tr key={row.id}>
        <td>{row.purpose.replace(/([a-z])([A-Z])/g, '$1 $2')}</td><td>{money(row.amount, row.currency)}</td>
        <td>{money(row.refundedAmount, row.currency)}</td><td>{manilaTimestamp.format(new Date(row.confirmedAt))}</td>
        <td>{row.providerTransactionReference ?? 'Unavailable'}</td><td>Awaiting tax and issuance review</td>
      </tr>)}</tbody></ScrollTable>}
    {total > 25 && <div className="connect-actions">
      <Button variant="light" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</Button>
      <span>Page {page}</span>
      <Button variant="light" disabled={page * 25 >= total} onClick={() => setPage(page + 1)}>Next</Button>
    </div>}
  </Card>
}
