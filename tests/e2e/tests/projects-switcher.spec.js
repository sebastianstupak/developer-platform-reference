const { test, expect } = require('@playwright/test');

// These run against a live stack: docker compose up -d, then the API (:5274) and Web (:5000).
// Mirrors the exact Keycloak login sequence used by audit-filters.spec.js.

async function login(page) {
  await page.goto('/login');
  await page.waitForSelector('#username', { timeout: 30_000 });
  await page.fill('#username', 'dev@example.com');
  await page.fill('#password', 'password');
  await page.click('#kc-login');
  await page.waitForURL('**/', { timeout: 30_000 });
}

test.describe('Projects & environment switcher', () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
    await page.goto('/projects');
  });

  test('projects render as cards', async ({ page }) => {
    // Don't assume specific seeded data — just that at least one project card renders,
    // with its environment-count caption.
    await expect(page.locator('.project-card').first()).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText(/environment/).first()).toBeVisible();
  });

  test('card click opens the overview, and an environment card navigates to secrets', async ({ page }) => {
    await expect(page.locator('.project-card').first()).toBeVisible({ timeout: 30_000 });
    await page.locator('.project-card').first().click();
    await page.waitForURL('**/projects/**');

    // Overview shows the "Recent activity" section.
    await expect(page.getByText('Recent activity', { exact: true })).toBeVisible();

    // The app-bar project combobox trigger reflects the active project (not the placeholder).
    const switcherTrigger = page.locator('.dp-combobox__trigger').first();
    await expect(switcherTrigger).toBeVisible();
    await expect(switcherTrigger).not.toContainText('Select project');

    // Real environment cards carry the .env-card class; the dashed "New environment"
    // placeholder is .env-card--new, so this locator excludes it.
    const envCard = page.locator('.env-card').first();
    await expect(envCard).toBeVisible({ timeout: 30_000 });
    await envCard.click();

    // Lands on the nested secrets route for that environment.
    await page.waitForURL('**/environments/**');
    await expect(page.getByText(/· secrets/)).toBeVisible();
  });

  test('the app-bar combobox searches and switches projects', async ({ page }) => {
    // Open the project combobox → search box + option list appear (the open one).
    await page.locator('.dp-combobox__trigger').first().click();
    const popover = page.locator('.dp-combobox-popover.mud-popover-open');
    await expect(popover).toBeVisible();
    await expect(popover.locator('.dp-command__search')).toBeFocused();

    // Type to filter, then pick the match; navigates and the trigger updates.
    await popover.locator('.dp-command__search').fill('payments');
    await popover.locator('.dp-command__item', { hasText: 'payments-api' }).first().click();
    await page.waitForURL('**/projects/**');
    await expect(page.locator('.dp-combobox__trigger').first()).toContainText('payments-api');
  });
});

test.describe('Mobile context dialog', () => {
  test.use({ viewport: { width: 390, height: 850 } });

  test('the acronym button opens a dialog to switch project', async ({ page }) => {
    await login(page);
    await page.goto('/projects');

    // The compact button replaces the inline comboboxes on phones.
    await page.locator('.dp-ctxbtn').click();
    await expect(page.locator('.dp-ctxdlg')).toBeVisible();

    // Pick a project from the dialog → navigates and the button reflects it.
    await page.locator('.dp-ctxdlg .dp-command__item', { hasText: 'payments-api' }).first().click();
    await page.waitForURL('**/projects/**');
    await expect(page.locator('.dp-ctxbtn')).toContainText('payments-api');
  });
});
