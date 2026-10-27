'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const web = path.join(__dirname, '../src/LiquidDesktop/companion-web');
const source = fs.readFileSync(path.join(web, 'companion-scan.js'), 'utf8');
const jsQR = require(path.join(web, 'jsqr.js'));
const origin = 'https://prismglass-a1b2c3d4.local:9443';
const rows = JSON.parse(fs.readFileSync(path.join(__dirname, 'fixtures/companion-pair-qr.json'), 'utf8'));
const width = rows.length * 4, pixels = new Uint8ClampedArray(width * width * 4);
for (let y = 0; y < width; y++) for (let x = 0; x < width; x++) { const at = (y * width + x) * 4, value = rows[Math.floor(y / 4)][Math.floor(x / 4)] === '1' ? 0 : 255; pixels.set([value, value, value, 255], at); }
assert.equal(jsQR(pixels, width, width, { inversionAttempts: 'dontInvert' }).data, origin + '/#pair=012345', 'Bundled decoder reads a real QRCoder-generated pairing matrix');

function fixture(options = {}) {
  const ids = {}, timers = new Map(), listeners = {}, windowListeners = {}, codes = [], scripts = [];
  let next = 1, stops = 0, requests = 0, qr = options.qr || null, constraints;
  for (const id of ['pair-camera', 'pair-video', 'pair-status', 'pair-scan', 'pair-scan-stop']) ids[id] = { hidden: true, disabled: false, textContent: '', pause() {}, focus() {} };
  const video = ids['pair-video']; Object.assign(video, { readyState: 2, videoWidth: 1280, videoHeight: 720, play: async () => {} });
  const context2d = { drawImage() {}, getImageData: (x, y, w, h) => ({ data: new Uint8ClampedArray(w * h * 4), width: w, height: h }) };
  const canvas = { width: 0, height: 0, getContext: () => context2d };
  const stream = { getTracks: () => [{ stop() { stops++; } }] };
  const document = { hidden: false, getElementById: id => ids[id], createElement: tag => tag === 'canvas' ? canvas : { remove() {} }, head: { appendChild: script => scripts.push(script) }, addEventListener: (name, callback) => { listeners[name] = callback; } };
  const window = { location: { origin }, jsQR: options.lazy ? undefined : () => qr ? { data: qr } : null, addEventListener: (name, callback) => { windowListeners[name] = callback; } };
  const navigator = options.unsupported ? {} : { mediaDevices: { getUserMedia: options.capture || (async value => { requests++; constraints = value; return stream; }) } };
  vm.runInNewContext(source, { window, document, navigator, URL, Promise, setTimeout: (callback, ms) => { const id = next++; timers.set(id, { callback, ms }); return id; }, clearTimeout: id => timers.delete(id) });
  return { ids, window, document, stream, canvas, scripts, listeners, windowListeners, codes, stops: () => stops, requests: () => requests, constraints: () => constraints, qr(value) { qr = value; }, start: () => window.PrismCompanionScan.start(code => codes.push(code)), stop: () => window.PrismCompanionScan.stop(), timer(ms) { for (const [id, item] of Array.from(timers)) if (item.ms === ms) { timers.delete(id); item.callback(); } } };
}
const flush = async () => { for (let i = 0; i < 10; i++) await Promise.resolve(); };
(async () => {
  const scan = fixture(); assert.equal(scan.requests(), 0, 'Opening the page never opens the camera');
  await scan.start(); assert.equal(scan.constraints().audio, false); assert.equal(scan.constraints().video.facingMode.ideal, 'environment'); assert.equal(scan.canvas.width, 640); assert.equal(scan.canvas.height, 360);
  for (const url of ['https://evil.example/#pair=012345', 'http://prismglass-a1b2c3d4.local:9443/#pair=012345', origin + '/', origin + '/#pair=12345', origin + '/#pair=1234567', origin + '/?token=secret#pair=012345', origin + '/other#pair=012345', 'https://user@prismglass-a1b2c3d4.local:9443/#pair=012345', '012345', 'javascript:alert(1)']) { scan.qr(url); scan.timer(160); assert.equal(scan.codes.length, 0, 'Reject foreign or malformed QR without navigating: ' + url); }
  scan.qr(origin + '/#pair=012345'); scan.timer(160); assert.deepEqual(scan.codes, ['012345']); assert.equal(scan.stops(), 1); assert.equal(scan.ids['pair-video'].srcObject, null); assert.equal(scan.ids['pair-camera'].hidden, true); assert.equal(scan.canvas.width, 0, 'Successful decoding closes the camera and clears frame memory');
  const hidden = fixture(); await hidden.start(); hidden.document.hidden = true; hidden.listeners.visibilitychange(); assert.equal(hidden.stops(), 1); hidden.timer(160); assert.equal(hidden.codes.length, 0);
  const pagehide = fixture(); await pagehide.start(); pagehide.windowListeners.pagehide(); assert.equal(pagehide.stops(), 1);
  const timeout = fixture(); await timeout.start(); timeout.timer(60000); assert.equal(timeout.stops(), 1); assert.match(timeout.ids['pair-status'].textContent, /扫描已暂停/);
  let allow; const late = fixture({ capture: () => new Promise(resolve => { allow = resolve; }) }); const started = late.start(); late.stop(); allow(late.stream); await started; assert.equal(late.stops(), 1, 'Camera permission arriving after cancel immediately releases its stream');
  const denied = fixture({ capture: async () => { const error = new Error(); error.name = 'NotAllowedError'; throw error; } }); await denied.start(); assert.match(denied.ids['pair-status'].textContent, /未获得相机权限/); assert.equal(denied.ids['pair-scan'].disabled, false);
  const unavailable = fixture({ unsupported: true }); await unavailable.start(); assert.match(unavailable.ids['pair-status'].textContent, /无法使用相机/);
  const lazy = fixture({ lazy: true }); assert.equal(lazy.scripts.length, 0); const loading = lazy.start(); await flush(); assert.equal(lazy.scripts[0].src, '/jsqr.js'); lazy.timer(60000); lazy.window.jsQR = () => null; lazy.scripts[0].onload(); await loading; assert.equal(lazy.stops(), 1, 'Decoder loading timeout also releases camera');
  console.log('PASS companion scan: real QR decoding, origin binding, camera lifecycle, denial and lazy loading');
})().catch(error => { console.error(error); process.exitCode = 1; });
