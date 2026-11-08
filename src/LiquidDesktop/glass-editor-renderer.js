/* Glass material editor renderer. Settings are managed by the editor host bridge.
 * Rounded-SDF / Snell / Fresnel model follows the project's existing MIT
 * webgl-apple-liquid-glass adaptation; see THIRD-PARTY-NOTICES in the repository.
 * Glasswork inward-rim / RGB split / tone: Copyright (c) 2026 Kubai087, MIT.
 */
(function () {
  'use strict';

  const presets = window.PrismGlassMaterials;
  if (!presets) throw Error('Load glass-materials.js before the style lab renderer.');
  const vertex = 'attribute vec2 position;uniform vec2 size;void main(){gl_Position=vec4(position/size*vec2(2.,-2.)+vec2(-1.,1.),0.,1.);}';
  const fragment = `precision highp float;
uniform sampler2D scene;
uniform vec2 resolution, size, center, halfSize, pointer;
uniform float radius, bevel, strength, ior, dispersion, blurRadius, diffusion, saturation, reflection, highlight, shadow, grain, hover, press, interaction;
uniform float brightness, contrast, edgeAmount, falloff, model, legacy, opacity, adaptive, density, tintMin, tintMax;
uniform vec4 tint;
float sdf(vec2 p){vec2 q=abs(p)-halfSize+radius;return length(max(q,0.))+min(max(q.x,q.y),0.)-radius;}
vec3 sampleAt(vec2 at){return texture2D(scene,clamp(at,vec2(0.),vec2(1.))).rgb;}
vec3 splitAt(vec2 red,vec2 green,vec2 blue){return vec3(sampleAt(red).r,sampleAt(green).g,sampleAt(blue).b);}
vec3 classicMaterial(vec2 p,vec2 gradient,float depth,vec2 uv){
 float curve=min(30.,max(10.,halfSize.y*.46)),rim=exp(-depth/curve),t=clamp(depth/curve,0.,1.),arc=sqrt(max(1.-(1.-t)*(1.-t),0.));
 vec3 normal=normalize(vec3(-gradient*.72*(1.-t)/max(arc,.16),1.));vec2 axis=vec2(1.,-1.)/size;
 vec2 lean=clamp((pointer-center)/max(halfSize,vec2(1.)),-1.,1.)*hover*interaction,toCursor=p-lean*halfSize*.82;
 float focus=exp(-dot(toCursor,toCursor)/4800.),path=curve*(.8+1.15*arc)*(1.+hover*interaction*.16);
 vec3 rr=refract(vec3(0.,0.,-1.),normal,1./(1.46-.065)),rg=refract(vec3(0.,0.,-1.),normal,1./1.46),rb=refract(vec3(0.,0.,-1.),normal,1./(1.46+.065));
 vec2 drift=p*.008+(lean*12.+toCursor*focus*hover*interaction*.12)*rim*rim;
 vec2 red=uv+(rr.xy/max(-rr.z,.3)*path+drift)*axis,green=uv+(rg.xy/max(-rg.z,.3)*path+drift)*axis,blue=uv+(rb.xy/max(-rb.z,.3)*path+drift)*axis;
 vec3 c=splitAt(red,green,blue);vec2 step=axis*mix(2.2,.65,rim);vec3 soft=sampleAt(green)*4.;
 soft+=(sampleAt(green+vec2(step.x,0.))+sampleAt(green-vec2(step.x,0.))+sampleAt(green+vec2(0.,step.y))+sampleAt(green-vec2(0.,step.y)))*2.;
 soft+=sampleAt(green+step)+sampleAt(green-step)+sampleAt(green+vec2(step.x,-step.y))+sampleAt(green+vec2(-step.x,step.y));c=mix(c,soft/16.,mix(.5,.18,rim));
 float luminance=dot(c,vec3(.2126,.7152,.0722)),bright=smoothstep(.35,.88,luminance),dark=1.-smoothstep(.04,.3,luminance);c=mix(vec3(luminance),c,1.16);
 vec3 ambient=sampleAt(vec2(center.x/size.x,1.-center.y/size.y));float chroma=max(c.r,max(c.g,c.b))-min(c.r,min(c.g,c.b));c=mix(c,ambient*luminance/max(dot(ambient,vec3(.2126,.7152,.0722)),.12),(1.-smoothstep(.05,.22,chroma))*.08);
 c*=mix(1.,.86,bright);c+=vec3(.012,.018,.027)*dark;
 float fresnel=.04+.96*pow(1.-normal.z,5.);c=mix(c,sampleAt(uv+gradient*axis*curve*2.)*.65+vec3(.28,.32,.36),fresnel*.38*rim);
 float light=pow(max(dot(gradient,normalize(vec2(-.65,-.85)+lean*.25)),0.),4.),specular=pow(max(dot(normal,normalize(vec3(.4,.6,1.))),0.),32.);
 c+=exp(-pow((depth-.9)/.48,2.))*(.10+.44*light)*(1.-bright*.35)+specular*rim*.16;c+=focus*hover*interaction*.13+rim*hover*interaction*.07;c-=rim*(1.-light)*.045;return c;
}
void main(){
 vec2 uv=gl_FragCoord.xy/resolution;
 vec2 p=vec2(uv.x,1.-uv.y)*size-center;
 float distance=sdf(p),aa=max(.55,size.x/resolution.x*.72);
 float coverage=1.-smoothstep(-aa,aa,distance);
 if(distance>aa){float shade=exp(-max(distance,0.)/3.2)*shadow*(.65+.35*smoothstep(-halfSize.y,halfSize.y,p.y));gl_FragColor=vec4(0.,0.,0.,shade);return;}
 float depth=max(-distance,0.),e=.5;
 vec2 gradient=normalize(vec2(sdf(p+vec2(e,0.))-sdf(p-vec2(e,0.)),sdf(p+vec2(0.,e))-sdf(p-vec2(0.,e)))+vec2(.00001));
 if(legacy>.5){gl_FragColor=vec4(clamp(classicMaterial(p,gradient,depth,uv),0.,1.)*coverage,coverage);return;}
 bool glasswork=model>.5;
 float curveWidth=min(bevel,max(2.,min(halfSize.x,halfSize.y)*(glasswork?2./3.5:.76)));
 float t=clamp(depth/curveWidth,0.,1.),arc=sqrt(max(1.-(1.-t)*(1.-t),0.));
 float edge=1.-smoothstep(0.,curveWidth,depth);
 vec3 normal=normalize(vec3(-gradient*.74*(1.-t)/max(arc,.17),1.));
 vec2 axis=vec2(1.,-1.)/size;
 // The body has exactly zero displacement. Pointer response is confined to the bevel.
 vec2 toward=pointer-center-p;
 float focus=exp(-dot(toward,toward)/7500.);
 vec2 cursor=clamp((pointer-center)/max(halfSize,vec2(1.)),-1.,1.),drift=cursor*edge*edge*hover*interaction*1.4;
 vec2 red,green,blue;
 if(glasswork){
  float amount=edgeAmount*curveWidth*pow(1.-t,falloff)*strength*(ior-1.)/.46;
  vec2 inward=-gradient*amount*(1.+hover*interaction*.10-press*interaction*.12);
  red=uv+(inward*(1.+dispersion)+drift)*axis;green=uv+(inward+drift)*axis;blue=uv+(inward*(1.-dispersion)+drift)*axis;
 }else{
  float path=curveWidth*(.72+1.18*arc)*strength*(1.+hover*interaction*.10-press*interaction*.12)*edgeAmount*pow(max(1.-t,.001),falloff/2.6-1.);
  vec3 rayR=refract(vec3(0.,0.,-1.),normal,1./max(1.05,ior-dispersion)),rayG=refract(vec3(0.,0.,-1.),normal,1./ior),rayB=refract(vec3(0.,0.,-1.),normal,1./(ior+dispersion));
  red=uv+(rayR.xy/max(-rayR.z,.3)*path+drift)*axis;green=uv+(rayG.xy/max(-rayG.z,.3)*path+drift)*axis;blue=uv+(rayB.xy/max(-rayB.z,.3)*path+drift)*axis;
 }
 vec3 c=splitAt(red,green,blue);
 if(blurRadius>.01&&diffusion>.001){
 if(glasswork){
  vec2 step=axis*blurRadius;vec3 soft=c*4.;
  soft+=(splitAt(red+vec2(step.x,0.),green+vec2(step.x,0.),blue+vec2(step.x,0.))+splitAt(red-vec2(step.x,0.),green-vec2(step.x,0.),blue-vec2(step.x,0.))+splitAt(red+vec2(0.,step.y),green+vec2(0.,step.y),blue+vec2(0.,step.y))+splitAt(red-vec2(0.,step.y),green-vec2(0.,step.y),blue-vec2(0.,step.y)))*2.;c=mix(c,soft/12.,diffusion);
 }else{
  vec2 step=axis*blurRadius*mix(1.,.22,edge);
  vec3 soft=sampleAt(green)*4.;
  soft+=(sampleAt(green+vec2(step.x,0.))+sampleAt(green-vec2(step.x,0.))+sampleAt(green+vec2(0.,step.y))+sampleAt(green-vec2(0.,step.y)))*2.;
  soft+=sampleAt(green+step)+sampleAt(green-step)+sampleAt(green+vec2(step.x,-step.y))+sampleAt(green+vec2(-step.x,step.y));
  c=mix(c,soft/16.,diffusion*mix(1.,.25,edge));
 }
 }
 float luminance=dot(c,vec3(.2126,.7152,.0722));
 c=mix(vec3(luminance),c,saturation);c=((c-.5)*contrast+.5)*brightness;
 float coating=tint.a;
 if(density>.001){float target=.5;if(adaptive>.001){vec3 w=vec3(.2126,.7152,.0722);float l=dot(sampleAt(uv),w),a=dot(sampleAt(uv+axis*vec2(12.,8.)),w),b=dot(sampleAt(uv-axis*vec2(12.,8.)),w);float activity=clamp(abs(a-b)*2.4+abs(l-(a+b)*.5)*1.3+(1.-l)*.12,0.,1.);target=mix(.5,activity,adaptive);}coating+=mix(tintMin,tintMax,target)*density;}
 c=mix(c,tint.rgb,clamp(coating,0.,1.));
 float fresnel=.04+.96*pow(1.-normal.z,5.);
 // Reflected color and dispersion both come from the visible scene, never a painted rainbow.
 vec3 reflected=sampleAt(uv+gradient*axis*curveWidth*1.6);
 c=mix(c,reflected,fresnel*reflection*edge);
 vec2 lightDirection=normalize(vec2(-.65,-.85)+cursor*hover*interaction*.16);
 float key=pow(max(dot(gradient,lightDirection),0.),3.);
 float fineRim=exp(-pow((depth-.65)/.65,2.));
 c+=max(vec3(0.),vec3(1.)-c)*fineRim*highlight*(.15+.85*key);
 c-=edge*edge*(1.-key)*shadow*.11;
 c+=max(vec3(0.),reflected-c)*focus*edge*hover*interaction*.19;
 c*=1.-press*interaction*edge*.025;
 if(grain>.001)c+=(fract(sin(dot(floor(p+center),vec2(127.1,311.7)))*43758.5453)-.5)*grain*(1.-edge*.8);
 c=mix(sampleAt(uv),c,opacity);
 gl_FragColor=vec4(clamp(c,0.,1.)*coverage,coverage);
}`;
  let active = null;
  const stats = { ready: false, frames: 0, sceneUploads: 0, regions: 0, errors: [] };
  const clamp = (value, low, high) => Math.max(low, Math.min(high, value));

  function Renderer(config) {
    const canvas = config.canvas, background = config.backgroundCanvas;
    if (!(canvas instanceof HTMLCanvasElement) || !(background instanceof HTMLCanvasElement)) throw Error('GlassLab needs glass and scene canvases.');
    const gl = canvas.getContext('webgl', { alpha: true, premultipliedAlpha: true, antialias: false, powerPreference: 'default' });
    if (!gl) throw Error('WebGL unavailable; optical previews cannot be rendered.');
    const context = background.getContext('2d', { alpha: false });
    const selector = config.regions || '[data-glass-preset]';
    const options = Object.assign({ refraction: 1, blur: 1, interaction: 1, motion: true, background: 'wallpaper', backgroundUrl: config.backgroundUrl || '', overrides: {}, presets: [], suspended: false }, config.options || {});
    options.overrides = presets.normalizeOverrides(options.overrides); options.presets = Array.isArray(options.presets) ? options.presets.slice(0, 64) : [];
    const materialCache = new Map();
    function materialFor(key) { if (!materialCache.has(key)) materialCache.set(key, presets.resolve(key, options.overrides, options.presets)); return materialCache.get(key); }
    const reduced = matchMedia('(prefers-reduced-motion: reduce)');
    const point = { x: -10000, y: -10000, down: false };
    let program, buffer, texture, attribute, locations = {}, image = null, regions = [], width = 1, height = 1, ratio = 1;
    let request = 0, previous = 0, time = 0, dirtyLayout = true, dirtyScene = true, stopped = false, lost = false, wallpaperRequest = 0, wallpaperUrl = '';
    const disposeListeners = [];
    function listen(target, type, callback, opts) { target.addEventListener(type, callback, opts); disposeListeners.push(() => target.removeEventListener(type, callback, opts)); }
    function error(value) { const message = String(value && value.message || value); stats.errors.push(message); document.documentElement.dataset.glassLabState = 'error'; if (config.onStatus) config.onStatus({ ready: false, error: message }); }
    function shader(type, source) { const result = gl.createShader(type); gl.shaderSource(result, source); gl.compileShader(result); if (!gl.getShaderParameter(result, gl.COMPILE_STATUS)) throw Error(gl.getShaderInfoLog(result)); return result; }
    function setup() {
      const v = shader(gl.VERTEX_SHADER, vertex), f = shader(gl.FRAGMENT_SHADER, fragment);
      program = gl.createProgram(); gl.attachShader(program, v); gl.attachShader(program, f); gl.linkProgram(program); gl.deleteShader(v); gl.deleteShader(f);
      if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw Error(gl.getProgramInfoLog(program));
      locations = {}; for (const name of ['scene', 'resolution', 'size', 'center', 'halfSize', 'pointer', 'radius', 'bevel', 'strength', 'ior', 'dispersion', 'blurRadius', 'diffusion', 'saturation', 'reflection', 'highlight', 'shadow', 'grain', 'hover', 'press', 'interaction', 'tint', 'brightness', 'contrast', 'edgeAmount', 'falloff', 'model', 'legacy', 'opacity', 'adaptive', 'density', 'tintMin', 'tintMax']) locations[name] = gl.getUniformLocation(program, name);
      attribute = gl.getAttribLocation(program, 'position'); buffer = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, buffer); gl.bufferData(gl.ARRAY_BUFFER, 48, gl.DYNAMIC_DRAW);
      texture = gl.createTexture(); gl.bindTexture(gl.TEXTURE_2D, texture);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
      gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, true); dirtyScene = true;
    }
    setup();
    const resize = new ResizeObserver(() => { dirtyLayout = true; wake(); });
    resize.observe(canvas);
    function measure() {
      const bounds = canvas.getBoundingClientRect(), old = new Map(regions.map(region => [region.node, region]));
      const nextWidth = Math.max(1, bounds.width), nextHeight = Math.max(1, bounds.height);
      const nextRatio = Math.min(devicePixelRatio || 1, 1.5, Math.sqrt(4000000 / (nextWidth * nextHeight)));
      if (width !== nextWidth || height !== nextHeight || ratio !== nextRatio) {
        width = nextWidth; height = nextHeight; ratio = nextRatio;
        canvas.width = background.width = Math.max(1, Math.round(width * ratio)); canvas.height = background.height = Math.max(1, Math.round(height * ratio)); dirtyScene = true;
      }
      regions = [];
      for (const node of document.querySelectorAll(selector)) {
        const rect = node.getBoundingClientRect();
        if (!rect.width || !rect.height || rect.bottom <= bounds.top - 12 || rect.top >= bounds.bottom + 12 || rect.right <= bounds.left - 12 || rect.left >= bounds.right + 12) continue;
        const region = old.get(node) || { node: node, hover: 0, press: 0, hoverVelocity: 0, pressVelocity: 0 };
        region.x = rect.left - bounds.left; region.y = rect.top - bounds.top; region.width = rect.width; region.height = rect.height;
        region.radius = clamp(parseFloat(node.dataset.glassRadius || getComputedStyle(node).borderTopLeftRadius) || 20, 0, Math.min(rect.width, rect.height) / 2);
        const x = region.x - 12, y = region.y - 12, right = region.x + region.width + 12, bottom = region.y + region.height + 12;
        region.vertices = new Float32Array([x, y, right, y, x, bottom, x, bottom, right, y, right, bottom]);
        region.preset = /^(?:[A-J]|user-[0-9a-f]{32})$/.test(node.dataset.glassPreset) ? node.dataset.glassPreset : 'I'; regions.push(region); resize.observe(node); old.delete(node);
      }
      for (const node of old.keys()) resize.unobserve(node);
      stats.regions = regions.length; dirtyLayout = false;
    }
    function radial(x, y, radius, start, end) { const gradient = context.createRadialGradient(x, y, 0, x, y, radius); gradient.addColorStop(0, start); gradient.addColorStop(1, end); context.fillStyle = gradient; context.fillRect(0, 0, width, height); }
    function paintScene() {
      context.setTransform(ratio, 0, 0, ratio, 0, 0);
      const mode = options.background;
      if (mode === 'wallpaper' && image) {
        const scale = Math.max(width / image.naturalWidth, height / image.naturalHeight);
        const w = image.naturalWidth * scale, h = image.naturalHeight * scale;
        context.drawImage(image, (width - w) / 2, (height - h) / 2, w, h);
      } else if (mode === 'grid') {
        context.fillStyle = '#e8e9ee'; context.fillRect(0, 0, width, height);
        const cells = ['#f5f3ed', '#1f3551', '#8baeb7', '#d6a78d'];
        const side = 180;
        for (let y = -side; y < height + side; y += side) for (let x = -side; x < width + side; x += side) {
          const index = ((Math.round(x / side) + Math.round(y / side)) % 4 + 4) % 4;
          context.fillStyle = cells[index]; context.fillRect(x, y, side, side);
        }
        context.strokeStyle = 'rgba(15,24,38,.25)'; context.lineWidth = .7; context.beginPath();
        for (let x = 0; x < width; x += 20) { context.moveTo(x, 0); context.lineTo(x, height); }
        for (let y = 0; y < height; y += 20) { context.moveTo(0, y); context.lineTo(width, y); } context.stroke();
      } else {
        const dark = mode === 'dark'; context.fillStyle = dark ? '#101722' : '#edeaf0'; context.fillRect(0, 0, width, height);
        radial(width * .16, height * .18, width * .68, dark ? '#304f67' : '#aecad4', dark ? '#304f6700' : '#aecad400');
        radial(width * .85, height * .68, width * .55, dark ? '#39314f' : '#d2bad3', dark ? '#39314f00' : '#d2bad300');
      }
      // These moving forms are visibly part of the background canvas and its sampled texture.
      const dark = mode === 'dark', sweep = ((time * 48) % (width + 520)) - 260;
      context.save(); context.translate(sweep, 0); context.rotate(-.20);
      const ribbon = context.createLinearGradient(-48, 0, 48, 0);
      ribbon.addColorStop(0, 'rgba(120,180,209,0)'); ribbon.addColorStop(.28, dark ? 'rgba(155,203,220,.65)' : 'rgba(255,255,255,.62)');
      ribbon.addColorStop(.48, dark ? 'rgba(196,177,215,.85)' : 'rgba(98,132,165,.53)'); ribbon.addColorStop(.64, 'rgba(220,198,219,.52)'); ribbon.addColorStop(1, 'rgba(120,180,209,0)');
      context.fillStyle = ribbon; context.fillRect(-48, -height, 96, height * 3); context.restore();
      const y = height * .64 + Math.sin(time * .32) * height * .11;
      context.beginPath(); context.moveTo(-80, y); context.bezierCurveTo(width * .25, y - 120, width * .63, y + 155, width + 80, y - 40);
      context.strokeStyle = dark ? 'rgba(175,198,216,.38)' : 'rgba(43,75,109,.26)'; context.lineWidth = 11; context.stroke();
      context.strokeStyle = dark ? 'rgba(218,229,238,.52)' : 'rgba(255,255,255,.52)'; context.lineWidth = 2; context.stroke();
      gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, texture); gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, background);
      dirtyScene = false; stats.sceneUploads++;
    }
    function spring(region, name, goal, seconds) {
      const velocity = name + 'Velocity';
      if (reduced.matches) { region[name] = goal; region[velocity] = 0; return; }
      const omega = name === 'press' ? 32 : 22, delta = region[name] - goal, c = region[velocity] + omega * delta, decay = Math.exp(-omega * seconds);
      region[name] = goal + (delta + c * seconds) * decay; region[velocity] = (region[velocity] - omega * c * seconds) * decay;
      if (Math.abs(region[name] - goal) < .0005 && Math.abs(region[velocity]) < .01) { region[name] = goal; region[velocity] = 0; }
    }
    function draw(seconds) {
      gl.viewport(0, 0, canvas.width, canvas.height); gl.disable(gl.SCISSOR_TEST); gl.clearColor(0, 0, 0, 0); gl.clear(gl.COLOR_BUFFER_BIT);
      gl.useProgram(program); gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
      gl.enableVertexAttribArray(attribute); gl.vertexAttribPointer(attribute, 2, gl.FLOAT, false, 0, 0);
      gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, texture); gl.uniform1i(locations.scene, 0);
      gl.uniform2f(locations.resolution, canvas.width, canvas.height); gl.uniform2f(locations.size, width, height); gl.uniform2f(locations.pointer, point.x, point.y);
      gl.uniform1f(locations.interaction, options.interaction); gl.enable(gl.BLEND); gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
      let changing = false;
      for (const region of regions) {
        const inside = point.x >= region.x && point.x <= region.x + region.width && point.y >= region.y && point.y <= region.y + region.height;
        spring(region, 'hover', inside ? 1 : 0, seconds); spring(region, 'press', inside && point.down ? 1 : 0, seconds);
        changing = changing || Math.abs(region.hoverVelocity) > .01 || Math.abs(region.pressVelocity) > .01;
        const material = materialFor(region.preset); gl.bufferSubData(gl.ARRAY_BUFFER, 0, region.vertices);
        gl.uniform2f(locations.center, region.x + region.width / 2, region.y + region.height / 2); gl.uniform2f(locations.halfSize, region.width / 2, region.height / 2);
        gl.uniform1f(locations.radius, region.radius); gl.uniform1f(locations.bevel, material.bevel); gl.uniform1f(locations.strength, material.refraction * options.refraction);
        gl.uniform1f(locations.ior, material.ior); gl.uniform1f(locations.dispersion, material.dispersion);
        gl.uniform1f(locations.blurRadius, material.blur * options.blur); gl.uniform1f(locations.diffusion, material.diffusion); gl.uniform1f(locations.saturation, material.saturation);
        gl.uniform4fv(locations.tint, material.tint); gl.uniform1f(locations.reflection, material.reflection); gl.uniform1f(locations.highlight, material.highlight); gl.uniform1f(locations.shadow, material.shadow); gl.uniform1f(locations.grain, material.grain);
        gl.uniform1f(locations.brightness, material.brightness); gl.uniform1f(locations.contrast, material.contrast); gl.uniform1f(locations.edgeAmount, material.edge); gl.uniform1f(locations.falloff, material.power); gl.uniform1f(locations.model, material.model === 'glasswork' ? 1 : 0); gl.uniform1f(locations.legacy, material.legacy ? 1 : 0);
        gl.uniform1f(locations.opacity, material.opacity); gl.uniform1f(locations.adaptive, material.adaptive); gl.uniform1f(locations.density, material.density); gl.uniform1f(locations.tintMin, material.tintMin); gl.uniform1f(locations.tintMax, material.tintMax);
        gl.uniform1f(locations.hover, region.hover); gl.uniform1f(locations.press, region.press); gl.drawArrays(gl.TRIANGLES, 0, 6);
      }
      stats.frames++; return changing;
    }
    function frame(stamp) {
      request = 0; if (stopped || lost || document.hidden || options.suspended) return;
      const seconds = previous ? Math.min((stamp - previous) / 1000, .10) : 1 / 60; previous = stamp;
      if (dirtyLayout) measure();
      const moving = !!options.motion && !reduced.matches;
      if (moving) { time += seconds; dirtyScene = true; }
      try { if (dirtyScene) paintScene(); const changing = draw(seconds); if (moving || changing) wake(); else previous = 0; }
      catch (failure) { error(failure); stopped = true; }
    }
    function wake() { if (!request && !stopped && !lost && !document.hidden && !options.suspended) request = requestAnimationFrame(frame); }
    function pointer(event) { const bounds = canvas.getBoundingClientRect(); point.x = event.clientX - bounds.left; point.y = event.clientY - bounds.top; wake(); }
    listen(window, 'pointermove', pointer, { passive: true });
    listen(window, 'pointerdown', event => { pointer(event); point.down = true; wake(); }, { passive: true });
    listen(window, 'pointerup', () => { point.down = false; wake(); }, { passive: true });
    listen(window, 'pointercancel', () => { point.down = false; point.x = point.y = -10000; wake(); }, { passive: true });
    listen(window, 'blur', () => { point.down = false; point.x = point.y = -10000; wake(); });
    listen(document.documentElement, 'pointerleave', () => { point.down = false; point.x = point.y = -10000; wake(); });
    listen(window, 'scroll', () => { dirtyLayout = true; wake(); }, { capture: true, passive: true });
    listen(window, 'resize', () => { dirtyLayout = true; wake(); }, { passive: true });
    listen(document, 'visibilitychange', () => { previous = 0; if (document.hidden) { cancelAnimationFrame(request); request = 0; } else wake(); });
    listen(reduced, 'change', () => { previous = 0; wake(); });
    listen(canvas, 'webglcontextlost', event => { event.preventDefault(); lost = true; stats.ready = false; cancelAnimationFrame(request); request = 0; error('WebGL context lost; waiting for recovery.'); });
    listen(canvas, 'webglcontextrestored', () => { try { setup(); lost = false; stats.ready = true; document.documentElement.dataset.glassLabState = 'ready'; previous = 0; wake(); } catch (failure) { error(failure); } });
    const changes = new MutationObserver(() => { dirtyLayout = true; wake(); });
    changes.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['data-glass-preset', 'data-glass-radius'] });
    function loadWallpaper(url) {
      const serial = ++wallpaperRequest;
      return new Promise(resolve => {
        const source = new Image();
        source.onload = () => {
          if (stopped || serial !== wallpaperRequest) { resolve(false); return; }
          image = source; wallpaperUrl = url; dirtyScene = true; stats.wallpaperUrl = url; stats.wallpaperError = ''; wake();
          if (config.onStatus) config.onStatus({ ready: stats.ready, wallpaperLoaded: true });
          resolve(true);
        };
        source.onerror = () => {
          if (!stopped && serial === wallpaperRequest) { stats.wallpaperError = 'Wallpaper could not be loaded.'; if (config.onStatus) config.onStatus({ ready: stats.ready, wallpaperLoaded: !!image, wallpaperError: stats.wallpaperError }); }
          resolve(false);
        };
        source.src = url;
      });
    }
    this.load = async function () {
      if (options.backgroundUrl) await loadWallpaper(options.backgroundUrl);
      if (stopped) return;
      stats.ready = true; document.documentElement.dataset.glassLabState = 'ready'; measure(); paintScene(); draw(1 / 60); wake();
      if (config.onStatus) config.onStatus({ ready: true, wallpaperLoaded: !!image });
      return this.state();
    };
    this.update = function (next) {
      for (const name of ['refraction', 'blur', 'interaction']) if (Number.isFinite(Number(next[name]))) options[name] = clamp(Number(next[name]), 0, 3);
      if (typeof next.motion === 'boolean') options.motion = next.motion;
      if (Object.prototype.hasOwnProperty.call(next, 'overrides')) { options.overrides = presets.normalizeOverrides(next.overrides); materialCache.clear(); }
      if (Object.prototype.hasOwnProperty.call(next, 'presets')) { options.presets = Array.isArray(next.presets) ? next.presets.slice(0, 64) : []; materialCache.clear(); }
      if (typeof next.suspended === 'boolean') { options.suspended = next.suspended; if (options.suspended) { cancelAnimationFrame(request); request = 0; } }
      if (typeof next.backgroundUrl === 'string' && next.backgroundUrl && next.backgroundUrl !== options.backgroundUrl) { options.backgroundUrl = next.backgroundUrl; loadWallpaper(next.backgroundUrl); }
      if (['wallpaper', 'light', 'dark', 'grid'].includes(next.background)) { options.background = next.background; dirtyScene = true; }
      previous = 0; wake(); return this.state();
    };
    this.refresh = () => { dirtyLayout = true; wake(); };
    this.state = () => ({ ready: stats.ready, options: Object.assign({}, options), regions: regions.length, wallpaperLoaded: !!image, wallpaperUrl: wallpaperUrl, reducedMotion: reduced.matches, errors: stats.errors.slice() });
    this.destroy = () => { stopped = true; wallpaperRequest++; cancelAnimationFrame(request); disposeListeners.forEach(dispose => dispose()); resize.disconnect(); changes.disconnect(); if (!lost) { gl.deleteTexture(texture); gl.deleteBuffer(buffer); gl.deleteProgram(program); } stats.ready = false; };
  }
  window.GlassLab = {
    presets: presets, stats: stats,
    init: async function (config) { if (active) active.destroy(); stats.errors.length = 0; stats.frames = stats.sceneUploads = 0; active = new Renderer(config); return await active.load(); },
    updateOptions: function (options) { return active ? active.update(options || {}) : null; },
    setPreset: function (element, key) { const node = typeof element === 'string' ? document.querySelector(element) : element; if (!node || typeof key !== 'string' || !/^(?:[A-J]|user-[0-9a-f]{32})$/.test(key)) return false; node.dataset.glassPreset = key; if (active) active.refresh(); return true; },
    refresh: function () { if (active) active.refresh(); },
    getState: function () { return active ? active.state() : { ready: false }; },
    destroy: function () { if (active) active.destroy(); active = null; }
  };
}());
