'use strict';
// Run with node tests/TestCompanionGlass.js. No GPU, server, or private data.
// Recorded GL calls verify scene ownership and lifecycle; real optical appearance
// remains a browser/device check rather than a claim made by this harness.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../src/LiquidDesktop/companion-web/companion-glass.js'), 'utf8');
const materialSource = fs.readFileSync(path.join(__dirname, '../src/LiquidDesktop/glass-materials.js'), 'utf8');
const css = fs.readFileSync(path.join(__dirname, '../src/LiquidDesktop/companion-web/companion.css'), 'utf8');
let groups = 0;

function target() {
  const listeners = new Map();
  return {
    addEventListener(name, fn) { if (!listeners.has(name)) listeners.set(name, new Set()); listeners.get(name).add(fn); },
    removeEventListener(name, fn) { if (listeners.has(name)) listeners.get(name).delete(fn); },
    emit(name, detail = {}) { for (const fn of [...(listeners.get(name) || [])]) fn({ type: name, ...detail }); },
    listenerCount() { return [...listeners.values()].reduce((sum, list) => sum + list.size, 0); }
  };
}
function rect(left, top, width, height) { return { left, top, width, height, right: left + width, bottom: top + height }; }
function fakeGL(options) {
  let counter = 0, currentProgram, currentTexture, currentFramebuffer = null, currentScissor = null;
  const uniforms = new Map(), draws = [], creates = [], deletes = [], allocations = [], shaderSources = [];
  const gl = { draws, creates, deletes, allocations, shaderSources };
  ['VERTEX_SHADER', 'FRAGMENT_SHADER', 'COMPILE_STATUS', 'LINK_STATUS', 'ARRAY_BUFFER', 'FLOAT', 'STATIC_DRAW', 'TEXTURE0', 'TEXTURE_2D', 'TEXTURE_MIN_FILTER', 'TEXTURE_MAG_FILTER', 'TEXTURE_WRAP_S', 'TEXTURE_WRAP_T', 'LINEAR', 'CLAMP_TO_EDGE', 'RGBA', 'UNSIGNED_BYTE', 'FRAMEBUFFER', 'FRAMEBUFFER_COMPLETE', 'COLOR_ATTACHMENT0', 'HIGH_FLOAT', 'SCISSOR_TEST', 'TRIANGLES'].forEach((key, index) => { gl[key] = index + 1; });
  for (const kind of ['Shader', 'Program', 'Buffer', 'Texture', 'Framebuffer']) {
    gl['create' + kind] = () => { const value = { kind, id: ++counter }; creates.push(value); return value; };
    gl['delete' + kind] = value => { if (value) deletes.push(value); };
  }
  Object.assign(gl, {
    shaderSource(shader, text) { shaderSources.push(text); }, compileShader() {}, attachShader() {}, linkProgram() {},
    getShaderParameter() { return !options.shaderFailure; }, getShaderInfoLog() { return 'fixture compile failure'; },
    getProgramParameter() { return true; }, getProgramInfoLog() { return ''; },
    getAttribLocation() { return 0; }, getUniformLocation(program, name) { return { program, name }; },
    useProgram(program) { currentProgram = program; if (!uniforms.has(program)) uniforms.set(program, {}); },
    uniform1f(location, a) { uniforms.get(location.program)[location.name] = a; },
    uniform1i(location, a) { uniforms.get(location.program)[location.name] = a; },
    uniform2f(location, a, b) { uniforms.get(location.program)[location.name] = [a, b]; },
    uniform4f(location, a, b, c, d) { uniforms.get(location.program)[location.name] = [a, b, c, d]; },
    bindBuffer() {}, bufferData() {}, enableVertexAttribArray() {}, vertexAttribPointer() {}, activeTexture() {},
    bindTexture(type, value) { currentTexture = value; }, texParameteri() {},
    texImage2D(...args) { allocations.push({ texture: currentTexture, args }); },
    bindFramebuffer(type, value) { currentFramebuffer = value; }, framebufferTexture2D() {},
    checkFramebufferStatus() { return options.framebufferFailure ? -1 : gl.FRAMEBUFFER_COMPLETE; },
    getShaderPrecisionFormat() { return { precision: options.lowPrecision ? 0 : 23 }; },
    viewport() {}, enable() {}, disable(value) { if (value === gl.SCISSOR_TEST) currentScissor = null; },
    scissor(...value) { currentScissor = value; },
    drawArrays() { draws.push({ program: currentProgram, texture: currentTexture, framebuffer: currentFramebuffer, scissor: currentScissor, uniforms: { ...uniforms.get(currentProgram) } }); }
  });
  return gl;
}
function fixture(options = {}) {
  const gl = fakeGL(options), frames = new Map(), images = [], observers = [], resizeObservers = [];
  const measurements = { boundsReads: 0, styleReads: 0, hiddenWrites: 0, opticsWrites: 0 };
  let nextFrame = 1, time = 0, contextCalls = 0, contextOptions, performanceCalls = 0;
  const viewport = { width: 1024, height: 768 };
  const body = Object.assign(target(), { style: {values:{},setProperty(key,value){this.values[key]=value;}}, dataset: { theme: 'Sky', reduced: options.reduced ? 'true' : 'false' }, firstChild: null, children: [], insertBefore(node) { this.children.unshift(node); node.parentNode = this; }, });
  let optics; Object.defineProperty(body.dataset, 'optics', { get() { return optics; }, set(value) { measurements.opticsWrites++; optics = value; } });
  const main = Object.assign(target(), { id: 'main-content', bounds: rect(20, 80, 984, 490), getBoundingClientRect() { return this.bounds; }, contains(node) { return node.inMain; } });
  function region(id, bounds, inMain = true) {
    return { id, nodeType:1, bounds, inMain, hidden: false, attributes:{}, children:[], style: { visibility: 'visible', display: 'block', borderTopLeftRadius: '35px' }, contains(node){return this.children.includes(node);}, closest(){return null;},matches(){return true;},querySelector(){return null;},getAttribute(key){return this.attributes[key];},hasAttribute(key){return key in this.attributes;},setAttribute(key,value){this.attributes[key]=value;},removeAttribute(key){delete this.attributes[key];},getClientRects() { return this.hidden ? [] : [this.bounds]; }, getBoundingClientRect() { return this.bounds; } };
  }
  // Deliberately reverse document priority and include a fourth candidate.
  const nodes = [region('decorative-extra', rect(30, 90, 100, 100)), region('home-focus', rect(60, 460, 240, 80)), region('music-card', rect(530, 70, 400, 290)), region('touch-bar', rect(20, 600, 984, 148), false)];
  nodes[0].hidden=!options.extra;
  let canvas;
  const document = Object.assign(target(), {
    body, hidden: !!options.hidden,
    createElement(name) {
      assert.equal(name, 'canvas');
      canvas = Object.assign(target(), { width: 300, height: 150, attributes: {}, setAttribute(key, value) { this.attributes[key] = value; }, getBoundingClientRect() { measurements.boundsReads++; return this.hidden ? rect(0, 0, 0, 0) : rect(0, 0, viewport.width, viewport.height); }, getContext(kind, opts) { contextCalls++; contextOptions = opts; return options.noGL ? null : gl; }, remove() { body.children = body.children.filter(node => node !== this); } });
      let hidden = false; Object.defineProperty(canvas, 'hidden', { get() { return hidden; }, set(value) { measurements.hiddenWrites++; hidden = value; } });
      return canvas;
    },
    querySelectorAll(selector) { assert.equal(selector, '[data-optical],.glass,.companion-widget'); return nodes; },
    getElementById(id) { return id === 'main-content' ? main : nodes.find(node => node.id === id); }
  });
  const media = Object.assign(target(), { matches: !!options.osReduced });
  const window = Object.assign(target(), { location: new URL('https://companion.test:9443/' + (options.query === undefined ? '?opticalTest=grid' : options.query)), devicePixelRatio: 2, getComputedStyle(node) { measurements.styleReads++; return node.style; }, matchMedia() { return media; } });
  class Image {
    constructor() { images.push(this); this.naturalWidth = 1448; this.naturalHeight = 1086; }
    set src(value) { this.url = value; }
  }
  class MutationObserver { constructor(fn) { this.fn = fn; observers.push(this); } observe() {} disconnect() { this.disconnected = true; } }
  class ResizeObserver { constructor(fn) { this.fn = fn; this.nodes = []; resizeObservers.push(this); } observe(node) { this.nodes.push(node); } unobserve(node){this.nodes=this.nodes.filter(n=>n!==node);} disconnect() { this.disconnected = true; } }
  if(!options.missingMaterials)vm.runInNewContext(materialSource,{window},{filename:'glass-materials.js'});
  vm.runInNewContext(source, { window, document, URLSearchParams, URL, Image, MutationObserver, ResizeObserver, Float32Array, Uint8Array, performance: { now: () => time + (performanceCalls++ % 2) * (options.frameCost || 0) }, requestAnimationFrame(fn) { const id = nextFrame++; frames.set(id, fn); return id; }, cancelAnimationFrame(id) { frames.delete(id); } }, { filename: 'companion-glass.js' });
  return { gl, images, body, document, window, media, canvas, nodes, main, viewport, observers, resizeObservers, measurements, api: window.PrismGlassOptics, pending: () => frames.size, contextCalls: () => contextCalls, contextOptions: () => contextOptions, step(ms) { time = ms; const batch = [...frames.values()]; frames.clear(); batch.forEach(fn => fn(ms)); }, mutation() { observers[0].fn(); } };
}

