const { defineConfig } = require('@playwright/test');

// Use a locally-cached full Chromium build so no browser download is required.
// Override with PW_CHROME if your cache differs, or delete this to use Playwright's
// managed browser (then run `npx playwright install chromium`).
const CHROME =
  process.env.PW_CHROME ||
  'C:\\Users\\sebas\\AppData\\Local\\ms-playwright\\chromium-1155\\chrome-win\\chrome.exe';

module.exports = defineConfig({
  testDir: './tests',
  timeout: 60_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  reporter: [['list']],
  use: {
    baseURL: process.env.BASE_URL || 'http://localhost:5000',
    headless: process.env.HEADLESS === 'true',
    viewport: { width: 1280, height: 860 },
    launchOptions: { executablePath: CHROME, slowMo: 200 },
    trace: 'retain-on-failure',
  },
});
