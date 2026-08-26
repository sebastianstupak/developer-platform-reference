# End-to-end tests

Playwright tests that drive the Web UI against a live stack.

## Prerequisites

1. Infrastructure up: `docker compose up -d` (from the repo root).
2. API running on `:5274` and Web on `:5000` (see the run recipe in the repo).
3. Signed-in user `dev@example.com` / `password` (seeded by the Keycloak realm).
4. A few audit events exist in the tenant — perform any Set/Reveal/Rotate/Create
   action once, or let the outbox relay drain existing entries.

## Install

```bash
cd e2e
npm install
```

The config points Playwright at a locally-cached Chromium
(`chromium-1155`); override with `PW_CHROME=<path-to-chrome.exe>`, or delete
`launchOptions.executablePath` from `playwright.config.js` and run
`npx playwright install chromium` to use Playwright's managed browser.

## Run

```bash
npm test              # headed by default (watch it), slowMo on
HEADLESS=true npm test # headless (CI)
```

Override the app URL with `BASE_URL=http://localhost:5000`.

## What's covered

- `audit-filters.spec.js` — the audit log filters:
  - **Action** multi-select narrows the grid to the chosen command types.
  - **Status** multi-select (Success + Failed) keeps both.
  - **Actor** search adds the actor as a removable chip and filters, then clears.
