import { useState } from 'react'
import { Button, ErrorState, Field, Input, Table } from '@/components/ui'
import { api, apiBlob, errorMessage } from '@/lib/account-api'
import type { ImportPreview, ImportResult } from '@/lib/master-data'

export function MasterImportPanel({ path, close, saved }: { path: string; close: () => void; saved: (message: string) => Promise<void> }) {
  const [file, setFile] = useState<File>()
  const [result, setResult] = useState<ImportPreview>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  function contents() {
    const form = new FormData()
    if (file) form.append('file', file)
    if (result?.previewToken) form.append('previewToken', result.previewToken)
    return form
  }
  async function run(action: 'preview' | 'confirm' | 'errors') {
    setBusy(true)
    setError('')
    try {
      if (action === 'preview') setResult(await api<ImportPreview>(path + '/import/preview', 'POST', contents()))
      if (action === 'confirm') {
        const imported = await api<ImportResult>(path + '/import/confirm', 'POST', contents())
        await saved(imported.imported + (imported.imported === 1 ? ' record imported.' : ' records imported.'))
      }
      if (action === 'errors') {
        const blob = await apiBlob(path + '/import/errors', 'POST', contents())
        const href = URL.createObjectURL(blob)
        const link = document.createElement('a')
        link.href = href
        link.download = 'master-data-import-errors.csv'
        link.click()
        window.setTimeout(() => URL.revokeObjectURL(href), 1000)
      }
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }
  return <section className="master-panel" aria-labelledby="master-import-heading">
    <h2 id="master-import-heading">Import records</h2>
    <p>Use the template headers. For linked records, enter a code from this company. Enter dates as YYYY-MM-DD and keep identifiers formatted as text.</p>
    <div className="master-actions">
      <a className="text-link" href={'/api' + path + '/file?format=csv&template=true'} download>CSV template</a>
      <a className="text-link" href={'/api' + path + '/file?format=xlsx&template=true'} download>Excel template</a>
    </div>
    <Field id="master-import-file" label="CSV or XLSX file (up to 2 MB)">
      <Input id="master-import-file" type="file" accept=".csv,.xlsx" disabled={busy} onChange={event => {
        setFile(event.target.files?.[0]); setResult(undefined); setError('')
      }} />
    </Field>
    {error && <ErrorState>{error}</ErrorState>}
    {result && <div className="master-stack">
      <p role="status">{result.valid} of {result.total} rows passed validation.</p>
      {result.errors.length > 0 && <div className="master-table" role="region" aria-label="Import validation errors" tabIndex={0}>
        <Table><thead><tr><th scope="col">Row</th><th scope="col">Field</th><th scope="col">Issue</th></tr></thead>
          <tbody>{result.errors.slice(0, 50).map(item => <tr key={item.row}><td>{item.row}</td><td>{item.field}</td><td>{item.message}</td></tr>)}</tbody>
        </Table>
      </div>}
    </div>}
    <div className="master-actions">
      <Button type="button" disabled={busy || !file} onClick={() => void run('preview')}>Validate file</Button>
      {result?.previewToken && <Button type="button" disabled={busy} onClick={() => void run('confirm')}>Confirm import</Button>}
      {!!result?.errors.length && <Button type="button" variant="light" disabled={busy} onClick={() => void run('errors')}>Download error report</Button>}
      <Button type="button" variant="light" disabled={busy} onClick={close}>Close import</Button>
    </div>
  </section>
}

