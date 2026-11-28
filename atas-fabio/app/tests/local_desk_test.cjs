// End-to-end test of Laya's LOCAL dashboard (laya/desk_server.py) in a real browser, with the practice feed standing in
// for ATAS and a fake "claude" (no Claude usage). Run from atas-fabio:  node app/tests/local_desk_test.cjs
// (build tools/BridgeDemo first: dotnet build tools/BridgeDemo)
const { chromium } = require('playwright');
const { spawn, execFileSync } = require('child_process');
const fs = require('fs'), path = require('path'), os = require('os'), http = require('http');
const REPO = path.resolve(__dirname, '../..');
const SP = fs.mkdtempSync(path.join(os.tmpdir(), 'localdesk-'));
const BRIDGE = 18795, DESK = 18771;
let failed = 0;
const ok = (cond, name) => { console.log((cond ? '  ok    ' : '  FAIL  ') + name); if (!cond) failed++; };
const sleep = ms => new Promise(r => setTimeout(r, ms));
async function until(fn, ms, every = 300) { const end = Date.now() + ms; while (Date.now() < end) { try { if (await fn()) return true; } catch (e) {} await sleep(every); } return false; }
function raw(method, p, headers, body) {
  return new Promise(res => {
    const req = http.request({ host: '127.0.0.1', port: DESK, path: p, method, headers }, r => { let d = ''; r.on('data', c => d += c); r.on('end', () => res({ status: r.statusCode, body: d })); });
    req.on('error', e => res({ status: 0, body: String(e) })); if (body) req.write(body); req.end();
  });
}

