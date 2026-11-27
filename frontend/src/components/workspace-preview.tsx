import { createColumnHelper, getCoreRowModel, useReactTable, flexRender } from '@tanstack/react-table'
import { Activity, ArrowDownUp, ArrowUpRight, Check, ChevronDown, CircleHelp, FileText, LayoutDashboard, PanelLeft, Wallet } from 'lucide-react'
import { lazy, Suspense } from 'react'
import { Badge, Money, Table } from './ui'

const documents = [
  { id: 'SI-2026-0084', customer: 'Northfield Trading', amount: 48600, status: 'Synced' },
  { id: 'SI-2026-0083', customer: 'Pacific Supply Co.', amount: 24750, status: 'Synced' },
  { id: 'SI-2026-0082', customer: 'Studio Meridian', amount: 18200, status: 'Pending' },
]

const column = createColumnHelper<(typeof documents)[number]>()
const columns = [
  column.accessor('id', { header: 'Invoice' }),
  column.accessor('customer', { header: 'Customer' }),
  column.accessor('amount', { header: 'Amount', cell: ({ getValue }) => <Money amount={getValue()} /> }),
  column.accessor('status', { header: 'Sync status', cell: ({ getValue }) => <Badge tone={getValue() === 'Synced' ? 'success' : 'warning'}>{getValue()}</Badge> }),
]

const WorkspaceChart = lazy(() => import('./workspace-chart'))
const navigation = [
  { label: 'Overview', icon: LayoutDashboard }, { label: 'Sales invoices', icon: FileText },
  { label: 'Synchronization', icon: ArrowDownUp }, { label: 'Reconciliation', icon: Activity },
  { label: 'Credits', icon: Wallet },
]

export function WorkspacePreview() {
  const table = useReactTable({ data: documents, columns, getCoreRowModel: getCoreRowModel() })
  return <div className="workspace" aria-label="Sample EMBM eFiscal workspace">
    <div className="workspace-bar"><span><PanelLeft size={13} /> e-Invoicing workspace</span><Badge>Sample data</Badge></div>
    <div className="workspace-body">
      <aside className="workspace-nav" aria-label="Sample workspace sections">
        <div className="workspace-team"><div className="team-avatar">S</div><span>Summit Trading<small>Sample company</small></span><ChevronDown size={11} /></div>
        <span className="workspace-label">WORKSPACE</span>
        {navigation.map(({ label, icon: Icon }, index) => <div key={label} className={index === 0 ? 'workspace-nav-current' : ''}><Icon size={13} />{label}</div>)}
        <div className="workspace-help"><CircleHelp size={13} /> Help center</div>
      </aside>
      <div className="workspace-main">
        <div className="workspace-heading"><div><h2>Invoice overview</h2><p>Electronic invoice issuance and accounting sync.</p></div><span className="workspace-period">Last 7 days <ChevronDown size={10} /></span></div>
        <div className="workspace-metrics">
          <div><span>Invoiced amount</span><strong><Money amount={284650} /></strong><small><ArrowUpRight size={11} /> Sample period</small></div>
          <div><span>Invoices synced</span><strong>124</strong><small><Check size={11} /> Example status</small></div>
          <div><span>Available Credits</span><strong>872</strong><small>128 Used Credits</small></div>
        </div>
        <div className="workspace-chart"><div><b>Invoice activity</b><span><i /> Invoices</span></div>
          <Suspense fallback={<div aria-hidden="true" style={{ height: 91 }} />}><WorkspaceChart /></Suspense>
        </div>
        <div className="workspace-recent"><b>Recent sales invoices</b><Table aria-label="Example sales invoice records">
          <thead>{table.getHeaderGroups().map(group => <tr key={group.id}>{group.headers.map(header =>
            <th key={header.id} scope="col">{flexRender(header.column.columnDef.header, header.getContext())}</th>)}</tr>)}</thead>
          <tbody>{table.getRowModel().rows.map(row => <tr key={row.id}>{row.getVisibleCells().map(cell =>
            <td key={cell.id}>{flexRender(cell.column.columnDef.cell, cell.getContext())}</td>)}</tr>)}</tbody>
        </Table></div>
      </div>
    </div>
  </div>
}
