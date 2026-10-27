'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../src/LiquidDesktop/companion-web/companion-settings.js'), 'utf8');
const clone = value => JSON.parse(JSON.stringify(value));
const desktopFields = ['theme','manualPaused','glassOpacity','textScale','reducedMotion','dockIconSize','dockAutoHide','showLabels','gridColumns','pauseOnBattery','pauseOnPowerSaver','pauseOnRemote','hiddenCards','hiddenLeftCards'];
const lyricsFields = ['font','layout','align','effect','texture','widthMode','color','secondary','accent','backgroundTheme','backgroundOpacity','backgroundMotion','fontSize','weight','spacing','intensity','speed','glow','width','secondaryOpacity'];
const initial = {
  settings: { available: true, revision: '638999999999999991', theme: 'Sky', glassOpacity: 90, textScale: 105, dockIconSize: 48, gridColumns: 6, showLabels: true, dockAutoHide: true, manualPaused: false, reducedMotion: false, pauseOnBattery: true, pauseOnPowerSaver: true, pauseOnRemote: true, hiddenCards: ['quota'], hiddenLeftCards: ['network', 'Life'] },
  lyricsSettings: { available: true, editable: true, revision: 'lyrics-revision-1', appearance: { font: 'sans', layout: 'duo', align: 'center', effect: 'float', texture: 'gradient', widthMode: 'adaptive', color: '#ffffff', secondary: '#dceaf9', accent: '#9edfff', backgroundTheme: 'transparent', backgroundOpacity: 35, backgroundMotion: false, fontSize: 26, weight: 650, spacing: 2, intensity: 65, speed: 100, glow: 30, width: 900, secondaryOpacity: 80 } }
};
function fixture() {
  const nodes = [], ids = {}, writes = [];
  class Element {
    constructor(tag) { this.tagName = tag; this.children = []; this.attrs = {}; this.disabled = false; this.checked = false; this.value = ''; this.type = tag === 'select' ? 'select-one' : ''; this._text = ''; nodes.push(this); }
    set id(value) { this._id = value; ids[value] = this; }
    get id() { return this._id; }
    set textContent(value) { this._text = String(value); this.children = []; }
    get textContent() { return this._text + this.children.map(child => child.textContent).join(''); }
    setAttribute(key, value) { this.attrs[key] = String(value); }
    appendChild(child) { this.children.push(child); if (this.tagName === 'select' && this.children.length === 1) this.value = child.value; return child; }
  }
  for (const name of ['desktop', 'lyrics']) for (const suffix of ['fields', 'status', 'save', 'reload', 'form']) { const node = new Element('div'); node.id = name + '-appearance-' + suffix; }
  const document = { createElement: tag => new Element(tag), getElementById: id => { assert(ids[id], 'Unknown settings element: ' + id); return ids[id]; } }, window = {};
  vm.runInNewContext(source, { document, window }, { filename: 'companion-settings.js' });
  const api = window.PrismCompanionSettings;
  api.init((key, url, payload, message, success) => { writes.push({ key, url, payload: clone(payload), message, success }); });
  const f = { nodes, ids, writes, api, state: clone(initial), grants: {control: true, music: true}, pending: {}, blocked: false,
    render() { api.render(this.state, this.grants, this.pending, this.blocked); },
    edit(id, value) { const input = ids[id]; assert(input, 'Missing field: ' + id); if (input.type === 'checkbox') input.checked = value; else input.value = String(value); (input.oninput || input.onchange).call(input); },
    submit(name) { ids[name + '-appearance-form'].onsubmit({ preventDefault() {} }); },
    card(label, checked) { const input = nodes.find(node => node.attrs['aria-label'] === '显示' + label); assert(input, 'Missing card: ' + label); if (checked !== undefined) { input.checked = checked; input.onchange(); } return input; }
  };
  return f;
}
let passed = 0, failed = 0;
function test(name, action) { try { action(); passed++; console.log('PASS ' + name); } catch (error) { failed++; console.error('FAIL ' + name + ': ' + error.message); } }

