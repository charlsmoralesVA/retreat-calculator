import { check, deleteTestUsers, launch, logIn, signUpAndConfirm, startApp, stopApp } from './lib.mjs'

const stamp = Date.now()
const password = 'correct-horse-9'
const users = {
  a: { email: `e2e-a-${stamp}@example.com`, budget: `A retreat ${stamp}`, headcount: '10', markup: '25' },
  b: { email: `e2e-b-${stamp}@example.com`, budget: `B retreat ${stamp}`, headcount: '25', perRoom: true },
}

async function saveBudget(page, { budget, headcount, markup, perRoom }) {
  await page.getByLabel('Attendees').fill(headcount)
  if (perRoom) {
    // 25 people, 2 per room, 2 nights at 100 per room: 13 rooms, 2,600 lodging
    await page.getByLabel(/^Nights/).fill('2')
    await page.getByRole('radio', { name: 'Room' }).check()
    await page.getByLabel('Lodging per room per night ($)').fill('100')
    await page.getByLabel('People per room').fill('2')
  }
  await page.getByLabel('Venue fee, fixed ($)').fill('1000')
  if (markup) await page.getByLabel('Markup (%)').fill(markup)
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
  // 1,000 cost x 1.25 / 10 attendees = 125.00 each, quoted 1,250.00, margin 250.00 (20.0%)
  check((await pageA.getByLabel('Markup (%)').inputValue()) === '25', 'opening a budget restores its markup')
  check((await pageA.getByTestId('client-per-person').textContent()) === '$125.00', 'client per-person price survives save, reload and open')
  check((await pageA.getByTestId('client-total').textContent()) === '$1,250.00', 'quoted total survives save, reload and open')
  check((await pageA.getByTestId('margin-pct').textContent()) === '20.0%', 'margin percent survives save, reload and open')

  // A budget saved without markup opens with no client price section.
  await pageB.getByRole('button', { name: `Open ${users.b.budget}` }).click()
  check((await pageB.getByRole('heading', { name: 'Client price' }).count()) === 0, 'a budget saved without markup shows no client price')

  // Per-room lodging survives save, reload and open (25 people / 2 per room = 13 rooms).
  check(await pageB.getByRole('radio', { name: 'Room' }).isChecked(), 'opening a budget restores per-room lodging mode')
  check((await pageB.getByLabel('People per room').inputValue()) === '2', 'opening a budget restores people per room')
  check((await pageB.getByTestId('rooms-needed').textContent()) === '13', 'rooms needed is rounded up (25 people at 2 per room is 13)')
  check((await pageB.getByTestId('total').textContent()) === '$3,600.00', 'per-room totals survive save, reload and open (2,600 lodging + 1,000 venue)')

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
