const assert = require('node:assert/strict');
const fs = require('node:fs'), vm = require('node:vm');
const code = fs.readFileSync('src/LiquidDesktop/companion-web/companion-screen.js', 'utf8');
function fixture() {
  const ids = {}, calls = [], events = {}, timers = new Map(), peers = [];
  let serial = 0, delayed = null;
  function node(id) { return ids[id] || (ids[id] = { value: id === 'screen-quality' ? '720' : '0', dataset: {}, children: [], hidden: true, textContent: '', setAttribute() {}, focus() {}, click() {}, appendChild(child) { this.children.push(child); this.value = child.value; }, pause() {}, play() { return Promise.resolve(); } }); }
  class Peer {
    constructor(options) { assert.deepEqual(JSON.parse(JSON.stringify(options)), { iceServers: [] }); this.iceGatheringState = 'complete'; peers.push(this); }
    addTransceiver(type, options) { assert.equal(type, 'video'); assert.equal(options.direction, 'recvonly'); }
    createOffer() { return Promise.resolve({ type: 'offer', sdp: 'v=0\r\n' }); }
    setLocalDescription(value) { this.localDescription = value; return Promise.resolve(); }
    setRemoteDescription(value) { this.remoteDescription = value; return Promise.resolve(); }
    close() { this.closed = true; }
  }
  const doc = { hidden: false, body: {dataset:{}}, getElementById: node, createElement: () => ({ value: '', textContent: '' }), addEventListener: (name, fn) => { events[name] = fn; } };
  const win = { RTCPeerConnection: Peer, addEventListener: (name, fn) => { events[name] = fn; } };
  const fetch = async (path, options) => {
    calls.push({ path, options });
    if (path === '/api/screen/begin' && delayed) return delayed;
    const data = path === '/api/screen' ? { available: true, monitors: [{ id: 0, name: '测试屏幕' }] } : path === '/api/screen/begin' ? { lease: 'fixture-lease', answer: 'v=0\r\n' } : { ok: true };
    return { ok: true, json: async () => data };
  };
  vm.runInNewContext(code, { window: win, document: doc, RTCPeerConnection: Peer, AbortController, fetch, setTimeout: (fn, delay) => { timers.set(++serial, { fn, delay }); return serial; }, clearTimeout: id => timers.delete(id), MediaStream: class { constructor(tracks) { this.getTracks = () => tracks; } } });
  return { ids, calls, events, timers, peers, doc, screen: win.PrismCompanionScreen, delay(value) { delayed = value; } };
}
const flush = async () => { for (let i = 0; i < 20; i++) await Promise.resolve(); };
(async () => {
  const f = fixture();
  await f.ids['screen-start'].onclick(); assert.equal(f.calls.length, 0, 'ungranted page cannot request a screen');
  f.screen.render({ screen: true }, 'csrf-test', true); assert(f.ids['screen-start'].disabled); await flush(); assert(!f.ids['screen-start'].disabled); assert.equal(f.ids['screen-monitor'].children.length,1); await f.ids['screen-start'].onclick();
  assert.equal(f.peers.length, 1); assert.equal(f.calls.filter(c => c.path === '/api/screen/begin').length, 1);
  const begin = f.calls.find(c => c.path === '/api/screen/begin'); assert.equal(begin.options.headers['X-CSRF-Token'], 'csrf-test'); assert.equal(begin.options.credentials, 'same-origin');
  await f.ids['screen-start'].onclick(); assert.equal(f.peers.length, 1, 'double click does not create another viewer');
  let trackStopped = false; f.peers[0].ontrack({ streams: [{ getTracks: () => [{ stop: () => { trackStopped = true; } }] }] });
  assert.equal(f.ids['screen-video'].hidden, false);
  f.ids['screen-video'].onplaying(); assert.equal(f.doc.body.dataset.screenExpanded,'true'); assert.equal(f.doc.body.dataset.screenTools,'false');
  f.ids['screen-tools-toggle'].onclick(); assert.equal(f.doc.body.dataset.screenTools,'true');
  f.ids['screen-tools-close'].onclick(); assert.equal(f.doc.body.dataset.screenTools,'false');
  f.ids['screen-expand'].onclick(); assert.equal(f.doc.body.dataset.screenExpanded,'false');
  f.ids['screen-video'].onplaying(); assert.equal(f.doc.body.dataset.screenExpanded,'false','buffering must not undo an intentional exit from fullscreen');
  f.screen.render({ screen: false }, 'csrf-test', true);
  assert(f.peers[0].closed && trackStopped); assert.equal(f.ids['screen-video'].srcObject, null); assert.equal(f.ids['screen-video'].hidden, true);
  assert.equal(f.doc.body.dataset.screenExpanded,'false');
  const end = f.calls.find(c => c.path === '/api/screen/end'); assert.equal(end.options.keepalive, true); assert.equal(end.options.headers['X-CSRF-Token'], 'csrf-test');
  f.screen.render({ screen: true }, 'csrf-test', true); assert.equal(f.peers.length, 1, 'restored grant does not restart capture');
  await flush(); await f.ids['screen-start'].onclick(); f.doc.hidden = true; f.events.visibilitychange(); assert(f.peers[1].closed); assert.equal(f.timers.size, 0, 'backgrounding cancels heartbeat and startup deadline');
  const pending = fixture(); let release; pending.delay(new Promise(resolve => { release = resolve; })); pending.screen.render({ screen: true }, 'original', true);
  await flush();
  const opening = pending.ids['screen-start'].onclick(); await flush(); pending.screen.clear(); release({ ok: true, json: async () => ({ lease: 'late-lease', answer: 'v=0\r\n' }) }); await opening;
  assert(pending.peers[0].closed); assert.equal(pending.ids['screen-video'].srcObject, null);
  const lateEnd = pending.calls.find(c => c.path === '/api/screen/end'); assert(lateEnd); assert.equal(lateEnd.options.headers['X-CSRF-Token'], 'original', 'late answer closes its own lease with its original session');
  console.log('Companion screen: grant gating, single viewer, authenticated signaling, revocation, background cleanup and late-answer checks passed.');
})().catch(error => { console.error(error); process.exitCode = 1; });
