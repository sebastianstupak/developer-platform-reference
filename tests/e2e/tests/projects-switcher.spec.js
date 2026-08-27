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

    // The app-bar project switcher reflects the active project (non-empty input value).
    const switcherInput = page.locator('.context-switcher-project input');
    await expect(switcherInput).toBeVisible();
    await expect(switcherInput).not.toHaveValue('');

    // Environment cards render an EnvTypeChip (Development/Staging/Production); the
    // dashed "New environment" placeholder tile doesn't, so this locator excludes it.
    const envCard = page.locator('.pa-4:has(.mud-chip)').first();
    await expect(envCard).toBeVisible({ timeout: 30_000 });
    await envCard.click();

    // Lands on the nested secrets route for that environment.
    await page.waitForURL('**/environments/**');
    await expect(page.getByText(/· secrets/)).toBeVisible();
  });
});
