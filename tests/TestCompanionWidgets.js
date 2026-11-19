'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../src/LiquidDesktop/companion-web/companion-widgets.js'), 'utf8');
let groups = 0;
function events() { const handlers = {}; return { addEventListener(name, fn) { (handlers[name] || (handlers[name] = [])).push(fn); }, dispatchEvent(event) { (handlers[event.type] || []).forEach(fn => fn(event)); }, emit(type) { this.dispatchEvent({ type }); } }; }
function fixture(options = {}) {
  let now = 0, nextInterval = 1, nextFrame = 1; const epoch = Date.parse('2026-10-26T00:00:00Z'), all = {}, intervals = new Map(), frames = new Map(), mutations = [], storageWrites = [], dispatched = [];
  class Node {
    constructor(tag) { this.tagName = tag; this.children = []; this.attributes = {}; this.dataset = {}; this.hidden = false; this.disabled = false; this.checked = false; this.value = ''; this.className = ''; this.parentNode = null; this._text = ''; this.textWrites = 0; this.classList = { add: name => { this.className += ' ' + name; } }; }
    set id(value) { this._id = value; all[value] = this; } get id() { return this._id; }
    get textContent() { return this._text + this.children.map(node => node.textContent).join(''); }
    set textContent(value) { this.textWrites++; this._text = String(value); this.children.forEach(node => { node.parentNode = null; }); this.children = []; }
    appendChild(node) { node.remove(); node.parentNode = this; this.children.push(node); return node; }
    insertBefore(node, sibling) { node.remove(); node.parentNode = this; const at = this.children.indexOf(sibling); this.children.splice(at < 0 ? this.children.length : at, 0, node); }
    remove() { if (this.parentNode) { this.parentNode.children = this.parentNode.children.filter(node => node !== this); this.parentNode = null; } }
    setAttribute(key, value) { this.attributes[key] = String(value); }
    removeAttribute(key) { delete this.attributes[key]; }
    focus() { document.activeElement = this; }
  }
  const body = new Node('body'); body.dataset.page = options.page || 'widgets';
  const page = new Node('section'), gallery = new Node('div'), customize = new Node('button'); gallery.id = 'widget-gallery'; customize.id = 'widget-customize'; body.appendChild(page); page.appendChild(customize); page.appendChild(gallery);
  const document = { ...events(), body, hidden: false, getElementById: id => all[id] || null, createElement: tag => new Node(tag), createElementNS: (ns, tag) => new Node(tag) };
  const media = { ...events(), matches: false };
  let saved = options.saved == null ? null : JSON.stringify(options.saved);
  const window = { ...events(), matchMedia: () => media, localStorage: { getItem(key) { assert.equal(key, 'prismglass.companion.widgets.v1'); if (options.storageBlocked) throw Error('storage denied'); return saved; }, setItem(key, value) { if (options.storageBlocked) throw Error('storage denied'); storageWrites.push({ key, value }); saved = value; } } };
  window.requestAnimationFrame = fn => { const id = nextFrame++; frames.set(id, fn); return id; }; window.cancelAnimationFrame = id => frames.delete(id);
  const dispatch = window.dispatchEvent; window.dispatchEvent = function (event) { dispatched.push(event); dispatch.call(this, event); };
  class FixtureDate extends Date { constructor(...args) { super(...(args.length ? args : [epoch + now])); } static now() { return epoch + now; } }
  class MutationObserver { constructor(fn) { mutations.push(fn); } observe() {} }
  class CustomEvent { constructor(type, init = {}) { this.type = type; this.detail = init.detail; } }
  vm.runInNewContext(source, { document, window, MutationObserver, CustomEvent, Date: FixtureDate, Intl, performance: { now: () => now }, setInterval(fn, delay) { assert.equal(delay, 250); const id = nextInterval++; intervals.set(id, fn); return id; }, clearInterval(id) { intervals.delete(id); } }, { filename: 'companion-widgets.js' });
  return { all, body, gallery, document, window, media, intervals, frames, storageWrites, dispatched, api: window.PrismCompanionWidgets,
    click(id) { const node = all[id]; assert.ok(node, 'missing ' + id); assert.equal(node.disabled, false, 'disabled ' + id); node.onclick(); },
    advance(ms, tick = true) { now += ms; if (tick) [...intervals.values()].forEach(fn => fn()); },
    frame(ms = 16) { now += ms; const batch = [...frames.values()]; frames.clear(); batch.forEach(fn => fn(now)); },
    page(name) { body.dataset.page = name; mutations.forEach(fn => fn()); },
    reduce(value) { body.dataset.reduced = String(value); mutations.forEach(fn => fn()); },
    hide() { document.hidden = true; document.emit('visibilitychange'); }, show() { document.hidden = false; document.emit('visibilitychange'); },
    preference(id) { return all['widget-up-' + id].parentNode.children[0].children[0]; },
    stored() { return saved ? JSON.parse(saved) : null; }
  };
}
const sample = { weather: { available: true, updatedAt: Date.parse('2026-10-26T00:00:00Z'), data: { temp: 21.4, code: 3, low: 18, high: 24 } }, tasks: { available: true, revision: 'opaque-1', items: [{ id: 'a', text: '<script>私人待办</script>', done: false }, { id: 'b', text: '已完成不显示', done: true }, { id: 'c', text: '第二件事', done: false }] } };
function test(name, fn) { fn(); groups++; console.log('PASS ' + name); }

