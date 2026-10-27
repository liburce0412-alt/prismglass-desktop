'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const webcrypto = require('node:crypto').webcrypto;
const source = fs.readFileSync(path.join(__dirname, '../src/LiquidDesktop/companion-web/companion.js'), 'utf8');
const html = fs.readFileSync(path.join(__dirname, '../src/LiquidDesktop/companion-web/index.html'), 'utf8');
const css = fs.readFileSync(path.join(__dirname, '../src/LiquidDesktop/companion-web/companion.css'), 'utf8');
assert.match(css, /button\{min-width:44px;min-height:44px;/, 'All buttons need a 44 px touch target in both dimensions');

class Node {
  constructor(tag) { this.tagName = tag; this.children = []; this.attributes = {}; this.dataset = {}; this.hidden = false; this.disabled = false; this.value = ''; this.checked = false; this._text = ''; this.style = { setProperty() {} }; this.parentNode = null; }
  set value(value) { this._value = String(value); this.selectionStart = this.selectionEnd = this._value.length; }
  get value() { return this._value; }
  set textContent(value) { this._text = String(value); this.children = []; }
  get textContent() { return this._text + this.children.map(child => child.textContent).join(''); }
  set src(value) { this.setAttribute('src', value); }
  setAttribute(name, value) { this.attributes[name] = String(value); if (name === 'class') this.className = value; }
  getAttribute(name) { return this.attributes[name] == null ? null : this.attributes[name]; }
  removeAttribute(name) { delete this.attributes[name]; }
  setSelectionRange(start, end) { this.selectionStart = start; this.selectionEnd = end; }
  appendChild(child) { child.remove(); child.parentNode = this; this.children.push(child); return child; }
  remove() { if (this.parentNode) { this.parentNode.children = this.parentNode.children.filter(child => child !== this); this.parentNode = null; } }
  replaceChildren(...children) { this._text = ''; this.children.forEach(child => { child.parentNode = null; }); this.children = []; children.forEach(child => this.appendChild(child)); this.replacements = (this.replacements || 0) + 1; }
}
const clone = value => JSON.parse(JSON.stringify(value));
const sample = {
  protocol: 1, serverTime: Date.now(), theme: 'Sky', settings: { revision: '638000000000000001', theme: 'Sky', manualPaused: false },
  weather: { available: true, updatedAt: Date.now(), data: { temp: 21, code: 3, high: 24, low: 18 } },
  system: { available: true, updatedAt: Date.now(), data: { cpu: 37, ram: 62, down: 2048, up: 1024 } }, volume: { available: true, data: { value: 45 } },
  music: { available: true, title: '真实曲目', artist: '歌手', source: '系统媒体', playing: true, current: '当前歌词', next: '下一句歌词', cover: '/api/assets/cover_1' },
  focus: { available: true, total: 1500, remaining: 1300, deadline: 0, completed: 3 },
  tasks: { available: true, revision: '638000000000000002', items: [{ id: 'task1', text: '<script>私人任务</script>', done: false }], countdownTitle: '一个事件', countdownDate: '2026-12-01' },
  notes: { available: true, revision: '638000000000000003', text: '私人笔记' }
};
const appSample = { revision: 'apps-1', items: [{ id: 'remote-safe', name: '<应用>', category: 'work', folder: '', order: 1, exists: true, icon: '/api/assets/icon_1' }, { id: 'missing', name: '失效入口', category: 'work', exists: false }], categories: [{ id: 'work', name: '工作' }], folders: [] };
const grants = { apps: true, tasks: true, notes: true, music: true, control: true };
const response = (data, status = 200, headers = {}) => ({ ok: status >= 200 && status < 300, status, headers: { get: key => headers[key] || null }, json: async () => clone(data) });
const flush = async () => { for (let i = 0; i < 5; i++) await new Promise(resolve => setImmediate(resolve)); };

function fixture(options = {}) {
  const ids = {}, all = [], listeners = {}, windowListeners = {}, timers = new Map(), calls = [], historyChanges = [], blobs = [], revokedBlobs = [];
  let nextTimer = 1, handler = options.handler, now = Date.now(), imagesCreated = 0;
  class FixtureDate extends Date { static now() { return now; } }
  for (const match of html.matchAll(/<([a-z]+)([^>]*)>/gi)) {
    const node = new Node(match[1]), attrs = match[2];
    for (const attr of attrs.matchAll(/([\w-]+)="([^"]*)"/g)) { node.setAttribute(attr[1], attr[2]); if (attr[1] === 'id') ids[attr[2]] = node; if (attr[1].startsWith('data-')) node.dataset[attr[1].slice(5)] = attr[2]; if (attr[1] === 'value') node.value = attr[2]; }
    node.hidden = /\bhidden\b/.test(attrs); node.disabled = /\bdisabled\b/.test(attrs); all.push(node);
  }
  const document = { hidden: false, body: new Node('body'), getElementById: id => { assert(ids[id], 'Unknown DOM id: ' + id); return ids[id]; }, createElement: tag => { if (tag === 'img') imagesCreated++; return new Node(tag); }, querySelectorAll: selector => selector === '[data-action]' ? all.filter(n => n.dataset.action) : selector === '.private-view' ? all.filter(n => (n.className || '').split(' ').includes('private-view')) : [], querySelector: selector => selector === '.brand' ? all.find(n => n.className === 'brand') : null, addEventListener: (name, fn) => { listeners[name] = fn; } };
  const current = clone(options.state || sample), applications = clone(options.apps || appSample);
  const fetch = async (url, request) => {
    const call = { url, request, body: request.body ? JSON.parse(request.body) : null }; calls.push(call);
    if (handler) { const result = handler(call); if (result !== undefined) return result; }
    if (url === '/api/session') return response({ protocol: 1, csrf: 'test-csrf', grants: options.grants || grants });
    if (url === '/api/state') return response(current);
    if (url === '/api/apps') return response(applications);
    if (url.startsWith('/api/assets/')) return { ...response({}), blob: async () => new Blob([new Uint8Array([137, 80, 78, 71])], { type: 'image/png' }) };
    return response({ ok: true });
  };
  const context = { document, crypto: webcrypto, AbortController, Uint8Array, Promise, Date: FixtureDate, isFinite, fetch, URL: { createObjectURL(blob) { assert(blob instanceof Blob); const url = 'blob:fixture-' + blobs.length; blobs.push(url); return url; }, revokeObjectURL(url) { revokedBlobs.push(url); } }, setTimeout: (fn, delay) => { const id = nextTimer++; timers.set(id, { fn, delay }); return id; }, clearTimeout: id => timers.delete(id), setInterval: () => 1, clearInterval: () => {}, console };
  const location = { hash: options.hash || '', pathname: '/', search: options.search || '' };
  context.window = { innerHeight: 768, location, history: { replaceState: (state, title, url) => { historyChanges.push(url); location.hash = ''; } }, matchMedia: () => ({ matches: false }), addEventListener: (name, fn) => { windowListeners[name] = fn; } };
  vm.runInNewContext(source, context, { filename: 'companion.js' });
  return { ids, all, document, calls, blobs, revokedBlobs, historyChanges, location, current, applications, listeners, windowListeners, imageCount: () => imagesCreated, advance: ms => { now += ms; }, handler: fn => { handler = fn; }, async timers(delay) { const ready = Array.from(timers.entries()).filter(([, item]) => item.delay === delay); ready.forEach(([id]) => timers.delete(id)); ready.forEach(([, item]) => item.fn()); await flush(); }, async boot() { await this.timers(0); }, async hide() { document.hidden = true; listeners.visibilitychange(); await flush(); }, async show() { document.hidden = false; listeners.visibilitychange(); await this.timers(0); } };
}
const click = (f, action) => (f.all.find(n => n.dataset.action === action) || f.ids['touch-actions'].children.find(n => n.dataset.touchId === action)).onclick();
const submit = node => node.onsubmit({ preventDefault() {} });

(async function () {
  const restoring = fixture(); assert.equal(restoring.ids['pair-form'].hidden, true, 'Startup restores the remembered session before offering pairing'); await restoring.boot();
  assert.equal(restoring.ids['pair-panel'].hidden, true); await restoring.hide(); assert.equal(restoring.ids['pair-form'].hidden, true); await restoring.show(); assert.equal(restoring.ids['pair-panel'].hidden, true);
  assert.equal(restoring.calls.filter(c => c.url === '/api/pair').length, 0, 'Reopening an authenticated home-screen window never submits another pairing request');
  const sessionReads = restoring.calls.filter(c => c.url === '/api/session').length; restoring.advance(86400001); await restoring.timers(2000); assert.equal(restoring.calls.filter(c => c.url === '/api/session').length, sessionReads + 1, 'Continuously open displays renew their remembered session once a day');
  const manyApps = { ...appSample, items: Array.from({ length: 221 }, (_, index) => ({ id: 'app' + index, name: '应用' + index, category: 'work', folder: '', exists: true, icon: '/api/assets/icon_' + index })) };
  const iconCalls = f => f.calls.filter(call => call.url.startsWith('/api/assets/icon_'));
  const search = async (f, value) => { f.ids['app-search'].value = value; f.ids['app-search'].oninput(); await f.timers(180); };
  const appTraffic = fixture({ apps: manyApps }); await appTraffic.boot();
  assert.equal(iconCalls(appTraffic).length, 0, 'Atmosphere does not load hidden app icons');
  appTraffic.ids['work-tab'].onclick(); await flush(); assert.equal(iconCalls(appTraffic).length, 6); const quickApp = appTraffic.ids['quick-apps'].children[0];
  for (let i = 0; i < 3; i++) { appTraffic.advance(11000); await appTraffic.timers(2000); }
  click(appTraffic, 'media.next'); await flush(); await appTraffic.timers(0); assert.equal(iconCalls(appTraffic).length, 6);
  appTraffic.ids['all-apps'].onclick(); await flush(); assert.equal(appTraffic.ids['app-grid'].children.length, 24); assert.equal(iconCalls(appTraffic).length, 24); assert.equal(appTraffic.ids['app-grid'].children[0], quickApp, 'Quick and full view move the same button and image');
  const firstFullApp = appTraffic.ids['app-grid'].children[0]; assert.match(firstFullApp.children[0].children[1].getAttribute('src'), /^blob:/, 'Images use controlled fetch results, never direct HTTP src');
  for (let i = 0; i < 4; i++) { appTraffic.ids['app-categories'].children[1].onclick(); appTraffic.ids['app-categories'].children[0].onclick(); await search(appTraffic, '应用1'); await search(appTraffic, ''); }
  const loaded = iconCalls(appTraffic).length; assert(loaded <= 48, 'Changing filters fetches each newly needed icon once'); assert.equal(appTraffic.ids['app-grid'].children[0], firstFullApp);
  await search(appTraffic, '应用1'); await search(appTraffic, ''); assert.equal(iconCalls(appTraffic).length, loaded, 'Returning to filters does not refetch already loaded icons');
  appTraffic.ids['app-search'].oncompositionstart(); appTraffic.ids['app-search'].value = '应用12'; appTraffic.ids['app-search'].oninput({ isComposing: true }); await appTraffic.timers(180); assert.equal(iconCalls(appTraffic).length, loaded, 'IME composition never renders intermediate filters'); appTraffic.ids['app-search'].oncompositionend(); await appTraffic.timers(180);
  let releaseApp; appTraffic.handler(call => call.url === '/api/action' ? new Promise(resolve => { releaseApp = resolve; }) : undefined); appTraffic.ids['app-grid'].children[0].onclick(); await flush(); const beforeWriteIcons = iconCalls(appTraffic).length; releaseApp(response({ ok: true })); await flush(); assert.equal(iconCalls(appTraffic).length, beforeWriteIcons);
  appTraffic.handler(undefined); appTraffic.ids['apps-back'].onclick(); appTraffic.applications.items[100].name = '后台改名'; appTraffic.advance(11000); await appTraffic.timers(0); assert.equal(iconCalls(appTraffic).length, beforeWriteIcons);
  const blobsBeforeHide = appTraffic.blobs.length; await appTraffic.hide(); assert.equal(appTraffic.revokedBlobs.length, blobsBeforeHide, 'Privacy purge revokes every private blob URL'); await appTraffic.show(); assert.equal(iconCalls(appTraffic).length, beforeWriteIcons, 'Returning to atmosphere does not reload the app library');
  const retired = fixture(); await retired.boot(); const firstCover = retired.ids.cover.getAttribute('src'); retired.current.music.cover = '/api/assets/cover_2'; await retired.timers(2000); assert(retired.revokedBlobs.includes(firstCover), 'A track change revokes the old cover instead of accumulating album artwork'); const secondCover = retired.ids.cover.getAttribute('src'); assert.notEqual(secondCover, firstCover);
  retired.ids['work-tab'].onclick(); await flush(); const retiredIcon = retired.ids['quick-apps'].children[0].children[0].children[1].getAttribute('src'); retired.applications.items = []; retired.applications.revision = 'removed'; retired.advance(11000); await retired.timers(2000); assert(retired.revokedBlobs.includes(retiredIcon)); assert.equal(retired.ids['quick-apps'].children.length, 0, 'Retired app IDs release their nodes and blob resources'); retired.current.music.available = false; await retired.timers(2000); assert(retired.revokedBlobs.includes(secondCover));
  let lateCover; const staleArt = fixture({ handler: call => call.url === '/api/assets/cover_1' ? new Promise(resolve => { lateCover = resolve; }) : undefined }); await staleArt.boot(); staleArt.current.music.cover = '/api/assets/cover_new'; await staleArt.timers(2000); const currentCover = staleArt.ids.cover.getAttribute('src'); lateCover({ ...response({}), blob: async () => new Blob(['old cover']) }); await flush(); assert.equal(staleArt.ids.cover.getAttribute('src'), currentCover); assert.equal(staleArt.blobs.length, 1, 'Retiring an in-flight cover prevents late private blob allocation or stale artwork');

  const library = fixture({ apps: { ...manyApps, items: Array.from({ length: 500 }, (_, i) => ({ ...manyApps.items[0], id: 'app' + i, name: '应用' + i, icon: '/api/assets/icon_' + i })) } }); await library.boot(); library.ids['all-apps'].onclick(); await flush();
  assert.equal(iconCalls(library).length, 24); assert.equal(library.ids['apps-page-label'].textContent, '1 / 21'); const firstPageButton = library.ids['app-grid'].children[0];
  library.ids['apps-next'].onclick(); await flush(); assert.equal(library.ids['app-grid'].children[0].dataset.appId, 'app24'); assert.equal(iconCalls(library).length, 48); library.ids['apps-prev'].onclick(); await flush(); assert.equal(library.ids['app-grid'].children[0], firstPageButton); assert.equal(iconCalls(library).length, 48);
  for (let i = 0; i < 20; i++) { library.ids['apps-next'].onclick(); await flush(); }
  assert.equal(library.ids['app-grid'].children.length, 20); assert.equal(library.ids['apps-page-label'].textContent, '21 / 21'); assert.equal(library.ids['apps-next'].disabled, true); assert.equal(iconCalls(library).length, 500, '500 icons require explicit paging; each is fetched at most once');

  const queued = fixture({ apps: manyApps }); await queued.boot(); let imageResolvers = [], activeImages = 0, maxImages = 0;
  queued.handler(call => call.url.startsWith('/api/assets/icon_') ? new Promise((resolve, reject) => { activeImages++; maxImages = Math.max(maxImages, activeImages); imageResolvers.push(() => { activeImages--; resolve({ ...response({}), blob: async () => new Blob(['icon'], { type: 'image/png' }) }); }); call.request.signal.addEventListener('abort', () => { activeImages--; reject(new Error('aborted')); }); }) : undefined);
  queued.ids['all-apps'].onclick(); await flush(); assert.equal(imageResolvers.length, 3); while (imageResolvers.length) { imageResolvers.shift()(); await flush(); } assert.equal(maxImages, 3, 'Actual image fetch queue bounds concurrent asset requests to three'); assert.equal(iconCalls(queued).length, 24);

  const imageLimited = fixture({ apps: manyApps }); await imageLimited.boot(); imageLimited.handler(call => call.url.startsWith('/api/assets/icon_') ? response({ error: 'rate_limited', retryAfter: 17 }, 429, { 'Retry-After': '17' }) : undefined); imageLimited.ids['all-apps'].onclick(); await flush(); const rejectedImages = iconCalls(imageLimited).length; assert.equal(rejectedImages, 3);
  imageLimited.ids['apps-next'].onclick(); await search(imageLimited, '应用19'); await search(imageLimited, ''); imageLimited.ids['app-categories'].children[1].onclick(); await flush(); assert.equal(iconCalls(imageLimited).length, rejectedImages, 'Images, filters and paging cannot bypass an asset429 cooldown');
  imageLimited.handler(undefined); imageLimited.advance(17000); await imageLimited.timers(17000); assert(iconCalls(imageLimited).length <= rejectedImages + 24); assert.equal(imageLimited.ids.feedback.hidden, true);
  const repeatedImageBusy = fixture({ apps: { ...manyApps, items: manyApps.items.slice(0, 1) } }); await repeatedImageBusy.boot(); repeatedImageBusy.handler(call => call.url.startsWith('/api/assets/icon_') ? response({ error: 'busy', retryAfter: 1 }, 429) : undefined); repeatedImageBusy.ids['all-apps'].onclick(); await flush(); repeatedImageBusy.advance(1000); await repeatedImageBusy.timers(1000); repeatedImageBusy.advance(1000); await repeatedImageBusy.timers(1000); await repeatedImageBusy.timers(2000); assert.equal(iconCalls(repeatedImageBusy).length, 2, 'An asset can retry only once after busy; ordinary polling cannot create an endless retry loop');

  const contextState = { ...sample, context: { id: 'window-1', scene: 'browser', appName: '浏览器', available: true, actions: [{ id: 'back', label: '后退' }, { id: 'new-tab', label: '新标签页' }] } };
  const touch = fixture({ state: contextState }); await touch.boot(); assert.equal(touch.ids['touch-actions'].children[0].textContent, '新标签页'); touch.ids['touch-actions'].children.find(b => b.dataset.touchId === 'back').onclick(); await flush(); assert.deepEqual(touch.calls.find(call => call.url === '/api/action').body, { action: 'context.execute', contextId: 'window-1', command: 'back', requestId: touch.calls.find(call => call.url === '/api/action').body.requestId });
  touch.ids['touch-scene'].value = 'browser'; touch.ids['touch-scene'].onchange(); touch.current.context = { id: 'window-2', scene: 'general', appName: '', available: true, actions: [] }; await touch.timers(0); assert.equal(touch.ids['touch-actions'].children[0].disabled, true); const touchCalls = touch.calls.length; touch.ids['touch-actions'].children[0].onclick(); await flush(); assert.equal(touch.calls.length, touchCalls, 'Pinned browser actions never execute against another foreground scene');
  touch.ids['touch-scene'].value = 'auto'; touch.ids['touch-scene'].onchange(); assert.equal(touch.ids['touch-actions'].children[0].dataset.touchId, 'pointer'); await touch.hide(); assert.equal(touch.ids['touch-actions'].children.length, 0); assert.equal(touch.ids['touch-scene'].value, 'auto');

  const limited = fixture(); await limited.boot(); limited.ids['note-text'].value = '限流期间保留的笔记'; limited.ids['note-text'].oninput(); limited.ids['task-text'].value = '限流期间保留的任务';
  limited.ids['presentation-toggle'].onclick(); assert.equal(limited.document.body.dataset.presentation, 'true');
  limited.handler(call => call.url === '/api/state' ? response({ error: 'rate_limited', message: '请求频繁' }, 429) : undefined); await limited.timers(2000); const limitedCalls = limited.calls.length;
  assert.equal(limited.document.body.dataset.presentation, 'false'); assert.equal(limited.ids['presentation-toggle'].disabled, true, 'Rate-limit feedback remains visible outside presentation mode');
  assert.match(limited.ids.feedback.textContent, /暂停同步 60 秒/); assert.equal(limited.ids['note-text'].value, '限流期间保留的笔记'); assert.equal(limited.ids['task-text'].value, '限流期间保留的任务');
  limited.handler(undefined); click(limited, 'media.next'); limited.ids['note-save'].onclick(); submit(limited.ids['task-form']); limited.windowListeners.online(); await flush(); await limited.timers(0);
  assert.equal(limited.calls.length, limitedCalls, 'Writes and schedule0 cannot bypass429 cooldown');
  limited.advance(59999); await limited.timers(60000); assert.equal(limited.calls.length, limitedCalls, 'No request before the full60second cooldown');
  limited.advance(1); await limited.timers(1); assert.equal(limited.ids.feedback.hidden, true, 'Successful recovery clears the rate-limit notice'); assert.equal(limited.ids['note-text'].value, '限流期间保留的笔记'); assert.equal(limited.ids['task-text'].value, '限流期间保留的任务');
  assert.equal(limited.calls.filter(call => ['/api/action', '/api/tasks', '/api/notes'].includes(call.url)).length, 0, 'Blocked writes are never replayed after recovery');

  const busy = fixture(); await busy.boot(); busy.handler(call => call.url === '/api/action' ? response({ error: 'busy', message: '繁忙', retryAfter: 1 }, 429, { 'Retry-After': '1' }) : undefined); click(busy, 'media.next'); await flush(); const busyCalls = busy.calls.length;
  busy.ids.reconnect.onclick(); await busy.timers(0); assert.equal(busy.calls.length, busyCalls, 'Explicit reconnect cannot bypass cooldown');
  busy.handler(undefined); busy.advance(999); await busy.timers(1000); assert.equal(busy.calls.length, busyCalls); busy.advance(1); await busy.timers(1); assert.equal(busy.ids.feedback.hidden, true, 'busy resumes after its one-second Retry-After, not sixty seconds'); click(busy, 'media.previous'); await flush(); const writeFeedback = busy.ids.feedback.textContent; await busy.timers(0);
  assert.equal(busy.ids.feedback.textContent, writeFeedback); assert.equal(busy.ids.feedback.hidden, false, 'A successful sync preserves unrelated write feedback'); assert.equal(busy.calls.filter(call => call.url === '/api/action').length, 2, 'Only a fresh explicit click retries after cooldown');
  const budget = fixture(); await budget.boot(); budget.ids['note-text'].value = '预算错误也不能丢失的编辑'; budget.ids['note-text'].oninput(); budget.handler(call => call.url === '/api/notes' ? response({ error: 'session_budget', message: '会话操作额度已用尽，请重新配对。' }, 409) : undefined); budget.ids['note-save'].onclick(); await flush(); assert.equal(budget.ids['note-text'].value, '预算错误也不能丢失的编辑'); assert.equal(budget.ids['note-reload'].hidden, true, 'session_budget409 is not a notes revision conflict'); assert.equal(budget.ids['note-save'].disabled, true); const budgetWrites = budget.calls.filter(call => call.url === '/api/notes').length; budget.ids['note-save'].onclick(); await budget.timers(0); await budget.timers(2000); assert.equal(budget.calls.filter(call => call.url === '/api/notes').length, budgetWrites); assert(budget.calls.filter(call => call.url === '/api/state').length >= 2, 'Read-only synchronization can continue after the write budget is exhausted');
  const changedContext = fixture({ state: contextState }); await changedContext.boot(); changedContext.ids['note-text'].value = '保留的笔记'; changedContext.ids['note-text'].oninput(); changedContext.handler(call => call.url === '/api/action' ? response({ error: 'context_changed', message: '电脑前台已改变，请等待场景更新。' }, 409) : undefined); changedContext.ids['touch-actions'].children[0].onclick(); await flush(); assert.equal(changedContext.ids['note-reload'].hidden, true); assert.equal(changedContext.ids['note-text'].value, '保留的笔记'); await changedContext.timers(0); assert.equal(changedContext.calls.filter(call => call.url === '/api/action').length, 1, 'Changed foreground never replays a context action');
  const readOnlyContext = fixture({ state: contextState, grants: { ...grants, control: false } }); await readOnlyContext.boot(); assert.equal(readOnlyContext.ids['touch-actions'].children[0].disabled, true); readOnlyContext.ids['touch-actions'].children[0].onclick(); assert.equal(readOnlyContext.calls.filter(call => call.url === '/api/action').length, 0);
  const enteringCode = fixture({ handler: call => call.url === '/api/session' ? response({ error: 'unpaired', message: '请配对' }, 401) : undefined });
  await enteringCode.boot(); enteringCode.ids['pair-code'].value = '123'; enteringCode.ids['pair-code'].setSelectionRange(1, 2); await enteringCode.timers(5000);
  assert.equal(enteringCode.ids['pair-form'].hidden, false, 'Only a confirmed unpaired response exposes pairing controls');
  assert.equal(enteringCode.ids['pair-code'].value, '123', 'Unpaired session polling must not erase a code being entered');
  assert.equal(enteringCode.ids['pair-code'].selectionStart, 1); assert.equal(enteringCode.ids['pair-code'].selectionEnd, 2, 'Anonymous401 must not move the caret or selection');
  await enteringCode.timers(5000); assert.equal(enteringCode.ids['pair-code'].value, '123');
  enteringCode.ids['local-widgets'].onclick(); await enteringCode.timers(5000); assert.equal(enteringCode.document.body.dataset.page,'widgets','Anonymous session polling leaves the local tools open'); enteringCode.ids['widgets-back'].onclick(); assert.equal(enteringCode.document.body.dataset.page,'home');
  const unpairedCalls = enteringCode.calls.length; enteringCode.ids['presentation-toggle'].onclick(); assert.equal(enteringCode.document.body.dataset.presentation, 'false'); assert.equal(enteringCode.calls.length, unpairedCalls, 'Presentation cannot start without authenticated state');
  const scanned = fixture({ hash: '#pair=012345', handler: call => call.url === '/api/session' ? response({ error: 'unpaired', message: '请配对' }, 401) : undefined });
  assert.equal(scanned.ids['pair-code'].value, '012345'); assert.deepEqual(scanned.historyChanges, ['/']); assert.equal(scanned.location.hash, ''); assert.equal(scanned.calls.length, 0, 'Fragment is removed and code filled before any API request');
  await scanned.boot(); await scanned.timers(5000); assert.equal(scanned.ids['pair-code'].value, '012345'); assert.equal(scanned.calls.filter(c => c.url === '/api/pair').length, 0, 'Scanning never submits automatically');
  scanned.ids['pair-code'].value = '123 456'; scanned.ids['pair-code'].selectionStart = 7; scanned.ids['pair-code'].selectionEnd = 7; scanned.ids['pair-code'].oninput();
  assert.equal(scanned.ids['pair-code'].value, '123456'); assert.equal(scanned.ids['pair-code'].selectionStart, 6, 'Grouped paste normalizes without losing digits or caret');
  scanned.ids['pair-code'].value = '1234567'; submit(scanned.ids['pair-form']); await flush(); assert.equal(scanned.calls.filter(c => c.url === '/api/pair').length, 0, 'Server contract remains exactly six digits');
  await scanned.hide(); await scanned.show(); assert.equal(scanned.ids['pair-code'].value, '', 'Hidden page clears scanned code without restoring fragment');
  for (const hash of ['#pair=12345', '#pair=1234567', '#pair=123456&extra=1', '#pair=%31%32%33%34%35%36']) { const invalid = fixture({ hash }); assert.equal(invalid.ids['pair-code'].value, ''); assert.equal(invalid.location.hash, ''); }
  const queryCode = fixture({ search: '?pair=123456' }); assert.equal(queryCode.ids['pair-code'].value, '', 'Query parameters never prefill pairing');
  const stableTasks = fixture(); await stableTasks.boot(); const originalTaskRow = stableTasks.ids['task-list'].children[0];
  stableTasks.ids['note-text'].setSelectionRange(1, 2); stableTasks.ids['task-text'].value = '正在输入的任务'; await stableTasks.timers(2000);
  assert.equal(stableTasks.ids['task-list'].children[0], originalTaskRow, 'Unchanged polling preserves task buttons and keyboard focus');
  assert.equal(stableTasks.ids['note-text'].selectionStart, 1); assert.equal(stableTasks.ids['note-text'].selectionEnd, 2, 'Unchanged note polling preserves caret and selection');
  const stableReplacements = stableTasks.ids['task-list'].replacements; stableTasks.current.system.data.cpu = 80; stableTasks.current.tasks.countdownTitle = '倒计时已改名'; await stableTasks.timers(2000); await stableTasks.timers(2000);
  assert.equal(stableTasks.ids['task-list'].replacements, stableReplacements); assert.equal(stableTasks.ids['task-list'].children.length, 1, 'Repeated polling never appends duplicate tasks');
  assert.equal(stableTasks.ids['task-text'].value, '正在输入的任务'); assert.match(stableTasks.ids.countdown.textContent, /倒计时已改名/, 'Countdown updates independently of task row rebuilding');
  stableTasks.current.tasks.revision = 'tasks-2'; stableTasks.current.tasks.items[0].done = true; stableTasks.current.tasks.items.push({ id: 'task2', text: '电脑新增的任务', done: false }); await stableTasks.timers(2000);
  assert.equal(stableTasks.ids['task-list'].children.length, 2); assert.equal(stableTasks.ids['task-list'].children[0].children[0].getAttribute('aria-pressed'), 'true');
  const updatedTaskRow = stableTasks.ids['task-list'].children[0]; let releaseTask;
  stableTasks.handler(call => call.url === '/api/tasks' ? new Promise(resolve => { releaseTask = resolve; }) : undefined); updatedTaskRow.children[0].onclick(); await flush();
  assert.equal(stableTasks.ids['task-list'].children[0], updatedTaskRow); assert.equal(updatedTaskRow.children[0].disabled, true); assert.equal(updatedTaskRow.children[1].disabled, true);
  assert.equal(stableTasks.calls.find(call => call.url === '/api/tasks').body.expectedRevision, 'tasks-2', 'Preserved controls still send the latest task revision');
  releaseTask(response({ ok: true })); await flush(); assert.equal(updatedTaskRow.children[0].disabled, false); assert.equal(stableTasks.ids['task-list'].children[0], updatedTaskRow);
  stableTasks.handler(undefined); stableTasks.current.tasks.items = []; stableTasks.current.tasks.revision = 'tasks-3'; await stableTasks.timers(0); assert.equal(stableTasks.ids['task-list'].children.length, 0); assert.equal(stableTasks.ids['tasks-empty'].hidden, false);
  const emptyReplacements = stableTasks.ids['task-list'].replacements; await stableTasks.timers(2000); assert.equal(stableTasks.ids['task-list'].replacements, emptyReplacements, 'Unchanged empty lists do not rebuild');
  stableTasks.current.tasks.available = false; stableTasks.current.notes.available = false; await stableTasks.timers(2000); assert.equal(stableTasks.ids['task-count'].textContent, '暂不可用'); assert.equal(stableTasks.ids['note-text'].value, '');
  stableTasks.current.tasks.available = true; stableTasks.current.tasks.items = clone(sample.tasks.items); stableTasks.current.notes.available = true; await stableTasks.timers(2000); assert.equal(stableTasks.ids['task-list'].children.length, 1); assert.equal(stableTasks.ids['note-text'].value, sample.notes.text);
  await stableTasks.hide(); await stableTasks.show(); assert.equal(stableTasks.ids['task-list'].children.length, 1, 'Privacy purge resets the render cache before restoring the same snapshot');
  let shareTasks = true; const sharing = fixture({ handler: call => call.url === '/api/session' ? response({ protocol: 1, csrf: 'sharing-csrf', grants: { ...grants, tasks: shareTasks, notes: shareTasks } }) : undefined }); await sharing.boot(); shareTasks = false; sharing.ids.reconnect.onclick(); await sharing.timers(0);
  assert.equal(sharing.ids['task-list'].children.length, 0); assert.equal(sharing.ids['note-text'].value, ''); shareTasks = true; sharing.ids.reconnect.onclick(); await sharing.timers(0); assert.equal(sharing.ids['task-list'].children.length, 1); assert.equal(sharing.ids['note-text'].value, sample.notes.text);
  const filteredTasks = fixture(); filteredTasks.current.tasks.items.push({ id: 'done-task', text: '已完成的事', done: true }); await filteredTasks.boot();
  filteredTasks.ids['note-text'].value = '筛选时保留笔记'; filteredTasks.ids['note-text'].oninput(); filteredTasks.ids['task-text'].value = '筛选时保留任务'; const filterCalls = filteredTasks.calls.length;
  filteredTasks.ids['task-filter-done'].onclick(); assert.equal(filteredTasks.ids['task-list'].children.length, 1); assert.match(filteredTasks.ids['task-list'].textContent, /已完成的事/); assert.equal(filteredTasks.ids['task-count'].textContent, '1 项未完成'); assert.equal(filteredTasks.ids['task-filter-done'].getAttribute('aria-pressed'), 'true');
  filteredTasks.ids['task-filter-open'].onclick(); assert.equal(filteredTasks.ids['task-list'].children.length, 1); filteredTasks.ids['task-filter-all'].onclick(); assert.equal(filteredTasks.ids['task-list'].children.length, 2);
  assert.equal(filteredTasks.ids['note-text'].value, '筛选时保留笔记'); assert.equal(filteredTasks.ids['task-text'].value, '筛选时保留任务'); assert.equal(filteredTasks.calls.length, filterCalls, 'Local filters never fetch or replay actions');
  filteredTasks.ids['task-filter-done'].onclick(); filteredTasks.ids['task-list'].children[0].children[0].onclick(); await flush(); const filteredWrite = filteredTasks.calls.find(call => call.url === '/api/tasks'); assert.equal(filteredWrite.body.id, 'done-task'); assert.equal(filteredWrite.body.expectedRevision, sample.tasks.revision);
  filteredTasks.current.tasks.items[1].done = false; filteredTasks.current.tasks.revision = 'filter-revision-2'; await filteredTasks.timers(0); assert.equal(filteredTasks.ids['task-list'].children.length, 0); assert.match(filteredTasks.ids['tasks-empty'].textContent, /还没有已完成/); assert.equal(filteredTasks.ids['task-count'].textContent, '2 项未完成');

  const presenting = fixture(); await presenting.boot(); presenting.ids['all-apps'].onclick(); const presentationCalls = presenting.calls.length; assert.equal(presenting.document.body.dataset.page, 'apps');
  presenting.ids['presentation-toggle'].onclick(); assert.equal(presenting.document.body.dataset.presentation, 'true'); assert.equal(presenting.document.body.dataset.page, 'home'); assert.equal(presenting.ids['presentation-toggle'].getAttribute('aria-pressed'), 'true');
  presenting.ids['presentation-exit'].onclick(); assert.equal(presenting.document.body.dataset.presentation, 'false'); assert.equal(presenting.calls.length, presentationCalls, 'Entering and exiting presentation are local-only');
  presenting.ids['presentation-toggle'].onclick(); await presenting.hide(); assert.equal(presenting.document.body.dataset.presentation, 'false'); await presenting.show(); presenting.ids['presentation-toggle'].onclick(); presenting.windowListeners.offline(); assert.equal(presenting.document.body.dataset.presentation, 'false'); assert.equal(presenting.ids['pair-panel'].hidden, false, 'Offline state exits presentation and exposes connection UI');
  const f = fixture(); await f.boot(); f.ids['work-tab'].onclick(); await flush();
  assert.equal(f.ids['connection-status'].textContent, '已连接电脑');
  assert.equal(f.ids['track-title'].textContent, '真实曲目'); assert.equal(f.ids['cpu-value'].textContent, '37%');
  assert.equal(f.ids['volume-value'].textContent, '45%'); assert.equal(f.ids['disk-value'].textContent, '未提供'); assert.equal(f.ids['disk-metric'].hidden, true);
  assert.equal(f.ids['note-text'].value, '私人笔记'); assert.equal(f.ids['task-text'].maxLength, 120); assert.equal(f.ids['note-text'].maxLength, 32768);
  assert.equal(f.ids['task-list'].children[0].children[0].children[1].textContent, '<script>私人任务</script>');
  assert.equal(f.ids['quick-apps'].children[1].disabled, true);
  assert(!/innerHTML|localStorage|sessionStorage|serviceWorker/.test(source), 'No markup injection or private browser database');

  let release;
  f.handler(call => call.url === '/api/action' ? new Promise(resolve => { release = resolve; }) : undefined);
  const appButton = f.ids['quick-apps'].children[0]; appButton.onclick(); appButton.onclick(); await flush();
  let actions = f.calls.filter(c => c.url === '/api/action'); assert.equal(actions.length, 1, 'Duplicate nonidempotent click suppressed');
  assert.equal(actions[0].body.expectedRevision, 'apps-1'); assert.equal(actions[0].body.targetId, 'remote-safe'); assert.equal(actions[0].request.headers['X-CSRF-Token'], 'test-csrf'); assert.match(actions[0].body.requestId, /^[a-f0-9]{32}$/);
  release(response({ ok: true })); await flush();

  f.handler(undefined); f.ids['note-text'].value = '本页正在编辑'; f.ids['note-text'].oninput();
  f.current.notes = { available: true, revision: '638000000000000004', text: '电脑有新的内容' }; await f.timers(0);
  assert.equal(f.ids['note-text'].value, '本页正在编辑', 'Polling must not overwrite edits'); assert.equal(f.ids['note-save'].disabled, true); assert.equal(f.ids['note-reload'].hidden, false);
  f.ids['note-reload'].onclick(); assert.equal(f.ids['note-text'].value, '电脑有新的内容');

  f.ids['task-text'].value = '新待办'; submit(f.ids['task-form']); await flush();
  const taskWrite = f.calls.find(c => c.url === '/api/tasks'); assert.equal(taskWrite.body.expectedRevision, '638000000000000002', 'Keep precise opaque revisions'); assert.equal(taskWrite.body.text, '新待办');
  f.ids['note-text'].value = '已保存的新笔记'; f.ids['note-text'].oninput(); f.ids['note-save'].onclick(); await flush();
  assert.equal(f.ids['note-text'].value, '已保存的新笔记', 'Successful save must not flash old server snapshot');

  await f.hide(); assert.equal(f.ids['note-text'].value, ''); assert.equal(f.ids['task-list'].children.length, 0); assert.equal(f.ids['track-title'].textContent, ''); assert.equal(f.ids.cover.getAttribute('src'), null); assert.equal(f.ids['app-grid'].children.length, 0); assert.equal(f.ids['apps-page'].hidden, true);
  const count = f.calls.length; await f.timers(2000); await f.timers(0); assert.equal(f.calls.length, count, 'No polling when hidden'); await f.show(); assert.equal(f.ids['track-title'].textContent, '真实曲目');

  let late;
  f.handler(call => call.url === '/api/state' ? new Promise(resolve => { late = resolve; }) : undefined);
  await f.timers(2000); await f.hide(); late(response(sample)); await flush(); assert.equal(f.ids['track-title'].textContent, '', 'Late pre-hide response cannot restore private DOM');

  const locked = fixture({ handler: call => call.url === '/api/state' ? response({ error: 'locked', message: '电脑已锁定' }, 423) : undefined }); await locked.boot(); assert.equal(locked.ids['connection-status'].textContent, '电脑已锁定'); assert.equal(locked.ids['note-text'].value, '');
  const revokedSession = fixture(); await revokedSession.boot(); revokedSession.ids['presentation-toggle'].onclick(); revokedSession.ids['pair-code'].value = '654321'; revokedSession.ids['note-text'].value = '不能保留的旧会话内容'; revokedSession.ids['note-text'].oninput(); revokedSession.ids['task-text'].value = '旧会话待办';
  revokedSession.handler(call => call.url === '/api/state' ? response({ error: 'unpaired', message: '设备已撤销' }, 401) : undefined); await revokedSession.timers(2000);
  assert.equal(revokedSession.ids['note-text'].value, ''); assert.equal(revokedSession.ids['task-text'].value, ''); assert.equal(revokedSession.ids['pair-code'].value, '', 'Authenticated session401 still clears sensitive content'); assert.equal(revokedSession.document.body.dataset.presentation, 'false');
  const lockedScan = fixture({ hash: '#pair=123456', handler: call => call.url === '/api/session' ? response({ message: '电脑锁定' }, 423) : undefined }); await lockedScan.boot(); assert.equal(lockedScan.ids['pair-code'].value, '');
  const denied = fixture({ grants: { apps: false, music: false, tasks: false, notes: false, control: false } }); await denied.boot(); assert.equal(denied.ids['note-text'].value, ''); assert.equal(denied.ids['task-list'].children.length, 0); assert.equal(denied.ids['apps-tab'].disabled, true); assert.equal(denied.ids['track-title'].textContent, '音乐尚未共享');

  const unsafe = clone(sample); unsafe.music.cover = 'https://prism-media.local/private.png'; const u = fixture({ state: unsafe }); await u.boot(); assert.equal(u.ids.cover.hidden, true); assert.equal(u.ids.cover.getAttribute('src'), null);

  const changedApps = fixture(); await changedApps.boot(); changedApps.ids['work-tab'].onclick(); changedApps.applications.items[0].name = '电脑重命名后的入口'; changedApps.applications.revision = 'apps-2';
  changedApps.advance(11000); await changedApps.timers(2000); assert.match(changedApps.ids['quick-apps'].textContent, /电脑重命名后的入口/, 'Overview refreshes shared apps without opening the app page');
  changedApps.handler(() => { throw new TypeError('Failed to fetch'); }); await changedApps.timers(2000);
  assert.equal(changedApps.ids['note-text'].value, ''); assert.match(changedApps.ids.feedback.textContent, /连接中断/); assert.doesNotMatch(changedApps.ids.feedback.textContent, /Failed to fetch/);

  const version = fixture(); await version.boot(); version.ids['note-text'].value = '保留编辑以便复制'; version.ids['note-text'].oninput(); version.current.protocol = 2; await version.timers(2000);
  assert.equal(version.ids['note-text'].value, '保留编辑以便复制'); assert.equal(version.ids['note-save'].disabled, true); click(version, 'media.next'); await flush(); assert.equal(version.calls.filter(c => c.url === '/api/action').length, 0, 'Version mismatch stops writes without silently losing unsaved text');

  const timeout = fixture(); await timeout.boot(); timeout.handler(call => call.url === '/api/action' ? new Promise((resolve, reject) => call.request.signal.addEventListener('abort', () => reject(new Error('aborted')))) : undefined);
  click(timeout, 'media.next'); await flush(); await timeout.timers(6500); assert.equal(timeout.ids['track-title'].textContent, ''); assert.match(timeout.ids.feedback.textContent, /结果未知/);
  timeout.handler(undefined); await timeout.timers(0); assert.equal(timeout.calls.filter(c => c.url === '/api/action').length, 1, 'Reconnect never replays action');

  const draft = fixture(); await draft.boot();
  const noteDraft = '  未确认保存的笔记\n第二行  ', taskDraft = '  尚未提交的待办  ';
  draft.ids['note-text'].value = noteDraft; draft.ids['note-text'].oninput(); draft.ids['task-text'].value = taskDraft;
  draft.handler(call => call.url === '/api/notes' ? new Promise((resolve, reject) => call.request.signal.addEventListener('abort', () => reject(new Error('aborted')))) : undefined);
  draft.ids['note-save'].onclick(); await flush(); await draft.timers(6500);
  assert.equal(draft.ids['note-text'].value, ''); assert.equal(draft.ids['task-text'].value, ''); assert.equal(draft.ids['tasks-card'].hidden, true, 'Offline drafts never remain in private DOM');
  draft.handler(() => { throw new TypeError('Failed to fetch'); }); await draft.timers(0); await draft.timers(5000);
  draft.current.notes = { available: true, revision: '638000000000000099', text: '电脑可能已经保存了本次写入' };
  draft.handler(undefined); await draft.timers(5000);
  assert.equal(draft.ids['note-text'].value, noteDraft, 'Reconnect restores exact unsaved note after repeated network failures');
  assert.equal(draft.ids['task-text'].value, taskDraft, 'Reconnect restores exact unsent task');
  assert.equal(draft.ids['note-save'].disabled, true); assert.equal(draft.ids['note-reload'].hidden, false, 'Original note revision detects uncertain success or concurrent edits');
  assert.equal(draft.calls.filter(c => c.url === '/api/notes').length, 1, 'Saved drafts never replay a write');
  assert.equal(draft.calls.find(c => c.url === '/api/notes').body.expectedRevision, sample.notes.revision);

  const emptyNote = fixture(); await emptyNote.boot(); emptyNote.ids['note-text'].value = ''; emptyNote.ids['note-text'].oninput(); emptyNote.windowListeners.offline(); emptyNote.windowListeners.online(); await emptyNote.timers(0);
  assert.equal(emptyNote.ids['note-text'].value, '', 'Intentional note deletion is also an unsaved draft'); assert.equal(emptyNote.ids['note-save'].disabled, false);

  const taskFailure = fixture(); await taskFailure.boot(); taskFailure.ids['task-text'].value = taskDraft;
  taskFailure.handler(call => { if (call.url === '/api/tasks') throw new TypeError('Failed to fetch'); }); submit(taskFailure.ids['task-form']); await flush();
  assert.equal(taskFailure.ids['task-text'].value, ''); taskFailure.handler(undefined); await taskFailure.timers(0);
  assert.equal(taskFailure.ids['task-text'].value, taskDraft); assert.equal(taskFailure.calls.filter(c => c.url === '/api/tasks').length, 1);

  for (const reset of ['hidden', 'pagehide', 'locked', 'revoked', 'logout']) {
    const discarded = fixture(); await discarded.boot(); discarded.ids['note-text'].value = noteDraft; discarded.ids['note-text'].oninput(); discarded.ids['task-text'].value = taskDraft;
    discarded.windowListeners.offline();
    if (reset === 'hidden') { await discarded.hide(); await discarded.show(); }
    else if (reset === 'pagehide') { discarded.windowListeners.pagehide({ type: 'pagehide' }); discarded.windowListeners.pageshow(); await discarded.timers(0); }
    else if (reset === 'locked' || reset === 'revoked') {
      discarded.handler(call => call.url === '/api/session' ? response({ message: '不可用' }, reset === 'locked' ? 423 : 401) : undefined);
      discarded.windowListeners.online(); await discarded.timers(0); discarded.handler(undefined); await discarded.timers(5000);
    } else { discarded.windowListeners.online(); await discarded.timers(0); discarded.ids.logout.onclick(); await flush(); discarded.ids.reconnect.onclick(); await discarded.timers(0); }
    assert.equal(discarded.ids['note-text'].value, sample.notes.text, reset + ' must discard retained note draft'); assert.equal(discarded.ids['task-text'].value, '', reset + ' must discard retained task draft');
  }

  const permissionLoss = fixture(); await permissionLoss.boot(); permissionLoss.ids['note-text'].value = noteDraft; permissionLoss.ids['note-text'].oninput(); permissionLoss.ids['task-text'].value = taskDraft; permissionLoss.windowListeners.offline();
  permissionLoss.handler(call => call.url === '/api/session' ? response({ protocol: 1, csrf: 'new-csrf', grants: { apps: true, music: true, control: true, notes: false, tasks: false } }) : undefined);
  permissionLoss.windowListeners.online(); await permissionLoss.timers(0); assert.equal(permissionLoss.ids['note-text'].value, ''); assert.equal(permissionLoss.ids['task-text'].value, '');
  permissionLoss.handler(undefined); permissionLoss.ids.reconnect.onclick(); await permissionLoss.timers(0); assert.equal(permissionLoss.ids['note-text'].value, sample.notes.text); assert.equal(permissionLoss.ids['task-text'].value, '', 'Removed sharing grants permanently discard corresponding drafts');

  const volume = fixture(); await volume.boot();
  for (let n = 10; n <= 70; n++) { volume.ids['control-volume'].value = n; volume.ids['control-volume'].oninput(); }
  await volume.timers(2000); assert.equal(volume.ids['control-volume'].value,'70','Polling never fights an active volume gesture');
  assert.equal(volume.calls.filter(c => c.url === '/api/action').length,0,'Volume drag previews locally without flooding requests');
  volume.ids['control-volume'].onchange(); await flush(); assert.equal(volume.calls.filter(c => c.url === '/api/action').length,1); assert.equal(volume.calls.find(c => c.url === '/api/action').body.value,70);
  volume.ids['control-volume'].value=83; volume.ids['control-volume'].oninput(); await volume.hide(); volume.current.volume.data.value=23; await volume.show(); assert.equal(volume.ids['control-volume'].value,'23','Interrupted volume gesture restores authoritative volume after reconnect');
  const restoredPage = fixture({state:contextState}); await restoredPage.boot(); restoredPage.ids['computer-tab'].onclick(); restoredPage.ids['touch-scene'].value='browser'; restoredPage.ids['touch-scene'].onchange(); await restoredPage.hide(); assert.equal(restoredPage.document.body.dataset.page,'home'); await restoredPage.show(); assert.equal(restoredPage.document.body.dataset.page,'computer'); assert.equal(restoredPage.ids['touch-scene'].value,'browser','Reconnect restores the page and pinned scene only after authentication');
  restoredPage.ids['desktop-overview'].onclick(); await flush(); assert.equal(restoredPage.calls.find(c=>c.url==='/api/action').body.command,'task-view');
  const pinned = fixture({state:contextState}); await pinned.boot(); pinned.ids['touch-pin'].onclick(); assert.equal(pinned.ids['touch-scene'].value,'browser'); pinned.current.context.scene='general'; pinned.current.context.id='new-context'; await pinned.timers(2000); assert.equal(pinned.ids['touch-scene'].value,'browser'); assert(pinned.ids['touch-actions'].children.every(n=>n.disabled)); pinned.ids['touch-pin'].onclick(); assert.equal(pinned.ids['touch-scene'].value,'auto'); assert.equal(pinned.calls.filter(c=>c.url==='/api/action').length,0,'Pinning never dispatches desktop input');
  const immersion = fixture(); await immersion.boot(); immersion.ids['presentation-mode'].value='lyrics'; immersion.ids['presentation-toggle'].onclick(); assert.equal(immersion.document.body.dataset.presentationMode,'lyrics'); immersion.ids['presentation-mode'].value='clock'; immersion.ids['presentation-mode'].onchange(); assert.equal(immersion.document.body.dataset.presentationMode,'clock'); assert.equal(immersion.document.body.dataset.presentation,'true'); await immersion.hide(); assert.equal(immersion.document.body.dataset.presentation,'false','Hidden page always exits every immersion layout');
  await immersion.show(); immersion.ids['presentation-mode'].value='lyrics'; immersion.ids['presentation-toggle'].onclick(); immersion.current.permissions={...grants,music:false}; await immersion.timers(2000); assert.equal(immersion.document.body.dataset.presentationMode,'clock','Revoking music during lyric presentation preserves a useful clock view');
  const desktopCommands=fixture({state:contextState}); await desktopCommands.boot(); for(const [id,command] of [['desktop-maximize','maximize'],['desktop-restore','restore'],['desktop-previous-space','previous-desktop'],['desktop-next-space','next-desktop']]) { desktopCommands.ids[id].onclick(); await flush(); const call=desktopCommands.calls.filter(c=>c.url==='/api/action').pop(); assert.equal(call.body.command,command); assert.equal(call.body.contextId,contextState.context.id); }

  const waiting = fixture({ handler: call => call.url === '/api/session' ? response({ error: 'unpaired', message: '请配对' }, 401) : call.url === '/api/pair' ? response(call.body.code ? { status: 'pending', ticket: 'waiting-ticket' } : { status: 'pending' }, 202) : undefined });
  await waiting.boot(); waiting.ids['pair-code'].value = '123 456'; submit(waiting.ids['pair-form']); await flush();
  assert.equal(waiting.calls.find(c => c.url === '/api/pair').body.code, '123456'); assert.match(waiting.ids['pair-status'].textContent, /电脑.*批准/);
  waiting.advance(31000); await waiting.timers(2000); assert.match(waiting.ids['pair-status'].textContent, /仍在等待电脑批准/);
  const waitingCalls = waiting.calls.length; waiting.advance(90000); await waiting.timers(2000); assert.match(waiting.ids['pair-status'].textContent, /已超时.*新的二维码或配对码/); assert.equal(waiting.ids['pair-submit'].disabled, false);
  await waiting.timers(2000); assert.equal(waiting.calls.length, waitingCalls + 1, 'Deadline permits one final query, then pending approval stops without endless polling');
  const finalTimeout = fixture({ handler: call => call.url === '/api/session' ? response({ error: 'unpaired', message: '请配对' }, 401) : call.url === '/api/pair' ? call.body.code ? response({ status: 'pending', ticket: 'final-ticket' }, 202) : new Promise((resolve, reject) => call.request.signal.addEventListener('abort', () => reject(new Error('aborted')))) : undefined });
  await finalTimeout.boot(); finalTimeout.ids['pair-code'].value = '123456'; submit(finalTimeout.ids['pair-form']); await flush(); finalTimeout.advance(120001); await finalTimeout.timers(2000);
  await finalTimeout.timers(6500); assert.match(finalTimeout.ids['pair-status'].textContent, /连接超时/); assert.equal(finalTimeout.ids['pair-submit'].disabled, false);
  await finalTimeout.timers(2000); assert.equal(finalTimeout.calls.filter(c => c.url === '/api/pair').length, 2, 'Final approval query has one6.5second timeout and cannot continue polling');
  const expired = fixture({ handler: call => call.url === '/api/session' ? response({ error: 'unpaired', message: '请配对' }, 401) : call.url === '/api/pair' ? call.body.code ? response({ status: 'pending', ticket: 'expired-ticket' }, 202) : response({ error: 'pair_expired', message: '配对请求已过期，请重新输入电脑上的配对码。' }, 401) : undefined });
  await expired.boot(); expired.ids['pair-code'].value = '123456'; submit(expired.ids['pair-form']); await flush(); await expired.timers(2000);
  assert.match(expired.ids['pair-status'].textContent, /已过期/); assert.equal(expired.ids['pair-submit'].disabled, false, 'Server expiry remains visible beside the input');
  const wrongCode = fixture({ handler: call => call.url === '/api/session' ? response({ error: 'unpaired', message: '请配对' }, 401) : call.url === '/api/pair' ? response({ error: 'pair_code', message: '配对码不正确或已过期。' }, 401) : undefined });
  await wrongCode.boot(); wrongCode.ids['pair-code'].value = '654 321'; submit(wrongCode.ids['pair-form']); await flush(); assert.equal(wrongCode.ids['pair-code'].value, '654321'); assert.match(wrongCode.ids['pair-status'].textContent, /不正确或已过期/);

  let paired = false;
  const pair = fixture({ handler: call => call.url === '/api/session' && !paired ? response({ error: 'unpaired', message: '请配对' }, 401) : call.url === '/api/pair' ? (call.body.code ? response({ status: 'pending', ticket: 'temporary-ticket' }, 202) : (paired = true, response({ status: 'paired' }))) : undefined });
  await pair.boot(); pair.ids['pair-code'].value = '123456'; pair.ids['device-name'].value = '测试 iPad'; submit(pair.ids['pair-form']); await flush(); assert.equal(pair.ids['pair-submit'].disabled, true); pair.advance(120001); await pair.timers(2000); await pair.timers(0); assert.equal(pair.ids['connection-status'].textContent, '已连接电脑', 'A late approval is collected by the final query even just after the client deadline');
  assert.deepEqual(pair.calls.filter(c => c.url === '/api/pair').map(c => c.body), [{ code: '123456', name: '测试 iPad' }, { ticket: 'temporary-ticket' }]);
  const noControls = fixture(); await noControls.boot(); noControls.ids['pointer-tab'].onclick();
  assert.equal(noControls.ids['pointer-enable'].disabled, true); noControls.ids['pointer-enable'].onclick(); noControls.ids['power-shutdown'].onclick(); await flush();
  assert(!noControls.calls.some(c => /api\/(pointer|power)/.test(c.url)), 'Old grants never acquire new controls');
  const controls = fixture({ state: { ...sample, permissions: { ...grants, pointer: true, power: true } } });
  const pointerPulse = async f => { f.advance(17); await f.timers(17); };
  let releasePointer, pointerCount = 0;
  controls.handler(c => c.url === '/api/pointer/begin' ? response({ leaseId: 'lease', expiresAt: Date.now() + 20000 }) : c.url === '/api/pointer' ? (++pointerCount, new Promise(resolve => { releasePointer = resolve; })) : undefined);
  await controls.boot(); controls.ids['pointer-tab'].onclick(); await controls.ids['pointer-enable'].onclick(); assert.equal(controls.ids['pointer-surface'].dataset.active, 'true', 'State permissions update an existing session');
  const pointerEvent = (x, y, id = 1) => ({ pointerId: id, clientX: x, clientY: y, preventDefault() {} });
  controls.ids['pointer-surface'].onpointerdown(pointerEvent(0, 0));
  for (let i = 1; i <= 8; i++) controls.ids['pointer-surface'].onpointermove(pointerEvent(i * 2, i));
  await pointerPulse(controls); assert.equal(pointerCount, 1); assert.equal(controls.calls.find(c => c.url === '/api/pointer').body.kind, 'move');
  controls.ids['pointer-surface'].onpointermove(pointerEvent(70, 30)); await pointerPulse(controls); assert.equal(pointerCount, 1, 'Pointer permits only one in-flight sample');
  controls.advance(250); releasePointer(response({ expiresAt: Date.now() + 20000 })); await flush(); await pointerPulse(controls); assert.equal(pointerCount, 1, 'Stale movement is dropped rather than replayed');
  controls.ids['pointer-surface'].onpointermove(pointerEvent(80, 40)); await pointerPulse(controls); assert.equal(pointerCount, 2); assert.equal(controls.calls.filter(c => c.url === '/api/pointer')[1].body.seq, 2);
  releasePointer(response({ expiresAt: Date.now() + 20000 })); await flush(); controls.ids['home-tab'].onclick(); await flush(); assert.equal(controls.ids['pointer-surface'].dataset.active, 'false'); assert(controls.calls.some(c => c.url === '/api/pointer/end'));
  controls.current.permissions.pointer = false; await controls.timers(2000); assert.equal(controls.ids['pointer-enable'].disabled, true);

  const slowScroll = fixture({ state: { ...sample, permissions: { ...grants, pointer: true } } });
  slowScroll.handler(c => c.url === '/api/pointer/begin' ? response({ leaseId: 'slow', expiresAt: Date.now() + 20000 }) : c.url === '/api/pointer' ? response({ expiresAt: Date.now() + 20000 }) : undefined);
  await slowScroll.boot(); slowScroll.ids['pointer-tab'].onclick(); await slowScroll.ids['pointer-enable'].onclick();
  slowScroll.ids['pointer-surface'].onpointerdown(pointerEvent(0, 100)); slowScroll.ids['pointer-surface'].onpointerdown(pointerEvent(30, 100, 2));
  for (let i = 1; i <= 6; i++) { slowScroll.ids['pointer-surface'].onpointermove(pointerEvent(0, 100 - i * 8)); await pointerPulse(slowScroll); }
  assert.equal(slowScroll.calls.filter(c => c.url === '/api/pointer' && c.body.kind === 'scroll').length, 1, 'Slow two-finger movement retains sub-step scroll remainder');
  let finishMove; slowScroll.handler(c => c.url === '/api/pointer' && c.body.kind === 'move' ? new Promise(resolve => { finishMove = resolve; }) : c.url === '/api/pointer' ? response({ expiresAt: Date.now() + 20000 }) : undefined);
  slowScroll.ids['pointer-surface'].onkeydown({ key: 'ArrowRight', preventDefault() {} }); await pointerPulse(slowScroll); slowScroll.ids['pointer-left'].onclick(); await flush();
  assert.equal(slowScroll.calls.filter(c => c.url === '/api/pointer' && c.body.kind === 'click').length, 0); finishMove(response({ expiresAt: Date.now() + 20000 })); await flush(); await pointerPulse(slowScroll);
  assert.equal(slowScroll.calls.filter(c => c.url === '/api/pointer' && c.body.kind === 'click').length, 1, 'One fresh click follows a successful in-flight move instead of disappearing');
  const pointerPackets = f => f.calls.filter(c => c.url === '/api/pointer').map(c => c.body);
  const readyPointer = async handler => {
    const f = fixture({ state: { ...sample, permissions: { ...grants, pointer: true } } });
    f.handler(c => { if (handler) { const got = handler(c); if (got !== undefined) return got; } return c.url === '/api/pointer/begin' ? response({ leaseId: 'gesture', expiresAt: Date.now() + 120000 }) : c.url === '/api/pointer' ? response({ expiresAt: Date.now() + 120000 }) : undefined; });
    await f.boot(); f.ids['pointer-tab'].onclick(); await f.ids['pointer-enable'].onclick(); return f;
  };
  const startDrag = async f => { const pad = f.ids['pointer-surface']; pad.onpointerdown(pointerEvent(0, 0)); pad.onpointerup(pointerEvent(0, 0)); await pointerPulse(f); pad.onpointerdown(pointerEvent(0, 0)); await pointerPulse(f); };
  const strip = await readyPointer(); strip.ids['pointer-wheel'].onpointerdown(pointerEvent(0, 100)); strip.ids['pointer-wheel'].onpointermove(pointerEvent(0, 148)); await pointerPulse(strip);
  assert.equal(pointerPackets(strip)[0].kind, 'scroll'); assert.equal(pointerPackets(strip)[0].steps, 2, 'Dedicated wheel sends the same bounded scroll packets without mouse movement'); strip.ids['pointer-wheel'].onpointerup(pointerEvent(0, 148));
  strip.ids['wheel-down'].onclick(); await pointerPulse(strip); assert.equal(pointerPackets(strip).at(-1).steps, -1);
  strip.ids['pointer-wheel'].onkeydown({key:'PageUp',preventDefault(){}}); await pointerPulse(strip); assert.equal(pointerPackets(strip).at(-1).steps, 5);
  strip.ids['pointer-wheel'].onpointerdown(pointerEvent(0, 0)); strip.ids['pointer-wheel'].onpointercancel(pointerEvent(0, 0)); await flush(); assert.equal(strip.ids['pointer-wheel'].dataset.active, 'false'); assert.equal(strip.ids['wheel-up'].disabled, true); assert(strip.calls.some(c=>c.url==='/api/pointer/end'));
  const smooth = await readyPointer(); smooth.ids['pointer-speed'].value = '50'; smooth.ids['pointer-speed'].oninput(); assert.equal(smooth.ids['pointer-speed-value'].textContent, '50%'); smooth.ids['pointer-surface'].onpointerdown(pointerEvent(0, 0));
  for (let i = 1; i <= 7; i++) { smooth.ids['pointer-surface'].onpointermove(pointerEvent(i * .2, 0)); await pointerPulse(smooth); }
  assert.equal(pointerPackets(smooth).reduce((sum, body) => sum + (body.dx || 0), 0), 1, 'Subpixel movement accumulates across samples instead of being rounded away');
  smooth.ids['pointer-speed'].value = '200'; smooth.ids['pointer-speed'].oninput(); smooth.ids['pointer-surface'].onpointermove(pointerEvent(11.4, 0)); await pointerPulse(smooth); assert.equal(pointerPackets(smooth).at(-1).dx, 30, 'Sensitivity changes the next movement without persisting private data');
  for (const natural of [true, false]) {
    const wheel = await readyPointer(); wheel.ids['pointer-natural'].checked = natural; wheel.ids['pointer-natural'].onchange(); const pad = wheel.ids['pointer-surface']; pad.onpointerdown(pointerEvent(0, 0)); pad.onpointerdown(pointerEvent(20, 0, 2)); pad.onpointermove(pointerEvent(0, 48)); pad.onpointermove(pointerEvent(20, 48, 2)); await pointerPulse(wheel);
    assert.equal(pointerPackets(wheel)[0].steps, natural ? 2 : -2, 'Natural and traditional scrolling reverse the same two-finger gesture');
  }
  const rightTap = await readyPointer(), rightPad = rightTap.ids['pointer-surface']; rightPad.onpointerdown(pointerEvent(0, 0)); rightPad.onpointerdown(pointerEvent(20, 0, 2)); rightPad.onpointerup(pointerEvent(0, 0)); await pointerPulse(rightTap); assert.equal(pointerPackets(rightTap).length, 0); rightPad.onpointerup(pointerEvent(20, 0, 2)); await pointerPulse(rightTap); assert.deepEqual(pointerPackets(rightTap).map(body => [body.kind, body.button]), [['click', 'right']], 'Two-finger tap emits only one right click after both fingers lift');
  const drag = await readyPointer(); await startDrag(drag); assert.deepEqual(pointerPackets(drag).map(body => [body.kind, body.phase || body.button]), [['click', 'left'], ['drag', 'start']], 'Double tap and hold starts a distinct left-button drag');
  for (let i = 0; i < 15; i++) await pointerPulse(drag); assert.equal(pointerPackets(drag).at(-1).phase, 'hold', 'Stationary dragging sends a bounded heartbeat before server release deadline');
  drag.ids['pointer-surface'].onpointermove(pointerEvent(12, 5)); await pointerPulse(drag); drag.ids['pointer-surface'].onpointerup(pointerEvent(12, 5)); await pointerPulse(drag); assert.equal(pointerPackets(drag).at(-1).phase, 'end', 'Lifting the dragging finger sends explicit mouse-up');
  const dragScroll = await readyPointer(); await startDrag(dragScroll); dragScroll.ids['pointer-surface'].onpointerdown(pointerEvent(20, 0, 2)); dragScroll.ids['pointer-surface'].onpointermove(pointerEvent(0, 48)); dragScroll.ids['pointer-surface'].onpointermove(pointerEvent(20, 48, 2)); await pointerPulse(dragScroll); await pointerPulse(dragScroll); assert.deepEqual(pointerPackets(dragScroll).slice(-2).map(body => [body.kind, body.phase || body.steps]), [['drag', 'end'], ['scroll', 2]], 'Adding a second finger releases a drag before sending scroll input');
  let mixedRelease; const mixed = await readyPointer(c => c.url === '/api/pointer' && !mixedRelease ? new Promise(resolve => { mixedRelease = resolve; }) : undefined); mixed.ids['pointer-surface'].onpointerdown(pointerEvent(0, 0)); mixed.ids['pointer-surface'].onpointermove(pointerEvent(10, 0)); await pointerPulse(mixed); mixed.ids['pointer-surface'].onpointermove(pointerEvent(20, 0)); mixed.advance(210); mixed.ids['pointer-surface'].onpointermove(pointerEvent(30, 0)); mixedRelease(response({ expiresAt: Date.now() + 120000 })); await flush(); await pointerPulse(mixed); assert.equal(pointerPackets(mixed).at(-1).dx, 15, 'Fresh input does not renew the age of older movement waiting behind a request');
  for (const reset of ['hidden', 'pagehide', 'blur', 'cancel', 'capture', 'stop', 'leave', 'revoke', 'network']) {
    const released = await readyPointer(); await startDrag(released);
    if (reset === 'hidden') await released.hide();
    else if (reset === 'pagehide') { released.windowListeners.pagehide({ type: 'pagehide' }); await flush(); }
    else if (reset === 'blur') released.windowListeners.blur();
    else if (reset === 'cancel') released.ids['pointer-surface'].onpointercancel(pointerEvent(0, 0));
    else if (reset === 'capture') released.ids['pointer-surface'].onlostpointercapture(pointerEvent(0, 0));
    else if (reset === 'stop') released.ids['pointer-stop'].onclick();
    else if (reset === 'leave') released.ids['home-tab'].onclick();
    else if (reset === 'revoke') { released.current.permissions.pointer = false; await released.timers(2000); }
    else { released.handler(c => c.url === '/api/pointer' ? Promise.reject(new TypeError('fixture offline')) : undefined); released.ids['pointer-surface'].onpointermove(pointerEvent(12, 0)); await pointerPulse(released); }
    await flush(); const ends = released.calls.filter(c => c.url === '/api/pointer/end'); assert.equal(released.ids['pointer-surface'].dataset.active, 'false'); assert(ends.length >= 1 && ends.every(c => c.request.keepalive && c.request.headers['X-CSRF-Token'] === 'test-csrf'), reset + ' attempts authenticated keepalive release even while private requests are aborted');
  }
  let elapsed = 0; const wireTimes = [], pointerBudget = await readyPointer(c => { if (/^\/api\/pointer/.test(c.url)) wireTimes.push(elapsed); }); pointerBudget.ids['pointer-surface'].onpointerdown(pointerEvent(0, 0));
  for (let i = 1; i <= 3529; i++) { elapsed += 17; pointerBudget.ids['pointer-surface'].onpointermove(pointerEvent(i, 0)); await pointerPulse(pointerBudget); }
  pointerBudget.ids['pointer-stop'].onclick(); await flush(); assert(pointerPackets(pointerBudget).length >= 3500 && wireTimes.length <= 5400, 'Continuous healthy coalescing approaches 59 Hz within the bounded pointer transport budget');
  for (let end = 0, start = 0; end < wireTimes.length; end++) { while (wireTimes[end] - wireTimes[start] >= 1000) start++; assert(end - start + 1 <= 61 && end - start + 1 <= 90, 'Near-sixty-Hz sampling includes begin/end with room in the ninety-request device budget'); }
  const power = fixture({ state: { ...sample, permissions: { ...grants, power: true }, controls: { power: { status: 'idle' } } } });
  power.handler(c => c.url === '/api/power/prepare' ? response({ ticketId: 'confirm-ticket', expiresAt: Date.now() + 30000 }) : c.url === '/api/power/commit' ? response({ status: 'pending', action: 'shutdown', id: 'job', executeAt: Date.now() + 15000 }, 202) : c.url === '/api/power/cancel' ? response({ status: 'cancelled', id: 'job', message: '已取消' }) : undefined);
  await power.boot(); power.ids['computer-tab'].onclick(); await power.ids['power-shutdown'].onclick(); await flush();
  assert.equal(power.ids['power-confirm'].hidden, false); assert.match(power.ids['power-confirm-detail'].textContent, /15 秒/); assert(!power.calls.some(c => c.url === '/api/power/commit'), 'Choosing shutdown only prepares a confirmation');
  power.ids['power-dismiss'].onclick(); power.ids['power-commit'].onclick(); await flush(); assert(!power.calls.some(c => c.url === '/api/power/commit'));
  await power.ids['power-shutdown'].onclick(); await flush(); power.ids['power-commit'].onclick(); power.ids['power-commit'].onclick(); await flush();
  assert.equal(power.calls.filter(c => c.url === '/api/power/commit').length, 1); assert.equal(power.ids['power-pending'].hidden, false);
  power.handler(c => c.url === '/api/action' ? response({ error: 'rate_limited' }, 429, { 'Retry-After': '60' }) : c.url === '/api/power/cancel' ? response({ status: 'cancelled', id: 'job', message: '已取消' }) : undefined);
  click(power, 'media.next'); await flush(); power.ids['power-cancel'].onclick(); await flush(); assert.equal(power.calls.filter(c => c.url === '/api/power/cancel').length, 1, 'Cancel bypasses unrelated core cooldown'); assert.equal(power.ids['power-pending'].hidden, true);
  await power.hide(); assert.equal(power.ids['power-confirm'].hidden, true); assert.equal(power.ids['power-pending'].hidden, true);
  console.log('Companion web: privacy, app request budget, revisions, pairing, pointer coalescing, permission updates and confirmed/cancellable power checks passed.');
}()).catch(error => { console.error(error); process.exitCode = 1; });
