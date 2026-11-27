// Browser test of the Laya Tape Desk: node app/tests/desk_test.cjs (needs Playwright + Chromium, dotnet, python3, curl;
// build tools/BridgeDemo first). Uses a fake claude: no Claude usage.
// Drives the Laya Tape Desk page in Chromium: example mode, then live mode against the real demo bridge + runner.
const { chromium } = require('playwright');
const { exec, spawn } = require('child_process');
const fs = require('fs');
const SP = process.env.DESK_TMP || require('fs').mkdtempSync(require('path').join(require('os').tmpdir(), 'desk-'));
const REPO = require('path').resolve(__dirname, '../..');
const body = fs.readFileSync(REPO + '/app/laya-desk.html', 'utf8');
const wrapped = '<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><style>:root{color-scheme:light}body{margin:0;font:14px system-ui}[hidden]{display:none!important}</style></head><body>' + body + '</body></html>';
fs.writeFileSync(SP + '/desk.html', wrapped);
const URL_ = 'file://' + SP + '/desk.html';
let fails = 0;
// a fake "claude" command on PATH so the runner never calls the real Claude in tests
const FAKEBIN = require('path').join(SP, 'fakebin'); fs.mkdirSync(FAKEBIN, { recursive: true });
fs.writeFileSync(FAKEBIN + '/claude', '#!/bin/sh\nexec python3 ' + REPO + '/laya/tests/fake_claude.py "$@"\n', { mode: 0o755 });
const check = (ok, name) => { console.log((ok ? '  ok    ' : '  FAIL  ') + name); if (!ok) fails++; };

