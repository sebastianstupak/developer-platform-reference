const { test, expect } = require('@playwright/test');

// These run against a live stack: docker compose up -d, then the API (:5274) and Web (:5000).
// Mirrors the exact Keycloak login sequence used by the sibling specs.

async function login(page) {
  await page.goto('/login');
  await page.waitForSelector('#username', { timeout: 30_000 });
  await page.fill('#username', 'dev@example.com');
  await page.fill('#password', 'password');
  await page.click('#kc-login');
  await page.waitForURL('**/', { timeout: 30_000 });
}

test('secret history: reveal a prior version and roll back', async ({ page }) => {
  test.setTimeout(60_000);

  await login(page);
  await page.goto('/projects');

  // Navigate into the first project, then its first real environment card
  // (excludes the dashed "New environment" placeholder, same as projects-switcher.spec.js).
  await expect(page.locator('.project-card').first()).toBeVisible({ timeout: 30_000 });
  await page.locator('.project-card').first().click();
  await page.waitForURL('**/projects/**');

  const envCard = page.locator('.env-card').first();
  await expect(envCard).toBeVisible({ timeout: 30_000 });
  await envCard.click();
  await page.waitForURL('**/environments/**');
  await expect(page.getByText(/· secrets/)).toBeVisible();

  const name = `E2E_HIST_${Date.now()}`;

  // Create the secret — this is v1.
  await page.getByRole('button', { name: 'Add secret' }).click();
  await page.getByLabel('Name', { exact: true }).fill(name);
  await page.getByLabel('Value', { exact: true }).fill('first-value');
  await page.getByRole('button', { name: 'Save' }).click();

  const row = page.getByRole('row', { name: new RegExp(name) });
  await expect(row).toBeVisible({ timeout: 30_000 });

  // Update the secret — this appends v2 as the new current version.
  await row.getByRole('button', { name: 'Edit secret' }).click();
  await page.getByLabel('Value', { exact: true }).fill('second-value');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(row).toBeVisible({ timeout: 30_000 });

  // Open the history dialog for that secret. Scope all assertions/clicks to that
  // dialog so the row's "Reveal secret" action button (behind the dialog scrim)
  // can't be matched by the "Reveal" text.
  await row.getByRole('button', { name: 'Version history' }).click();
  const history = page.locator('.mud-dialog').filter({ hasText: 'History' });
  await expect(history.getByText('v2', { exact: true })).toBeVisible();
  await expect(history.getByText('current', { exact: true })).toBeVisible();

  // Reveal v1 — versions are listed newest-first, so it's the last exact-"Reveal" button.
  await history.getByRole('button', { name: 'Reveal', exact: true }).last().click();
  const reveal = page.locator('.mud-dialog').filter({ hasText: 'Secret value' });
  await expect(reveal).toBeVisible();
  await reveal.getByRole('button', { name: 'Close' }).click();

  // Roll back to v1 (only the non-current entry has a "Roll back" button), then confirm.
  await history.getByRole('button', { name: 'Roll back' }).click();
  await page.locator('.mud-dialog').filter({ hasText: 'Roll back secret' })
    .getByRole('button', { name: 'Roll back' }).click();

  // A new current version (v3) appears, recorded as rolled back from v1.
  await expect(history.getByText('v3', { exact: true })).toBeVisible();
  await expect(history.getByText('rolled back from v1')).toBeVisible();
});
