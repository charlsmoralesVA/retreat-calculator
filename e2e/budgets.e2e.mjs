import { check, deleteTestUsers, launch, logIn, signUpAndConfirm, startApp, stopApp } from './lib.mjs'

const stamp = Date.now()
const password = 'correct-horse-9'
const users = {
  a: { email: `e2e-a-${stamp}@example.com`, budget: `A retreat ${stamp}`, headcount: '10' },
  b: { email: `e2e-b-${stamp}@example.com`, budget: `B retreat ${stamp}`, headcount: '25' },
}

async function saveBudget(page, { budget, headcount }) {
  await page.getByLabel('Attendees').fill(headcount)
  await page.getByLabel('Venue fee, fixed ($)').fill('1000')
  await page.getByLabel('Budget name').fill(budget)
  await page.getByRole('button', { name: 'Save budget' }).click()
  await page.getByText(`Saved "${budget}".`).waitFor()
}

const listedNames = (page) => page.locator('ul li strong').allTextContents()

const app = await startApp()
const browser = await launch()
try {
  const pageA = await (await browser.newContext()).newPage()
  const pageB = await (await browser.newContext()).newPage()

  for (const [page, u] of [[pageA, users.a], [pageB, users.b]]) {
    await signUpAndConfirm(page, u.email, password)
    await logIn(page, u.email, password)
  }
  check(true, 'two real accounts signed up, confirmed and logged in')

  await pageB.getByText(/no saved budgets yet/i).waitFor()
  check(true, 'a brand-new account starts with an empty list')

  await saveBudget(pageA, users.a)
  await saveBudget(pageB, users.b)
  check(true, 'both users saved a budget')

  // Persistence across reload, and isolation between accounts.
  await pageA.reload()
  await pageA.getByText(users.a.budget, { exact: true }).waitFor()
  check(JSON.stringify(await listedNames(pageA)) === JSON.stringify([users.a.budget]), 'A sees only A\'s budget after reload')

  await pageB.reload()
  await pageB.getByText(users.b.budget, { exact: true }).waitFor()
  check(JSON.stringify(await listedNames(pageB)) === JSON.stringify([users.b.budget]), 'B sees only B\'s budget after reload')

  // Opening restores the stored inputs and totals.
  await pageA.getByLabel('Attendees').fill('')
  await pageA.getByRole('button', { name: `Open ${users.a.budget}` }).click()
  check((await pageA.getByLabel('Attendees').inputValue()) === '10', 'opening a budget restores its inputs')
  check((await pageA.getByTestId('total').textContent()) === '$1,000.00', 'opening a budget restores its totals')

  // Rename, then delete with confirmation.
  const renamed = `${users.a.budget} v2`
  await pageA.getByRole('button', { name: `Rename ${users.a.budget}` }).click()
  await pageA.getByLabel(`New name for ${users.a.budget}`).fill(renamed)
  await pageA.getByRole('button', { name: 'Save name' }).click()
  await pageA.getByText(renamed, { exact: true }).waitFor()
  check(true, 'rename persists')

  await pageA.getByRole('button', { name: `Delete ${renamed}` }).click()
  await pageA.getByRole('button', { name: 'Confirm delete' }).click()
  await pageA.getByText(/no saved budgets yet/i).waitFor()
  await pageA.reload()
  await pageA.getByText(/no saved budgets yet/i).waitFor()
  check(true, 'delete persists after reload')

  // B is unaffected by A's delete.
  await pageB.reload()
  await pageB.getByText(users.b.budget, { exact: true }).waitFor()
  check(true, "B's budget is untouched by A's delete")

  // Logging out hides saved budgets.
  await pageB.getByRole('button', { name: 'Log out' }).click()
  await pageB.getByRole('button', { name: 'Log in' }).waitFor()
  check((await pageB.getByText(users.b.budget).count()) === 0, 'no saved budgets visible after logout')

  console.log('\nE2E budgets: PASS')
} finally {
  await browser.close()
  stopApp(app)
  deleteTestUsers()
}
