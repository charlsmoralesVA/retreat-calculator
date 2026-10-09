import { APP, check, deleteTestUsers, launch, logIn, signUpAndConfirm, startApp, stopApp } from './lib.mjs'

const email = `e2e-auth-${Date.now()}@example.com`
const password = 'correct-horse-9'

const app = await startApp()
const browser = await launch()
try {
  const page = await (await browser.newContext()).newPage()

  await signUpAndConfirm(page, email, password)
  check(page.url().startsWith(APP), 'confirmation link returns to the running app')

  await logIn(page, email, password)
  check(await page.getByRole('button', { name: 'Log out' }).isVisible(), 'login succeeds after confirmation')

  await page.reload()
  await page.getByText(email).waitFor()
  check(true, 'session survives a page reload')

  await page.getByRole('button', { name: 'Log out' }).click()
  await page.getByRole('button', { name: 'Log in' }).waitFor()
  check(true, 'logout returns to the signed-out view')

  await page.reload()
  await page.getByRole('button', { name: 'Log in' }).waitFor()
  check(true, 'still signed out after reload')
  console.log('\nE2E auth: PASS')
} finally {
  await browser.close()
  stopApp(app)
  deleteTestUsers()
}
