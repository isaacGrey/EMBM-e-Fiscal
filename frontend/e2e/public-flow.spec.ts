import { mkdir } from 'node:fs/promises'
import { join } from 'node:path'
import { expect, test, type Page } from '@playwright/test'

async function capture(page: Page, name: string) {
  await page.evaluate(() => document.fonts.ready.then(() => undefined))
  const width = page.viewportSize()!.width
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width)
  const directory = process.env.EFISCAL_SCREENSHOTS
  if (!directory) return
  await mkdir(directory, { recursive: true })
  await page.screenshot({ path: join(directory, name + '.png'), fullPage: true, animations: 'disabled' })
}

test('pricing, billing, estimation and registration form validation work together', async ({ page }, info) => {
  const faults: string[] = []
  const mutations: string[] = []
  page.on('pageerror', error => faults.push(error.message))
  page.on('requestfailed', request => faults.push(request.url() + ' ' + request.failure()?.errorText))
  page.on('response', response => {
    if (response.status() >= 400) faults.push(response.status() + ' ' + response.url())
  })
  page.on('request', request => {
    if (!['GET', 'HEAD', 'OPTIONS'].includes(request.method())) mutations.push(request.method() + ' ' + request.url())
  })
  const devTools = await page.context().newCDPSession(page)
  await devTools.send('Log.enable')
  devTools.on('Log.entryAdded', ({ entry }) => {
    if (entry.level === 'warning' || entry.level === 'error') faults.push(entry.text)
  })
  page.on('console', message => {
    if (['warning', 'error'].includes(message.type())) faults.push(message.text())
  })

  await page.goto('/')
  const openMenu = page.getByRole('button', { name: 'Open navigation' })
  const compact = await openMenu.isVisible()
  if (compact) await openMenu.click()
  await page.getByRole('navigation', { name: compact ? 'Mobile navigation' : 'Primary', exact: true })
    .getByRole('link', { name: 'Pricing', exact: true }).click()
  await expect(page).toHaveURL(/\/pricing$/)
  const starter = page.locator('.subscription-card').filter({ has: page.getByRole('heading', { name: 'Starter', exact: true }) })
  await expect(page.getByRole('radio', { name: 'Monthly' })).toBeChecked()
  await expect(starter).toContainText('₱499.00')
  await page.getByRole('radio', { name: 'Annual' }).click()
  await expect(page.getByRole('radio', { name: 'Annual' })).toBeChecked()
  await expect(starter).toContainText('₱4,990.00')
  await expect(starter).toContainText('3,000')
  const comparison = page.getByRole('table', { name: 'Subscription comparison' })
  await expect(comparison.getByRole('row', { name: /Included Credits \/ year/ })).toContainText('3,000')
  await expect(page.getByRole('heading', { name: 'Buy Credits' })).toBeVisible()
  await capture(page, info.project.name + '-pricing')

  const estimate = page.locator('.phase2-estimator')
  await page.getByRole('link', { name: 'Estimate credits', exact: true }).click()
  await expect(page).toHaveURL(/\/pricing\?billing=annual#estimator$/)
  await expect(estimate.locator('.estimate-price')).toHaveText('Starter')
  const rates = page.locator('.credit-rates')
  for (const [name, action, credits] of [
    ['Sales Invoice', 'Save & Send', '1 credit'],
    ['Credit Memo', 'Save & Send', '1 credit'],
    ['Sales Order', 'Save', '0 credits'],
  ]) {
    const row = rates.locator('div').filter({ hasText: name })
    await expect(row.getByText(action, { exact: true })).toBeVisible()
    await expect(row.getByText(credits, { exact: true })).toBeVisible()
  }
  await expect(estimate.locator('dd')).toHaveText(['192', '2,304', '0'])
  await estimate.getByLabel('Monthly Sales Invoices').fill('238')
  await expect(estimate.locator('dd')).toHaveText(['250', '3,000', '0'])
  await estimate.getByLabel('Monthly Sales Orders').fill('100000')
  await expect(estimate.locator('dd')).toHaveText(['250', '3,000', '0'])
  await expect(estimate.locator('.estimate-price')).toHaveText('Starter')
  await estimate.getByLabel('Monthly Credit Memos').fill('13')
  await expect(estimate.locator('dd')).toHaveText(['251', '3,012', '0'])
  await expect(estimate.locator('.estimate-price')).toHaveText('Business')
  await estimate.getByLabel('Monthly Sales Invoices').fill('500')
  await estimate.getByLabel('Monthly Credit Memos').fill('25')
  await estimate.getByLabel('Monthly Sales Orders').fill('20')
  await estimate.getByLabel('Company files').fill('2')
  await estimate.getByLabel('Authorized users').fill('7')
  await expect(estimate.locator('.estimate-price')).toHaveText('Business')
  await expect(estimate.locator('dd')).toHaveText(['525', '6,300', '0'])
  await estimate.getByLabel('Monthly Sales Invoices').fill('11000')
  await expect(estimate.locator('.estimate-price')).toHaveText('Enterprise')
  await expect(estimate.locator('dd').last()).toHaveText('12,300')
  await estimate.getByLabel('Authorized users').fill('51')
  await expect(estimate.locator('.estimate-price')).toHaveText('No suitable plan')
  await expect(estimate.getByRole('link', { name: /Select/ })).toHaveCount(0)
  await estimate.getByLabel('Authorized users').fill('0')
  await expect(estimate.getByRole('alert')).toContainText('Enter a whole number from 1')
  await estimate.getByLabel('Authorized users').fill('7')
  await estimate.getByLabel('Monthly Sales Invoices').fill('500')
  await expect(estimate.getByRole('link', { name: 'Select Business' })).toHaveAttribute('href', '/get-started?plan=business&billing=annual')
  await page.getByRole('link', { name: 'Select Plan: Business', exact: true }).click()
  await expect(page).toHaveURL(/\/get-started\?plan=business&billing=annual$/)
  const summary = page.locator('.registration-summary')
  await expect(summary).toContainText('₱14,990.00')
  await expect(summary).toContainText('12,000')
  await expect(summary.getByText('Included Credits', { exact: true })).toBeVisible()
  await expect(page.getByLabel(/RDO|TIN/)).toHaveCount(0)
  await capture(page, info.project.name + '-registration')

  await page.getByRole('button', { name: 'Create Account' }).click()
  await expect(page.getByText('Enter a full name.', { exact: true })).toBeVisible()
  await expect(page.getByText('Enter the business name.', { exact: true })).toBeVisible()
  await expect(page.getByText('Acknowledge the terms.', { exact: true })).toBeVisible()
  await expect(page.getByText('Acknowledge the privacy notice.', { exact: true })).toBeVisible()
  await page.getByLabel('Subscriber classification').selectOption('b2c')
  await expect(page.getByLabel('Business name')).toHaveCount(0)
  await page.getByRole('button', { name: 'Show password', exact: true }).click()
  await expect(page.getByLabel('Password', { exact: true })).toHaveAttribute('type', 'text')
  await page.getByRole('button', { name: 'Read development terms and privacy notice' }).click()
  await expect(page.getByRole('dialog', { name: 'Development terms and privacy' })).toBeVisible()
  await page.keyboard.press('Escape')
  expect(mutations).toEqual([])
  await page.getByRole('link', { name: 'Back to Pricing' }).click()
  await expect(page.getByRole('radio', { name: 'Annual' })).toBeChecked()
  await page.getByRole('radio', { name: 'Monthly' }).click()
  await expect(page.getByRole('radio', { name: 'Monthly' })).toBeChecked()
  await expect(starter).toContainText('₱499.00')
  expect(faults).toEqual([])
})

test('catalog, resources, support and public pages are reachable', async ({ page }) => {
  await page.goto('/')
  const footer = page.getByRole('navigation', { name: 'Footer', exact: true })
  await footer.getByRole('link', { name: 'Integrations', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Integration catalog', exact: true })).toBeVisible()
  for (const name of ['AutoCount Accounting', 'QuickBooks Desktop', 'QuickBooks Online', 'AccountingSuite / 1C', 'Shopee', 'TikTok Shop']) {
    await expect(page.getByRole('heading', { name, exact: true })).toBeVisible()
  }
  await expect(page.getByText('Planned', { exact: true })).toHaveCount(6)
  await footer.getByRole('link', { name: 'Features', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'e-Invoicing Features', exact: true })).toBeVisible()
  await page.getByRole('link', { name: 'See the invoicing workflow' }).click()
  await expect(page.getByRole('heading', { name: 'How it works', exact: true })).toBeVisible()
  await footer.getByRole('link', { name: 'Resources', exact: true }).click()
  await page.getByRole('link', { name: 'Read the FAQ' }).click()
  await page.getByText('Does a Sales Order count as an electronic invoice?', { exact: true }).click()
  await expect(page.getByText('Save commits the order with zero credit use.', { exact: false })).toBeVisible()
  await page.getByRole('link', { name: 'Go to Contact & Support' }).click()
  await page.getByLabel('Topic').selectOption('Connectors')
  await page.getByLabel('Question or issue').fill('Is AutoCount available in this preview?')
  await page.getByRole('button', { name: 'Prepare request' }).click()
  await expect(page.getByLabel('Prepared request')).toHaveValue(/Connectors[\s\S]+Is AutoCount available/)
})
