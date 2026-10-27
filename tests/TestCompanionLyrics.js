'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const coreSource = fs.readFileSync(path.join(__dirname, '../src/LiquidDesktop/lyrics-style.js'), 'utf8');
const source = fs.readFileSync(path.join(__dirname, '../src/LiquidDesktop/companion-web/companion-lyrics.js'), 'utf8');
const css = fs.readFileSync(path.join(__dirname, '../src/LiquidDesktop/companion-web/companion-appearance.css'), 'utf8');
let groups = 0;
const flush = async () => { for (let i = 0; i < 3; i++) await Promise.resolve(); };
function events() {
  const listeners = new Map();
  return { addEventListener(name, fn) { if (!listeners.has(name)) listeners.set(name, []); listeners.get(name).push(fn); }, emit(name) { for (const fn of listeners.get(name) || []) fn(); } };
}
function fixture(options = {}) {
  const createdAnimations = [], mutations = [], calls = { apply: 0, background: 0, animate: 0, cancel: 0 };
  const style = () => ({ setProperty(key, value) { this[key] = String(value); } });
  class Node {
    constructor(tag, className = '') {
      this.tagName = tag; this.className = className; this.children = []; this.parentElement = null; this.hidden = false; this.dataset = {}; this.style = style(); this._text = ''; this.attributes = {}; this.ownAnimations = [];
      this.classList = {
        contains: name => this.className.split(/\s+/).includes(name),
        add: name => { if (!this.classList.contains(name)) this.className = (this.className + ' ' + name).trim(); },
        remove: name => { this.className = this.className.split(/\s+/).filter(value => value !== name).join(' '); },
        toggle: (name, enabled) => { if (enabled) this.classList.add(name); else this.classList.remove(name); }
      };
    }
    get textContent() { return this._text + this.children.map(child => child.textContent).join(''); }
    set textContent(value) { this.children.forEach(child => { child.parentElement = null; }); this.children = []; this._text = String(value); }
    get firstElementChild() { return this.children[0] || null; }
    append(...children) { children.forEach(child => { child.remove(); child.parentElement = this; this.children.push(child); }); }
    prepend(child) { child.remove(); child.parentElement = this; this.children.unshift(child); }
    replaceChildren(...children) { this.textContent = ''; this.append(...children); }
    remove() { if (this.parentElement) { this.parentElement.children = this.parentElement.children.filter(child => child !== this); this.parentElement = null; } }
    setAttribute(name, value) { this.attributes[name] = String(value); }
    removeAttribute(name) { delete this.attributes[name]; if (name === 'style') this.style = style(); }
    matches(selector) { return selector.startsWith('.') && this.classList.contains(selector.slice(1)); }
    querySelectorAll(selector) { const selectors = selector.split(','); return this.children.flatMap(child => [...(selectors.some(value => child.matches(value)) ? [child] : []), ...child.querySelectorAll(selector)]); }
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
    getAnimations(opts = {}) { return [...this.ownAnimations, ...(opts.subtree ? this.children.flatMap(child => child.getAnimations(opts)) : [])].filter(animation => animation.playState === 'running'); }
    getClientRects() { for (let node = this; node; node = node.parentElement) if (node.hidden) return []; return [{}]; }
    animate(frames, settings) {
      let resolve, reject;
      const animation = { element: this, frames, settings, id: '', playState: 'running', finished: new Promise((yes, no) => { resolve = yes; reject = no; }), cancel() { if (this.playState === 'running') { this.playState = 'idle'; reject(new Error('cancelled')); } }, finish() { this.playState = 'finished'; resolve(); } };
      animation.finished.catch(() => {}); this.ownAnimations.push(animation); createdAnimations.push(animation); return animation;
    }
  }
  const body = new Node('body'), card = new Node('section'), container = new Node('div', 'lyrics'), currentWindow = new Node('div', 'lyric-window current-window'), nextWindow = new Node('div', 'lyric-window next-window'), current = new Node('span', 'lyric-main'), next = new Node('span', 'lyric-secondary');
  currentWindow.append(current); nextWindow.append(next); container.append(currentWindow, nextWindow); card.append(container); body.append(card);
  const media = { ...events(), matches: !!options.reduced };
  const window = { ...events(), matchMedia: () => media };
  const document = { ...events(), body, hidden: false, getElementById: id => ({ 'ipad-lyrics': container, 'lyric-current': current, 'lyric-next': next })[id], createElement: tag => new Node(tag) };
  class MutationObserver { constructor(fn) { mutations.push(fn); } observe() {} }
  const context = { window, document, MutationObserver, performance: { now: () => 1000 }, Promise, Intl };
  if (!options.missingCore) {
    vm.runInNewContext(coreSource, context, { filename: 'lyrics-style.js' });
    for (const key of Object.keys(calls)) { const original = window.PrismLyrics[key]; window.PrismLyrics[key] = (...args) => { calls[key]++; return original(...args); }; }
  }
  if (options.noAnimations) { container.getAnimations = undefined; currentWindow.animate = undefined; }
  vm.runInNewContext(source, context, { filename: 'companion-lyrics.js' });
  return { window, document, body, card, container, currentWindow, nextWindow, current, next, media, calls, createdAnimations, api: window.PrismCompanionLyrics, mutation() { mutations.forEach(fn => fn()); } };
}
const sample = { available: true, current: '此刻真实歌词', next: '真实下一句', appearance: { effect: 'float', texture: 'solid', layout: 'duo' }, palette: { album: ['#123456', '#234567', '#345678'], wallpaper: ['#456789', '#56789a', '#6789ab'] }, motionPaused: false, gap: false };
async function test(name, fn) { await fn(); groups++; console.log('PASS ' + name); }
(async function () {
  await test('real shared core applies styles but unchanged polls and progress never replay a line', () => {
    const f = fixture(); f.api.render(sample, false);
    assert.equal(f.current.textContent, sample.current); assert.equal(f.next.textContent, sample.next); assert.equal(f.calls.animate, 1);
    const count = f.createdAnimations.length, first = f.createdAnimations[0];
    for (let i = 0; i < 5; i++) f.api.render({ ...sample, position: i * 2, duration: 200 }, false);
    assert.equal(f.createdAnimations.length, count); assert.equal(first.playState, 'running'); assert.equal(f.calls.apply, 1); assert.equal(f.calls.background, 1);
    f.api.render({ ...sample, next: '校正下一行' }, false); assert.equal(f.calls.animate, 1); assert.equal(f.current.textContent, sample.current);
    f.api.render({ ...sample, current: '下一句已到' }, false); assert.equal(f.calls.animate, 2); assert.equal(first.playState, 'idle'); assert.equal(f.current.textContent, '下一句已到');
  });
  await test('layout, font category, texture, glow, palette and background flow reuse desktop normalization', () => {
    const f = fixture();
    f.api.render({ ...sample, appearance: { font: 'song', layout: 'split', align: 'right', texture: 'shimmer', color: '#aBcDeF', secondary: '#aabbcc', accent: '#99eeff', fontSize: 29, glow: 75, widthMode: 'fixed', width: 1200, backgroundTheme: 'album', backgroundOpacity: 42, backgroundMotion: true } }, false);
    assert.equal(f.container.dataset.layout, 'split'); assert.equal(f.container.dataset.texture, 'shimmer'); assert.equal(f.container.style['--lyric-color'], '#abcdef'); assert.equal(f.container.style['--lyric-glow'], '9px'); assert.equal(f.container.style['--lyric-size'], '29px'); assert.equal(f.container.style.textAlign, 'right'); assert.match(f.container.style.fontFamily, /Songti SC/); assert.equal(f.container.style['--copy-width'], '100%');
    const wash = f.container.querySelector('.companion-lyric-wash'), layer = wash.querySelector('.lyric-background');
    assert.equal(layer.hidden, false); assert.equal(layer.style.opacity, '0.42'); assert.equal(layer.classList.contains('flowing'), true); assert.match(layer.children[0].style.backgroundImage, /#123456/);
    const initialEntrances = f.calls.animate;
    for (const layout of ['single', 'stack', 'duo']) f.api.render({ ...sample, appearance: { layout, font: 'kai', texture: 'outline' } }, false);
    assert.equal(f.calls.animate, initialEntrances, 'style changes do not replay the current lyric'); assert.match(f.container.style.fontFamily, /Kaiti SC/);
    f.api.render({ ...sample, gap: true, appearance: { effect: 'none' } }, false); assert.equal(f.container.classList.contains('interlude'), true);
  });
  await test('real per-character entrances preserve text and are cancelled for reduced motion without invented timings', async () => {
    const f = fixture(); const music = { ...sample, current: '一👋二', appearance: { effect: 'wave', texture: 'neon', backgroundTheme: 'ocean', backgroundMotion: true } };
    f.api.render(music, false); assert.equal(f.current.querySelectorAll('.lyric-letter').length, 3); assert.equal(f.current.textContent, music.current);
    const before = f.createdAnimations.length; f.api.render({ ...music, position: 99 }, false); assert.equal(f.createdAnimations.length, before);
    f.body.dataset.reduced = 'true'; f.mutation(); await flush();
    assert.equal(f.current.querySelectorAll('.lyric-letter').length, 0); assert.equal(f.current.textContent, music.current); assert.equal(f.container.classList.contains('reduced'), true); assert.equal(f.container.getAnimations({ subtree: true }).length, 0);
    assert.equal(f.container.querySelector('.lyric-background').style['--background-play'], 'paused');
    f.body.dataset.reduced = 'false'; f.mutation(); assert.equal(f.container.classList.contains('reduced'), false); assert.equal(f.createdAnimations.length, before, 'resume does not replay text');
  });
  await test('explicit still, desktop pause, OS preference and offscreen pages pause every effect', () => {
    for (const mode of ['argument', 'desktop', 'os', 'page']) {
      const f = fixture({ reduced: mode === 'os' }); if (mode === 'page') f.card.hidden = true;
      f.api.render({ ...sample, motionPaused: mode === 'desktop', appearance: { texture: 'shimmer', backgroundTheme: 'album', backgroundMotion: true } }, mode === 'argument');
      assert.equal(f.calls.animate, 0, mode); assert.equal(f.container.classList.contains('reduced'), true, mode); assert.equal(f.container.querySelector('.lyric-background').style['--background-play'], 'paused', mode);
    }
    const f = fixture(); f.api.render(sample, false); f.media.matches = true; f.media.emit('change'); assert.equal(f.container.getAnimations({ subtree: true }).length, 0); assert.equal(f.container.classList.contains('reduced'), true);
  });
  await test('palette transitions are interruptible and unchanged polling keeps the same background host', () => {
    const f = fixture(); const music = { ...sample, appearance: { backgroundTheme: 'album', backgroundOpacity: 50, backgroundMotion: true } };
    f.api.render(music, false); const wash = f.container.querySelector('.companion-lyric-wash');
    f.api.render({ ...music, palette: { album: ['#aaaabb', '#bbbbcc', '#ccccdd'] } }, false);
    assert.equal(f.container.querySelector('.companion-lyric-wash'), wash); assert.equal(f.calls.animate, 1);
    const transition = f.createdAnimations.find(animation => animation.id === 'lyric-background-transition'); assert.ok(transition); assert.equal(transition.playState, 'running');
    f.api.render({ ...music, palette: { album: ['#aaaabb', '#bbbbcc', '#ccccdd'] } }, true); assert.equal(transition.playState, 'idle'); assert.equal(f.current.textContent, sample.current);
  });
  await test('privacy clear, unavailable, hidden and pagehide erase text, colours and running copies', async () => {
    for (const cause of ['clear', 'unavailable', 'hidden', 'pagehide', 'empty']) {
      const f = fixture(); f.api.render({ ...sample, appearance: { effect: 'cascade', backgroundTheme: 'album', backgroundMotion: true } }, false); const host = f.container.querySelector('.companion-lyric-wash');
      if (cause === 'clear') f.api.clear();
      if (cause === 'unavailable') f.api.render({ ...sample, available: false }, false);
      if (cause === 'hidden') { f.document.hidden = true; f.document.emit('visibilitychange'); }
      if (cause === 'pagehide') f.window.emit('pagehide');
      if (cause === 'empty') f.api.render({ available: true, current: '', next: '' }, false);
      await flush(); assert.equal(f.current.textContent, '', cause); assert.equal(f.next.textContent, '', cause); assert.equal(f.container.hidden, true, cause); assert.equal(f.container.querySelector('.companion-lyric-wash'), null, cause); assert.equal(host.parentElement, null, cause); assert.equal(f.container.style['--lyric-color'], undefined, cause); assert.equal(f.container.getAnimations({ subtree: true }).length, 0, cause);
      f.document.hidden = false; f.api.render(sample, false); assert.equal(f.current.textContent, sample.current); assert.notEqual(f.container.querySelector('.companion-lyric-wash'), host);
    }
    const hidden = fixture(); hidden.document.hidden = true; hidden.api.render(sample, false); assert.equal(hidden.current.textContent, ''); assert.equal(hidden.createdAnimations.length, 0);
  });
  await test('missing animation capabilities and missing shared script retain truthful readable text', () => {
    for (const options of [{ noAnimations: true }, { missingCore: true }]) {
      const f = fixture(options); f.api.render({ ...sample, current: '<img src=x onerror=alert(1)>', appearance: { backgroundTheme: 'album' } }, false);
      assert.equal(f.current.textContent, '<img src=x onerror=alert(1)>'); assert.equal(f.current.children.length, 0); assert.equal(f.createdAnimations.length, 0); f.api.clear(); assert.equal(f.current.textContent, '');
    }
    assert.match(css, /#ipad-lyrics \.lyric-window\{[^}]*width:100%[^}]*white-space:normal/);
    assert.match(css, /#ipad-lyrics \.lyric-main,#ipad-lyrics \.lyric-secondary\{[^}]*white-space:pre-wrap[^}]*overflow-wrap:anywhere/);
    assert.doesNotMatch(css, /(?:^|[;{])\s*width:\s*(900|1200)px/);
    assert.match(css, /@media\(prefers-reduced-motion:reduce\)/);
    assert.match(css, /#appearance-page \.appearance-presets button\{[^}]*min-width:44px;min-height:44px/);
  });
  console.log(`Companion lyrics: ${groups} test groups passed using the real shared PrismLyrics core.`);
}()).catch(error => { console.error(error); process.exitCode = 1; });
