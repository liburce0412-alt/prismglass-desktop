'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const web = path.join(__dirname, '../src/LiquidDesktop/companion-web');
const html = fs.readFileSync(path.join(web, 'index.html'), 'utf8');
const manifest = JSON.parse(fs.readFileSync(path.join(web, 'manifest.webmanifest'), 'utf8'));

// The installed entry must never capture an expiring pairing code or a changing IP.
assert.equal(manifest.start_url, '/');
assert.equal(manifest.scope, '/');
assert.equal(manifest.id, '/');
assert.equal(manifest.display, 'standalone');
assert.match(html, /<link\b[^>]*rel="manifest"[^>]*href="\/manifest\.webmanifest"/);
assert.match(html, /<meta\b[^>]*name="apple-mobile-web-app-title"[^>]*content="PrismGlass"/);
assert.match(html, /<meta\b[^>]*name="apple-mobile-web-app-capable"[^>]*content="yes"/);
const appleIcon = html.match(/<link\b[^>]*rel="apple-touch-icon"[^>]*href="([^"]+)"/);
assert.ok(appleIcon, 'iPad needs an explicit home-screen icon');
assert.ok(manifest.icons.length > 0);
for (const icon of manifest.icons) {
  assert.match(icon.src, /^\/[a-z0-9-]+\.png$/);
  assert.equal(icon.src, appleIcon[1]);
  assert.equal(icon.type, 'image/png');
  const png = fs.readFileSync(path.join(web, icon.src.slice(1)));
  assert.deepEqual(png.subarray(0, 8), Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]));
  assert.equal(icon.sizes, png.readUInt32BE(16) + 'x' + png.readUInt32BE(20));
  assert.equal(png.readUInt32BE(16), 512);
}

// Native, initially collapsed help stays usable without an authenticated session.
const connectionSettings = html.match(/<section id="connection-settings"[\s\S]*?<\/section>/)[0];
const help = connectionSettings.match(/<details\b([^>]*)id="install-help"([^>]*)>([\s\S]*?)<\/details>/);
assert.ok(help, 'Home-screen help belongs to the existing connection settings');
assert.doesNotMatch(help[1] + help[2], /\b(?:open|hidden|disabled)\b|private-view/);
assert.match(help[3], /<summary>/);
assert.match(help[3], /Safari/);
assert.match(help[3], /分享/);
assert.match(help[3], /添加到主屏幕/);
assert.match(help[3], /固定名称入口/);
assert.match(help[3], /仍由电脑确认/);
assert.doesNotMatch(help[3], /<script|https?:\/\//);

// No service worker or private offline cache is introduced by the install affordance.
const scripts = Array.from(html.matchAll(/<script\b[^>]*src="([^"]+)"/g), m => m[1]);
for (const script of scripts) {
  assert.match(script, /^\/[a-z0-9-]+\.js$/);
  const directory = script === '/lyrics-style.js' ? path.dirname(web) : web;
  const source = fs.readFileSync(path.join(directory, script.slice(1)), 'utf8');
  assert.doesNotMatch(source, /serviceWorker\s*\.\s*register\s*\(|\bcaches\s*\.\s*open\s*\(/);
}
console.log('PASS companion install: stable entry, local icon, native iPad help, no service worker');