test('forms begin disabled and never save before authoritative state', () => {
  const f = fixture(); assert(f.ids['desktop-appearance-save'].disabled && f.ids['lyrics-appearance-save'].disabled); f.submit('desktop'); f.submit('lyrics'); assert.equal(f.writes.length, 0);
});
test('desktop form exposes the complete permitted contract and loads exact values', () => {
  const f = fixture(); f.render(); for (const key of desktopFields.filter(key => !key.startsWith('hidden'))) assert(f.ids['desktop-' + key], key);
  assert.equal(f.ids['desktop-glassOpacity'].value, '90'); assert.equal(f.ids['desktop-manualPaused'].checked, false); assert.equal(f.card('额度').checked, false); assert.equal(f.card('桌面入口').checked, true);
  f.edit('desktop-theme', 'Astro'); assert.equal(f.writes.length, 0); assert.match(f.ids['desktop-appearance-status'].textContent, /尚未保存/); f.submit('desktop');
  const call = f.writes[0]; assert.equal(call.key, 'settings'); assert.equal(call.url, '/api/settings'); assert.equal(call.payload.expectedRevision, initial.settings.revision); assert.deepEqual(Object.keys(call.payload.changes).sort(), desktopFields.slice().sort()); assert.equal(call.payload.changes.theme, 'Astro'); assert.equal(call.payload.changes.textScale, 105); assert.equal(typeof call.payload.changes.showLabels, 'boolean');
});
test('lyrics form includes every appearance field with typed explicit-save payload', () => {
  const f = fixture(); f.render(); for (const key of lyricsFields) assert(f.ids['lyrics-' + key], 'Missing permitted lyric field: ' + key);
  assert.equal(f.ids['lyrics-widthMode'].value, 'adaptive'); assert.equal(f.ids['lyrics-width'].value, '900'); assert.equal(Number(f.ids['lyrics-width'].min), 420); assert.equal(Number(f.ids['lyrics-width'].max), 1200);
  f.edit('lyrics-effect', 'spring'); f.edit('lyrics-fontSize', 30); f.edit('lyrics-backgroundMotion', true); f.edit('lyrics-widthMode', 'fixed'); f.edit('lyrics-width', 1000); assert.equal(f.writes.length, 0); f.submit('lyrics');
  const call = f.writes[0]; assert.equal(call.key, 'lyricsStyle'); assert.equal(call.url, '/api/lyrics-style'); assert.equal(call.payload.expectedRevision, 'lyrics-revision-1'); assert.deepEqual(Object.keys(call.payload.appearance).sort(), lyricsFields.slice().sort()); assert.equal(call.payload.appearance.fontSize, 30); assert.equal(call.payload.appearance.backgroundMotion, true); assert.equal(call.payload.appearance.color, '#ffffff'); assert.equal(call.payload.appearance.width, 1000); assert(!Object.hasOwn(call.payload, 'changes'));
});
test('card checkboxes serialize canonical hidden arrays with no duplicates', () => {
  const f = fixture(); f.render(); f.card('随手记', false); f.card('天气', false); f.card('生活', true); f.submit('desktop');
  assert.deepEqual(f.writes[0].payload.changes.hiddenCards, ['quota', 'notes']); assert.deepEqual(f.writes[0].payload.changes.hiddenLeftCards, ['network', 'Weather']); assert.equal(f.writes.length, 1);
});
test('same-revision polling preserves unsaved edits in both forms', () => {
  const f = fixture(); f.render(); f.edit('desktop-glassOpacity', 72); f.edit('lyrics-intensity', 81); f.state.settings.glassOpacity = 99; f.state.lyricsSettings.appearance.intensity = 10; f.render();
  assert.equal(f.ids['desktop-glassOpacity'].value, '72'); assert.equal(f.ids['lyrics-intensity'].value, '81'); assert(!f.ids['desktop-appearance-save'].disabled && !f.ids['lyrics-appearance-save'].disabled); assert.equal(f.writes.length, 0);
});
test('revision conflict retains draft, blocks overwrite and reloads only explicitly', () => {
  const f = fixture(); f.render(); f.edit('desktop-textScale', 120); f.card('计算器', false); f.state.settings.revision = '638999999999999992'; f.state.settings.textScale = 95; f.state.settings.hiddenCards = ['links']; f.render();
  assert.equal(f.ids['desktop-textScale'].value, '120'); assert.equal(f.card('计算器').checked, false); assert.match(f.ids['desktop-appearance-status'].textContent, /保留/); assert(f.ids['desktop-appearance-save'].disabled); f.submit('desktop'); assert.equal(f.writes.length, 0);
  f.ids['desktop-appearance-reload'].onclick(); assert.equal(f.ids['desktop-textScale'].value, '95'); assert.equal(f.card('计算器').checked, true); assert.equal(f.card('链接').checked, false); assert(f.ids['desktop-appearance-save'].disabled); f.edit('desktop-textScale', 100); f.submit('desktop'); assert.equal(f.writes[0].payload.expectedRevision, '638999999999999992');
});
test('failed lyric write has no success callback and retains original draft for conflict resolution', () => {
  const f = fixture(); f.render(); f.edit('lyrics-font', 'song'); f.submit('lyrics'); assert.equal(f.writes.length, 1); f.state.lyricsSettings.revision = 'lyrics-revision-2'; f.state.lyricsSettings.appearance.font = 'mono'; f.render();
  assert.equal(f.ids['lyrics-font'].value, 'song'); assert.match(f.ids['lyrics-appearance-status'].textContent, /保留/); assert(f.ids['lyrics-appearance-save'].disabled); f.ids['lyrics-appearance-reload'].onclick(); assert.equal(f.ids['lyrics-font'].value, 'mono');
});
test('save callback displays authoritative normalized readback and clears dirty state', () => {
  const f = fixture(); f.render(); f.edit('desktop-glassOpacity', 71); f.submit('desktop'); const saved = {...clone(initial.settings), revision: '638999999999999993', glassOpacity: 70}; f.writes[0].success(saved);
  assert.equal(f.ids['desktop-glassOpacity'].value, '70'); assert(f.ids['desktop-appearance-save'].disabled); assert.match(f.ids['desktop-appearance-status'].textContent, /已与电脑同步/); f.state.settings = saved; f.render(); assert.equal(f.ids['desktop-glassOpacity'].value, '70'); f.edit('desktop-textScale', 110); f.submit('desktop'); assert.equal(f.writes[1].payload.expectedRevision, saved.revision);
  f.edit('lyrics-color', '#ABCDEF'); f.submit('lyrics'); const lyric = {...clone(initial.lyricsSettings), revision: 'lyrics-revision-3'}; lyric.appearance.color = '#abcdef'; f.writes[2].success(lyric); f.state.lyricsSettings = lyric; f.render(); assert.equal(f.ids['lyrics-color'].value, '#abcdef'); assert(f.ids['lyrics-appearance-save'].disabled);
});
test('control and music permissions independently disable fields and writes', () => {
  const f = fixture(); f.render(); f.edit('desktop-theme', 'Astro'); f.edit('lyrics-font', 'mono'); f.grants.control = false; f.render(); assert(f.ids['desktop-theme'].disabled && f.ids['lyrics-font'].disabled && f.card('天气').disabled); f.submit('desktop'); f.submit('lyrics'); assert.equal(f.writes.length, 0);
  f.grants.control = true; f.grants.music = false; f.render(); assert(!f.ids['desktop-theme'].disabled && f.ids['lyrics-font'].disabled); f.submit('lyrics'); assert.equal(f.writes.length, 0);
  f.grants.music = true; f.state.lyricsSettings.editable = false; f.render(); assert(f.ids['lyrics-font'].disabled); f.state.settings.available = false; f.state.settings.reason = 'fixture unavailable'; f.render(); assert(f.ids['desktop-theme'].disabled); assert.equal(f.ids['desktop-appearance-status'].textContent, 'fixture unavailable');
});
test('busy and global write cooldown disable actions without discarding drafts', () => {
  const f = fixture(); f.render(); f.edit('desktop-theme', 'Astro'); f.edit('lyrics-font', 'mono'); f.pending = {settings: true, lyricsStyle: true}; f.render(); assert(f.ids['desktop-theme'].disabled && f.ids['lyrics-font'].disabled); f.submit('desktop'); f.submit('lyrics'); assert.equal(f.writes.length, 0);
  f.pending = {}; f.blocked = true; f.render(); assert(f.ids['desktop-appearance-save'].disabled && f.ids['lyrics-appearance-save'].disabled); f.blocked = false; f.render(); assert.equal(f.ids['desktop-theme'].value, 'Astro'); assert.equal(f.ids['lyrics-font'].value, 'mono'); assert(!f.ids['desktop-appearance-save'].disabled && !f.ids['lyrics-appearance-save'].disabled);
});
test('privacy clear disables all controls and does not create an automatic write', () => {
  const f = fixture(); f.render(); f.edit('desktop-theme', 'Astro'); f.api.clear(); assert(f.ids['desktop-theme'].disabled && f.ids['lyrics-font'].disabled); f.submit('desktop'); f.submit('lyrics'); assert.equal(f.writes.length, 0); f.render(); assert.equal(f.ids['desktop-theme'].value, 'Sky'); assert(f.ids['desktop-appearance-save'].disabled);
});
console.log('Companion settings: ' + passed + ' passed, ' + failed + ' failed; fake DOM and no service or user configuration access.');
if (failed) process.exitCode = 1;
