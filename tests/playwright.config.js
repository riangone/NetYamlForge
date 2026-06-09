const { defineConfig } = require('@playwright/test');

module.exports = defineConfig({
  testDir: '.',
  timeout: 30000,
  retries: 0,
  reporter: [
    ['list'],
    ['json', { outputFile: 'test-results/results.json' }],
    ['html', { outputFolder: '/tmp/playwright-html-report', open: 'never' }]
  ],
  use: {
    baseURL: 'http://localhost:5001/nyf',
    headless: true,
    screenshot: 'on',
    video: 'off',
    trace: 'off',
    ignoreHTTPSErrors: true,
  },
  projects: [
    {
      name: 'chromium',
      use: { browserName: 'chromium' },
    }
  ],
});
