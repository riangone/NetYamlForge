const { test, expect } = require('@playwright/test');
const { login, logout, gotoPage } = require('./helpers');

test.describe('Suzuki (営業 / Sales Rep)', () => {
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
    await login(page, 'suzuki');
  });

  test.afterEach(async ({ page }) => {
    await logout(page);
  });

  test('SalesRepDashboard loads without errors', async ({ page }) => {
    await gotoPage(page, 'SalesRepDashboard');
    await expect(page.locator('body')).not.toContainText('Exception');
    const httpErrors = pageErrors.filter(e => e.includes('[HTTP 5'));
    expect(httpErrors).toEqual([]);
  });

  test('SalesRepDashboard has task action buttons', async ({ page }) => {
    await gotoPage(page, 'SalesRepDashboard');
    const actionBtns = page.locator('a.btn, button.btn').filter({ hasText: /実行|完了|対応|確認|詳細/ });
    const count = await actionBtns.count();
    console.log(`SalesRepDashboard: ${count} action buttons`);
    expect(count).toBeGreaterThan(0);
  });

  test('Task action button does not lead to error page', async ({ page }) => {
    await gotoPage(page, 'SalesRepDashboard');
    const actionBtns = page.locator('a.btn').filter({ hasText: /実行|完了|対応/ });
    if (await actionBtns.count() === 0) {
      console.log('No task action buttons found');
      return;
    }
    const href = await actionBtns.first().getAttribute('href');
    console.log(`Clicking: ${href}`);
    await actionBtns.first().click();
    await page.waitForLoadState('networkidle');
    const body = await page.locator('body').textContent();
    expect(body).not.toContain('アクションハンドラー');
    expect(body).not.toContain('Exception');
  });

  test('SalesLeads page loads', async ({ page }) => {
    await gotoPage(page, 'SalesLeads');
    await expect(page.locator('body')).not.toContainText('Exception');
  });

  test('Customers page loads', async ({ page }) => {
    await gotoPage(page, 'Customers');
    await expect(page.locator('body')).not.toContainText('Exception');
  });

  test('Appointments page loads', async ({ page }) => {
    await gotoPage(page, 'Appointments');
    await expect(page.locator('body')).not.toContainText('Exception');
  });
});
