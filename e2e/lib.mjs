// Shared helpers for end-to-end checks against the real local Supabase stack.
// Requires: `supabase start` running, .env.local present, Google Chrome installed.
import { spawn, execFileSync } from 'node:child_process'
import { chromium } from 'playwright-core'

export const APP = 'http://127.0.0.1:5174'
export const MAILPIT = 'http://127.0.0.1:54324'
const DB_CONTAINER = 'supabase_db_retreat-budget-calculator'

export async function startApp() {
  const proc = spawn('npm', ['run', 'dev'], { stdio: 'ignore' })
  for (let i = 0; i < 50; i++) {
    try {
      if ((await fetch(APP)).ok) return proc
    } catch {}
    await new Promise((r) => setTimeout(r, 200))
  }
  proc.kill()
  throw new Error(`App did not start at ${APP} (is port 5174 free?)`)
}

export const stopApp = (proc) => {
  proc.kill()
  try {
    execFileSync('pkill', ['-f', 'vite --port 5174'])
  } catch {}
}

export const launch = () => chromium.launch({ channel: 'chrome', headless: true })

export function check(cond, msg) {
  if (!cond) throw new Error(`FAIL: ${msg}`)
  console.log(`ok - ${msg}`)
}

/** Wait for the confirmation email for `email` in Mailpit and return its verify link. */
export async function confirmationLink(email) {
  for (let i = 0; i < 30; i++) {
    const list = await (await fetch(`${MAILPIT}/api/v1/search?query=${encodeURIComponent('to:' + email)}`)).json()
    if (list.messages?.length) {
      const msg = await (await fetch(`${MAILPIT}/api/v1/message/${list.messages[0].ID}`)).json()
      const m = /href="([^"]*\/auth\/v1\/verify[^"]*)"/.exec(msg.HTML) ?? /(http\S*\/auth\/v1\/verify\S*)/.exec(msg.Text)
      if (m) return m[1].replaceAll('&amp;', '&')
    }
    await new Promise((r) => setTimeout(r, 300))
  }
  throw new Error(`No confirmation email for ${email}`)
}

export async function signUpAndConfirm(page, email, password) {
  await page.goto(APP)
  await page.getByRole('button', { name: 'Sign up' }).click()
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password').fill(password)
  await page.locator('form button[type=submit]').click()
  await page.getByRole('status').filter({ hasText: /check your email/i }).waitFor()
  const link = await confirmationLink(email)
  await page.goto(link) // confirms the account, then redirects back to the app
  await page.waitForURL(new RegExp('^' + APP.replaceAll('.', '\\.')))
}

export async function logIn(page, email, password) {
  await page.goto(APP)
  // The confirmation redirect may already have signed the user in.
  const loginButton = page.getByRole('button', { name: 'Log in' })
  const signedIn = page.getByText(email)
  await loginButton.or(signedIn).first().waitFor()
  if (await signedIn.isVisible()) return
  await loginButton.click()
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password').fill(password)
  await page.locator('form button[type=submit]').click()
  await page.getByText(email).waitFor()
}

export function deleteTestUsers() {
  execFileSync('docker', [
    'exec', DB_CONTAINER, 'psql', '-U', 'postgres', '-c',
    "delete from auth.users where email like 'e2e-%@example.com'",
  ], { env: { ...process.env, PATH: process.env.PATH + ':/usr/local/bin' }, stdio: 'ignore' })
}
