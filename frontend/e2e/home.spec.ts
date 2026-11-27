import { mkdir } from 'node:fs/promises'
import { join } from 'node:path'
import { expect, test } from '@playwright/test'

test('homepage renders, and section navigation and FAQs work', async ({ page }, testInfo) => {
  const faults: string[] = []
  page.on('pageerror', error => faults.push(error.message))
  page.on('console', message => {
    if (message.type() === 'error' || message.type() === 'warning') faults.push(message.text())
  })
  page.on('requestfailed', request => faults.push(request.url() + ' ' + request.failure()?.errorText))
  page.on('response', response => {
    if (response.status() >= 400) faults.push(response.status() + ' ' + response.url())
  })

  const devTools = await page.context().newCDPSession(page)
  await devTools.send('Performance.enable')
  await devTools.send('Log.enable')
  devTools.on('Log.entryAdded', ({ entry }) => {
    if (entry.level === 'error' || entry.level === 'warning') faults.push(entry.text)
  })

  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Electronic Invoicing, Built for the Philippines.' })).toBeVisible()
  await expect(page.getByText('Sample data', { exact: true })).toBeVisible()
  await page.evaluate(() => document.fonts.ready.then(() => undefined))
  await expect(page.locator('.workspace-chart .recharts-line-curve')).toBeVisible()
  await expect(page.locator('.workspace-chart').getByText('Mon', { exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Save & Send credit estimator' })).toBeVisible()
  const metrics = Object.fromEntries((await devTools.send('Performance.getMetrics')).metrics.map(metric => [metric.name, metric.value]))
  console.log(testInfo.project.name, 'browser script duration (s):', metrics.ScriptDuration, 'layout duration (s):', metrics.LayoutDuration)

  const viewportWidth = page.viewportSize()!.width
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(viewportWidth)
  const screenshots = process.env.EFISCAL_SCREENSHOTS
  if (screenshots) {
    await mkdir(screenshots, { recursive: true })
    const name = testInfo.project.name
    await page.screenshot({ path: join(screenshots, name + '.png'), animations: 'disabled' })
    await page.screenshot({ path: join(screenshots, name + '-full.png'), fullPage: true, animations: 'disabled' })
  }

  await page.getByRole('link', { name: 'Explore e-Invoicing' }).click()
  await expect(page).toHaveURL(/#product$/)
  const toggle = page.getByRole('button', { name: 'Open navigation' })
  const compact = await toggle.isVisible()
  if (compact) await toggle.click()
  const nav = page.getByRole('navigation', { name: compact ? 'Mobile navigation' : 'Primary', exact: true })
  await nav.getByRole('link', { name: 'Pricing' }).click()
  await expect(page).toHaveURL(/\/pricing$/)
  if (compact) await expect(nav).toBeHidden()
  await page.goBack()
  await expect(page).toHaveURL(/#product$/)

  await page.getByText('Can I edit an invoice after Save & Send?', { exact: true }).click()
  await expect(page.getByText('Committed electronic invoices are permanent.', { exact: false })).toBeVisible()
  await page.getByText('Does a Sales Order count as an electronic invoice?', { exact: true }).click()
  await expect(page.getByText('Save commits the order with zero credit use.', { exact: false })).toBeVisible()
  await expect(page.locator('#product').getByRole('heading', { level: 3 })).toHaveText([
    'Electronic Invoicing', 'Philippine Tax Configuration', 'Accounting Integration',
    'Electronic Invoice Reconciliation', 'BIR Reporting Readiness', 'Secure Invoice Records',
  ])
  await expect(page.getByText('External accounting system', { exact: true })).toHaveCount(4)
  await expect(page.getByText('Source orders for e-Invoicing', { exact: true })).toHaveCount(2)
  await expect(page.getByText('Planned', { exact: true })).toHaveCount(6)
  await expect(page.locator('#compliance').getByText('Live BIR transmission is disabled pending formal authorization.')).toBeVisible()
  expect(faults).toEqual([])
})

test('onboarding leads to the sample client portal and the real health endpoint', async ({ page }) => {
  await page.goto('/')
  await page.locator('.hero').getByRole('link', { name: 'Get Started' }).click()
  await expect(page.getByRole('heading', { name: 'Get started with EMBM eFiscal' })).toBeVisible()
  await page.getByRole('link', { name: 'Explore sample workspace' }).click()
  await expect(page.getByText('Local API healthy')).toBeVisible()
  await page.getByRole('button', { name: 'About this preview' }).click()
  await expect(page.getByRole('dialog', { name: 'About this preview' })).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toBeHidden()
  await page.getByRole('link', { name: 'Sign In' }).click()
  await expect(page.getByRole('heading', { name: 'Client Business Portal' })).toBeVisible()
  await expect(page.getByLabel(/RDO/)).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Sign In' })).toBeEnabled()
  await page.getByRole('link', { name: 'Forgot Password' }).click()
  await expect(page.getByRole('heading', { name: 'Forgot Password' })).toBeVisible()
})

test('privileged previews expose no records or usable account sign-in', async ({ page }) => {
  await page.goto('/portal/vendor')
  await expect(page.getByRole('table')).toHaveCount(0)
  await page.getByRole('link', { name: 'Sign In', exact: true }).click()
  await expect(page.getByLabel(/RDO/)).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Sign In' })).toBeEnabled()

  await expect(page.getByLabel('Administrator email')).toBeVisible()

  await page.goto('/portal/bir')
  await expect(page.getByRole('heading', { name: 'Access unavailable' })).toBeVisible()
  await page.getByRole('link', { name: 'Sign In' }).click()
  await expect(page.getByLabel('Authorized government email or user ID')).toBeVisible()
  await page.getByLabel('Requested RDO context (optional)').fill('039')
  await expect(page.getByRole('button', { name: 'Sign In' })).toBeDisabled()
  await expect(page.getByText('An RDO request never grants access.', { exact: false })).toBeVisible()
})