(async () => {
  const fakeClaude = 'python3 ' + REPO + '/laya/tests/fake_claude.py';
  const FAKEBIN = path.join(SP, 'bin'); fs.mkdirSync(FAKEBIN);
  fs.writeFileSync(FAKEBIN + '/claude', '#!/bin/sh\nexec ' + fakeClaude + ' "$@"\n', { mode: 0o755 });
  const exportsDir = path.join(SP, 'exports'); fs.mkdirSync(exportsDir);
  const exportName = 'ESZ6_5m_localtest';
  execFileSync(REPO + '/backtest.sh', ['demo', '--out', path.join(exportsDir, exportName + '.bars.gz'), '--days', '140', '--seed', '5'], { stdio: 'ignore' });
  ok(fs.existsSync(path.join(exportsDir, exportName + '.bars.gz')), 'an ATAS-style export is ready for the Backtests page');

  const env = Object.assign({}, process.env, { PATH: FAKEBIN + ':' + process.env.PATH, LAYA_CLAUDE: fakeClaude });
  const demo = spawn('dotnet', [REPO + '/tools/BridgeDemo/bin/Debug/net8.0/fabio-bridge-demo.dll', '--port', String(BRIDGE), '--speed', '60', '--review-timeout', '25', '--hold'],
                     { stdio: ['ignore', fs.openSync(SP + '/demo.log', 'w'), 'inherit'] });
  const desk = spawn('python3', [REPO + '/laya/desk_server.py', '--port', String(DESK), '--bridge', 'http://localhost:' + BRIDGE, '--claude', fakeClaude, '--exports', exportsDir],
                     { env, stdio: ['ignore', fs.openSync(SP + '/desk.log', 'w'), 'inherit'] });
  const browser = await chromium.launch();
  try {
    ok(await until(async () => (await raw('GET', '/local/info', { Host: 'localhost:' + DESK })).status === 200, 15000), 'dashboard server answers on localhost');
    ok((await raw('GET', '/local/info', { Host: 'evil.example:' + DESK })).status === 403, 'a foreign Host header is refused (DNS rebinding)');
    ok((await raw('POST', '/local/runner/start', { Host: 'localhost:' + DESK, 'Content-Type': 'application/json' }, '{}')).status === 403, 'a POST without the desk header is refused');
    ok((await raw('POST', '/local/runner/start', { Host: 'localhost:' + DESK, 'X-Laya-Desk': '1', Origin: 'https://evil.example' }, '{}')).status === 403, 'a POST from another web page is refused');
    ok((await raw('GET', '/reports/../../etc/passwd', { Host: 'localhost:' + DESK })).status === 404, 'no path traversal in reports');

    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    const errors = []; page.on('pageerror', e => errors.push(String(e)));
    await page.goto('http://localhost:' + DESK + '/');
    ok(await until(async () => (await page.textContent('#bridgeText')) === 'Bridge: connected', 20000), 'local mode: the desk reaches the bridge through the server');
    ok(await page.isHidden('#exampleBanner'), 'no example banner in local mode');
    ok(await page.isVisible('#backtestsBtn'), 'Backtests button shown');
    await page.click('#settingsBtn');
    ok(await page.isHidden('#setPort') && await page.isVisible('#setModel'), 'server-side settings hidden, model / skill still there');
    await page.click('#settingsBtn');

    await page.click('#startBtn');
    ok(await until(async () => (await page.textContent('#layaText')) === 'Laya: on duty', 8000), 'Start Laya starts the runner through the server');
    ok(await until(async () => /Laya is reading it/.test(await page.textContent('#console')), 30000), "Laya's console streams the runner's log");
    ok(await until(async () => (await page.$$('#tape li')).length > 0 && /away from zones|approaching|set-up|battle|wait/i.test(await page.textContent('#tape')), 40000), "candles arrive with Laya's reads");

    await page.click('#tape li');
    ok(await until(async () => (await page.$$('#footprint tr')).length > 2, 5000), 'footprint ladder of the selected candle shown');
    ok((await page.textContent('#map')).includes('Options levels (gamma)'), 'practice gamma levels shown in the market map');
    await page.click('#optForm summary');
    await page.fill('#optCall', '9999.25'); await page.fill('#optPut', '9000'); await page.fill('#optZero', '');
    await page.click('#optSave');
    ok(await until(async () => (await page.textContent('#map')).includes('Call wall 9999.25'), 8000), 'gamma levels saved from the dashboard reach the bridge and come back in the state');
    await page.click('#askBtn');
    ok(await until(async () => /Fake Laya: sellers were absorbed/.test(await page.textContent('#answer')), 20000), 'Ask Laya answers through Claude Code (fake here)');

    await page.click('#startBtn');
    ok(await until(async () => (await page.textContent('#layaText')) === 'Laya: off duty', 10000), 'Stop Laya stops the runner');

    const rp = await browser.newPage();
    await rp.goto('http://localhost:' + DESK + '/reports/');
    ok(/Latest ATAS export/.test(await rp.textContent('body')), 'Backtests page finds the latest ATAS export');
    await rp.click('#run');
    ok(await until(async () => /Reports:/.test(await rp.textContent('#log')) || (await rp.$$('table tr')).length > 2, 240000, 1000), 'backtest of the export runs from the dashboard');
    await rp.reload();
    const body = await rp.textContent('body');
    ok(body.includes(exportName + '-sweep') && body.includes(exportName + '-defaults'), 'both reports listed');
    const rep = await raw('GET', '/reports/' + exportName + '-sweep/report.html', { Host: 'localhost:' + DESK });
    ok(rep.status === 200 && rep.body.includes('Fabio orderflow sweep'), 'report served');
    const rev = await raw('GET', '/reports/' + exportName + '-sweep/laya_review.md', { Host: 'localhost:' + DESK });
    ok(rev.status === 200 && rev.body.includes("Laya's review"), "Laya's review of the backtest served");
    await page.screenshot({ path: path.join(SP, 'local-desk.png') });
    await rp.screenshot({ path: path.join(SP, 'local-reports.png'), fullPage: true });
    ok(errors.length === 0, 'no script errors' + (errors.length ? ': ' + errors.join(' | ') : ''));
    console.log('screenshots: ' + SP);
  } catch (e) { console.log('  FAIL  ' + e); failed++; }
  finally {
    await browser.close(); desk.kill('SIGTERM'); demo.kill('SIGTERM');
    for (const d of ['defaults', 'sweep']) fs.rmSync(path.join(REPO, 'reports', exportName + '-' + d), { recursive: true, force: true });
  }
  console.log(failed ? failed + ' LOCAL DESK CHECK(S) FAILED' : 'ALL LOCAL DESK CHECKS PASSED');
  process.exit(failed ? 1 : 0);
})();
