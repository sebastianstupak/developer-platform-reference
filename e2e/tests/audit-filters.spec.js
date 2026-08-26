const { test, expect } = require('@playwright/test');

// These run against a live stack: docker compose up -d, then the API (:5274) and Web (:5000).
// The tenant needs a few audit events (any Set/Reveal/Rotate/Create action generates them).

async function login(page) {
  await page.goto('/login');
  await page.waitForSelector('#username', { timeout: 30_000 });
  await page.fill('#username', 'dev@example.com');
  await page.fill('#password', 'password');
  await page.click('#kc-login');
  await page.waitForURL('**/', { timeout: 30_000 });
}

// The grid columns are: Time(1) · Actor(2) · Action(3) · Status(4) · Scope(5) · IP(6).
const ACTION_CELL = 'tbody tr td:nth-child(3)';
const STATUS_CELL = 'tbody tr td:nth-child(4)';

test.beforeEach(async ({ page }) => {
  await login(page);
  await page.goto('/audit');
  await expect(page.locator('tbody tr').first()).toBeVisible({ timeout: 30_000 });
});

test('Action multi-select narrows the grid to the chosen command types', async ({ page }) => {
  const wanted = ['RevealSecretCommand', 'SetSecretCommand'];

  await page.getByLabel('Action', { exact: true }).click();
  for (const w of wanted) {
    await page.locator('.mud-list-item', { hasText: w }).click();
  }
  await page.keyboard.press('Escape');

  // Every visible Action cell must be one of the two selected — and there must be at least one.
  await expect(page.locator(ACTION_CELL).first()).toBeVisible();
  await expect
    .poll(async () => {
      const cells = await page.locator(ACTION_CELL).allInnerTexts();
      return cells.length > 0 && cells.every((c) => wanted.includes(c.trim()));
    })
    .toBe(true);
});

test('Status multi-select with both values keeps successes and failures', async ({ page }) => {
  await page.getByLabel('Status', { exact: true }).click();
  await page.locator('.mud-list-item', { hasText: 'Success' }).click();
  await page.locator('.mud-list-item', { hasText: 'Failed' }).click();
  await page.keyboard.press('Escape');

  await expect
    .poll(async () => {
      const cells = await page.locator(STATUS_CELL).allInnerTexts();
      return cells.length > 0 && cells.every((c) => ['Success', 'Failed'].includes(c.trim()));
    })
    .toBe(true);
});

test('Actor search selects an actor as a removable chip', async ({ page }) => {
  const actor = page.getByLabel('Actor', { exact: true });
  await actor.click();
  await actor.fill('unknown'); // matches the seeded member email (…@unknown)

  await page.locator('.mud-list-item').first().click();

  // A removable chip appears in the filter bar (scoped to the filter paper so the
  // grid's Status chips don't count) and the grid reloads.
  const filterChips = page.locator('.pa-4 .mud-chip');
  await expect(filterChips).toHaveCount(1);
  await expect(page.locator('tbody tr').first()).toBeVisible();

  // Removing the chip clears the actor filter.
  await filterChips.first().locator('button').first().click();
  await expect(filterChips).toHaveCount(0);
});
