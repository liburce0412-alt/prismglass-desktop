const assert = require('node:assert/strict');
const fs = require('node:fs'), vm = require('node:vm');
const code = fs.readFileSync('src/LiquidDesktop/companion-web/companion-screen.js', 'utf8');
function fixture(info = {}) {
  const ids = {}, calls = [], events = {}, timers = new Map(), peers = [];
  let serial = 0, delayed = null, delayedEnd = null; const beginReplies = [];
  function node(id) { return ids[id] || (ids[id] = { value: id === 'screen-quality' ? '720' : '0', dataset: {}, attributes: {}, children: [], hidden: true, text: '', get textContent() { return this.text; }, set textContent(value) { this.text = value; this.children = []; }, setAttribute(key, value) { this.attributes[key] = value; }, focus() {}, click() {}, appendChild(child) { this.children.push(child); this.value = child.value; }, pause() {}, play() { return Promise.resolve(); } }); }
  class Peer {
    constructor(options) { assert.deepEqual(JSON.parse(JSON.stringify(options)), { iceServers: [] }); this.iceGatheringState = 'complete'; peers.push(this); }
    addTransceiver(type, options) { assert.equal(type, 'video'); assert.equal(options.direction, 'recvonly'); }
    createOffer() { return Promise.resolve({ type: 'offer', sdp: 'v=0\r\n' }); }
    setLocalDescription(value) { this.localDescription = value; return Promise.resolve(); }
    setRemoteDescription(value) { this.remoteDescription = value; return Promise.resolve(); }
    close() { this.closed = true; }
  }
  const doc = { hidden: false, body: {dataset:{}, style:{values:{},setProperty(key,value){this.values[key]=value;}}}, getElementById: node, createElement: () => ({ value: '', textContent: '' }), addEventListener: (name, fn) => { events[name] = fn; } };
  const win = { innerWidth:1024,innerHeight:768,visualViewport:{width:1024,height:768},RTCPeerConnection: Peer, addEventListener: (name, fn) => { events[name] = fn; } };
  const fetch = async (path, options) => {
    calls.push({ path, options });
    if (path === '/api/screen/begin' && beginReplies.length) return beginReplies.shift();
    if (path === '/api/screen/begin' && delayed) {
      if (!options.signal) return delayed;
      return new Promise((resolve, reject) => {
        const abort = () => reject(new Error('The operation was aborted'));
        if (options.signal.aborted) { abort(); return; }
        options.signal.addEventListener('abort', abort, { once: true });
        delayed.then(resolve, reject);
      });
    }
    if (path === '/api/screen/end' && delayedEnd) return delayedEnd;
    const data = path === '/api/screen' ? { available: true, extensionAvailable: true, monitors: [{ id: 0, name: '测试屏幕' }], ...info } : path === '/api/screen/begin' ? { lease: 'fixture-lease', answer: 'v=0\r\n' } : { ok: true };
    return { ok: true, json: async () => data };
  };
  vm.runInNewContext(code, { window: win, document: doc, RTCPeerConnection: Peer, AbortController, fetch, setTimeout: (fn, delay) => { timers.set(++serial, { fn, delay }); return serial; }, clearTimeout: id => timers.delete(id), MediaStream: class { constructor(tracks) { this.getTracks = () => tracks; } } });
  return { ids, calls, events, timers, peers, doc, win, beginReplies, screen: win.PrismCompanionScreen, delay(value) { delayed = value; }, delayEnd(value) { delayedEnd = value; } };
}
const flush = async () => { for (let i = 0; i < 20; i++) await Promise.resolve(); };
(async () => {
  const f = fixture();
  f.doc.body.dataset.screenKeyboard='true'; f.screen.layout(); assert.equal(f.doc.body.dataset.screenLayout,'true', 'Keyboard input gets a viewport layout even before fullscreen playback'); assert.equal(f.doc.body.dataset.screenTools,'true', 'Initial visible tools and their toggle agree');
  f.doc.body.dataset.screenKeyboard='false'; f.screen.layout(); assert.equal(f.doc.body.dataset.screenLayout,'false', 'Closing keyboard restores the original non-fullscreen layout');
  const layout = fixture(), picture = layout.ids['screen-video']; picture.hidden=false; picture.videoWidth=1280; picture.videoHeight=720; layout.doc.body.dataset.screenExpanded='true'; picture.onloadedmetadata();
  assert.equal(layout.doc.body.dataset.screenControls,'bottom','Mirror shortcuts use the existing lower letterbox');
  layout.doc.body.dataset.screenKeyboard='true'; layout.win.visualViewport.height=380; layout.screen.layout();
  assert.equal(layout.doc.body.dataset.screenInput,'side','Landscape keyboard uses existing space beside the picture');
  assert(Math.abs(parseFloat(layout.doc.body.style.values['--screen-input-width'])-(1024-380*16/9))<.01);
  picture.videoWidth=1024; picture.videoHeight=768; picture.onresize(); assert.equal(layout.doc.body.style.values['--screen-input-width'],'360px','A wide natural gutter does not create an oversized input panel');
  layout.win.visualViewport.width=768; layout.win.visualViewport.height=664; layout.screen.layout(); assert.equal(layout.doc.body.dataset.screenInput,'bottom','Portrait keeps a thin bottom input bar when the natural side space is insufficient');
  layout.screen.closeTools(); assert.equal(layout.doc.body.dataset.screenTools,'false','Keyboard can close tools without moving input focus');
  const approval = fixture(); approval.screen.render({screen:true}, 'approval', true); await flush();
  const pendingRule = {ok:false, json:async()=>({error:'screen_network_pending',message:'请在电脑确认投屏连接'})};
  approval.beginReplies.push(pendingRule); const approvedStart = approval.ids['screen-start'].onclick(); await flush();
  assert.match(approval.ids['screen-status'].textContent,/电脑确认/); assert([...approval.timers.values()].some(t=>t.delay===120000), 'approval has its own bounded deadline');
  [...approval.timers.values()].find(t=>t.delay===1500).fn(); await approvedStart;
  assert.equal(approval.peers.length,1,'approval retry reuses the original offer and viewer');
  assert.equal(approval.calls.filter(c=>c.path==='/api/screen/begin').length,2); assert(approval.peers[0].remoteDescription);
  const cancelApproval = fixture(); cancelApproval.screen.render({screen:true}, 'cancel', true); await flush(); cancelApproval.beginReplies.push(pendingRule);
  const cancelledStart = cancelApproval.ids['screen-start'].onclick(); await flush(); await cancelApproval.screen.stop();
  [...cancelApproval.timers.values()].find(t=>t.delay===1500).fn(); await cancelledStart;
  assert.equal(cancelApproval.calls.filter(c=>c.path==='/api/screen/begin').length,1,'stopping while awaiting approval cannot retry capture');
  assert(cancelApproval.peers[0].closed);
  const deniedApproval = fixture(); deniedApproval.screen.render({screen:true}, 'denied', true); await flush();
  deniedApproval.beginReplies.push({ok:false,json:async()=>({error:'screen_network_cancelled',message:'电脑已取消修复'})});
  await deniedApproval.ids['screen-start'].onclick(); assert(deniedApproval.peers[0].closed); assert.match(deniedApproval.ids['screen-status'].textContent,/取消/); assert(![...deniedApproval.timers.values()].some(t=>t.delay===1500),'declined approval is never automatically retried');
  await f.ids['screen-start'].onclick(); assert.equal(f.calls.length, 0, 'ungranted page cannot request a screen');
  f.screen.render({ screen: true }, 'csrf-test', true); assert(f.ids['screen-start'].disabled); await flush(); assert(!f.ids['screen-start'].disabled); assert.equal(f.ids['screen-monitor'].children.length,1); await f.ids['screen-start'].onclick();
  assert.equal(f.peers.length, 1); assert.equal(f.calls.filter(c => c.path === '/api/screen/begin').length, 1);
  const begin = f.calls.find(c => c.path === '/api/screen/begin'); assert.equal(begin.options.headers['X-CSRF-Token'], 'csrf-test'); assert.equal(begin.options.credentials, 'same-origin');
  assert.deepEqual(JSON.parse(begin.options.body), { mode: 'mirror', monitor: 0, quality: '720', offer: 'v=0\r\n' });
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
  await flush(); await f.ids['screen-start'].onclick(); f.doc.hidden = true; f.events.visibilitychange(); await flush(); assert(f.peers[1].closed); assert.equal(f.timers.size, 0, 'backgrounding cancels heartbeat and startup deadline');
  const pending = fixture(); let release; pending.delay(new Promise(resolve => { release = resolve; })); pending.screen.render({ screen: true }, 'original', true);
  await flush();
  const opening = pending.ids['screen-start'].onclick(); await flush(); pending.screen.clear(); release({ ok: true, json: async () => ({ lease: 'late-lease', answer: 'v=0\r\n' }) }); await opening;
  assert(pending.peers[0].closed); assert.equal(pending.ids['screen-video'].srcObject, null);
  const lateEnd = pending.calls.find(c => c.path === '/api/screen/end'); assert(lateEnd); assert.equal(lateEnd.options.headers['X-CSRF-Token'], 'original', 'late answer closes its own lease with its original session');
  assert.equal(pending.calls.find(c => c.path === '/api/screen/begin').options.signal, undefined, 'cancelling locally does not discard an already submitted server lease');
  const modes = fixture({ monitors: [{ id: 0, name: '主屏幕' }, { id: 2, name: '有线屏幕' }] });
  modes.screen.render({ screen: true }, 'mode-token', true); await flush(); modes.ids['screen-monitor'].value = '2'; await modes.ids['screen-start'].onclick();
  assert.equal(JSON.parse(modes.calls.find(c => c.path === '/api/screen/begin').options.body).monitor, 2, 'mirror captures the selected physical screen');
  let releaseEnd; modes.delayEnd(new Promise(resolve => { releaseEnd = resolve; }));
  const changing = modes.ids['screen-mode-extend'].onclick(); await flush();
  assert(modes.peers[0].closed, 'switching mode closes the existing peer first'); assert(modes.ids['screen-start'].disabled);
  assert.equal(modes.calls.filter(c => c.path === '/api/screen').length, 1, 'new display metadata waits for the old lease to end');
  await modes.ids['screen-start'].onclick(); assert.equal(modes.peers.length, 1, 'cannot overlap viewers while stopping the old mode');
  releaseEnd({ ok: true }); await changing;
  assert.equal(modes.ids['screen-monitor'].children.length, 2, 'refresh replaces display choices');
  assert.equal(modes.ids['screen-monitor-field'].hidden, true); assert.equal(modes.ids['screen-start'].textContent, '打开副屏');
  assert.equal(modes.ids['screen-mode-extend'].attributes['aria-pressed'], 'true');
  assert.equal(modes.peers.length, 1, 'choosing extension mode does not create a display until Start is pressed');
  await modes.ids['screen-start'].onclick();
  assert.deepEqual(JSON.parse(modes.calls.filter(c => c.path === '/api/screen/begin')[1].options.body), { mode: 'extend', monitor: -1, quality: '720', offer: 'v=0\r\n' });
  modes.ids['screen-video'].onplaying(); assert.match(modes.ids['screen-status'].textContent, /副屏已打开/);
  modes.ids['screen-video'].hidden=false; modes.ids['screen-video'].videoWidth=1280; modes.ids['screen-video'].videoHeight=720; modes.screen.layout(); assert.equal(modes.doc.body.dataset.screenControls,'top','Extension mode keeps its floating controls without manufacturing letterboxes');
  modes.events.offline(); await flush(); assert(modes.peers[1].closed); assert.equal(modes.ids['screen-video'].srcObject, null);
  const missing = fixture({ extensionAvailable: false, extensionMessage: '请先安装虚拟显示驱动。' });
  missing.screen.render({ screen: true }, 'missing', true); await flush(); await missing.ids['screen-mode-extend'].onclick();
  assert(missing.ids['screen-start'].disabled); assert.equal(missing.ids['screen-status'].textContent, '请先安装虚拟显示驱动。');
  await missing.ids['screen-start'].onclick(); assert.equal(missing.peers.length, 0, 'missing driver cannot start a physical display by fallback');
  await missing.ids['screen-mode-mirror'].onclick(); assert(!missing.ids['screen-start'].disabled); assert.equal(missing.ids['screen-monitor-field'].hidden, false);
  const noPhysical = fixture({ monitors: [] }); noPhysical.screen.render({ screen: true }, 'virtual', true); await flush();
  assert(noPhysical.ids['screen-start'].disabled); await noPhysical.ids['screen-mode-extend'].onclick(); assert(!noPhysical.ids['screen-start'].disabled, 'extension capability is independent of mirror monitor choices');
  const unconfigured = fixture({ available: false, extensionAvailable: true, message: '请先配置投屏。' }); unconfigured.screen.render({ screen: true }, 'setup', true); await flush(); await unconfigured.ids['screen-mode-extend'].onclick();
  assert(unconfigured.ids['screen-start'].disabled); assert.equal(unconfigured.ids['screen-status'].textContent, '请先配置投屏。');
  const revoked = fixture(); revoked.screen.render({ screen: true }, 'old', true); await flush(); await revoked.ids['screen-start'].onclick();
  let finishEnd; revoked.delayEnd(new Promise(resolve => { finishEnd = resolve; }));
  const revokedSwitch = revoked.ids['screen-mode-extend'].onclick(); await flush(); revoked.screen.clear(); finishEnd({ ok: true }); await revokedSwitch;
  assert(revoked.ids['screen-start'].disabled && revoked.ids['screen-mode-extend'].disabled); assert.equal(revoked.calls.filter(c => c.path === '/api/screen').length, 1, 'revocation during mode change cannot reload or enable capture');
  const interrupted = fixture(); interrupted.screen.render({ screen: true }, 'lost', true); await flush(); await interrupted.ids['screen-start'].onclick(); interrupted.delayEnd(new Promise(() => {}));
  const waiting = interrupted.ids['screen-mode-extend'].onclick(); await flush();
  const endTimeout = [...interrupted.timers.values()].find(t => t.delay === 5000); assert(endTimeout); endTimeout.fn(); await waiting;
  assert(!interrupted.ids['screen-start'].disabled, 'a lost end response cannot freeze the mode controls forever'); assert.equal(interrupted.peers.length, 1, 'ending timeout never automatically starts another capture');
  assert.match(interrupted.ids['screen-status'].textContent, /尚未确认电脑端关闭/, 'a missing stop acknowledgement is not presented as completed cleanup');
  const failedEnd = fixture(); failedEnd.screen.render({ screen: true }, 'failure', true); await flush(); await failedEnd.ids['screen-mode-extend'].onclick(); await failedEnd.ids['screen-start'].onclick();
  failedEnd.delayEnd(Promise.resolve({ ok: false, json: async () => ({ message: '副屏未能关闭，已保留恢复记录。' }) }));
  await failedEnd.screen.stop(); assert.equal(failedEnd.ids['screen-status'].textContent, '副屏未能关闭，已保留恢复记录。', 'native cleanup failure remains visible to the user');
  const staleEnd = fixture(); staleEnd.screen.render({ screen: true }, 'stale', true); await flush(); await staleEnd.ids['screen-start'].onclick();
  let rejectOld; staleEnd.delayEnd(new Promise(resolve => { rejectOld = resolve; })); const closing = staleEnd.screen.stop(); await staleEnd.ids['screen-start'].onclick();
  rejectOld({ ok: false, json: async () => ({ message: '旧会话错误' }) }); await closing; assert.notEqual(staleEnd.ids['screen-status'].textContent, '旧会话错误', 'a late stop failure cannot overwrite the new session status');
  console.log('Companion screen: mirror/extension choice, capability gating, lease ordering, authenticated signaling, fullscreen, revocation and disconnect cleanup passed.');
})().catch(error => { console.error(error); process.exitCode = 1; });
