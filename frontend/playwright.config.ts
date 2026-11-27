import { defineConfig } from '@playwright/test'

const preview = process.env.EFISCAL_PREVIEW === '1'
const baseURL = preview ? 'http://127.0.0.1:4173' : 'http://127.0.0.1:5173'

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  workers: 2,
  outputDir: 'TestResults/playwright',
  reporter: 'list',
  webServer: preview ? { command: 'npm run preview', url: baseURL } : undefined,
  use: { baseURL, trace: 'retain-on-failure', channel: 'msedge' },
  projects: [
    { name: 'desktop', use: { viewport: { width: 1440, height: 1000 } } },
    { name: 'tablet', use: { viewport: { width: 820, height: 1180 }, hasTouch: true } },
    { name: 'mobile', use: { viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true, deviceScaleFactor: 1 } },
  ],
})
