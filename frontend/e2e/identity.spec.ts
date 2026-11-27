import { execFileSync } from 'node:child_process'
import { randomUUID } from 'node:crypto'
import { mkdir, readdir, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { expect, test, type Page } from '@playwright/test'

const mailbox = process.env.EFISCAL_DEV_MAILBOX
const fixtureDatabase = process.env.ConnectionStrings__Efiscal
const suffix = () => randomUUID().slice(0, 10)

async function emailLink(email: string, subject: string) {
  async function search() {
    const names = (await readdir(mailbox!).catch(() => [])).filter(name => name.endsWith('.json')).sort().reverse()
    for (const name of names) {
      const data = JSON.parse(await readFile(join(mailbox!, name), 'utf8'))
      if (data.recipient === email && data.subject === subject) return String(data.url)
    }
    return ''
  }
  await expect.poll(search).not.toBe('')
  return search()
}

async function capture(page: Page, name: string) {
  await page.evaluate(() => document.fonts.ready.then(() => undefined))
  const viewport = page.viewportSize()!
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(viewport.width)
  const destination = process.env.EFISCAL_SCREENSHOTS
  if (!destination) return
  await mkdir(destination, { recursive: true })
  await page.screenshot({ path: join(destination, name + '.png'), fullPage: true, animations: 'disabled' })
}

async function signIn(page: Page, email: string, password: string) {
  await page.goto('/sign-in')
  await page.getByLabel('Email address').fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Sign In', exact: true }).click()
  await expect(page).toHaveURL(/\/portal\/client$/)
}

async function register(page: Page, email: string, password: string, classification: 'b2b' | 'b2c') {
  await page.goto('/pricing?billing=annual')
  await page.getByRole('link', { name: 'Select Plan: Business', exact: true }).click()
  await expect(page).toHaveURL(/plan=business&billing=annual/)
  await page.getByLabel('Subscriber classification').selectOption(classification)
  await page.getByLabel('Full name').fill('Synthetic Subscriber')
  if (classification === 'b2b') await page.getByLabel('Business name').fill('Synthetic Invoicing Company')
  else await expect(page.getByLabel('Business name')).toHaveCount(0)
  await page.getByLabel('Email address').fill(email)
  await page.getByLabel('Mobile number').fill('09000000000')
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByLabel('Confirm password', { exact: true }).fill(password)
  await page.getByRole('checkbox', { name: /acknowledge the development terms/ }).check()
  await page.getByRole('checkbox', { name: /acknowledge the development privacy/ }).check()
  await expect(page.getByLabel(/RDO|TIN/)).toHaveCount(0)
  await page.getByRole('button', { name: 'Create Account' }).click()
  await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible()
  await page.goto(await emailLink(email, 'Verify your email'))
  await page.getByRole('button', { name: 'Verify email' }).click()
  await expect(page.getByRole('heading', { name: 'Email verified' })).toBeVisible()
  await signIn(page, email, password)
  await expect(page.getByText('Pending Payment', { exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Add Company File' })).toHaveCount(0)
  expect(await page.evaluate(() => [localStorage.length, sessionStorage.length])).toEqual([0, 0])
}

async function createCompany(page: Page, code: string, name: string, screenshot?: string) {
  await page.getByRole('link', { name: 'Add Company File' }).click()
  await page.getByLabel('Company File Code').fill(code)
  await page.getByLabel('Registered Business Name').fill(name)
  await page.getByLabel('Business Classification').fill('Corporation')
  await page.getByLabel('Registered Address').fill('Synthetic development address')
  await page.getByLabel('Business Email').fill('office@example.test')
  await page.getByLabel('Contact Number').fill('09000000000')
  await page.getByRole('button', { name: 'Continue' }).click()
  await page.getByLabel('Registered TIN').fill('000000000')
  await page.getByLabel('VAT Registration Status').selectOption('VAT')
  await page.getByLabel('Registered RDO', { exact: true }).selectOption('001')
  if (screenshot) {
    await page.getByLabel('Registration Evidence PDF (optional)', { exact: true }).setInputFiles({
      name: 'synthetic-onboarding.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4\n%%EOF'),
    })
    await capture(page, screenshot)
  }
  await page.getByRole('button', { name: 'Continue' }).click()
  await page.getByLabel('Branch Code').fill('HQ')
  await page.getByLabel('Registered Branch Name').fill('Synthetic Head Office')
  await page.getByLabel('Branch Address').fill('Synthetic head office address')
  await page.getByRole('button', { name: 'Continue' }).click()
  await expect(page.getByRole('heading', { name: 'Review', exact: true, level: 2 })).toBeVisible()
  await page.getByRole('button', { name: 'Continue' }).click()
  await page.getByRole('button', { name: 'Complete Company Setup' }).click()
  await expect(page.getByRole('heading', { name, exact: true })).toBeVisible()
}

test.describe('PostgreSQL identity and onboarding', () => {
  test.skip(!mailbox || !fixtureDatabase, 'Set the local development mailbox and dedicated database connection.')
  test.setTimeout(100_000)

  test('corporate registration, company and branch setup, company switching, and team invitation', async ({ page }, info) => {
    const servedErrors: string[] = []
    page.on('pageerror', error => servedErrors.push(error.message))
    page.on('response', response => { if (response.status() >= 500) servedErrors.push(response.url() + ': ' + response.status()) })
    const cdp = await page.context().newCDPSession(page)
    await cdp.send('Log.enable')
    cdp.on('Log.entryAdded', ({ entry }) => {
      if (entry.level === 'error' && !entry.text.includes('401')) servedErrors.push(entry.text)
    })
    const email = 'corporate-' + suffix() + '@example.test'
    const password = randomUUID()
    await register(page, email, password, 'b2b')
    await capture(page, info.project.name + '-pending-payment')

    execFileSync('dotnet', [resolve('../backend/Embm.EFiscal.Api/bin/Release/net10.0/Embm.EFiscal.Api.dll'), '--provision-dev', email], {
      env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development' },
      stdio: 'pipe',
    })
    await page.reload()
    await expect(page.getByText('Development subscriber', { exact: true })).toBeVisible()
    await createCompany(page, 'MNL', 'Synthetic Manila Company', info.project.name + '-onboarding')
    await createCompany(page, 'CEB', 'Synthetic Cebu Company')
    await page.getByLabel('Company File', { exact: true }).selectOption({ label: 'MNL · Synthetic Manila Company' })
    await expect(page.getByRole('heading', { name: 'Synthetic Manila Company' })).toBeVisible()
    await page.getByRole('link', { name: 'Invoicing Workstations', exact: true }).click()
    await expect(page.getByText('Save · 0 credits', { exact: false })).toBeVisible()
    await page.getByRole('link', { name: 'Delivery & Templates' }).click()
    await page.getByLabel('Sender name').fill('Synthetic Manila Sender')
    await page.getByLabel('Sender email').fill('sender@example.test')
    await page.getByRole('button', { name: 'Validate settings' }).click()
    await expect(page.getByRole('status')).toContainText('live connectivity are pending')
    await page.getByRole('button', { name: 'Save sender settings' }).click()
    await expect(page.getByRole('status')).toContainText('Sender settings saved')
    await page.getByRole('button', { name: 'Write local sender test' }).click()
    await expect(page.getByRole('status')).toContainText('Sender sample written')
    await expect(page.getByRole('table', { name: 'Local email test history' })).toContainText('Local preview written')

    await page.getByRole('button', { name: 'Email Templates', exact: true }).click()
    await page.getByLabel('Template name', { exact: true }).fill('Manila sales email')
    await page.getByLabel('Email subject').fill('Invoice {{DocumentNumber}}')
    await page.getByRole('button', { name: 'Preview email' }).click()
    await expect(page.getByText('Invoice SI-SAMPLE-001', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Save template', exact: true }).click()
    await expect(page.getByText('Saved version 1', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Write local test email' }).click()
    await expect(page.getByRole('status')).toContainText('No message was sent externally')

    await page.getByLabel('Document type').selectOption('credit-memo')
    await page.getByLabel('Template name', { exact: true }).fill('Manila credit memo email')
    await page.getByLabel('Email subject').fill('Credit Memo {{CreditMemoNumber}}')
    await page.getByRole('button', { name: 'Save template', exact: true }).click()
    await expect(page.getByText('Saved version 1', { exact: true })).toBeVisible()

    await page.getByRole('button', { name: 'PDF Templates', exact: true }).click()
    await page.getByLabel('Document type').selectOption('invoice')
    await page.getByLabel('Template name', { exact: true }).fill('Manila invoice layout')
    await page.getByLabel('Document title').fill('Manila Sales Invoice')
    await page.getByRole('button', { name: 'Preview PDF', exact: true }).click()
    await expect(page.getByRole('link', { name: 'Download sample PDF' })).toBeVisible()
    await page.getByRole('button', { name: 'Save template', exact: true }).click()
    await expect(page.getByText('Saved version 1', { exact: true })).toBeVisible()
    await page.getByLabel('Document type').selectOption('credit-memo')
    await page.getByLabel('Template name', { exact: true }).fill('Manila credit memo layout')
    await page.getByLabel('Document title').fill('Sample branded memo')
    await page.getByRole('button', { name: 'Move Quantity earlier' }).click()
    await expect(page.getByRole('table', { name: 'Fictional line items' }).getByRole('columnheader').first()).toHaveText('Quantity')
    await page.getByRole('button', { name: 'Save template', exact: true }).click()
    await expect(page.getByText('Saved version 1', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Set as company default' }).click()
    await expect(page.getByRole('button', { name: 'Use built-in default instead' })).toBeVisible()
    await capture(page, info.project.name + '-company-template')

    await page.getByLabel('Company File', { exact: true }).selectOption({ label: 'CEB · Synthetic Cebu Company' })
    await expect(page.getByRole('heading', { name: 'Email Sender', exact: true })).toBeVisible()
    await expect(page.getByLabel('Sender name')).toHaveValue('')
    await page.getByLabel('Company File', { exact: true }).selectOption({ label: 'MNL · Synthetic Manila Company' })
    await expect(page.getByLabel('Sender name')).toHaveValue('Synthetic Manila Sender')
    await expect(page.getByRole('table', { name: 'Local email test history' }).getByText('Local preview written')).toHaveCount(2)
    await page.getByRole('link', { name: 'Subscriber Information', exact: true }).click()
    await page.getByLabel('Subscriber Name').fill('Updated Synthetic Subscriber')
    await page.getByRole('button', { name: 'Save subscriber information' }).click()
    await expect(page.getByRole('status')).toContainText('Saved.')
    await page.getByRole('link', { name: 'Taxpayer Registration', exact: true }).click()
    await expect(page.getByText(/synthetic-onboarding\.pdf/)).toBeVisible()
    await page.getByLabel('Registration Evidence PDF').setInputFiles({
      name: 'synthetic-registration.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4\n%%EOF'),
    })
    await page.getByRole('button', { name: 'Attach PDF evidence' }).click()
    await expect(page.getByText(/synthetic-registration\.pdf/)).toBeVisible()
    await page.getByRole('link', { name: 'Branch Management' }).click()
    await page.getByLabel('Branch Code').fill('ANNEX')
    await page.getByLabel('Registered Branch Name').fill('Synthetic Annex')
    await page.getByLabel('Branch Address').fill('Synthetic annex address')
    await page.getByRole('button', { name: 'Create branch' }).click()
    await expect(page.getByRole('table').getByText('Synthetic Annex', { exact: true })).toBeVisible()
    await page.getByLabel('Branch', { exact: true }).selectOption({ label: 'ANNEX · Synthetic Annex' })
    await page.getByRole('link', { name: 'Overview', exact: true }).click()
    await expect(page.locator('dd').getByText('Synthetic Annex', { exact: true })).toBeVisible()
    await expect(page.locator('dd').getByText('Not verified', { exact: true })).toBeVisible()
    await expect(page.locator('dd').getByText('Disabled', { exact: true })).toBeVisible()
    await capture(page, info.project.name + '-workspace')

    const memberEmail = 'member-' + suffix() + '@example.test'
    await page.getByRole('link', { name: 'Team Members', exact: true }).click()
    await page.getByLabel('Invite Email').fill(memberEmail)
    await page.getByLabel('Assigned Role').selectOption('readonly')
    await page.getByRole('checkbox', { name: 'MNL · Synthetic Manila Company' }).check()
    await page.getByRole('button', { name: 'Send invitation' }).click()
    await expect(page.getByRole('table', { name: 'Team invitations' }).getByText(memberEmail)).toBeVisible()
    await page.getByRole('button', { name: 'Sign Out' }).click()
    await expect(page).toHaveURL(/\/sign-in$/)
    await page.goto(await emailLink(memberEmail, 'Your eFiscal invitation'))
    await page.getByLabel('Full name').fill('Synthetic Team Member')
    await page.getByLabel('Mobile number').fill('09000000000')
    const memberPassword = randomUUID()
    await page.getByLabel('New password', { exact: true }).fill(memberPassword)
    await page.getByLabel('Confirm new password').fill(memberPassword)
    await page.getByRole('checkbox', { name: /acknowledge the development terms/ }).check()
    await page.getByRole('checkbox', { name: /acknowledge the development privacy/ }).check()
    await page.getByRole('button', { name: 'Create account and accept' }).click()
    await expect(page.getByRole('heading', { name: 'Invitation accepted' })).toBeVisible()
    await signIn(page, memberEmail, memberPassword)
    await expect(page.getByLabel('Company File', { exact: true }).locator('option')).toHaveText(['Select a company file', 'MNL · Synthetic Manila Company'])
    await expect(page.getByRole('link', { name: 'Add Company File' })).toHaveCount(0)
    expect(servedErrors).toEqual([])
  })

  test('subscription checkout provisions the workspace and a duplicate top-up only adds credits once', async ({ page }, info) => {
    const email = 'paid-' + suffix() + '@example.test'
    const servedErrors: string[] = []
    const cdp = await page.context().newCDPSession(page)
    await cdp.send('Log.enable')
    cdp.on('Log.entryAdded', ({ entry }) => {
      if (entry.level === 'error' && !entry.text.includes('401')) servedErrors.push(entry.text)
    })
    page.on('pageerror', error => servedErrors.push(error.message))
    await register(page, email, randomUUID(), 'b2c')
    await page.getByRole('button', { name: 'Continue to checkout' }).click()
    await expect(page.getByText('Development payment simulator')).toBeVisible()
    await page.getByRole('button', { name: 'Simulate delayed confirmation' }).click()
    await expect(page.getByText('Processing', { exact: true })).toBeVisible()
    await capture(page, info.project.name + '-billing-checkout')
    await page.getByRole('button', { name: 'Confirm delayed payment' }).click()
    await expect(page.getByText(/subscriber account is ready/)).toBeVisible()
    await page.getByRole('link', { name: 'Open client workspace' }).click()
    await expect(page.getByRole('link', { name: 'Add Company File' })).toBeVisible()
    await page.getByRole('link', { name: 'Subscription', exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Business subscription' })).toBeVisible()
    await page.getByRole('button', { name: 'Add demo payment method' }).click()
    await page.getByRole('button', { name: 'Authorize automatic renewal' }).click()
    await expect(page.getByText('Consent recorded', { exact: true })).toBeVisible()
    await capture(page, info.project.name + '-subscription')
    await page.getByRole('link', { name: 'Buy Credits', exact: true }).click()
    await page.getByLabel('Credit package', { exact: true }).selectOption({ label: '100 Save & Send Credits' })
    await page.getByRole('button', { name: 'Proceed to checkout' }).click()
    await page.getByRole('button', { name: 'Simulate duplicate confirmation' }).click()
    await expect(page.getByText(/Purchased credits are available/)).toBeVisible()
    await page.getByRole('link', { name: 'Return to workspace' }).click()
    await expect(page.getByText('12,100', { exact: true })).toBeVisible()
    await expect(page.getByText('Purchased top-up', { exact: true })).toBeVisible()
    await capture(page, info.project.name + '-credit-wallet')
    const download = page.waitForEvent('download')
    await page.getByRole('link', { name: /Export credit history/ }).click()
    expect((await download).suggestedFilename()).toBe('save-and-send-credit-history.csv')
    await page.getByRole('link', { name: 'Billing History', exact: true }).click()
    await page.getByLabel('Payment purpose').selectOption('CreditTopUp')
    await page.getByRole('button', { name: /^mock-/ }).click()
    await expect(page.getByRole('heading', { name: 'Development payment record' })).toBeVisible()
    await capture(page, info.project.name + '-billing-history')
    expect(servedErrors).toEqual([])
  })


  test('commits a Sales Order, issues a partial invoice and delivers its original PDF', async ({ page }, info) => {
    const faults: string[] = []
    const cdp = await page.context().newCDPSession(page)
    await cdp.send('Log.enable')
    cdp.on('Log.entryAdded', ({ entry }) => {
      if (entry.level === 'error' && !entry.text.includes('401')) faults.push(entry.text)
    })
    page.on('pageerror', error => faults.push(error.message))
    page.on('response', response => { if (response.status() >= 500) faults.push(response.url() + ': ' + response.status()) })
    const email = 'documents-' + suffix() + '@example.test'
    await register(page, email, randomUUID(), 'b2b')
    await page.getByRole('button', { name: 'Continue to checkout' }).click()
    await page.getByRole('button', { name: 'Simulate successful payment' }).click()
    await page.getByRole('link', { name: 'Open client workspace' }).click()
    await createCompany(page, 'DOCS', 'Synthetic Invoice Company')
    const workspace = await (await page.request.get('/api/workspace')).json()
    const { subscriberId, companyId } = workspace.active
    const company = '/api/workspace/' + subscriberId + '/companies/' + companyId
    const token = (await (await page.request.get('/api/auth/csrf')).json()).token
    async function mutate(path: string, data: object, method = 'POST') {
      const response = await page.request.fetch(path, { data, method, headers: { 'X-EFiscal-CSRF': token } })
      expect(response.ok(), await response.text()).toBeTruthy()
      return response.json()
    }
    const master = company + '/master-data/'
    const unit = (await mutate(master + 'units', { code: 'EA', name: 'Each', active: true, data: { symbol: 'ea' } })).id
    const taxGroup = (await mutate(master + 'tax-groups', { code: 'VAT', name: 'Domestic VAT', active: true, data: {
      classification: 'VATable', treatment: 'Exclusive', rounding: 'HalfAway', effectiveFrom: '2026-01-01', companyAuthorized: true,
      authority: 'PH-NIRC-STANDARD-VAT', components: [{ kind: 'VAT', basis: 'NetSales', rate: '12' }],
    } })).id
    const item = (await mutate(master + 'items', { code: 'SERVICE', name: 'Consultation', active: true,
      data: { itemType: 'Services', unit, price: '100', taxGroup, description: 'Synthetic consulting service' } })).id
    const customer = (await mutate(master + 'customers', { code: 'BUYER', name: 'Buyer Company', active: true,
      data: { classification: 'Corporate', registeredName: 'Buyer Company', email: 'synthetic-buyer@example.test' } })).id
    await mutate(company + '/configuration/sender', { expectedRevision: 0, profile: {
      senderName: 'Demo Seller', senderEmail: 'seller@example.test', provider: 'platform', cc: [], bcc: [],
    } }, 'PUT')

    const root = '/portal/client/documents'
    await page.goto(root + '/numbering')
    await page.getByLabel('Document type', { exact: true }).selectOption('SalesOrder')
    await page.getByLabel('Prefix', { exact: true }).fill('SO-')
    await page.getByLabel('Effective from', { exact: true }).fill('2026-01-01')
    await page.locator('form').filter({ has: page.getByLabel('Prefix', { exact: true }) }).getByRole('button', { name: /publish/i }).click()
    await expect(page.getByRole('region', { name: 'Series revisions' })).toContainText('SO-')
    for (const [type, prefix] of [['SalesInvoice', 'SI-'], ['CreditMemo', 'CM-']]) {
      await mutate(company + '/documents/numbering/' + type, {
        effectiveFrom: '2026-01-01', prefix, digits: 6, initialNumber: 1, expectedRevision: 0,
      })
    }
    async function balance() {
      return (await (await page.request.get('/api/billing/subscribers/' + subscriberId + '/summary')).json()).wallet.available
    }
    const starting = await balance()
    await page.goto(root + '/SalesOrder')
    await page.getByRole('link', { name: 'New Sales Order', exact: true }).click()
    await page.getByLabel('Customer', { exact: true }).selectOption(customer)
    await page.getByLabel('Item for line 1', { exact: true }).selectOption(item)
    await page.getByLabel('Quantity for line 1', { exact: true }).fill('3')
    await page.getByRole('checkbox', { name: 'Allow partial invoicing' }).check()
    await page.getByRole('button', { name: 'Calculate totals' }).click()
    await expect(page.getByRole('heading', { name: 'Calculated document totals' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Save & Send', exact: true })).toHaveCount(0)
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page.getByText('Committed — Permanent Record')).toBeVisible()
    expect(await balance()).toBe(starting)
    await page.getByRole('link', { name: 'Create invoice from order' }).click()
    await page.getByRole('button', { name: 'Add SERVICE from source' }).click()
    await page.getByLabel('Quantity for line 1', { exact: true }).fill('2')
    await page.getByRole('button', { name: 'Calculate totals' }).click()
    await expect(page.getByLabel('Financial totals')).toContainText('224')
    await page.getByRole('button', { name: 'Preview PDF' }).click()
    const previewDownload = page.waitForEvent('download')
    await page.getByRole('link', { name: 'Download preview PDF' }).click()
    expect((await previewDownload).suggestedFilename()).toBe('SalesInvoice-preview.pdf')
    await page.getByRole('button', { name: 'Preview email' }).click()
    await expect(page.getByRole('heading', { name: 'Email preview' })).toBeVisible()
    expect(await balance()).toBe(starting)
    await capture(page, info.project.name + '-invoice-workstation')
    await page.getByRole('button', { name: 'Save & Send', exact: true }).click()
    await expect(page.getByRole('dialog')).toContainText('Credits to consume')
    await capture(page, info.project.name + '-invoice-confirmation')
    await page.getByRole('button', { name: 'Confirm Save & Send' }).click()
    await expect(page.getByRole('heading', { name: 'SI-000001', exact: true })).toBeVisible()
    await expect(page.getByText('Accepted by development adapter').first()).toBeVisible({ timeout: 15_000 })
    await expect(page.getByRole('link', { name: /^SO-/ })).toBeVisible()
    const issuedDownload = page.waitForEvent('download')
    await page.getByRole('link', { name: 'Download issued PDF' }).click()
    expect((await issuedDownload).suggestedFilename()).toContain('SI-000001')
    await capture(page, info.project.name + '-issued-invoice')
    await page.getByRole('button', { name: 'Resend email', exact: true }).click()
    await expect(page.getByRole('region', { name: 'Document delivery attempts' }).getByText('Accepted by development adapter')).toHaveCount(2)
    expect(await balance()).toBe(starting - 1)
    await page.getByRole('link', { name: 'Create linked Credit Memo' }).click()
    await page.getByRole('button', { name: 'Add SERVICE from source' }).click()
    await page.getByLabel('Quantity for line 1', { exact: true }).fill('1')
    await page.getByLabel('Adjustment reason').fill('One returned unit')
    await page.getByRole('button', { name: 'Calculate totals' }).click()
    await expect(page.getByLabel('Financial totals')).toContainText('112')
    await page.getByRole('button', { name: 'Save & Send', exact: true }).click()
    await page.getByRole('button', { name: 'Confirm Save & Send' }).click()
    await expect(page.getByRole('heading', { name: 'CM-000001', exact: true })).toBeVisible()
    await expect(page.getByText('Accepted by development adapter').first()).toBeVisible({ timeout: 15_000 })
    expect(await balance()).toBe(starting - 2)
    await page.getByRole('link', { name: 'Email Delivery History', exact: true }).click()
    await expect(page.getByRole('region', { name: 'Delivery attempt history' })).toContainText('CM-000001')

    await page.getByRole('link', { name: 'Sales Invoices', exact: true }).click()
    await page.getByRole('link', { name: 'New Sales Invoice', exact: true }).click()
    await page.getByLabel('Customer', { exact: true }).selectOption(customer)
    await page.getByLabel('Item for line 1', { exact: true }).selectOption(item)
    await page.getByRole('button', { name: 'Calculate totals' }).click()
    await expect(page.getByLabel('Financial totals')).toContainText('112')
    await page.getByRole('button', { name: 'Save & Send', exact: true }).click()
    await page.getByRole('button', { name: 'Confirm Save & Send' }).click()
    await expect(page.getByRole('heading', { name: 'SI-000002', exact: true })).toBeVisible()
    expect(await balance()).toBe(starting - 3)
    await page.getByRole('link', { name: 'BIR Reporting Preparation', exact: true }).click()
    await expect(page.getByRole('heading', { name: 'BIR Reporting Preparation', level: 2 })).toBeVisible()
    await expect(page.getByRole('table')).not.toContainText('SO-000001')
    const reportRow = page.getByRole('row').filter({ hasText: 'SI-000002' })
    await reportRow.getByRole('button', { name: 'Prepare', exact: true }).click()
    await expect(page.getByRole('region', { name: 'Preparation evidence' })).toContainText('embm-preparation-v1')
    await reportRow.getByRole('button', { name: 'Run local simulation' }).click()
    await expect(reportRow).toContainText('Local simulation validated')
    await expect(reportRow).toContainText('Not submitted')
    expect(await balance()).toBe(starting - 3)
    await capture(page, info.project.name + '-reporting-preparation')
    await page.getByRole('link', { name: 'Regulatory Authorizations', exact: true }).click()
    await page.getByRole('button', { name: 'Submit authorization evidence', exact: true }).click()
    await page.getByLabel('Covered branch').selectOption({ index: 1 })
    await page.getByLabel('Documented reference').fill('000123')
    await page.getByLabel('Issuing authority').fill('Synthetic issuing authority')
    await page.getByLabel('Solution', { exact: true }).fill('EMBM eFiscal')
    await page.getByLabel('Solution version').fill('development')
    await page.getByLabel('Documented scope').fill('Synthetic evidence workflow')
    await page.getByLabel('Effective from').fill('2026-01-01')
    await page.getByLabel('Evidence file', { exact: false }).setInputFiles({
      name: 'synthetic.json', mimeType: 'application/json', buffer: Buffer.from('{"synthetic":true}'),
    })
    await page.getByRole('button', { name: 'Send for review', exact: true }).click()
    const authorization = page.getByRole('row').filter({ hasText: '000123' })
    await expect(authorization).toContainText('Awaiting review')
    await authorization.getByRole('button', { name: 'View history' }).click()
    await expect(page.getByRole('heading', { name: 'Version 1 · 000123' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Record review' })).toHaveCount(0)
    const evidenceDownload = page.getByRole('link', { name: 'Download synthetic.json' })
    const download = await page.request.get((await evidenceDownload.getAttribute('href'))!)
    expect(download.ok()).toBeTruthy()
    expect(await download.json()).toEqual({ synthetic: true })
    expect(await balance()).toBe(starting - 3)
    await capture(page, info.project.name + '-regulatory-authorizations')
    expect(await page.evaluate(() => [localStorage.length, sessionStorage.length])).toEqual([0, 0])
    expect(faults).toEqual([])
  })


  test('publishes a reviewed connector and connects accounting and store source orders', async ({ page, browser }, info) => {
    test.setTimeout(150_000)
    page.setDefaultTimeout(15_000)
    const vendorEmail = process.env.EFISCAL_PHASE7_VENDOR_EMAIL
    const vendorPassword = process.env.EFISCAL_PHASE7_VENDOR_PASSWORD
    test.skip(!vendorEmail || !vendorPassword, 'Set the local Connector Builder fixture credentials.')
    const faults: string[] = []
    async function inspect(target: Page) {
      target.on('pageerror', error => faults.push(error.message))
      target.on('response', response => { if (response.status() >= 500) faults.push(response.url() + ': ' + response.status()) })
      const cdp = await target.context().newCDPSession(target)
      await cdp.send('Log.enable')
      cdp.on('Log.entryAdded', ({ entry }) => { if (entry.level === 'error' && !entry.text.includes('401')) faults.push(entry.text) })
    }
    await inspect(page)
    const tag = 'e2e-' + suffix()
    const name = 'Synthetic Accounts ' + tag
    const vendorContext = await browser.newContext({ baseURL: info.project.use.baseURL, viewport: page.viewportSize()!,
      hasTouch: info.project.name !== 'desktop', isMobile: info.project.name === 'mobile' })
    try {
      const vendor = await vendorContext.newPage()
      vendor.setDefaultTimeout(15_000)
      await inspect(vendor)
      await vendor.goto('/vendor/sign-in')
      await vendor.getByLabel('Administrator email').fill(vendorEmail!)
      await vendor.getByLabel('Password', { exact: true }).fill(vendorPassword!)
      await vendor.getByRole('button', { name: 'Sign In', exact: true }).click()
      await vendor.getByRole('link', { name: 'Regulatory Compliance', exact: true }).click()
      await expect(vendor.getByRole('heading', { name: 'ESP Accreditation' })).toBeVisible()
      await vendor.getByRole('button', { name: 'Accreditation Readiness', exact: true }).click()
      await expect(vendor.getByText('Electronic invoice integrity', { exact: true })).toBeVisible()
      await vendor.getByRole('button', { name: 'Material Change Reviews', exact: true }).click()
      await vendor.getByRole('button', { name: 'Add record', exact: true }).click()
      await vendor.getByLabel('Record key').fill('change-' + tag)
      await vendor.getByLabel('Title', { exact: true }).fill('Synthetic reporting change ' + tag)
      await vendor.getByLabel('Affected solution version').fill('development')
      await vendor.getByLabel('Regulatory impact assessment').fill('Review effect on local reporting evidence.')
      await vendor.getByRole('button', { name: 'Save version' }).click()
      await expect(vendor.getByRole('status')).toContainText('Version recorded.')
      await vendor.getByRole('button', { name: 'Regulatory Requirements', exact: true }).click()
      await expect(vendor.getByRole('heading', { name: 'Regulatory Requirements', exact: true })).toBeVisible()
      await vendor.getByRole('link', { name: 'Universal API Connector Builder' }).click()
      await vendor.getByRole('link', { name: 'Create connector' }).click()
      await vendor.getByLabel('Connector code', { exact: true }).fill(tag)
      await vendor.getByLabel('Platform name', { exact: true }).fill(name)
      await vendor.getByLabel('Platform description', { exact: true }).fill('Synthetic accounting source orders for an invoicing company.')
      await vendor.getByLabel('Execution adapter').selectOption('mock-accounting')
      await vendor.getByText('Authentication definitions', { exact: true }).click()
      await vendor.getByRole('checkbox', { name: 'Synthetic Authorization' }).check()
      await vendor.getByText('Capabilities', { exact: true }).click()
      for (const capability of ['Discover Company Files', 'Sales Order Read', 'Sales Order Line Read', 'Invoice Order Link', 'Partial Invoicing']) {
        await vendor.getByRole('checkbox', { name: capability, exact: true }).check()
        await vendor.getByLabel(capability + ' verification', { exact: true }).selectOption('Development Verified')
      }
      await vendor.getByRole('button', { name: 'Create draft configuration', exact: true }).click()
      await expect(vendor.getByRole('heading', { name: 'Import API specification' })).toBeVisible()
      const schema = { type: 'object', required: ['id'], properties: { id: { type: 'string' } } }
      const response = { description: 'Synthetic response', content: { 'application/json': { schema } } }
      const spec = {
        openapi: '3.1.0', info: { title: 'Synthetic account order API', version: '1' }, servers: [{ url: 'https://example.com' }],
        paths: { '/orders': { get: { responses: { '200': response } } } },
      }
      await vendor.getByLabel('API specification file').setInputFiles({ name: 'synthetic-openapi.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(spec)) })
      await vendor.getByRole('button', { name: 'Upload specification' }).click()
      await expect(vendor.getByRole('region', { name: 'Imported API operations' })).toContainText('/orders')
      await vendor.getByRole('button', { name: 'Validate configuration' }).click()
      await expect(vendor.getByText('Configuration valid', { exact: true })).toBeVisible()
      await capture(vendor, info.project.name + '-connector-review')
      await vendor.getByRole('button', { name: 'Approve development version' }).click()
      await vendor.getByRole('button', { name: 'Publish development connector' }).click()
      await expect(vendor.getByText('Published', { exact: true })).toBeVisible()
    } finally {
      await vendorContext.close()
    }

    await register(page, 'connect-' + suffix() + '@example.test', randomUUID(), 'b2b')
    await page.getByRole('button', { name: 'Continue to checkout' }).click()
    await page.getByRole('button', { name: 'Simulate successful payment' }).click()
    await page.getByRole('link', { name: 'Open client workspace' }).click()
    await createCompany(page, 'CONNECT', 'Synthetic Connect Company')
    const { active } = await (await page.request.get('/api/workspace')).json()
    const company = '/api/workspace/' + active.subscriberId + '/companies/' + active.companyId
    const token = (await (await page.request.get('/api/auth/csrf')).json()).token
    async function createRecord(kind: string, code: string, name: string, data: object) {
      const response = await page.request.post(company + '/master-data/' + kind, {
        data: { code, name, active: true, data }, headers: { 'X-EFiscal-CSRF': token },
      })
      expect(response.ok(), await response.text()).toBeTruthy()
      return (await response.json()).id
    }
    const unit = await createRecord('units', 'EA', 'Each', { symbol: 'ea' })
    const vat = await createRecord('tax-groups', 'VAT', 'Domestic VAT', { classification: 'VATable', treatment: 'Exclusive',
      rounding: 'HalfAway', effectiveFrom: '2026-01-01', companyAuthorized: true, authority: 'PH-NIRC-STANDARD-VAT',
      components: [{ kind: 'VAT', basis: 'NetSales', rate: '12' }] })
    const item = await createRecord('items', 'SERVICE', 'Company Service', { itemType: 'Services', price: '100', unit, taxGroup: vat })
    const customer = await createRecord('customers', 'BUYER', 'Company Buyer', {
      classification: 'Corporate', registeredName: 'Company Buyer', email: 'company-buyer@example.test',
    })
    const wallet = '/api/billing/subscribers/' + active.subscriberId + '/summary'
    const starting = (await (await page.request.get(wallet)).json()).wallet.available
    await page.getByRole('link', { name: 'Integration Marketplace', exact: true }).click()
    await page.getByLabel('Category', { exact: true }).selectOption('E-Commerce')
    await expect(page.getByRole('heading', { name: 'Shopee Philippines' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'MYOB Business' })).toHaveCount(0)
    await page.getByLabel('Category', { exact: true }).selectOption('')
    await capture(page, info.project.name + '-integration-marketplace')
    await page.getByLabel('Search integrations').fill(name)
    await page.getByRole('link', { name: 'View Details for ' + name }).click()
    await expect(page.getByRole('heading', { name, exact: true })).toBeVisible()

    async function activate(resource: string, commerce: boolean) {
      await page.getByRole('button', { name: 'Authorize demo account' }).click()
      await page.getByLabel(commerce ? 'External store' : 'External company file', { exact: true }).selectOption({ label: resource })
      await page.getByRole('button', { name: 'Bind to this eFiscal company' }).click()
      await page.getByRole('link', { name: 'Continue to test and mapping' }).click()
      await page.getByRole('button', { name: 'Test connection' }).click()
      await expect(page.getByText('Mapping Required', { exact: true })).toBeVisible()
      await page.getByLabel('Company customer', { exact: true }).selectOption(customer)
      await page.getByLabel('Company item', { exact: true }).selectOption(item)
      await page.getByLabel('Company base unit', { exact: true }).selectOption(unit)
      await page.getByRole('button', { name: 'Save mapping version' }).click()
      await page.getByRole('button', { name: 'Activate connection' }).click()
      await expect(page.getByText('Connected', { exact: true })).toBeVisible()
    }
    await page.getByRole('link', { name: 'Connect', exact: true }).click()
    await activate('Demo Manila Company File', false)
    await page.getByLabel('External Sales Order number').fill('1001')
    await page.getByRole('button', { name: 'Find Sales Orders' }).click()
    await page.getByText('EXT-SO-MNL-1001 · Open', { exact: true }).click()
    await page.getByRole('link', { name: 'Use in Sales Invoice' }).click()
    await expect(page.getByLabel('Quantity for line 1')).toHaveValue('5')
    await expect(page.getByText(/EXT-SO-MNL-1001/)).toBeVisible()

    await page.getByRole('link', { name: 'Integration Marketplace', exact: true }).click()
    await page.getByLabel('Search integrations').fill('Development Commerce')
    await page.getByRole('link', { name: 'Connect Development Commerce' }).click()
    await activate('Demo Manila Store', true)
    await page.getByRole('button', { name: 'Retrieve marketplace orders' }).click()
    await expect(page.getByRole('status')).toContainText('3 imported')
    await page.getByRole('link', { name: 'Open Source Orders' }).click()
    await expect(page.getByRole('link', { name: 'SHOP-MNL-1001' })).toBeVisible()
    await capture(page, info.project.name + '-source-orders')
    await page.getByRole('link', { name: 'SHOP-MNL-1001' }).click()
    await expect(page.getByText('Source shipping charge ₱25.00')).toBeVisible()
    await page.getByRole('link', { name: 'Use in Sales Invoice' }).click()
    await expect(page.getByLabel('Quantity for line 1')).toHaveValue('5')
    await expect(page.getByText(/SHOP-MNL-1001/)).toBeVisible()
    expect((await (await page.request.get(wallet)).json()).wallet.available).toBe(starting)
    expect((await page.request.get('/api/vendor/connectors')).status()).toBe(403)
    const registerResponse = await (await page.request.get(company + '/documents/SalesInvoice/register')).json()
    expect(registerResponse.items).toEqual([])
    await capture(page, info.project.name + '-invoice-from-store')
    expect(faults).toEqual([])
  })


  test('maps fields, tests the sandbox and verifies accounting identities from committed documents', async ({ page, browser }, info) => {
    test.setTimeout(150_000)
    page.setDefaultTimeout(15_000)
    const vendorEmail = process.env.EFISCAL_PHASE7_VENDOR_EMAIL
    const vendorPassword = process.env.EFISCAL_PHASE7_VENDOR_PASSWORD
    test.skip(!vendorEmail || !vendorPassword, 'Set the local Connector Builder fixture credentials.')
    const faults: string[] = []
    async function inspect(target: Page) {
      const cdp = await target.context().newCDPSession(target)
      await cdp.send('Log.enable')
      cdp.on('Log.entryAdded', ({ entry }) => { if (entry.level === 'error' && !entry.text.includes('401')) faults.push(entry.text) })
      target.on('pageerror', error => faults.push(error.message))
      target.on('response', response => { if (response.status() >= 500) faults.push(response.url() + ': ' + response.status()) })
    }
    await inspect(page)
    const label = 'Invoice defaults ' + suffix()
    const vendorContext = await browser.newContext({ baseURL: info.project.use.baseURL, viewport: page.viewportSize()!,
      hasTouch: info.project.name !== 'desktop', isMobile: info.project.name === 'mobile' })
    try {
      const vendor = await vendorContext.newPage()
      vendor.setDefaultTimeout(15_000)
      await inspect(vendor)
      await vendor.goto('/vendor/sign-in')
      await vendor.getByLabel('Administrator email').fill(vendorEmail!)
      await vendor.getByLabel('Password', { exact: true }).fill(vendorPassword!)
      await vendor.getByRole('button', { name: 'Sign In', exact: true }).click()
      await vendor.getByRole('link', { name: 'Universal API Connector Builder' }).click()
      await vendor.getByRole('link', { name: 'Mapping templates', exact: true }).click()
      await vendor.getByLabel('Template name').fill(label)
      await vendor.getByLabel('Template connector version').selectOption({ label: 'Development Accounting · v2' })
      await vendor.getByRole('button', { name: 'Publish mapping template' }).click()
      await expect(vendor.getByRole('status')).toContainText('Vendor template published')
      await vendor.getByRole('link', { name: 'API sandbox', exact: true }).click()
      await vendor.getByLabel('Sandbox connector version').selectOption({ label: 'Development Accounting · v2' })
      await vendor.getByLabel('Approved endpoint').selectOption({ label: 'GET /v1/accounting/status · ExternalDocumentStatusRead' })
      await vendor.getByText('Preview sanitized request', { exact: true }).click()
      await vendor.getByRole('button', { name: 'Execute sandbox request' }).click()
      await expect(vendor.getByText('Confirmed', { exact: true })).toBeVisible()
      await capture(vendor, info.project.name + '-api-sandbox')
    } finally { await vendorContext.close() }

    await register(page, 'sync-' + suffix() + '@example.test', randomUUID(), 'b2b')
    await page.getByRole('button', { name: 'Continue to checkout' }).click()
    await page.getByRole('button', { name: 'Simulate successful payment' }).click()
    await page.getByRole('link', { name: 'Open client workspace' }).click()
    await createCompany(page, 'SYNC', 'Synthetic Accounting Company')
    const { active } = await (await page.request.get('/api/workspace')).json()
    const company = '/api/workspace/' + active.subscriberId + '/companies/' + active.companyId
    const proof = (await (await page.request.get('/api/auth/csrf')).json()).token
    async function mutate(path: string, data?: object, method = 'POST') {
      const response = await page.request.fetch(path, { method, data, headers: { 'X-EFiscal-CSRF': proof } })
      expect(response.ok(), await response.text()).toBeTruthy()
      return response.json()
    }
    const record = (kind: string, code: string, name: string, data: object) =>
      mutate(company + '/master-data/' + kind, { code, name, active: true, data })
    const unit = (await record('units', 'EA', 'Each', { symbol: 'ea' })).id
    const taxGroup = (await record('tax-groups', 'VAT', 'Domestic VAT', { classification: 'VATable', treatment: 'Exclusive',
      rounding: 'HalfAway', effectiveFrom: '2026-01-01', companyAuthorized: true, authority: 'PH-NIRC-STANDARD-VAT',
      components: [{ kind: 'VAT', basis: 'NetSales', rate: '12' }] })).id
    const customer = (await record('customers', 'BUYER', 'Buyer company', { classification: 'Corporate', registeredName: 'Buyer company',
      email: 'mock-purchaser@example.test' })).id
    const item = (await record('items', 'SERVICE', 'Company service', { itemType: 'Services', unit, price: '100', taxGroup })).id
    await mutate(company + '/configuration/sender', { expectedRevision: 0, profile: {
      senderName: 'Sandbox sender', senderEmail: 'sender@example.test', provider: 'platform', cc: [], bcc: [],
    } }, 'PUT')
    for (const [kind, prefix] of [['SalesOrder', 'SO-'], ['SalesInvoice', 'SI-'], ['CreditMemo', 'CM-']])
      await mutate(company + '/documents/numbering/' + kind, { effectiveFrom: '2026-01-01', prefix, digits: 6, initialNumber: 1, expectedRevision: 0 })

    const connect = company + '/connect'
    const declared = await mutate(connect + '/catalog/development-accounting/authorize')
    const link = await mutate(connect + '/connections', { versionId: declared.versionId, proof: declared.proof, resourceId: 'sample-company-mnl',
      name: 'Accounting fixture', branchId: active.branchId })
    const connection = connect + '/connections/' + link.id
    await mutate(connection + '/test', { scenario: 'success' })
    await mutate(connection + '/mapping', { customerId: customer, itemId: item, unitId: unit, expectedVersion: 0 }, 'PUT')
    await mutate(connection + '/activate')
    await page.getByRole('link', { name: 'Reference Mapping', exact: true }).click()
    await page.getByLabel('Reference connection').selectOption(link.id)
    await page.getByLabel('Reference type').selectOption('Tax')
    await page.getByLabel('Company reference').selectOption(taxGroup)
    await page.getByLabel('External identifier').selectOption('VAT12')
    await page.getByRole('button', { name: 'Save reference mapping' }).click()
    await expect(page.getByRole('status')).toContainText('Reference mapping saved')

    await page.getByRole('link', { name: 'Field Mapping', exact: true }).click()
    await page.getByLabel('Mapping profile name').fill('Invoice field mapping')
    await page.getByLabel('Mapping connection').selectOption(link.id)
    await page.getByRole('button', { name: 'Create mapping profile' }).click()
    await page.getByLabel('Published vendor template').selectOption({ label: label + ' · v1' })
    await page.getByRole('button', { name: 'Copy to company' }).click()
    await expect(page.getByText('v1 · Validated', { exact: true })).toBeVisible()
    await page.getByLabel('Search fields').fill('Custom header fields')
    await page.getByLabel('Custom header fields transformation', { exact: true }).selectOption('object')
    await page.getByLabel('Custom header fields source', { exact: true }).selectOption('remarks')
    await page.getByRole('button', { name: 'Preview mapped payload' }).click()
    await expect(page.getByText('Mapping valid', { exact: true })).toBeVisible()
    await page.getByText('Transformed payload', { exact: true }).click()
    await expect(page.locator('details[open] pre').filter({ hasText: 'customHeader' })).toContainText('"customHeader": "[redacted]"')
    await page.getByLabel('Field scope').selectOption('Line')
    await page.getByLabel('Search fields').fill('External discount code')
    await expect(page.getByLabel('External discount code transformation', { exact: true })).toHaveValue('reference')
    await page.getByLabel('External discount code transformation', { exact: true }).selectOption('omit')
    await page.getByLabel('Search fields').fill('description')
    await page.getByLabel('Description transformation', { exact: true }).selectOption('trim')
    await page.getByRole('button', { name: 'Validate mapping', exact: true }).click()
    await expect(page.getByText('Mapping valid', { exact: true })).toBeVisible()
    await capture(page, info.project.name + '-field-designer')
    await page.getByRole('button', { name: 'Save mapping version' }).click()
    await page.getByRole('button', { name: 'Activate version 2' }).click()
    await expect(page.getByText('v2 · Active', { exact: true })).toBeVisible()
    const outboundId = page.url().split('/').at(-1)
    await page.getByRole('link', { name: 'Synchronization Center', exact: true }).click()
    await page.getByLabel('Destination branch').selectOption(active.branchId)
    await page.getByLabel('Outbound mapping profile').selectOption(outboundId!)
    await page.getByRole('button', { name: 'Save accounting destination' }).click()
    await expect(page.getByRole('status')).toContainText('Accounting destination saved')

    const fields = await (await page.request.get(connect + '/mapping-fields/Outbound')).json()
    for (const [kind, scenario] of [['SalesOrder', 'Normal'], ['CreditMemo', 'UncertainAfterWrite']]) {
      const path = connect + '/mapping-profiles'
      const profile = await mutate(path, { connectionId: link.id, name: kind + ' field mapping', documentType: kind, direction: 'Outbound' })
      await mutate(path + '/' + profile.id + '/versions', { rules: fields.defaults, expectedVersion: 0 })
      await mutate(path + '/' + profile.id + '/activate', { version: 1 })
      await mutate(connect + '/accounting/destinations', { profileId: profile.id, branchId: active.branchId, documentType: kind,
        enabled: true, automatic: true, scenario }, 'PUT')
    }
    const wallet = '/api/billing/subscribers/' + active.subscriberId + '/summary'
    const before = (await (await page.request.get(wallet)).json()).wallet.available

    await page.getByRole('link', { name: 'Sales Orders', exact: true }).click()
    await page.getByRole('link', { name: 'New Sales Order', exact: true }).click()
    await page.getByLabel('Customer', { exact: true }).selectOption(customer)
    await page.getByLabel('Item for line 1', { exact: true }).selectOption(item)
    await page.getByRole('button', { name: 'Calculate totals' }).click()
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page.getByText('ACCT-SO-000001', { exact: true }).first()).toBeVisible({ timeout: 18_000 })
    await page.getByRole('link', { name: 'Create invoice from order' }).click()
    await page.getByRole('button', { name: 'Add SERVICE from source' }).click()
    await page.getByRole('button', { name: 'Calculate totals' }).click()
    await page.getByRole('button', { name: 'Save & Send', exact: true }).click()
    await page.getByRole('button', { name: 'Confirm Save & Send' }).click()
    await expect(page.getByText('ACCT-SI-000001', { exact: true }).first()).toBeVisible({ timeout: 18_000 })
    await capture(page, info.project.name + '-synced-invoice')
    await page.getByRole('link', { name: 'Create linked Credit Memo' }).click()
    await page.getByRole('button', { name: 'Add SERVICE from source' }).click()
    await page.getByLabel('Adjustment reason').fill('Synthetic return')
    await page.getByRole('button', { name: 'Calculate totals' }).click()
    await page.getByRole('button', { name: 'Save & Send', exact: true }).click()
    await page.getByRole('button', { name: 'Confirm Save & Send' }).click()
    await page.getByRole('button', { name: 'Verify external outcome' }).click({ timeout: 18_000 })
    await expect(page.getByText('ACCT-CM-000001', { exact: true }).first()).toBeVisible({ timeout: 18_000 })
    await page.getByRole('link', { name: 'Synchronization Center', exact: true }).click()
    await page.getByLabel('Synchronization status').selectOption('Synchronized')
    await expect(page.getByRole('region', { name: 'Synchronization jobs' })).toContainText('ACCT-CM-000001')
    expect((await (await page.request.get(wallet)).json()).wallet.available).toBe(before - 2)
    await capture(page, info.project.name + '-synchronization-center')
    expect(faults).toEqual([])
  })

  test('master data import, versioned tax setup, and read-only financial preview', async ({ page }, info) => {
    const faults: string[] = []
    const cdp = await page.context().newCDPSession(page)
    await cdp.send('Log.enable')
    cdp.on('Log.entryAdded', ({ entry }) => {
      if (entry.level === 'error' && !entry.text.includes('401')) faults.push(entry.text)
    })
    page.on('pageerror', error => faults.push(error.message))
    page.on('response', response => { if (response.status() >= 500) faults.push(response.url() + ': ' + response.status()) })
    const email = 'master-data-' + suffix() + '@example.test'
    await register(page, email, randomUUID(), 'b2b')
    execFileSync('dotnet', [resolve('../backend/Embm.EFiscal.Api/bin/Release/net10.0/Embm.EFiscal.Api.dll'), '--provision-dev', email], {
      env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development' }, stdio: 'pipe',
    })
    await page.reload()
    await createCompany(page, 'MASTER', 'Synthetic Master Data Company')
    await page.getByRole('link', { name: 'Master Data', exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Customers', exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'New Customer', exact: true }).click()
    const customer = page.getByRole('region', { name: 'New Customer' })
    await customer.getByLabel('Code', { exact: true }).fill('CON-01')
    await customer.getByLabel('Name', { exact: true }).fill('Synthetic Consumer')
    await customer.getByLabel('Customer classification', { exact: true }).selectOption('Individual')
    await customer.getByRole('button', { name: 'Create record' }).click()
    await expect(page.getByText('Record created.', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Close record' }).click()

    await page.getByRole('button', { name: 'Import', exact: true }).click()
    await page.getByLabel('CSV or XLSX file (up to 2 MB)').setInputFiles({
      name: 'customers.csv', mimeType: 'text/csv', buffer: Buffer.from('code,name,classification\nIMPORT-02,Imported Consumer,Individual\n'),
    })
    await page.getByRole('button', { name: 'Validate file' }).click()
    await expect(page.getByText('1 of 1 rows passed validation.')).toBeVisible()
    await expect(page.getByRole('row', { name: /Imported Consumer/ })).toHaveCount(0)
    await page.getByRole('button', { name: 'Confirm import' }).click()
    await expect(page.getByText('1 record imported.', { exact: true })).toBeVisible()
    await page.getByLabel('Search by code or name').fill('IMPORT-02')
    await expect(page.getByRole('row', { name: /Imported Consumer/ })).toBeVisible()
    const download = page.waitForEvent('download')
    await page.getByRole('link', { name: 'Export Excel', exact: true }).click()
    expect((await download).suggestedFilename()).toBe('customers-export.xlsx')
    await capture(page, info.project.name + '-master-customers')

    async function form(kind: string, newName: string, code: string, name: string) {
      await page.getByLabel('Master data area').selectOption(kind)
      await page.getByRole('button', { name: 'New ' + newName, exact: true }).click()
      const pane = page.getByRole('region', { name: 'New ' + newName })
      await pane.getByLabel('Code', { exact: true }).fill(code)
      await pane.getByLabel('Name', { exact: true }).fill(name)
      return pane
    }
    async function save() {
      await page.getByRole('button', { name: 'Create record' }).click()
      await expect(page.getByText('Record created.', { exact: true })).toBeVisible()
    }
    await form('units', 'Unit of Measure', 'EA', 'Each')
    await save()

    const tax = await form('tax-groups', 'Tax Group', 'STD-VAT', 'Standard domestic VAT')
    await tax.getByLabel('Classification', { exact: true }).selectOption('VATable')
    await tax.getByLabel('Price treatment', { exact: true }).selectOption('Exclusive')
    await tax.getByLabel('Rounding', { exact: true }).selectOption('HalfAway')
    await tax.getByLabel('Regulatory template', { exact: true }).selectOption('PH-NIRC-STANDARD-VAT')
    await tax.getByLabel('Authorize this verified rule for the company').check()
    await tax.getByRole('button', { name: 'Add tax component' }).click()
    await tax.getByLabel('Component type').selectOption('VAT')
    await tax.getByLabel('Rate (%)').fill('12')
    await save()
    await expect(page.getByRole('row', { name: /Authorized for preview/ })).toBeVisible()
    await capture(page, info.project.name + '-master-tax')

    const item = await form('items', 'Item or Service', 'SERVICE-01', 'Synthetic Invoice Service')
    await item.getByLabel('Item type', { exact: true }).selectOption('Services')
    await item.getByLabel('Base unit', { exact: true }).selectOption({ label: 'EA · Each' })
    await item.getByLabel('Default selling price', { exact: true }).fill('125.50')
    await item.getByLabel('Default tax group', { exact: true }).selectOption({ label: 'STD-VAT · Standard domestic VAT' })
    await save()

    await page.getByLabel('Master data area').selectOption('calculator')
    await page.getByLabel('Item for line 1').selectOption({ label: 'SERVICE-01 · Synthetic Invoice Service' })
    await page.getByRole('button', { name: 'Resolve item price' }).click()
    await expect(page.getByText(/125\.5 per unit/)).toBeVisible()
    await page.getByRole('button', { name: 'Calculate preview' }).click()
    await expect(page.getByRole('heading', { name: 'Calculated totals' })).toBeVisible()
    await expect(page.getByRole('row', { name: /Synthetic Invoice Service.*15\.06.*140\.56/ })).toBeVisible()
    expect(await page.evaluate(() => [sessionStorage.length, localStorage.length])).toEqual([0, 0])
    await capture(page, info.project.name + '-master-calculation')
    expect(faults).toEqual([])
  })

  test('individual registration and password recovery', async ({ page }, info) => {
    const email = 'individual-' + suffix() + '@example.test'
    await register(page, email, randomUUID(), 'b2c')
    await page.getByRole('button', { name: 'Sign Out' }).click()
    await page.getByRole('link', { name: 'Forgot Password' }).click()
    await page.getByLabel('Email address').fill(email)
    await page.getByRole('button', { name: 'Send email' }).click()
    await expect(page.getByRole('status')).toContainText('If eligible')
    await page.goto(await emailLink(email, 'Reset your password'))
    const password = randomUUID()
    await page.getByLabel('New password', { exact: true }).fill(password)
    await page.getByLabel('Confirm new password').fill(password)
    await page.getByRole('button', { name: 'Reset password' }).click()
    await expect(page.getByRole('heading', { name: 'Password reset', exact: true })).toBeVisible()
    await signIn(page, email, password)
    await expect(page.getByText('Pending Payment', { exact: true })).toBeVisible()
    if (info.project.name === 'desktop') {
      await page.getByRole('button', { name: 'Sign Out' }).click()
      await capture(page, 'desktop-client-sign-in')
    }
  })
})
