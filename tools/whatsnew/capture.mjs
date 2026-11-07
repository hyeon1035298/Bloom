// Takes a scene of scenes.html frame by frame and packs it into an animated WebP (pack.py).
//   node tools/whatsnew/capture.mjs <scene> <out.webp> [--fps 25] [--lang ko|en]
//   node tools/whatsnew/capture.mjs <scene> <out.png> --at 1.5,3.2   (stills of those moments)
// Needs puppeteer-core (found as an npm package, or at PUPPETEER_CORE) and a Chrome (CHROME, or puppeteer's own cache)
// and Python with Pillow.
import { createServer } from 'node:http';
import { readFile, mkdtemp, rm, writeFile, readdir } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { tmpdir, homedir } from 'node:os';
import { join, extname, dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { spawnSync } from 'node:child_process';

const here = dirname(fileURLToPath(import.meta.url));
const [scene, out] = process.argv.slice(2);
const flag = name => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : undefined;
const fps = Number(flag('--fps')) || 25;
const stills = flag('--at')?.split(',').map(Number);
const lang = flag('--lang') || 'ko';
if (!scene || !out) {
  console.error('usage: node capture.mjs <scene> <out.webp> [--fps 25]');
  process.exit(2);
}

async function puppeteer() {
  try { return (await import('puppeteer-core')).default; } catch {}
  if (process.env.PUPPETEER_CORE) {
    const pkg = JSON.parse(await readFile(join(process.env.PUPPETEER_CORE, 'package.json'), 'utf8'));
    const entry = pkg.exports?.['.']?.import ?? pkg.module ?? pkg.main;
    const m = await import(pathToFileURL(join(process.env.PUPPETEER_CORE, entry)).href);
    return m.default ?? m;
  }
  throw new Error('puppeteer-core not found: install it or set PUPPETEER_CORE to its folder');
}

async function chrome() {
  if (process.env.CHROME) return process.env.CHROME;
  const cache = join(homedir(), '.cache', 'puppeteer', 'chrome');
  for (const v of (existsSync(cache) ? await readdir(cache) : []).sort().reverse()) {
    const exe = join(cache, v, 'chrome-win64', 'chrome.exe');
    if (existsSync(exe)) return exe;
  }
  throw new Error('Chrome not found: set CHROME');
}

const types = { '.html': 'text/html', '.svg': 'image/svg+xml', '.png': 'image/png', '.webp': 'image/webp', '.js': 'text/javascript' };
const server = createServer(async (req, res) => {
  try {
    const path = resolve(here, '.' + decodeURIComponent(new URL(req.url, 'http://x').pathname));
    if (!path.startsWith(here)) throw new Error('outside');
    const body = await readFile(path);
    res.writeHead(200, { 'content-type': types[extname(path)] || 'application/octet-stream' });
    res.end(body);
  } catch {
    res.writeHead(404).end();
  }
}).listen(0, '127.0.0.1');
await new Promise(r => server.once('listening', r));
const port = server.address().port;

const dir = await mkdtemp(join(tmpdir(), 'bloom-scene-'));
const browser = await (await puppeteer()).launch({ executablePath: await chrome(), headless: true, args: ['--hide-scrollbars', '--force-color-profile=srgb'] });
try {
  const page = await browser.newPage();
  await page.setViewport({ width: 960, height: 480, deviceScaleFactor: 1 });
  await page.goto(`http://127.0.0.1:${port}/scenes.html?scene=${encodeURIComponent(scene)}&lang=${lang}`, { waitUntil: 'networkidle0' });
  await page.waitForFunction('window.scene && window.scene.ready');
  const dur = await page.evaluate('window.scene.dur');
  const count = Math.round(dur * fps);
  const el = await page.$('#screen');
  if (stills) {
    // stills of chosen moments to look at, no WebP: <out>-<t>.png
    for (const t of stills) {
      await page.evaluate(t => window.scene.seek(t), t);
      await el.screenshot({ path: out.replace(/\.\w+$/, '') + `-${t}.png` });
    }
  } else for (let i = 0; i < count; i++) {
    await page.evaluate(t => window.scene.seek(t), i / fps);
    await el.screenshot({ path: join(dir, `f${String(i).padStart(4, '0')}.png`) });
  }
  if (!stills) {
    await writeFile(join(dir, 'meta.json'), JSON.stringify({ fps, count }));
    console.log(`${scene}: ${count} frames`);
  }
} finally {
  await browser.close();
  server.close();
}
if (stills) {
  await rm(dir, { recursive: true, force: true });
  process.exit(0);
}
const py = spawnSync('python', [join(here, 'pack.py'), dir, out], { stdio: 'inherit' });
await rm(dir, { recursive: true, force: true });
process.exit(py.status ?? 1);
