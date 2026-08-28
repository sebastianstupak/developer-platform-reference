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

  // Open the history dialog for that secret.
  await row.getByRole('button', { name: 'Version history' }).click();
  await expect(page.getByText('v2', { exact: true })).toBeVisible();
  await expect(page.getByText('current', { exact: true })).toBeVisible();

  // Reveal v1 — versions are listed newest-first, so it's the second (last) "Reveal" button.
  await page.getByRole('button', { name: 'Reveal' }).last().click();
  await expect(page.getByText(/Secret value/)).toBeVisible();
  await page.getByRole('button', { name: 'Close' }).last().click();

  // Roll back to v1 (only the non-current entry has a "Roll back" button at this point).
  await page.getByRole('button', { name: 'Roll back' }).click();
  await page.getByRole('button', { name: 'Roll back' }).last().click(); // confirm dialog

  // A new current version (v3) appears, recorded as rolled back from v1.
  await expect(page.getByText('v3', { exact: true })).toBeVisible();
  await expect(page.getByText('rolled back from v1')).toBeVisible();
});
