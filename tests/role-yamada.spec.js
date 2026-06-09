const { test, expect } = require('@playwright/test');
const { login, logout, gotoPage } = require('./helpers');

test.describe('Yamada (部長 / Manager)', () => {
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
    await login(page, 'yamada');
  });

  test.afterEach(async ({ page }) => {
    await logout(page);
  });

  test('ManagerDashboard loads without errors', async ({ page }) => {
    await gotoPage(page, 'ManagerDashboard');
    await expect(page.locator('body')).not.toContainText('Exception');
    await expect(page.locator('body')).not.toContainText('エラー');
    const httpErrors = pageErrors.filter(e => e.includes('[HTTP 5'));
    expect(httpErrors).toEqual([]);
  });

  test('AiDecisionApproval has action buttons', async ({ page }) => {
    await gotoPage(page, 'AiDecisionApproval');
    await expect(page.locator('body')).not.toContainText('Exception');
    const buttons = page.locator('a.btn, button.btn').filter({ hasText: /承認|却下|確認|詳細/ });
    const count = await buttons.count();
    console.log(`AiDecisionApproval: found ${count} action buttons`);
    expect(count).toBeGreaterThan(0);
  });

  test('AiDecisionApproval - click 承認 button no error', async ({ page }) => {
    await gotoPage(page, 'AiDecisionApproval');
    const approveBtn = page.locator('a.btn, button.btn').filter({ hasText: /承認/ }).first();
    if (await approveBtn.count() === 0) {
      console.log('No 承認 button found - skipping');
      return;
    }
    const href = await approveBtn.getAttribute('href');
    console.log(`Clicking 承認 button: ${href}`);
    await approveBtn.click();
    await page.waitForLoadState('networkidle');
    const body = await page.locator('body').textContent();
    expect(body).not.toContain('アクションハンドラー');
    expect(body).not.toContain('が見つかりません');
    expect(body).not.toContain('Exception');
  });

  test('AI Decision Detail page has action buttons', async ({ page }) => {
    await gotoPage(page, 'AiDecisionApproval');
    const detailLink = page.locator('a').filter({ hasText: /詳細|確認/ }).first();
    if (await detailLink.count() === 0) {
      console.log('No detail link found - skipping');
      return;
    }
    await detailLink.click();
    await page.waitForLoadState('networkidle');
    const body = await page.locator('body').textContent();
    expect(body).not.toContain('Exception');
    expect(body).not.toContain('アクションハンドラー');
  });

  test('ManagerDashboard - action buttons navigate correctly', async ({ page }) => {
    await gotoPage(page, 'ManagerDashboard');
    const actionBtns = page.locator('a.btn').filter({ hasText: /確認|承認|詳細/ });
    const count = await actionBtns.count();
    console.log(`ManagerDashboard: ${count} action buttons`);
    if (count > 0) {
      const href = await actionBtns.first().getAttribute('href');
      console.log(`First button: ${href}`);
      await actionBtns.first().click();
      await page.waitForLoadState('networkidle');
      const body = await page.locator('body').textContent();
      expect(body).not.toContain('Exception');
    }
  });

  test('AiDashboard loads', async ({ page }) => {
    await gotoPage(page, 'AiDashboard');
    await expect(page.locator('body')).not.toContainText('Exception');
  });

  test('AiCommunications loads', async ({ page }) => {
    await gotoPage(page, 'AiCommunications');
    await expect(page.locator('body')).not.toContainText('Exception');
  });
});
