const { test, expect } = require('@playwright/test');
const { login, logout, gotoPage } = require('./helpers');

test.describe('Takahashi (オペレーター / Operator)', () => {
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
    await login(page, 'takahashi');
  });

  test.afterEach(async ({ page }) => {
    await logout(page);
  });

  test('OperatorConsole loads without errors', async ({ page }) => {
    await gotoPage(page, 'OperatorConsole');
    await expect(page.locator('body')).not.toContainText('Exception');
    const httpErrors = pageErrors.filter(e => e.includes('[HTTP 5'));
    expect(httpErrors).toEqual([]);
  });

  test('OperatorConsole has action buttons', async ({ page }) => {
    await gotoPage(page, 'OperatorConsole');
    const actionBtns = page.locator('a.btn, button.btn').filter({ hasText: /対応開始|確認|詳細|割り当て/ });
    const count = await actionBtns.count();
    console.log(`OperatorConsole: ${count} action buttons`);
    expect(count).toBeGreaterThan(0);
  });

  test('対応開始 navigates without error', async ({ page }) => {
    await gotoPage(page, 'OperatorConsole');
    const btn = page.locator('a.btn, button.btn').filter({ hasText: /対応開始/ }).first();
    if (await btn.count() === 0) {
      console.log('No 対応開始 button found');
      return;
    }
    const href = await btn.getAttribute('href');
    console.log(`対応開始 href: ${href}`);
    await btn.click();
    await page.waitForLoadState('networkidle');
    const body = await page.locator('body').textContent();
    expect(body).not.toContain('アクションハンドラー');
    expect(body).not.toContain('Exception');
  });

  test('OperatorChat page loads', async ({ page }) => {
    await gotoPage(page, 'OperatorChat');
    await expect(page.locator('body')).not.toContainText('Exception');
  });

  test('ServiceRequests page loads', async ({ page }) => {
    await gotoPage(page, 'ServiceRequests');
    await expect(page.locator('body')).not.toContainText('Exception');
  });
});