function test(name, fn) { fn(); groups++; console.log('PASS ' + name); }

test('inactive, forced off, hidden and reduced paths have no GPU or image work', () => {
  const inactive = fixture({ query: '' });
  assert.equal(inactive.contextCalls(), 0); assert.equal(inactive.images.length, 0); assert.equal(inactive.pending(), 0);
  for (const options of [{ query: '?optical=off' }, { hidden: true }, { reduced: true }, { osReduced: true }]) {
    const f = fixture(options); f.api.setActive(true); assert.equal(f.contextCalls(), 0); assert.equal(f.pending(), 0); assert.equal(f.images.length, 0); assert.equal(f.body.dataset.optics, 'fallback');
  }
  assert.match(css, /\.optical-scene\{[^}]*pointer-events:none/);
  assert.match(css, /button\{min-width:44px;min-height:44px;/);
});

test('one shared scene, three priority regions, real clipped bounds and bounded resolution', () => {
  const f = fixture(); f.api.setActive(true); f.step(0);
  assert.equal(f.contextCalls(), 1); assert.equal(f.images.length, 0); assert.equal(f.body.dataset.optics, 'ready'); assert.equal(f.canvas.hidden, false);
  assert.equal(f.api.inspect().regions, 3); assert.equal(f.gl.draws.length, 5);
  const [scene, background, touch, music, focus] = f.gl.draws;
  assert.ok(scene.framebuffer); assert.equal(background.framebuffer, null);
  assert.equal(background.texture, touch.texture); assert.equal(background.texture, music.texture); assert.equal(background.texture, focus.texture);
  assert.notEqual(scene.texture, background.texture); assert.equal(background.uniforms.backgroundOnly, 1); assert.equal(music.uniforms.backgroundOnly, 0);
  assert.deepEqual(touch.uniforms.center, [512, 674]); assert.deepEqual(music.uniforms.center, [730, 215]); assert.deepEqual(focus.uniforms.center, [180, 500]);
  assert.deepEqual(music.uniforms.halfSize, [200, 145], 'clipping must not change lens geometry');
  const scale = Math.sqrt(1800000 / (1024 * 768));
  assert.equal(music.scissor[1] + music.scissor[3], f.canvas.height - Math.floor(80 * scale), 'music paint clipped at main top');
  assert.ok(f.canvas.width * f.canvas.height < 1803000); assert.ok(f.api.inspect().sceneWidth < f.canvas.width);
  assert.equal(f.contextOptions().preserveDrawingBuffer, false); assert.equal(f.contextOptions().antialias, false);
  assert.equal(f.gl.creates.filter(x => x.kind === 'Framebuffer').length, 1);
});

test('display cadence accepts high-refresh callbacks and theme uses same scene path', () => {
  const f = fixture(); f.api.setActive(true); f.step(0);
  f.api.setActive(true); f.step(8.333); f.step(16.667); assert.equal(f.api.inspect().frames, 3);
  f.step(25); assert.equal(f.api.inspect().frames, 4);
  f.body.dataset.theme = 'Astro'; f.mutation(); f.step(68);
  const draws = f.gl.draws.slice(-5); assert.equal(draws[0].uniforms.night, 1); assert.equal(draws[1].texture, draws[2].texture);
  assert.equal(f.contextCalls(), 1); assert.equal(f.gl.creates.filter(x => x.kind === 'Texture').length, 2);
  f.nodes.forEach(node => { node.hidden = true; }); f.api.invalidate(); f.step(102); assert.equal(f.api.inspect().regions, 0); assert.equal(f.pending(), 0);
});

test('image is same origin, loaded once, and GPU scene uploads only once per context', () => {
  const f = fixture({ query: '' }); f.api.setActive(true);
  assert.equal(f.images.length, 1); assert.equal(f.images[0].url, 'https://companion.test:9443/companion-scene.jpg'); assert.equal(f.pending(), 0);
  f.images[0].onload(); f.step(0); f.step(34); f.api.invalidate(); f.step(68);
  assert.equal(f.images.length, 1); assert.equal(f.gl.allocations.filter(call => call.args.length === 6).length, 1);
  const originalSize = f.canvas.width; f.viewport.width = 768; f.viewport.height = 1024; f.window.emit('resize'); f.step(102);
  assert.notEqual(f.canvas.width, originalSize); assert.equal(f.gl.creates.filter(x => x.kind === 'Framebuffer').length, 1);
});

test('stable animation keeps every optical pass without repeated DOM writes or layout reads', () => {
  const f = fixture(); f.api.setActive(true); f.step(0); const initial = { ...f.measurements }, allocations = f.gl.allocations.length;
  for (let i = 1; i <= 60; i++) f.step(i * 34);
  assert.deepEqual(f.measurements, initial, 'unchanged frames must reuse layout and ready/visible state');
  assert.equal(f.api.inspect().frames, 61); assert.equal(f.api.inspect().scenePasses, 1); assert.equal(f.gl.draws.length, 245); assert.equal(f.gl.allocations.length, allocations);
  assert.ok(f.gl.draws.at(-2).uniforms.time > f.gl.draws[1].uniforms.time, 'glass lighting advances while the background stays cached');
  f.document.emit('pointerdown', { clientX: 730, clientY: 215 }); f.step(2074); assert.equal(f.gl.draws.at(-2).uniforms.press, 1, 'pointer response still updates without remeasuring layout'); assert.deepEqual(f.measurements, initial);
});

test('resize, scrolling, region resize and foreground restoration refresh cached geometry', () => {
  const f = fixture(); f.api.setActive(true); f.step(0); const initialWidth = f.canvas.width;
  f.viewport.width = 768; f.viewport.height = 1024; f.window.emit('resize'); f.step(34); assert.notEqual(f.canvas.width, initialWidth);
  const music = f.nodes.find(node => node.id === 'music-card'), originalClip = f.gl.draws.at(-2).scissor;
  music.bounds = rect(530, 170, 400, 290); f.main.emit('scroll'); f.step(68); assert.deepEqual(f.gl.draws.at(-2).uniforms.center, [730, 315]); assert.notDeepEqual(f.gl.draws.at(-2).scissor, originalClip);
  music.bounds = rect(200, 170, 500, 290); f.resizeObservers[0].fn(); f.step(102); assert.deepEqual(f.gl.draws.at(-2).uniforms.halfSize, [250, 145]);
  f.document.hidden = true; f.document.emit('visibilitychange'); f.viewport.width = 1200; f.viewport.height = 700;
  const beforeResume = f.measurements.boundsReads; f.document.hidden = false; f.document.emit('visibilitychange'); f.step(1000);
  assert.ok(f.measurements.boundsReads > beforeResume); assert.deepEqual(f.gl.draws.at(-4).uniforms.resolution, [1200, 700]); assert.equal(f.canvas.hidden, false); assert.equal(f.body.dataset.optics, 'ready'); assert.equal(f.contextCalls(), 1);
});

test('slow-frame quality reduction reallocates and remeasures once, then keeps cached geometry', () => {
  const f = fixture({ frameCost: 31 }); f.api.setActive(true); f.step(0); const originalWidth = f.canvas.width, originalSceneWidth = f.api.inspect().sceneWidth, allocations = f.gl.allocations.length;
  for (let i = 1; i <= 13; i++) f.step(i * 34);
  assert.equal(f.canvas.width, Math.round(originalWidth * .75)); assert.ok(f.api.inspect().sceneWidth < originalSceneWidth); assert.equal(f.gl.allocations.length, allocations + 1); assert.equal(f.api.inspect().regions, 3);
  const stable = { ...f.measurements }; f.step(476); f.step(510); assert.deepEqual(f.measurements, stable); assert.equal(f.gl.draws.length, 66); assert.equal(f.contextCalls(), 1);
});

test('hidden, reduced, pagehide and authentication stops preserve DOM and resume without extra context', () => {
  const f = fixture(); f.api.setActive(true); f.step(0);
  f.document.hidden = true; f.document.emit('visibilitychange'); assert.equal(f.pending(), 0); assert.equal(f.canvas.hidden, true); assert.equal(f.nodes[2].hidden, false);
  f.document.hidden = false; f.document.emit('visibilitychange'); f.step(1000); assert.equal(f.body.dataset.optics, 'ready');
  f.body.dataset.reduced = 'true'; f.mutation(); assert.equal(f.pending(), 0); assert.equal(f.body.dataset.optics, 'fallback');
  f.body.dataset.reduced = 'false'; f.mutation(); f.step(1034); assert.equal(f.body.dataset.optics, 'ready');
  f.window.emit('pagehide'); assert.equal(f.pending(), 0); f.window.emit('pageshow'); f.step(2000); assert.equal(f.body.dataset.optics, 'ready');
  f.api.setActive(false); f.step(3000); assert.equal(f.pending(), 0); assert.equal(f.canvas.hidden, true); assert.equal(f.contextCalls(), 1);
});

test('context loss falls back and restoration recreates resources without another canvas/context', () => {
  const f = fixture({ query: '' }); f.api.setActive(true); f.images[0].onload(); f.step(0);
  let prevented = false; f.canvas.emit('webglcontextlost', { preventDefault() { prevented = true; } });
  assert.equal(prevented, true); assert.equal(f.pending(), 0); assert.equal(f.body.dataset.optics, 'fallback');
  f.canvas.emit('webglcontextrestored'); f.step(1000);
  assert.equal(f.body.dataset.optics, 'ready'); assert.equal(f.contextCalls(), 1); assert.equal(f.body.children.length, 1); assert.equal(f.images.length, 1);
  assert.equal(f.gl.creates.filter(x => x.kind === 'Framebuffer').length, 2); assert.equal(f.gl.allocations.filter(call => call.args.length === 6).length, 2);
});

test('capability, shader, image and framebuffer failures keep usable CSS fallback without retry loops', () => {
  for (const options of [{ noGL: true }, { lowPrecision: true }, { shaderFailure: true }, { framebufferFailure: true }]) {
    const f = fixture(options); f.api.setActive(true); f.step(0); f.api.invalidate(); f.step(1000);
    assert.equal(f.body.dataset.optics, 'fallback'); assert.equal(f.canvas.hidden, true); assert.equal(f.pending(), 0); assert.equal(f.contextCalls(), 1);
    assert.ok(f.api.inspect().error);
    if (options.shaderFailure) assert.equal(f.gl.creates.length, f.gl.deletes.length, 'partly compiled resources released');
  }
  const f = fixture({ query: '' }); f.api.setActive(true); f.images[0].onerror(); assert.equal(f.pending(), 0); assert.equal(f.body.dataset.optics, 'fallback');
});

test('destroy releases owned resources, observers and listeners without touching private nodes', () => {
  const f = fixture(); f.api.setActive(true); f.step(0); f.api.destroy(); f.api.destroy();
  assert.equal(f.pending(), 0); assert.equal(f.body.children.length, 0); assert.equal(f.nodes.length, 4);
  assert.equal(f.document.listenerCount(), 0); assert.equal(f.window.listenerCount(), 0); assert.equal(f.media.listenerCount(), 0); assert.equal(f.main.listenerCount(), 0); assert.equal(f.canvas.listenerCount(), 0);
  assert.equal(f.observers[0].disconnected, true); assert.equal(f.resizeObservers[0].disconnected, true);
  assert.equal(f.gl.creates.length, f.gl.deletes.length);
});
test('touch and visible ambience follow display cadence without claiming presentation',()=>{
  const f=fixture();f.api.setActive(true);f.step(0);f.document.emit('pointerdown',{clientX:730,clientY:215});f.step(17);f.step(34);
  assert.equal(f.api.inspect().frames,3);assert.equal(f.api.inspect().targetFps,null);assert.equal(f.api.inspect().scenePasses,1);assert.equal(f.api.inspect().cadence,'display');assert.equal(f.api.inspect().presentationMeasured,false);
  f.document.emit('pointerup',{clientX:730,clientY:215});f.step(340);assert.equal(f.api.inspect().targetFps,null);
});
test('all ten materials use shared resolution without new contexts or scene passes',()=>{
  const f=fixture();f.api.setActive(true);f.step(0);let time=34;
  assert.equal(Object.keys(f.window.PrismGlassMaterials).length,10);
  for(const key of Object.keys(f.window.PrismGlassMaterials)){
    f.api.setStyle(key);f.step(time);time+=34;const values=f.gl.draws.at(-1).uniforms,p=f.window.PrismGlassMaterials.resolve(key);
    assert.equal(values.materialOn,p.legacy?0:1);assert.equal(f.body.dataset.glassStyle,key);
    if(p.legacy){assert.equal(f.body.dataset.materialCustom,'false');continue;}
    assert.equal(values.materialBevel,p.bevel);assert.equal(values.materialIor,p.ior);assert.equal(values.materialBlur,p.blur);assert.equal(values.materialDiffusion,p.diffusion);assert.equal(values.materialGrain,p.grain);assert.deepEqual(values.materialTint,Array.from(p.tint));assert.equal(values.materialGlasswork,p.model==='glasswork'?1:0);
    for(const field of ['brightness','contrast','edge','power','opacity','adaptive','density','tintMin','tintMax'])assert.equal(values['material'+field[0].toUpperCase()+field.slice(1)],p[field],field);
  }
  assert.equal(f.contextCalls(),1);assert.equal(f.api.inspect().scenePasses,1);
});
test('empty unknown and inherited IDs restore the original material path',()=>{
  const f=fixture();f.api.setActive(true);f.step(0);f.api.setStyle('G');f.step(34);let time=68;
  for(const key of ['', 'constructor', 'K', null, {toString(){return 'H';}}]){f.api.setStyle(key);f.step(time);time+=34;const values=f.gl.draws.at(-1).uniforms;assert.equal(values.materialOn,0);assert.equal(values.materialGrain,0);assert.equal(values.materialGlasswork,0);assert.equal(f.body.dataset.materialCustom,'false');assert.deepEqual(values.materialTint,[0,0,0,0]);}
});
test('changing overrides of the selected style updates immediately and switching keeps independent values',()=>{
  const f=fixture();f.api.setActive(true);let time=0;
  function apply(key,map){f.api.setStyle(key,map);f.step(time);time+=34;return f.gl.draws.at(-1).uniforms;}
  const overrides={G:{blur:9,opacity:.8},H:{blur:3,tintR:.5},J:{brightness:1.7,saturation:2.28,edge:1.9,power:3.5,density:.4,tintMin:.1,tintMax:.4,adaptive:.6}};
  assert.equal(apply('G',overrides).materialBlur,9);overrides.G.blur=4;assert.equal(apply('G',overrides).materialBlur,4);
  assert.equal(apply('H',overrides).materialBlur,3);assert.equal(apply('G',overrides).materialBlur,4);
  const j=apply('J',overrides);assert.equal(j.materialGlasswork,1);assert.equal(j.materialBrightness,1.7);assert.equal(j.materialSaturation,2.28);assert.equal(j.materialEdge,1.9);assert.equal(j.materialPower,3.5);assert.equal(j.materialDensity,.4);assert.equal(j.materialAdaptive,.6);assert.equal(j.materialTintMin,.1);assert.equal(j.materialTintMax,.4);
  assert.equal(apply('G',{}).materialBlur,f.window.PrismGlassMaterials.G.blur);assert.equal(f.contextCalls(),1);assert.equal(f.api.inspect().scenePasses,1);
});
test('classic empty and explicit I stay original until I is edited and reset restores it',()=>{
  const f=fixture();f.api.setActive(true);f.api.setStyle('I');f.step(0);assert.equal(f.gl.draws.at(-1).uniforms.materialOn,0);
  f.api.setStyle('I',{I:{blur:12}});f.step(34);assert.equal(f.gl.draws.at(-1).uniforms.materialOn,1);assert.equal(f.gl.draws.at(-1).uniforms.materialBlur,12);
  f.api.setStyle('',{I:{blur:6}});f.step(68);assert.equal(f.gl.draws.at(-1).uniforms.materialBlur,6);assert.equal(f.body.dataset.materialCustom,'true');
  f.api.setStyle('I',{});f.step(102);assert.equal(f.gl.draws.at(-1).uniforms.materialOn,0);assert.equal(f.body.dataset.materialCustom,'false');
});
test('saved custom material is independent of its base overrides and remains live editable',()=>{
  const f=fixture(),id='user-'+'a'.repeat(32),presets=[{id,name:'My glass',baseStyle:'J',parameters:{blur:7,brightness:1.3}}];f.api.setActive(true);
  f.api.setStyle(id,{J:{blur:25}},presets);f.step(0);assert.equal(f.body.dataset.glassStyle,id);assert.equal(f.gl.draws.at(-1).uniforms.materialBlur,7);assert.equal(f.gl.draws.at(-1).uniforms.materialGlasswork,1);
  presets[0].parameters.blur=2;f.api.setStyle(id,{J:{blur:25}},presets);f.step(34);assert.equal(f.gl.draws.at(-1).uniforms.materialBlur,2);
  f.api.setStyle(id,{},[]);f.step(68);assert.equal(f.gl.draws.at(-1).uniforms.materialOn,0);assert.equal(f.body.dataset.materialCustom,'false');
});
test('material changes preserve Astro and pointer feedback',()=>{
  const f=fixture();f.body.dataset.theme='Astro';f.api.setActive(true);f.api.setStyle('H');f.step(0);f.document.emit('pointerdown',{clientX:730,clientY:215});f.step(17);const values=f.gl.draws.at(-2).uniforms;
  assert.equal(values.night,1);assert.equal(values.press,1);assert.equal(values.materialOn,1);assert.equal(f.api.inspect().targetFps,null);
});
test('mouse hover lights the body without pressing and released touch clears it',()=>{
 const f=fixture();f.api.setActive(true);f.step(0);f.document.emit('pointermove',{clientX:830,clientY:215,pointerType:'mouse'});f.step(8.333);
 assert.equal(f.gl.draws.at(-2).uniforms.hover,1);assert.equal(f.gl.draws.at(-2).uniforms.press,0);assert.deepEqual(f.gl.draws.at(-2).uniforms.point,[.5,0]);
 f.document.emit('pointerup',{clientX:830,clientY:215,pointerType:'touch'});f.step(16.667);assert.equal(f.gl.draws.at(-2).uniforms.hover,0);
 f.document.emit('pointerdown',{clientX:830,clientY:215,pointerType:'touch'});f.step(25);assert.equal(f.gl.draws.at(-2).uniforms.press,1);
 f.document.emit('pointercancel',{clientX:830,clientY:215,pointerType:'touch'});f.step(33.333);assert.equal(f.gl.draws.at(-2).uniforms.hover,0);assert.equal(f.gl.draws.at(-2).uniforms.press,0);
});
test('reduced mode applies static material without waking the GPU',()=>{
  const f=fixture({reduced:true});f.api.setStyle('G');f.api.setActive(true);assert.equal(f.contextCalls(),0);assert.equal(f.pending(),0);assert.equal(f.body.style.values['--material-blur'],'18px');assert.equal(f.body.dataset.glassStyle,'G');
  f.api.setStyle('J',{J:{brightness:1.3,contrast:.8,opacity:0}});assert.equal(f.body.style.values['--material-blur'],'0px');assert.equal(f.body.style.values['--material-brightness'],'1');assert.equal(f.body.style.values['--material-edge'],'0');assert.equal(f.body.style.values['--material-inset'],'0');assert.equal(f.contextCalls(),0);
});
test('leaving the page, hiding or losing focus cannot leave a detached pointer highlight',()=>{
 const f=fixture();f.api.setActive(true);f.step(0);let now=1;
 function point(){f.document.emit('pointermove',{clientX:830,clientY:215,pointerType:'mouse'});f.step(now++);assert.equal(f.gl.draws.at(-2).uniforms.hover,1);}
 point();f.document.emit('pointerout',{relatedTarget:{}});f.step(now++);assert.equal(f.gl.draws.at(-2).uniforms.hover,1,'moving between child controls keeps the same light');
 f.document.emit('pointerout',{relatedTarget:null});f.step(now++);assert.equal(f.gl.draws.at(-2).uniforms.hover,0);
 point();f.window.emit('blur');f.step(now++);assert.equal(f.gl.draws.at(-2).uniforms.hover,0);
 point();f.document.hidden=true;f.document.emit('visibilitychange');f.document.hidden=false;f.document.emit('visibilitychange');f.step(now++);assert.equal(f.gl.draws.at(-2).uniforms.hover,0);
 f.api.destroy();assert.equal(f.document.listenerCount(),0);assert.equal(f.window.listenerCount(),0);
});
test('a missing material table safely retains the original renderer',()=>{
  const f=fixture({missingMaterials:true});f.api.setStyle('A');f.api.setActive(true);f.step(0);assert.equal(f.gl.draws.at(-1).uniforms.materialOn,0);assert.equal(f.body.dataset.optics,'ready');
});
test('frosted grain stays fixed in screen pixels and sampling keeps a single registered scene',()=>{
  assert.match(source,/floor\(p\),vec2\(127\.1,311\.7\)/);assert.doesNotMatch(source,/floor\(p\s*\+\s*time/);
  const f=fixture();f.api.setActive(true);f.api.setStyle('G');f.step(0);const allocations=f.gl.allocations.length;f.step(34);assert.equal(f.gl.allocations.length,allocations);assert.equal(f.api.inspect().scenePasses,1);
});
test('Glasswork uses its powered inward displacement and bounded adaptive tint in the registered scene',()=>{
  assert.match(source,/pow\(1\.-t,materialPower\)\*materialRefraction\*\(materialIor-1\.\)\/\.46/);
  assert.match(source,/bendR=inward\*\(1\.\+materialDispersion\);bendG=inward;bendB=inward\*\(1\.-materialDispersion\)/);
  assert.match(source,/sampleSplit\(red\+x,green\+x,blue\+x\)/,'blur retains each dispersed channel instead of collapsing to green');
  assert.match(source,/materialDensity>\.001/);assert.match(source,/abs\(a-b\)\*2\.4\+abs\(l-\(a\+b\)\*\.5\)\*1\.3/);
  assert.doesNotMatch(source,/fetch\(|XMLHttpRequest|html2canvas|drawImage\(document/);
});
test('desktop glass concentration reaches actual uniforms and static material without context churn',()=>{
  const f=fixture();f.api.setActive(true);f.api.setStyle('G',{G:{opacity:.8}},[],60);f.step(0);
  assert.equal(f.gl.draws.at(-1).uniforms.glassOpacity,.6);assert.equal(f.gl.draws.at(-1).uniforms.materialOpacity,.8);
  assert.equal(f.body.style.values['--material-blur'],String(18*.8*.6)+'px');
  f.api.setStyle('G',{G:{opacity:.8}},[],100);f.step(17);assert.equal(f.gl.draws.at(-1).uniforms.glassOpacity,1);
  f.api.setStyle('I',{},[],60);f.step(34);assert.equal(f.gl.draws.at(-1).uniforms.materialOn,0);assert.equal(f.gl.draws.at(-1).uniforms.glassOpacity,.6);assert.equal(f.contextCalls(),1);assert.equal(f.api.inspect().scenePasses,1);
});
test('all visible surfaces render once, including a fourth card, with nested and hidden panels excluded',()=>{
 const f=fixture({extra:true});f.api.setActive(true);f.step(0);assert.equal(f.api.inspect().regions,4);assert.equal(f.gl.draws.length,6);assert.equal(f.contextCalls(),1);assert.equal(f.nodes[0].attributes['data-optical-surface'],'true');
 f.nodes[0].bounds=rect(550,100,100,100);f.nodes[2].children.push(f.nodes[0]);f.api.invalidate();f.step(17);assert.equal(f.api.inspect().regions,3);assert.equal(f.api.inspect().nestedRegions,1);assert.equal(f.nodes[0].attributes['data-optical-surface'],'true','nested panel inherits its enclosing optical surface');
 f.nodes[0].hidden=true;f.nodes[2].hidden=true;f.api.invalidate();f.step(34);assert.equal(f.api.inspect().regions,2);assert.equal(f.nodes[0].attributes['data-optical-surface'],undefined);assert.equal(f.nodes[2].attributes['data-optical-surface'],undefined);
 f.nodes[2].hidden=false;f.nodes[2].bounds=rect(530,800,400,200);f.api.invalidate();f.step(51);assert.equal(f.api.inspect().regions,2,'offscreen panels do no glass draw');assert.equal(f.contextCalls(),1);assert.ok(f.canvas.width*f.canvas.height<1803000);
 f.api.destroy();assert(f.nodes.every(n=>!n.hasAttribute('data-optical-surface')));assert(f.observers.every(o=>o.disconnected));
});
test('dynamic surface changes invalidate geometry while ordinary clock and artwork updates do not',()=>{
 const f=fixture({extra:true});f.api.setActive(true);f.step(0);const reads=f.measurements.boundsReads;
 f.observers[1].fn([{type:'childList',addedNodes:[{nodeType:3}],removedNodes:[]}]);f.step(17);assert.equal(f.measurements.boundsReads,reads);
 f.observers[1].fn([{type:'attributes',target:f.nodes[0]}]);f.step(34);assert(f.measurements.boundsReads>reads);assert.equal(f.contextCalls(),1);
});
test('expanded coverage retains bounded low-performance rendering and zero draws while hidden',()=>{
 const f=fixture({extra:true,frameCost:40});f.api.setActive(true);f.step(0);const width=f.canvas.width;
 for(let i=1;i<=16;i++)f.step(i*34);assert.equal(f.api.inspect().targetFps,30);assert(f.canvas.width<width);assert.equal(f.api.inspect().regions,4);assert.equal(f.contextCalls(),1);
 f.document.hidden=true;f.document.emit('visibilitychange');const draws=f.gl.draws.length;f.step(1000);assert.equal(f.gl.draws.length,draws);assert.equal(f.pending(),0);
});
console.log(`Companion glass: ${groups} test groups passed.`);
test('selected material sleeps until an input changes', () => {
  const f = fixture(); f.api.setActive(true); f.api.setStyle('G'); f.step(0);
  assert.equal(f.pending(), 0);
  f.document.emit('pointerdown', {clientX:730,clientY:215}); f.step(34);
  assert.ok(f.api.inspect().frames > 1);
});
