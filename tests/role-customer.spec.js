const { test, expect } = require('@playwright/test');
const { login, logout, gotoPage } = require('./helpers');

test.describe('Customer1 (顧客 / Customer)', () => {
  let pageErrors = [];

  test.beforeEach(async ({ page }) => {
    pageErrors = [];
    page.on('console', msg => {
      if (msg.type() === 'error') pageErrors.push(`[console.error] ${msg.text()}`);
    });
    page.on('pageerror', err => pageErrors.push(`[pageerror] ${err.message}`));
    page.on('response', res => {
      if (res.status() >= 400 && !res.url().includes('favicon'))
        pageErrors.push(`[HTTP ${res.status()}] ${res.url()}`);
    });
    await login(page, 'customer1');
  });

  test.afterEach(async ({ page }) => {
    await logout(page);
  });

  test('CustomerDashboard loads without errors', async ({ page }) => {
    await gotoPage(page, 'CustomerDashboard');
    await expect(page.locator('body')).not.toContainText('Exception');
    const httpErrors = pageErrors.filter(e => e.includes('[HTTP 5'));
    expect(httpErrors).toEqual([]);
  });

  test('CustomerDashboard has action buttons', async ({ page }) => {
    await gotoPage(page, 'CustomerDashboard');
    const actionBtns = page.locator('a.btn, button.btn').filter({ hasText: /予約|確認|変更|詳細|メッセージ/ });
    const count = await actionBtns.count();
    console.log(`CustomerDashboard: ${count} action buttons`);
    expect(count).toBeGreaterThan(0);
  });

  test('Appointment button does not error', async ({ page }) => {
    await gotoPage(page, 'CustomerDashboard');
    const btn = page.locator('a.btn').filter({ hasText: /予約|確認|変更/ }).first();
    if (await btn.count() === 0) {
      console.log('No appointment button found');
      return;
    }
    const href = await btn.getAttribute('href');
    console.log(`Appointment button: ${href}`);
    await btn.click();
    await page.waitForLoadState('networkidle');
    const body = await page.locator('body').textContent();
    expect(body).not.toContain('Exception');
    expect(body).not.toContain('アクションハンドラー');
  });

  test('CustomerVehicles page loads', async ({ page }) => {
    await gotoPage(page, 'CustomerVehicles');
    await expect(page.locator('body')).not.toContainText('Exception');
  });

  test('PublicVehicles page loads', async ({ page }) => {
    await gotoPage(page, 'PublicVehicles');
    await expect(page.locator('body')).not.toContainText('Exception');
  });
});