(async () => {
  const browser = await chromium.launch();

  // ---------------- 1. example mode (browser, no Claude runtime)
  for (const scheme of ['light', 'dark']) {
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 }, colorScheme: scheme });
    const errors = []; page.on('pageerror', e => errors.push(e.message)); page.on('console', m => { if (m.type() === 'error' && !/fonts\.g/.test(m.text())) errors.push(m.text()); });
    await page.goto(URL_); await page.waitForTimeout(600);
    check(errors.length === 0, `example (${scheme}): no script errors ${errors.join(' | ')}`);
    check(await page.isVisible('#exampleBanner'), `example (${scheme}): example banner shown`);
    check((await page.locator('#tape li.row').count()) === 14, `example (${scheme}): 14 candles in the tape`);
    check((await page.textContent('#tape')).includes('Buyers won'), `example (${scheme}): Laya's reads shown`);
    check((await page.locator('#chart rect').count()) === 14, `example (${scheme}): chart draws one body per candle`);
    await page.screenshot({ path: `${SP}/desk_example_${scheme}.png`, fullPage: false });
    await page.close();
  }
  const phone = await browser.newPage({ viewport: { width: 400, height: 860 } });
  await phone.goto(URL_); await phone.waitForTimeout(400);
  const overflow = await phone.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1);
  check(!overflow, 'phone width: no horizontal page scroll');
  await phone.screenshot({ path: `${SP}/desk_phone.png` });
  await phone.close();

  // ---------------- 2. live mode: simulated installed app, real bridge + runner
  const demo = spawn('dotnet', [REPO + '/tools/BridgeDemo/bin/Debug/net8.0/fabio-bridge-demo.dll', '--port', '18794', '--speed', '60', '--review-timeout', '25', '--hold'], { stdio: ['ignore', fs.openSync(SP + '/demo_live.log', 'w'), 'inherit'] });
  await new Promise(r => setTimeout(r, 2500));
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  const procs = {}; let pid = 0;
  await page.exposeFunction('__exec', (cmd, opts) => new Promise(res => {
    const child = exec(cmd, { timeout: (opts && opts.timeout) || 10000, shell: '/bin/bash' }, (err, stdout, stderr) =>
      res({ stdout, stderr, code: err ? (err.code || 1) : 0, signal: null, truncated: false }));
    if (opts && opts.input) { child.stdin.write(opts.input); } child.stdin.end();
  }));
  await page.exposeFunction('__spawn', (file, args) => {
    args = args.slice();
    const id = ++pid;
    const f = file === '/bin/zsh' ? '/bin/bash' : file;
    if (f === '/bin/bash' && args[0] === '-lc') args = ['-c', args[1]];   // non-login shell keeps the fake claude on PATH
    const env = Object.assign({}, process.env, { PATH: FAKEBIN + ':' + process.env.PATH });
    const p = spawn(f, args, { env }); procs[id] = p;
    const send = (ev, data) => page.evaluate(([i, e, d]) => window.__procEvent(i, e, d), [id, ev, data]).catch(() => {});
    p.stdout.on('data', d => send('stdout', d.toString())); p.stderr.on('data', d => send('stderr', d.toString()));
    p.on('exit', code => send('exit', code));
    return id;
  });
  await page.exposeFunction('__kill', (id, sig) => { if (procs[id]) procs[id].kill(sig || 'SIGTERM'); return true; });
  await page.addInitScript((repo) => {
    try { localStorage.setItem('laya-desk-settings', JSON.stringify({ setPort: '18794', setRepo: repo, setModel: 'sonnet', setNotify: 'all' })); } catch (e) {}
    window.__notes = []; window.__tray = []; const handlers = {};
    window.__procEvent = (id, ev, data) => (handlers[id] && handlers[id][ev] || []).forEach(f => f(data));
    const desktop = Object.freeze({
      install: { state: async () => 'installed', request: async () => 'installed' },
      window: { setOptions: async () => ({ applied: 'now', reloading: false }), show: async () => {} },
      notification: { show: async (o) => { window.__notes.push(o.body); } },
      tray: { set: async (o) => { window.__tray.push(o.title); return true; }, remove: async () => {} },
      childProcess: {
        exec: (cmd, opts) => window.__exec(cmd, opts || {}),
        spawn: (file, args) => {
          const idP = window.__spawn(file, args || []); const local = { stdout: [], stderr: [], exit: [], error: [] };
          idP.then(id => { handlers[id] = local; });
          const on = (ev) => (e, f) => { local[ev === 'data' ? e : e] = local[e] || []; (local[e] = local[e] || []).push(f); };
          return { pid: idP, stdout: { on: (e, f) => local.stdout.push(f) }, stderr: { on: (e, f) => local.stderr.push(f) },
                   stdin: { write: async () => {}, end: async () => {} },
                   on: (e, f) => { (local[e] = local[e] || []).push(f); }, kill: async (sig) => window.__kill(await idP, sig) };
        }
      }
    });
    const sample = Object.assign(async (input, opts) => { const text = 'Example answer: sellers absorbed at VAL, buyers won the body.'; if (opts && opts.onText) opts.onText({ text, delta: text }); return { text, truncated: false }; }, {});
    window.claude = { use: async (name) => name === 'desktop' ? desktop : name === 'sample' ? sample : null };
  }, REPO);
  await page.goto(URL_);
  await page.waitForTimeout(4000);
  check(errors.length === 0, 'live: no script errors ' + errors.join(' | '));
  check(!(await page.isVisible('#exampleBanner')), 'live: example banner hidden');
  check((await page.textContent('#bridgeText')).includes('connected'), 'live: bridge connected');
  await page.click('#startBtn');
  // wait until the practice day is fully reviewed (the demo waits for each review)
  let done = false;
  for (let i = 0; i < 90 && !done; i++) { await page.waitForTimeout(1000); done = fs.readFileSync(SP + '/demo_live.log', 'utf8').includes('REPLAY OUTCOME'); }
  await page.waitForTimeout(6000);
  check(done, 'live: the replay reached the set-up and its outcome');
  check((await page.textContent('#layaText')).includes('on duty'), 'live: Laya shown on duty');
  const reads = await page.locator('#tape li.row').evaluateAll(rows => rows.filter(r => !r.textContent.includes('not read yet')).length);
  check(reads >= 10, `live: Laya's reads appear in the tape (${reads} rows read)`);
  check((await page.textContent('#setups')).toLowerCase().includes('approved') || (await page.textContent('#setups')).toLowerCase().includes('taken'), 'live: the set-up shows Laya\'s decision');
  const notes = await page.evaluate(() => window.__notes);
  check(notes.some(n => /alert long/i.test(n)), 'live: Mac notification for Laya\'s long alert');
  check(notes.some(n => /waiting for Laya/.test(n)), 'live: Mac notification for the pending set-up');
  const tray = await page.evaluate(() => window.__tray);
  check(tray.length > 0 && tray[tray.length - 1].startsWith('Laya'), 'live: menu bar item updated: ' + tray[tray.length - 1]);
  check((await page.textContent('#console')).includes('Laya is reading it'), 'live: runner output streamed to the console');
  await page.click('#tape li.row >> nth=0'); await page.click('#askBtn'); await page.waitForTimeout(500);
  check((await page.textContent('#answer')).includes('Example answer'), 'live: Ask Laya shows the answer');
  await page.screenshot({ path: `${SP}/desk_live.png` });
  await page.click('#startBtn'); await page.waitForTimeout(4500);
  check((await page.textContent('#layaText')).includes('off duty'), 'live: Stop Laya ends the runner');
  await browser.close();
  demo.kill(); Object.values(procs).forEach(p => p.kill());
  console.log(fails === 0 ? 'ALL DESK CHECKS PASSED' : fails + ' DESK CHECK(S) FAILED');
  process.exit(fails ? 1 : 0);
})();