test('weather keeps real time, missing fields, stale reasons and source identity distinct', () => {
  const f = fixture(), weather = { available: true, updatedAt: Date.parse('2026-10-25T00:00:00Z'), data: { temp: 0, code: null, feelsLike: null, high: null, low: null, location: '杭州 · 浙江 · 中国', source: 'CMA GRAPES · Open-Meteo', kind: 'model', fetchedAt: Date.parse('2026-10-26T00:00:00Z') } };
  f.api.render({ weather }, {}); assert.equal(f.all['widget-weather-temp'].textContent, '0°'); assert.equal(f.all['widget-weather-summary'].textContent, '天气状况未提供'); assert.equal(f.all['widget-weather-location'].textContent, '杭州 · 浙江 · 中国'); assert.match(f.all['widget-weather-status'].textContent, /10\/25/); assert.match(f.all['widget-weather-status'].textContent, /CMA GRAPES · Open-Meteo · 数值预报/);
  weather.data.feelsLike = -2; weather.data.stale = true; weather.reason = '天气更新失败，保留上次数据。'; f.api.render({ weather }, {}); assert.match(f.all['widget-weather-summary'].textContent, /体感 -2°/); assert.match(f.all['widget-weather-status'].textContent, /^上次天气/); assert.equal(f.all['widget-weather-reason'].textContent, weather.reason);
  weather.data.code = 1; f.api.render({ weather }, {}); assert.match(f.all['widget-weather-summary'].textContent, /^晴间少云/);
  weather.available = false; weather.data = null; weather.reason = '天气数据已过期，请检查电脑网络。'; f.api.render({ weather }, {}); assert.equal(f.all['widget-weather-temp'].textContent, '—'); assert.equal(f.all['widget-weather-summary'].textContent, weather.reason); assert.equal(f.all['widget-weather-location'].textContent, '电脑天气位置');
  f.api.clear(); assert.equal(f.all['widget-weather-location'].textContent, '电脑天气位置'); assert.equal(f.all['widget-weather-reason'].textContent, ''); assert.equal(f.all['widget-weather-status'].textContent, '尚未连接');
});
test('fifteen useful and decorative cards initialize without transport and settings are strictly normalized', () => {
  const f = fixture({ page: 'home', saved: { version: 1, order: ['world', 'world', 'untrusted'], visible: ['world', 'tasks', 'untrusted'], duration: 9, zone: 'not/a/zone', secret: 'never retain this' } });
  assert.equal(Object.keys(f.all).filter(id => id.startsWith('widget-card-')).length, 15); assert.equal(f.gallery.children[0].id, 'widget-card-world'); assert.equal(f.all['widget-card-timer'].hidden, true); assert.equal(f.all['widget-card-world'].hidden, false); assert.equal(f.all['widget-timer-minutes'].value, '3'); assert.equal(f.all['widget-zone'].value, 'Asia/Shanghai'); assert.equal(f.intervals.size, 0); assert.equal(f.storageWrites.length, 0);
  f.click('widget-customize'); assert.equal(f.all['widget-settings'].hidden, false); assert.equal(f.all['widget-customize'].attributes['aria-expanded'], 'true'); f.click('widget-settings-close'); assert.equal(f.all['widget-settings'].hidden, true);
});
test('countdown reaches its deadline while another page is open', () => {
  const f = fixture(); f.all['widget-timer-minutes'].value = '1'; f.all['widget-timer-minutes'].onchange(); f.click('widget-timer-toggle');
  f.advance(12345); assert.equal(f.all['widget-timer'].textContent, '00:48'); assert.equal(f.all['widget-timer-minutes'].disabled, true);
  f.api.pause(); f.page('home'); f.advance(60000); f.api.render(sample, { tasks: true }); f.page('widgets');
  assert.equal(f.all['widget-timer'].textContent, '00:00'); assert.match(f.all['widget-timer-status'].textContent, /时间到了/);
  f.advance(20000); assert.equal(f.all['widget-timer'].textContent, '00:00');
  f.click('widget-timer-reset'); assert.equal(f.all['widget-timer'].textContent, '01:00'); assert.equal(f.all['widget-timer-minutes'].disabled, false);
  f.all['widget-timer-minutes'].value = '999'; f.all['widget-timer-minutes'].onchange(); assert.equal(f.all['widget-timer-minutes'].value, '1'); assert.match(f.all['widget-timer-status'].textContent, /1 到 180/);
});
test('stopwatch includes time spent hidden and in the back-forward cache', () => {
  const f = fixture(); f.click('widget-stopwatch-toggle'); f.advance(5500); assert.equal(f.all['widget-stopwatch'].textContent, '00:05');
  f.hide(); assert.equal(f.intervals.size, 0); f.advance(180000); f.show(); f.api.render(sample, { tasks: true }); assert.equal(f.all['widget-stopwatch'].textContent, '03:05'); assert.equal(f.all['widget-stopwatch-toggle'].textContent, '暂停');
  f.advance(2500); assert.equal(f.all['widget-stopwatch'].textContent, '03:08'); f.window.emit('pagehide'); assert.equal(f.intervals.size, 0); f.advance(20000); f.window.emit('pageshow'); assert.equal(f.all['widget-stopwatch'].textContent, '03:28'); f.click('widget-stopwatch-reset'); assert.equal(f.all['widget-stopwatch'].textContent, '00:00');
});
test('timer presets reuse duration validation and stay disabled while counting down', () => {
  const f = fixture();
  for (const minutes of [1, 3, 5, 15]) {
    f.click('widget-timer-preset-' + minutes); assert.equal(f.all['widget-timer-minutes'].value, String(minutes)); assert.equal(f.all['widget-timer'].textContent, String(minutes).padStart(2, '0') + ':00'); assert.equal(f.stored().duration, minutes * 60);
    for (const other of [1, 3, 5, 15]) assert.equal(f.all['widget-timer-preset-' + other].attributes['aria-pressed'], String(other === minutes));
  }
  f.click('widget-timer-toggle'); f.advance(1000);
  for (const minutes of [1, 3, 5, 15]) { assert.equal(f.all['widget-timer-preset-' + minutes].disabled, true); f.all['widget-timer-preset-' + minutes].onclick(); }
  assert.equal(f.all['widget-timer'].textContent, '14:59'); assert.equal(f.all['widget-timer-minutes'].value, '15');
  f.click('widget-timer-toggle'); f.click('widget-timer-preset-1'); assert.equal(f.all['widget-timer'].textContent, '01:00');
  for (const invalid of ['0', '181', '1.5', '']) { f.all['widget-timer-minutes'].value = invalid; f.all['widget-timer-minutes'].onchange(); assert.equal(f.all['widget-timer-minutes'].value, '1'); }
  f.all['widget-timer-minutes'].value = '180'; f.all['widget-timer-minutes'].onchange(); assert.equal(f.all['widget-timer'].textContent, '3:00:00'); assert.equal(f.stored().duration, 10800);
});
test('stopwatch retains only five lap durations across pauses and hides, never persists them, and resets numbering', () => {
  const f = fixture(), laps = f.all['widget-stopwatch-laps']; assert.equal(laps.hidden, true); assert.equal(f.all['widget-stopwatch-lap'].disabled, true);
  f.click('widget-stopwatch-toggle'); f.advance(2500); f.click('widget-stopwatch-lap'); assert.equal(laps.children[0].children[1].textContent, '00:02');
  f.click('widget-stopwatch-toggle'); assert.equal(f.all['widget-stopwatch-lap'].disabled, true); f.all['widget-stopwatch-lap'].onclick(); assert.equal(laps.children.length, 1); f.advance(20000);
  f.click('widget-stopwatch-toggle'); f.advance(3500); f.click('widget-stopwatch-lap'); assert.equal(laps.children[1].children[1].textContent, '00:03'); assert.equal(f.all['widget-stopwatch'].textContent, '00:06');
  f.hide(); f.advance(30000); f.show(); assert.equal(laps.children.length, 2); assert.equal(f.all['widget-stopwatch-lap'].disabled, false);

  for (let lap = 3; lap <= 7; lap++) { f.advance(lap * 1000); f.click('widget-stopwatch-lap'); }
  assert.deepEqual(laps.children.map(row => row.children.map(node => node.textContent)), [3, 4, 5, 6, 7].map(lap => ['第 ' + lap + ' 圈', lap === 3 ? '00:33' : '00:0' + lap]));
  f.window.emit('pagehide'); f.advance(60000); f.window.emit('pageshow'); assert.equal(laps.children.length, 5); assert.equal(f.all['widget-stopwatch-lap'].disabled, false); assert.equal(f.storageWrites.length, 0);
  f.click('widget-timer-preset-5'); const saved = f.stored(); assert.deepEqual(Object.keys(saved).sort(), ['duration', 'order', 'sandMinutes', 'version', 'visible', 'zone']); assert.equal(fixture({ saved }).all['widget-stopwatch-laps'].children.length, 0);
  f.click('widget-stopwatch-reset'); assert.equal(laps.children.length, 0); assert.equal(laps.hidden, true); f.click('widget-stopwatch-toggle'); f.advance(1000); f.click('widget-stopwatch-lap'); assert.equal(laps.children[0].textContent, '第 1 圈00:01');
});
test('hide and reorder retain card nodes and persist only non-private preferences', () => {
  const f = fixture(); f.api.render(sample, { tasks: true }); const timerCard = f.all['widget-card-timer'];
  f.click('widget-timer-toggle'); f.advance(2000); const checkbox = f.preference('timer'); checkbox.checked = false; checkbox.onchange(); assert.equal(timerCard.hidden, true); f.advance(20000); checkbox.checked = true; checkbox.onchange(); assert.equal(f.all['widget-timer'].textContent, '02:58'); assert.equal(f.all['widget-timer-toggle'].textContent, '继续');
  f.click('widget-up-stopwatch'); assert.equal(f.gallery.children[0].id, 'widget-card-stopwatch'); assert.equal(f.all['widget-card-timer'], timerCard); assert.equal(f.document.activeElement.id, 'widget-up-stopwatch');
  const saved = f.stored(); assert.deepEqual(Object.keys(saved).sort(), ['duration', 'order', 'sandMinutes', 'version', 'visible', 'zone']); assert.equal(saved.order[0], 'stopwatch'); assert.doesNotMatch(JSON.stringify(saved), /私人|第二件|21\.4|elapsed|remaining|revision|opaque/);
  const reopened = fixture({ saved }); assert.equal(reopened.gallery.children[0].id, 'widget-card-stopwatch'); assert.equal(reopened.all['widget-timer'].textContent, '03:00', 'timer state never persisted');
});
test('world clock follows selected time zone and storage denial never blocks local tools', () => {
  const f = fixture(); assert.equal(f.all['widget-world-time'].textContent, '08:00'); f.all['widget-zone'].value = 'Asia/Tokyo'; f.all['widget-zone'].onchange(); assert.equal(f.all['widget-world-time'].textContent, '09:00'); assert.equal(f.stored().zone, 'Asia/Tokyo'); f.advance(60000); assert.equal(f.all['widget-world-time'].textContent, '09:01');
  const blocked = fixture({ storageBlocked: true }); blocked.all['widget-timer-minutes'].value = '2'; blocked.all['widget-timer-minutes'].onchange(); blocked.click('widget-timer-toggle'); blocked.advance(1000); assert.equal(blocked.all['widget-timer'].textContent, '01:59'); assert.match(blocked.all['widget-settings'].textContent, /无法保存偏好/);
});
test('real private summaries update without rebuilding unchanged task text and route semantic actions', () => {
  const f = fixture(); f.api.render(sample, { tasks: true }); assert.equal(f.all['widget-weather-temp'].textContent, '21°'); assert.equal(f.all['widget-task-count'].textContent, '2 件未完成'); assert.equal(f.all['widget-task-list'].children[0].textContent, '<script>私人待办</script>'); assert.equal(f.all['widget-task-list'].children[0].children.length, 0);
  const first = f.all['widget-task-list'].children[0], writes = f.all['widget-task-list'].textWrites;
  for (let i = 0; i < 5; i++) f.api.render(sample, { tasks: true }); assert.equal(f.all['widget-task-list'].children[0], first); assert.equal(f.all['widget-task-list'].textWrites, writes);
  f.click('widget-tasks-open'); assert.equal(f.dispatched.at(-1).type, 'companion-widget-navigate'); assert.equal(f.dispatched.at(-1).detail.page, 'work'); f.click('widget-immerse'); assert.equal(f.dispatched.at(-1).type, 'companion-widget-immerse');
  f.api.render(sample, { tasks: false }); assert.equal(f.all['widget-task-list'].textContent, ''); assert.equal(f.all['widget-tasks-open'].disabled, true);
  f.api.render({ weather: { available: false }, tasks: { available: true, items: [] } }, { tasks: true }); assert.equal(f.all['widget-weather-temp'].textContent, '—'); assert.equal(f.all['widget-task-count'].textContent, '0 件未完成'); assert.match(f.all['widget-tasks-status'].textContent, /没有未完成/);
});
test('clear and hidden state remove private copies while local tools remain usable without connection', () => {
  const f = fixture(); f.api.render(sample, { tasks: true }); f.click('widget-stopwatch-toggle'); f.advance(3000); f.api.clear(); assert.equal(f.all['widget-task-list'].textContent, ''); assert.equal(f.all['widget-weather-temp'].textContent, '—'); assert.equal(f.all['widget-immerse'].disabled, true); assert.equal(f.all['widget-stopwatch-toggle'].textContent, '暂停');
  f.advance(2000); assert.equal(f.all['widget-stopwatch'].textContent, '00:05'); f.api.render(sample, { tasks: true }); f.hide(); assert.equal(f.all['widget-task-list'].textContent, ''); f.api.render(sample, { tasks: true }); assert.equal(f.all['widget-task-list'].textContent, ''); assert.equal(f.intervals.size, 0);
  const anonymous = fixture(); anonymous.click('widget-stopwatch-toggle');
  for (let i = 0; i < 4; i++) { anonymous.advance(1000); anonymous.api.render(null, {}); }
  assert.equal(anonymous.all['widget-stopwatch'].textContent, '00:04'); assert.equal(anonymous.all['widget-stopwatch-toggle'].textContent, '暂停', 'anonymous polling must not interrupt local tools');
});
test('breathing obeys reduced motion and hiding its card stops activity', () => {
  const f = fixture(); f.click('widget-breath-toggle'); assert.equal(f.all['widget-breath-orb'].dataset.running, 'true'); assert.equal(f.all['widget-breath-status'].textContent, '吸气'); f.advance(4500); assert.equal(f.all['widget-breath-status'].textContent, '呼气');
  f.reduce(true); assert.equal(f.all['widget-breath-orb'].dataset.running, 'false'); assert.match(f.all['widget-breath-status'].textContent, /减少动态/); f.reduce(false); assert.equal(f.all['widget-breath-orb'].dataset.running, 'true');
  const checkbox = f.preference('breath'); checkbox.checked = false; checkbox.onchange(); assert.equal(f.all['widget-breath-orb'].dataset.running, 'false'); checkbox.checked = true; checkbox.onchange(); assert.equal(f.all['widget-breath-orb'].dataset.running, 'false');
  f.click('widget-breath-toggle'); f.media.matches = true; f.media.emit('change'); assert.equal(f.all['widget-breath-orb'].dataset.running, 'false');
});
test('calculator performs bounded local arithmetic without evaluating input or persisting results', () => {
  const f = fixture(); const key = label => { const button = f.all['widget-calculator-result'].parentNode.children.find(node=>node.className==='calculator-keys').children.find(node=>node.textContent===label); assert(button); button.onclick(); };
  for (const symbol of ['0','.','1','+','0','.','2','=']) key(symbol); assert.equal(f.all['widget-calculator-result'].textContent,'0.3');
  for (const symbol of ['×','2','=']) key(symbol); assert.equal(f.all['widget-calculator-result'].textContent,'0.6');
  for (const symbol of ['÷','0','=']) key(symbol); assert.equal(f.all['widget-calculator-result'].textContent,'错误'); key('7'); assert.equal(f.all['widget-calculator-result'].textContent,'7'); key('±'); assert.equal(f.all['widget-calculator-result'].textContent,'-7'); key('AC');
  for (let i=0;i<20;i++) key('9'); assert.equal(f.all['widget-calculator-result'].textContent,'999999999999'); key('⌫'); assert.equal(f.all['widget-calculator-result'].textContent,'99999999999'); assert.equal(f.storageWrites.length,0);
  assert.doesNotMatch(source,/\beval\s*\(|new\s+Function\s*\(/);
});
test('date distance uses civil days and rejects overflowing dates without storing the selected day',()=>{
  const f=fixture(); const input=f.all['widget-date-target']; input.value='2026-10-27'; input.onchange(); assert.equal(f.all['widget-date-distance'].textContent,'1 天');
  input.value='2026-10-26';input.onchange();assert.equal(f.all['widget-date-status'].textContent,'就是今天');input.value='2026-02-31';input.onchange();assert.equal(f.all['widget-date-distance'].textContent,'—');assert.equal(f.storageWrites.length,0);
});
test('six work sculptures remain opt-in; presets, rest lifecycle and shared work actions are useful', () => {
  const f = fixture();
  for (const id of ['hourglass', 'sunpath', 'yeardots', 'tide', 'orrery', 'bonsai']) { assert.equal(f.all['widget-card-' + id].hidden, true); const checkbox = f.preference(id); checkbox.checked = true; checkbox.onchange(); }
  for (const minutes of [5, 15, 25, 50]) { f.click('widget-hourglass-preset-' + minutes); assert.equal(f.all['widget-hourglass-status'].textContent, String(minutes).padStart(2, '0') + ':00'); }
  f.click('widget-hourglass-toggle'); f.advance(12300); assert.equal(f.all['widget-hourglass-status'].textContent, '49:48'); assert.equal(f.all['widget-timer'].textContent, '03:00');
  f.page('home'); f.advance(120000); f.page('widgets'); assert.equal(f.all['widget-hourglass-status'].textContent, '47:48'); assert.equal(f.all['widget-hourglass-toggle'].textContent, '暂停');
  f.click('widget-hourglass-reset'); assert.equal(f.all['widget-hourglass-status'].textContent, '50:00');
  f.click('widget-tide-toggle'); f.advance(5000); assert.match(f.all['widget-tide-detail'].textContent, /呼气/); f.page('home'); f.advance(10000); f.page('widgets'); assert.equal(f.all['widget-tide-status'].textContent, '00:55'); f.click('widget-tide-toggle'); f.advance(60000); assert.equal(f.all['widget-tide-status'].textContent, '00:00'); assert.match(f.all['widget-tide-detail'].textContent, /慢慢回到/);
  const state = Object.assign({}, sample, { focus: { available: true, total: 1500, remaining: 1200, deadline: 0, completed: 3 }, tasks: Object.assign({}, sample.tasks, { countdownTitle: '作品提交', countdownDate: '2026-10-30' }) });
  f.api.render(state, { tasks: true, control: true }); assert.equal(f.all['widget-orrery-status'].textContent, '20:00'); assert.equal(f.all['widget-bonsai-status'].textContent, '2 件待办'); assert.equal(f.all['widget-bonsai-detail'].textContent, '<script>私人待办</script>'); assert.equal(f.all['widget-bonsai-progress'].textContent, '1 / 3 已完成'); assert.match(f.all['widget-yeardots-detail'].textContent, /作品提交 · 还有 4 天 · 待办已完成 1\/3/);
  state.tasks.items=state.tasks.items.map(item=>({...item,done:true}));f.api.render(state,{tasks:true,control:true});assert.match(f.all['widget-yeardots-detail'].textContent,/3\/3/);assert.equal(f.all['widget-bonsai-status'].textContent,'都做好了');
  state.tasks.items=[];f.api.render(state,{tasks:true,control:true});assert.equal(f.all['widget-bonsai-status'].textContent,'暂无待办');assert.doesNotMatch(f.all['widget-yeardots-detail'].textContent,/待办已完成/);
  f.click('widget-orrery-toggle'); assert.equal(f.dispatched.at(-1).detail.action, 'focus-toggle'); f.click('widget-bonsai-toggle'); assert.equal(f.dispatched.at(-1).detail.page, 'work');
  f.api.render(state, { tasks: false, control: false }); assert.equal(f.all['widget-orrery-toggle'].disabled, true); assert.equal(f.all['widget-bonsai-toggle'].disabled, true); assert.doesNotMatch(f.all['widget-bonsai-detail'].textContent, /私人待办/); assert.doesNotMatch(f.all['widget-yeardots-detail'].textContent, /作品提交/);
  f.api.clear(); assert.equal(f.all['widget-orrery-status'].textContent, '--:--'); assert.doesNotMatch(JSON.stringify(f.stored()), /私人待办|作品提交|remaining|deadline/);
});
test('sunpath uses actual weather and clears stale daylight instead of inventing a position', () => {
  const f = fixture(), data = { temp: 21, sunrise: '2026-10-26T06:00', sunset: '2026-10-26T18:00', utcOffsetSeconds: 28800, fetchedAt: Date.parse('2026-10-26T00:00:00Z'), stale: false };
  const show = f.preference('sunpath'); show.checked = true; show.onchange(); f.api.render({ weather: { available: true, data } }, {});
  assert.equal(f.all['widget-sunpath-status'].textContent, '21°'); assert.match(f.all['widget-sunpath-detail'].textContent, /日落还有 10时0分/);
  data.stale = true; f.api.render({ weather: { available: true, data } }, {}); assert.equal(f.all['widget-sunpath-detail'].textContent, '天气待同步');
  data.stale = false; data.sunset = '2026-10-26T05:00'; f.api.render({ weather: { available: true, data } }, {}); assert.equal(f.all['widget-sunpath-detail'].textContent, '天气待同步');
  f.api.clear(); assert.equal(f.all['widget-sunpath-status'].textContent, '日光'); assert.doesNotMatch(JSON.stringify(f.stored()), /sunrise|sunset|fetchedAt/);
});
test('live geometry retains its SVG nodes, runs between data ticks and stops on every visibility policy', () => {
  const f=fixture(), walk=n=>[n,...n.children.flatMap(walk)], svg=id=>walk(f.all['widget-card-'+id]).find(n=>n.className===' widget-art');
  for(const id of ['hourglass','tide','orrery','bonsai']){const checkbox=f.preference(id);checkbox.checked=true;checkbox.onchange();}
  assert.equal(f.frames.size,0,'idle ornaments do not schedule a frame loop');
  assert.equal(walk(f.gallery).filter(n=>n.tagName==='image').length,0,'no PNG/image foreground');
  f.click('widget-tide-toggle'); const ribbon=walk(svg('tide')).find(n=>n.tagName==='path'), before=ribbon.attributes.d, defs=svg('tide').children[0];
  f.frame(17);assert.equal(svg('tide').children[0],defs);assert.equal(walk(svg('tide')).find(n=>n.tagName==='path'),ribbon);assert.notEqual(ribbon.attributes.d,before,'path moves within one second');assert.equal(f.frames.size,1,'one shared frame callback');
  f.reduce(true);assert.equal(f.frames.size,0);const reducedCurve=ribbon.attributes.d;f.advance(1200);assert.equal(ribbon.attributes.d,reducedCurve,'reduced motion does not keep changing the ribbon');
  f.reduce(false);assert.equal(f.frames.size,1);f.click('widget-tide-toggle');assert.equal(f.frames.size,0);
  f.click('widget-hourglass-toggle');assert.equal(f.frames.size,1);f.page('home');assert.equal(f.frames.size,0);f.page('widgets');assert.equal(f.frames.size,1,'return resumes drawing the running timer');f.click('widget-hourglass-toggle');
  const data=JSON.parse(JSON.stringify(sample));f.api.render(data,{tasks:true});f.frame(1300);assert.equal(f.frames.size,0);data.tasks.items[0].done=true;f.api.render(data,{tasks:true});assert.equal(f.frames.size,1);f.frame(1300);assert.equal(f.frames.size,0,'task change settles without an idle animation');
  const focus={available:true,total:1500,remaining:1200,deadline:(Date.parse('2026-10-26T00:00:00Z')+300000)/1000};f.api.render({focus},{});assert.equal(f.frames.size,1);f.api.pause();assert.equal(f.frames.size,0);f.hide();assert.equal(f.frames.size,0);
});
console.log(`Companion widgets: ${groups} test groups passed.`);
