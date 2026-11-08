/* PrismGlass Companion shared-scene optics.
 * Rounded bevel, Snell and Fresnel equations adapted from the existing
 * glass-renderer.js / Oliver Nemo's MIT webgl-apple-liquid-glass.
 * Glasswork's distance-profile displacement comes from kubai087/glasswork
 * refract.js at 4f0b6744b898b3a9b98fbd913ab24fdb83c53162 (MIT).
 * See THIRD-PARTY-NOTICES.md and licenses/webgl-apple-liquid-glass-MIT.txt.
 * No DOM text, controls, private metadata or business images enter this canvas.
 */
(function () {
  'use strict';
  var params = new URLSearchParams(window.location.search), forcedOff = params.get('optical') === 'off', testGrid = params.get('opticalTest') === 'grid';
  var canvas = document.createElement('canvas'); canvas.id = 'optical-scene'; canvas.className = 'optical-scene'; canvas.setAttribute('aria-hidden', 'true'); canvas.hidden = true;
  document.body.insertBefore(canvas, document.body.firstChild);
  document.body.dataset.optics = 'fallback'; if (testGrid) document.body.dataset.opticalTest = 'grid';
  var gl = null, sourceProgram = null, glassProgram = null, quad = null, sceneTexture = null, sourceTexture = null, framebuffer = null;
  var active = false, destroyed = false, lost = false, failed = false, initialized = false, raf = 0, lastFrame = -Infinity, frameTime = 0;
  var image = null, sourceReady = testGrid, sourceUploaded = false, width = 0, height = 0, scale = 1, sceneWidth = 0, sceneHeight = 0, dirty = true, regions = [];
  var pointer = { x: -9999, y: -9999, down: false }, quality = 1, slowFrames = 0, generation = 0, sceneTheme = -1, interactiveUntil = 0, material = null, materialSignature = '';
  var reduced = window.matchMedia ? window.matchMedia('(prefers-reduced-motion: reduce)') : null;
  var stats = { frames: 0, regions: 0, scenePasses: 0, contextCreations: 0, fallback: 'inactive', width: 0, height: 0, sceneWidth: 0, sceneHeight: 0, maxRegions: 3, maxFps: 60, targetFps: 60 };
  var vertex = 'attribute vec2 a; varying vec2 uv; void main(){uv=a*.5+.5;gl_Position=vec4(a,0.,1.);}';
  var sourceFragment = [
    'precision highp float; varying vec2 uv; uniform sampler2D picture;',
    'uniform vec2 resolution; uniform vec2 imageSize; uniform float night; uniform float time; uniform float grid;',
    'void main(){',
    ' vec2 p=vec2(uv.x,1.-uv.y); vec3 c;',
    ' if(grid>.5){ vec2 q=p*resolution/48.; float tile=mod(floor(q.x)+floor(q.y),2.);',
    ' c=mix(vec3(.12,.30,.53),vec3(.73,.85,.92),tile);',
    ' c=mix(c,vec3(.92,.39,.27),1.-smoothstep(.012,.017,abs(p.x-.31)));',
    ' c=mix(c,vec3(.94,.76,.25),1.-smoothstep(.007,.011,abs(p.y-.61)));',
    ' }else{ float target=resolution.x/resolution.y; float source=imageSize.x/imageSize.y;',
    ' vec2 crop=target<source?vec2(target/source,1.):vec2(1.,source/target);',
    ' p=(p-.5)*crop+.5;',
    ' c=texture2D(picture,clamp(p,vec2(.001),vec2(.999))).rgb; }',
    ' float gray=dot(c,vec3(.2126,.7152,.0722)); c=mix(c,mix(vec3(gray),c,.74)*.94+vec3(.008,.01,.014),night); gl_FragColor=vec4(c,1.);',
    '}'
  ].join('\n');
  var glassFragment = [
    'precision highp float; varying vec2 uv; uniform sampler2D scene;',
    'uniform vec2 resolution; uniform vec2 center; uniform vec2 halfSize; uniform vec2 point;',
    'uniform float radius; uniform float backgroundOnly; uniform float time; uniform float press; uniform float night; uniform float orb;',
    'uniform float materialOn; uniform float materialBevel; uniform float materialRefraction; uniform float materialIor; uniform float materialDispersion;',
    'uniform float materialBlur; uniform float materialDiffusion; uniform float materialSaturation; uniform vec4 materialTint;',
    'uniform float materialReflection; uniform float materialHighlight; uniform float materialShadow; uniform float materialGrain;',
    'uniform float materialBrightness; uniform float materialContrast; uniform float materialEdge; uniform float materialPower; uniform float materialGlasswork;',
    'uniform float materialOpacity; uniform float materialAdaptive; uniform float materialDensity; uniform float materialTintMin; uniform float materialTintMax;',
    'float sdf(vec2 p,vec2 b,float r){vec2 q=abs(p)-b+r;return length(max(q,0.))+min(max(q.x,q.y),0.)-r;}',
    'vec3 sampleSplit(vec2 red,vec2 green,vec2 blue){return vec3(texture2D(scene,clamp(red,vec2(0.),vec2(1.))).r,texture2D(scene,clamp(green,vec2(0.),vec2(1.))).g,texture2D(scene,clamp(blue,vec2(0.),vec2(1.))).b);}',
    'void main(){',
    ' vec3 original=texture2D(scene,uv).rgb; if(backgroundOnly>.5){gl_FragColor=vec4(original,1.);return;}',
    ' vec2 p=vec2(uv.x,1.-uv.y)*resolution; vec2 d=p-center;',
    ' float distance=sdf(d,halfSize,radius); if(distance>11.)discard;',
    ' vec3 result=original*(1.-exp(-max(distance,0.)/3.2)*mix(.07,materialShadow*.4*materialOpacity,materialOn));',
    ' if(distance<.85){',
    ' float epsilon=.65; vec2 gradient=normalize(vec2(sdf(d+vec2(epsilon,0.),halfSize,radius)-sdf(d-vec2(epsilon,0.),halfSize,radius),sdf(d+vec2(0.,epsilon),halfSize,radius)-sdf(d-vec2(0.,epsilon),halfSize,radius))+.00001);',
    ' float depth=max(-distance,0.); float bevel=mix(clamp(halfSize.y*.21,10.,24.),min(materialBevel,max(2.,min(halfSize.x,halfSize.y)*.45)),materialOn);',
    ' if(materialGlasswork>.5)bevel=min(materialBevel,min(halfSize.x,halfSize.y)*2./3.5);',
    ' float t=clamp(depth/bevel,0.,1.); float arc=sqrt(max(1.-(1.-t)*(1.-t),0.));',
    ' float slope=.79*(1.-t)/max(arc,.14); vec3 normal=normalize(vec3(-gradient*slope,1.));',
    ' float path=bevel*(.65+1.2*arc)*mix(1.4,materialRefraction,materialOn)*mix(1.,materialEdge*pow(max(1.-t,.001),materialPower/2.6-1.),materialOn); vec2 axis=vec2(1.,-1.)/resolution;',
    ' vec2 lensCenter=mix(vec2(0.),vec2(-halfSize.x+halfSize.y,0.),orb);',
    ' float lens=orb>.5?exp(-dot(d-lensCenter,d-lensCenter)/max(halfSize.y*halfSize.y*2.,1.)):.5;',
    // Keep the body registered to the shared scene; only the bevel bends it.
    ' float edge=1.-smoothstep(0.,bevel,depth); vec2 drift=-(d-lensCenter)*(.035+.075*lens)*edge*edge;',
    ' float chroma=mix(.065,materialDispersion,materialOn); float ior=mix(1.46,materialIor,materialOn); vec3 rayR=refract(vec3(0.,0.,-1.),normal,1./max(1.05,ior-chroma));',
    ' vec3 rayG=refract(vec3(0.,0.,-1.),normal,1./ior); vec3 rayB=refract(vec3(0.,0.,-1.),normal,1./(ior+chroma));',
    ' vec2 bendR=rayR.xy/max(-rayR.z,.25)*path+drift; vec2 bendG=rayG.xy/max(-rayG.z,.25)*path+drift; vec2 bendB=rayB.xy/max(-rayB.z,.25)*path+drift;',
    // Glasswork's powered rim displacement, in the same CSS-pixel coordinates as its refract.js.
    ' if(materialGlasswork>.5){float amount=materialEdge*bevel*pow(1.-t,materialPower)*materialRefraction*(materialIor-1.)/.46;vec2 inward=-gradient*amount;bendR=inward*(1.+materialDispersion);bendG=inward;bendB=inward*(1.-materialDispersion);}',
    ' vec2 red=uv+bendR*axis,green=uv+bendG*axis,blue=uv+bendB*axis;vec3 c=sampleSplit(red,green,blue);',
    // Five fixed taps share the same registered center; no second scene or animated grain.
    ' if(materialOn>.5 && materialBlur>.01 && materialDiffusion>.001){vec2 q=clamp(green,vec2(0.),vec2(1.));vec2 b=materialBlur/resolution;vec3 soft;',
    ' if(materialGlasswork>.5){vec2 x=vec2(b.x,0.),y=vec2(0.,b.y);soft=(c*4.+(sampleSplit(red+x,green+x,blue+x)+sampleSplit(red-x,green-x,blue-x)+sampleSplit(red+y,green+y,blue+y)+sampleSplit(red-y,green-y,blue-y))*2.)/12.;}',
    ' else{soft=texture2D(scene,q).rgb*.28+(texture2D(scene,clamp(q+vec2(b.x,0.),vec2(0.),vec2(1.))).rgb+texture2D(scene,clamp(q-vec2(b.x,0.),vec2(0.),vec2(1.))).rgb+texture2D(scene,clamp(q+vec2(0.,b.y),vec2(0.),vec2(1.))).rgb+texture2D(scene,clamp(q-vec2(0.,b.y),vec2(0.),vec2(1.))).rgb)*.18;}c=mix(c,soft,materialDiffusion);}',
    ' float luminance=dot(c,vec3(.2126,.7152,.0722)); c=mix(vec3(luminance),c,mix(mix(1.16,.86,night),materialSaturation,materialOn));',
    ' if(materialGlasswork<.5)c*=mix(1.,mix(.96,.94,night),smoothstep(.35,.88,luminance));',
    ' c=((c-.5)*mix(1.,materialContrast,materialOn)+.5)*mix(1.,materialBrightness,materialOn);',
    ' float tintWeight=materialTint.a;if(materialOn>.5 && materialDensity>.001){float target=.5;if(materialAdaptive>.001){vec3 w=vec3(.2126,.7152,.0722);float l=dot(original,w);float a=dot(texture2D(scene,clamp(uv+axis*vec2(12.,8.),vec2(0.),vec2(1.))).rgb,w);float b=dot(texture2D(scene,clamp(uv-axis*vec2(12.,8.),vec2(0.),vec2(1.))).rgb,w);float activity=clamp(abs(a-b)*2.4+abs(l-(a+b)*.5)*1.3+(1.-l)*.12,0.,1.);target=mix(.5,activity,materialAdaptive);}tintWeight=clamp(tintWeight+mix(materialTintMin,materialTintMax,target)*materialDensity,0.,1.);}',
    ' c=mix(c,materialTint.rgb,tintWeight*materialOn);',
    ' float fresnel=.04+.96*pow(1.-normal.z,5.); vec3 reflection=texture2D(scene,clamp(uv+gradient*axis*bevel*1.8,vec2(0.),vec2(1.))).rgb;',
    ' c=mix(c,reflection*1.18+vec3(.11,.14,.19),fresnel*mix(.65,materialReflection,materialOn)*edge);',
    ' vec2 lightDirection=normalize(vec2(-.65,-.85)+point*.22+vec2(sin(time*.18)*.1,cos(time*.13)*.055));',
    ' float key=pow(max(dot(gradient,lightDirection),0.),5.); float opposite=pow(max(dot(gradient,-lightDirection),0.),9.);',
    ' float rim=exp(-depth/bevel); float specular=pow(max(dot(normal,normalize(vec3(-lightDirection*.8,1.))),0.),38.);',
    ' float glow=mix(1.,materialHighlight,materialOn); c+=exp(-pow((depth-1.3)/.82,2.))*(.16+key*.78+opposite*.32)*glow;',
    ' c+=exp(-pow((depth-bevel*.68)/2.1,2.))*key*.16*glow;',
    ' c+=specular*rim*vec3(.40,.43,.48)*glow; c-=rim*(1.-key)*mix(.085,materialShadow,materialOn);',
    ' c+=(fract(sin(dot(floor(p),vec2(127.1,311.7)))*43758.5453)-.5)*materialGrain*materialOn*(1.-edge*.8);',
    ' float touch=exp(-dot(d-point*halfSize,d-point*halfSize)/max(halfSize.y*halfSize.y*.75,1.)); c+=touch*press*.055;',
    ' c=mix(original,c,mix(1.,materialOpacity,materialOn));',
    ' result=mix(original,c,1.-smoothstep(-.75,.75,distance)); }',
    ' gl_FragColor=vec4(result,1.);',
    '}'
  ].join('\n');

  function canRun() { return active && !destroyed && !lost && !failed && !forcedOff && !document.hidden && document.body.dataset.reduced !== 'true' && !(reduced && reduced.matches); }
  function stop(reason) { if (raf) cancelAnimationFrame(raf); raf = 0; canvas.hidden = true; document.body.dataset.optics = 'fallback'; stats.fallback = reason; }
  function failure(error) { failed = true; stop('renderer-failed'); stats.error = String(error && error.message || error); release(); }
  function shader(type, source) {
    var value = gl.createShader(type); gl.shaderSource(value, source); gl.compileShader(value);
    if (!gl.getShaderParameter(value, gl.COMPILE_STATUS)) { var message = gl.getShaderInfoLog(value); gl.deleteShader(value); throw new Error(message || 'Shader compilation failed'); }
    return value;
  }
  function program(fragment) {
    var handle = gl.createProgram(), vert = null, frag = null;
    try {
      vert = shader(gl.VERTEX_SHADER, vertex); frag = shader(gl.FRAGMENT_SHADER, fragment);
      gl.attachShader(handle, vert); gl.attachShader(handle, frag); gl.linkProgram(handle);
      if (!gl.getProgramParameter(handle, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(handle) || 'Program link failed');
      return { handle: handle, attribute: gl.getAttribLocation(handle, 'a'), uniforms: Object.create(null) };
    } catch (error) { gl.deleteProgram(handle); throw error; }
    finally { if (vert) gl.deleteShader(vert); if (frag) gl.deleteShader(frag); }
  }
  function use(value) { gl.useProgram(value.handle); gl.bindBuffer(gl.ARRAY_BUFFER, quad); gl.enableVertexAttribArray(value.attribute); gl.vertexAttribPointer(value.attribute, 2, gl.FLOAT, false, 0, 0); }
  function location(value, key) { if (!(key in value.uniforms)) value.uniforms[key] = gl.getUniformLocation(value.handle, key); return value.uniforms[key]; }
  function one(value, key, x) { gl.uniform1f(location(value, key), x); }
  function two(value, key, x, y) { gl.uniform2f(location(value, key), x, y); }
  function setStyle(value, overrides, presets) {
    var catalog = window.PrismGlassMaterials, requested = typeof value === 'string' && (/^[A-J]$/.test(value) || /^user-[a-f0-9]{32}$/.test(value)) ? value : '';
    var resolved = catalog && typeof catalog.resolve === 'function' ? catalog.resolve(requested, overrides, presets) : requested && catalog && Object.prototype.hasOwnProperty.call(catalog, requested) ? catalog[requested] : null;
    var key = requested && resolved ? resolved.key || requested : '';
    var signature = key + ':' + JSON.stringify(resolved);
    if (signature === materialSignature) return;
    materialSignature = signature; material = resolved && !resolved.legacy ? resolved : null; stats.glassStyle = key; document.body.dataset.glassStyle = key;
    document.body.dataset.materialCustom = material ? 'true' : 'false';
    var style = document.body.style;
    if (material) {
      // CSS fallback uses a quiet midpoint tint; no private DOM pixels or extra images are sampled.
      var tint = material.tint, opacity = material.opacity == null ? 1 : material.opacity;
      var tintWeight = Math.min(1, tint[3] + ((material.tintMin || 0) + (material.tintMax || 0)) * .5 * (material.density || 0)) * opacity;
      style.setProperty('--material-tint', 'rgba(' + Math.round(tint[0] * 255) + ',' + Math.round(tint[1] * 255) + ',' + Math.round(tint[2] * 255) + ',' + tintWeight + ')');
      style.setProperty('--material-blur', material.blur * opacity + 'px'); style.setProperty('--material-saturation', String(1 + (material.saturation - 1) * opacity));
      style.setProperty('--material-brightness', String(1 + ((material.brightness == null ? 1 : material.brightness) - 1) * opacity)); style.setProperty('--material-contrast', String(1 + ((material.contrast == null ? 1 : material.contrast) - 1) * opacity));
      style.setProperty('--material-edge', String(Math.min(.65, material.highlight * 2) * opacity)); style.setProperty('--material-shadow', String(Math.min(.4, material.shadow) * opacity));
      style.setProperty('--material-inset', String(.14 * opacity));
    }
    invalidate();
  }
  function materialUniforms() {
    one(glassProgram, 'materialOn', material ? 1 : 0);
    one(glassProgram, 'materialGlasswork', material && material.model === 'glasswork' ? 1 : 0);
    if (!material) { gl.uniform4f(location(glassProgram, 'materialTint'), 0, 0, 0, 0); one(glassProgram, 'materialGrain', 0); return; }
    var keys = ['bevel', 'refraction', 'ior', 'dispersion', 'blur', 'diffusion', 'saturation', 'reflection', 'highlight', 'shadow', 'grain', 'brightness', 'contrast', 'edge', 'power', 'opacity', 'adaptive', 'density', 'tintMin', 'tintMax'];
    keys.forEach(function (key) { one(glassProgram, 'material' + key.charAt(0).toUpperCase() + key.slice(1), material[key]); });
    gl.uniform4f(location(glassProgram, 'materialTint'), material.tint[0], material.tint[1], material.tint[2], material.tint[3]);
  }
  function bind(value, key, texture) { gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, texture); gl.uniform1i(location(value, key), 0); }
  function texture() {
    var value = gl.createTexture(); gl.bindTexture(gl.TEXTURE_2D, value); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array([15, 45, 95, 255])); return value;
  }
  function initialize() {
    if (initialized) return true;
    try {
      if (!gl) { gl = canvas.getContext('webgl', { alpha: false, antialias: false, depth: false, stencil: false, preserveDrawingBuffer: false, powerPreference: 'low-power', failIfMajorPerformanceCaveat: true }); stats.contextCreations++; }
      if (!gl) throw new Error('WebGL is unavailable');
      var precision = gl.getShaderPrecisionFormat(gl.FRAGMENT_SHADER, gl.HIGH_FLOAT); if (!precision || !precision.precision) throw new Error('Fragment high precision is unavailable');
      sourceProgram = program(sourceFragment); glassProgram = program(glassFragment); quad = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, quad); gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1,-1,1,-1,-1,1,-1,1,1,-1,1,1]), gl.STATIC_DRAW);
      sourceTexture = texture(); sceneTexture = texture(); framebuffer = gl.createFramebuffer(); initialized = true; sourceUploaded = false; width = 0; dirty = true; return true;
    } catch (error) { failure(error); return false; }
  }
  function loadSource() {
    if (testGrid || image) return;
    var url = new URL('/companion-scene.jpg', window.location.href); if (url.origin !== window.location.origin) { failure('Scene must be same-origin'); return; }
    var token = generation; image = new Image(); image.decoding = 'async';
    image.onload = function () { if (destroyed || token !== generation) return; sourceReady = true; dirty = true; wake(); };
    image.onerror = function () { if (!destroyed && token === generation) failure('Scene image could not be loaded'); };
    image.src = url.href;
  }
  function uploadSource() {
    if (sourceUploaded || testGrid) return;
    gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, sourceTexture); gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, image); sourceUploaded = true;
  }
  function resize() {
    var rect = canvas.getBoundingClientRect(), nextWidth = Math.max(1, Math.round(rect.width)), nextHeight = Math.max(1, Math.round(rect.height));
    var ratio = Math.min(window.devicePixelRatio || 1, Math.sqrt(1800000 / (nextWidth * nextHeight))) * quality;
    var bufferWidth = Math.max(1, Math.round(nextWidth * ratio)), bufferHeight = Math.max(1, Math.round(nextHeight * ratio));
    if (width === nextWidth && height === nextHeight && canvas.width === bufferWidth && canvas.height === bufferHeight) return;
    width = nextWidth; height = nextHeight; scale = ratio; canvas.width = bufferWidth; canvas.height = bufferHeight;
    sceneWidth = Math.max(1, Math.round(bufferWidth * .65)); sceneHeight = Math.max(1, Math.round(bufferHeight * .65));
    gl.bindTexture(gl.TEXTURE_2D, sceneTexture); gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, sceneWidth, sceneHeight, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
    gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer); gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, sceneTexture, 0);
    if (gl.checkFramebufferStatus(gl.FRAMEBUFFER) !== gl.FRAMEBUFFER_COMPLETE) throw new Error('Incomplete scene framebuffer');
    gl.bindFramebuffer(gl.FRAMEBUFFER, null); sceneTheme = -1; stats.width = bufferWidth; stats.height = bufferHeight; stats.sceneWidth = sceneWidth; stats.sceneHeight = sceneHeight; dirty = true;
  }
  function collectRegions() {
    var bounds = canvas.getBoundingClientRect(), main = document.getElementById('main-content'), mainRect = main ? main.getBoundingClientRect() : null;
    regions = Array.prototype.slice.call(document.querySelectorAll('[data-optical]')).map(function (node) {
      if (!node.getClientRects().length) return null;
      var style = window.getComputedStyle(node); if (style.visibility === 'hidden' || style.display === 'none') return null;
      var rect = node.getBoundingClientRect(), clip = { left: bounds.left, top: bounds.top, right: bounds.right, bottom: bounds.bottom };
      if (main && main.contains(node)) { clip.left = Math.max(clip.left, mainRect.left); clip.top = Math.max(clip.top, mainRect.top); clip.right = Math.min(clip.right, mainRect.right); clip.bottom = Math.min(clip.bottom, mainRect.bottom); }
      var left = Math.max(rect.left - 11, clip.left), top = Math.max(rect.top - 11, clip.top), right = Math.min(rect.right + 11, clip.right), bottom = Math.min(rect.bottom + 11, clip.bottom);
      if (right <= left || bottom <= top || rect.width < 2 || rect.height < 2) return null;
      return { id: node.id, x: rect.left - bounds.left, y: rect.top - bounds.top, w: rect.width, h: rect.height, radius: Math.min(parseFloat(style.borderTopLeftRadius) || 24, rect.width / 2, rect.height / 2), clip: [left - bounds.left, top - bounds.top, right - bounds.left, bottom - bounds.top], priority: node.id === 'touch-bar' ? 0 : node.id === 'music-card' ? 1 : node.id === 'home-focus' ? 2 : 3 };
    }).filter(function (region) { return !!region; }).sort(function (a, b) { return a.priority - b.priority; }).slice(0, 3);
    stats.regions = regions.length; dirty = false;
  }
  function draw(now) {
    // Restore layout before measuring a canvas hidden by the CSS fallback.
    if (canvas.hidden) canvas.hidden = false; uploadSource(); if (dirty) { resize(); collectRegions(); }
    var night = document.body.dataset.theme === 'Astro' ? 1 : 0, seconds = frameTime / 1000;
    gl.disable(gl.SCISSOR_TEST);
    // The background is static; pointer lighting only redraws the glass pass.
    if (sceneTheme !== night) {
      gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer); gl.viewport(0, 0, sceneWidth, sceneHeight); use(sourceProgram); bind(sourceProgram, 'picture', sourceTexture); two(sourceProgram, 'resolution', width, height); two(sourceProgram, 'imageSize', testGrid ? 1 : image.naturalWidth, testGrid ? 1 : image.naturalHeight); one(sourceProgram, 'night', night); one(sourceProgram, 'time', seconds); one(sourceProgram, 'grid', testGrid ? 1 : 0); gl.drawArrays(gl.TRIANGLES, 0, 6); stats.scenePasses++; sceneTheme = night;
    }
    gl.bindFramebuffer(gl.FRAMEBUFFER, null); gl.viewport(0, 0, canvas.width, canvas.height); use(glassProgram); bind(glassProgram, 'scene', sceneTexture); materialUniforms(); two(glassProgram, 'resolution', width, height); one(glassProgram, 'night', night); one(glassProgram, 'time', seconds); one(glassProgram, 'backgroundOnly', 1); gl.drawArrays(gl.TRIANGLES, 0, 6);
    one(glassProgram, 'backgroundOnly', 0); gl.enable(gl.SCISSOR_TEST);
    regions.forEach(function (region) {
      var cx = region.x + region.w / 2, cy = region.y + region.h / 2, inside = pointer.x >= region.x && pointer.x <= region.x + region.w && pointer.y >= region.y && pointer.y <= region.y + region.h;
      two(glassProgram, 'center', cx, cy); two(glassProgram, 'halfSize', region.w / 2, region.h / 2); one(glassProgram, 'radius', region.radius); one(glassProgram, 'orb', region.id === 'home-focus' ? 1 : 0); two(glassProgram, 'point', inside ? (pointer.x - cx) / (region.w / 2) : 0, inside ? (pointer.y - cy) / (region.h / 2) : 0); one(glassProgram, 'press', inside && pointer.down ? 1 : 0);
      var c = region.clip, left = Math.max(0, Math.floor(c[0] * scale)), top = Math.max(0, Math.floor(c[1] * scale)), right = Math.min(canvas.width, Math.ceil(c[2] * scale)), bottom = Math.min(canvas.height, Math.ceil(c[3] * scale));
      gl.scissor(left, canvas.height - bottom, Math.max(0, right - left), Math.max(0, bottom - top)); gl.drawArrays(gl.TRIANGLES, 0, 6);
    });
    gl.disable(gl.SCISSOR_TEST); stats.frames++; stats.fallback = ''; if (document.body.dataset.optics !== 'ready') document.body.dataset.optics = 'ready';
  }
  function frame(now) {
    raf = 0; if (!canRun()) { stop('paused'); return; }
    if (!sourceReady) return;
    var fps = quality > .76 ? 60 : 30; stats.targetFps = fps;
    if (now - lastFrame >= 1000 / fps - .5) {
      var start = performance.now(); frameTime += Math.min(100, isFinite(lastFrame) ? now - lastFrame : 0); lastFrame = now;
      try { draw(now); } catch (error) { failure(error); return; }
      if (performance.now() - start > 28) slowFrames++; else slowFrames = Math.max(0, slowFrames - 1);
      if (slowFrames > 12 && quality > .76) { quality = .75; width = 0; dirty = true; }
    }
    if (regions.length) raf = requestAnimationFrame(frame);
  }
  function wake() {
    if (!canRun()) { stop(forcedOff ? 'forced-off' : document.hidden ? 'hidden' : 'inactive-or-reduced'); return; }
    if (!initialize()) return; loadSource();
    if (sourceReady && !raf) raf = requestAnimationFrame(frame);
  }
  function invalidate() { dirty = true; wake(); }
  function onPointer(event) { pointer.x = event.clientX; pointer.y = event.clientY; interactiveUntil = performance.now() + 250; if (event.type === 'pointerdown') pointer.down = true; if (event.type === 'pointerup' || event.type === 'pointercancel') pointer.down = false; wake(); }
  function visibility() { if (document.hidden) { pointer.down = false; stop('hidden'); } else { lastFrame = -Infinity; invalidate(); } }
  function pageHide() { pointer.down = false; stop('page-hidden'); }
  function pageShow() { lastFrame = -Infinity; invalidate(); }
  function contextLost(event) { event.preventDefault(); lost = true; initialized = false; sourceUploaded = false; stop('context-lost'); }
  function contextRestored() { lost = false; failed = false; sourceProgram = glassProgram = quad = sceneTexture = sourceTexture = framebuffer = null; initialized = false; sourceUploaded = false; lastFrame = -Infinity; invalidate(); }
  function release() {
    if (!gl || lost) return;
    if (sourceTexture) gl.deleteTexture(sourceTexture); if (sceneTexture) gl.deleteTexture(sceneTexture); if (framebuffer) gl.deleteFramebuffer(framebuffer); if (quad) gl.deleteBuffer(quad); if (sourceProgram) gl.deleteProgram(sourceProgram.handle); if (glassProgram) gl.deleteProgram(glassProgram.handle);
    sourceProgram = glassProgram = quad = sceneTexture = sourceTexture = framebuffer = null; initialized = false;
  }
  var observer = new MutationObserver(function () { invalidate(); }); observer.observe(document.body, { attributes: true, attributeFilter: ['data-theme', 'data-reduced', 'data-page', 'data-music', 'data-presentation'] });
  var resizeObserver = typeof ResizeObserver === 'function' ? new ResizeObserver(invalidate) : null;
  if (resizeObserver) { resizeObserver.observe(document.body); Array.prototype.forEach.call(document.querySelectorAll('[data-optical]'), function (node) { resizeObserver.observe(node); }); }
  document.addEventListener('visibilitychange', visibility); window.addEventListener('resize', invalidate); window.addEventListener('pagehide', pageHide); window.addEventListener('pageshow', pageShow);
  var main = document.getElementById('main-content'); if (main) main.addEventListener('scroll', invalidate, { passive: true });
  ['pointermove', 'pointerdown', 'pointerup', 'pointercancel'].forEach(function (type) { document.addEventListener(type, onPointer, { passive: true }); });
  canvas.addEventListener('webglcontextlost', contextLost); canvas.addEventListener('webglcontextrestored', contextRestored);
  if (reduced) { if (reduced.addEventListener) reduced.addEventListener('change', invalidate); else if (reduced.addListener) reduced.addListener(invalidate); }
  window.PrismGlassOptics = {
    setStyle: setStyle,
    setActive: function (value) { var next = value === true; if (next === active) { if (active) wake(); return; } lastFrame = -Infinity; active = next; if (active) invalidate(); else stop('inactive'); },
    invalidate: invalidate,
    inspect: function () { return Object.assign({}, stats); },
    destroy: function () { if (destroyed) return; destroyed = true; generation++; stop('destroyed'); observer.disconnect(); if (resizeObserver) resizeObserver.disconnect(); release(); canvas.remove(); document.removeEventListener('visibilitychange', visibility); window.removeEventListener('resize', invalidate); window.removeEventListener('pagehide', pageHide); window.removeEventListener('pageshow', pageShow); if (main) main.removeEventListener('scroll', invalidate); ['pointermove', 'pointerdown', 'pointerup', 'pointercancel'].forEach(function (type) { document.removeEventListener(type, onPointer); }); canvas.removeEventListener('webglcontextlost', contextLost); canvas.removeEventListener('webglcontextrestored', contextRestored); if (reduced && reduced.removeEventListener) reduced.removeEventListener('change', invalidate); else if (reduced && reduced.removeListener) reduced.removeListener(invalidate); if (image) { image.onload = image.onerror = null; } }
  };
}());
