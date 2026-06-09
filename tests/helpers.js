const BASE_URL = 'http://localhost:5001';
const PATH_PREFIX = '/nyf';
const PROJECT = 'auto-dealer-demo';

const USERS = {
  yamada:    { username: 'yamada',    password: 'Demo@123', role: '部長 (Manager)' },
  suzuki:    { username: 'suzuki',    password: 'Demo@123', role: '営業 (Sales Rep)' },
  takahashi: { username: 'takahashi', password: 'Demo@123', role: 'オペレーター (Operator)' },
  customer1: { username: 'customer1', password: 'Demo@123', role: '顧客 (Customer)' },
};

function projectUrl(path) {
  return `${PATH_PREFIX}/${PROJECT}${path}`;
}

async function login(page, username, password = 'Demo@123') {
  const loginUrl = projectUrl('/Account/Login');
  await page.goto(loginUrl);
  await page.waitForLoadState('domcontentloaded');
  await page.fill('input[name="UserName"]', username);
  await page.fill('input[name="Password"]', password);
  // Use text-based selector to avoid clicking the language-switcher submit buttons
  await page.click('button.btn-primary:has-text("Login")');
  await page.waitForURL(url => !url.toString().includes('/Account/Login'), { timeout: 20000 });
}

async function logout(page) {
  try {
    await page.goto(projectUrl('/Account/Logout'));
  } catch (e) {
    // ignore
  }
}

async function gotoPage(page, pageName) {
  await page.goto(projectUrl(`/Page/${pageName}`));
  await page.waitForLoadState('networkidle');
}

module.exports = { BASE_URL, PATH_PREFIX, PROJECT, USERS, login, logout, gotoPage, projectUrl };
