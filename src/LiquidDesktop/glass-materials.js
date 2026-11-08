/* Shared optical material definitions for desktop, companion and the style lab.
 * bevel / blur use scene pixels; tint is RGB plus a 0..1 mixing weight.
 * I aliases the original material; valid numeric edits enable its editable form.
 * J adapts Glasswork's MIT rim model / Lumno light defaults (Kubai087, 2026).
 */
(function (root) {
  'use strict';
  var materials = {
    A: { name: '清透', bevel: 7, refraction: .82, ior: 1.36, dispersion: .005, blur: .6, diffusion: .08, saturation: 1.03, tint: [.80, .86, .94, .008], reflection: .12, highlight: .13, shadow: .10, grain: 0 },
    B: { name: '柔雾', bevel: 12, refraction: .62, ior: 1.40, dispersion: .006, blur: 8.5, diffusion: .86, saturation: .98, tint: [.83, .88, .95, .025], reflection: .15, highlight: .09, shadow: .09, grain: 0 },
    C: { name: '厚边', bevel: 27, refraction: 1.58, ior: 1.52, dispersion: .018, blur: 1.3, diffusion: .16, saturation: 1.04, tint: [.55, .62, .74, .012], reflection: .28, highlight: .18, shadow: .17, grain: 0 },
    D: { name: '中性灰', bevel: 11, refraction: .93, ior: 1.46, dispersion: .003, blur: 2.8, diffusion: .38, saturation: .58, tint: [.28, .31, .37, .095], reflection: .18, highlight: .10, shadow: .12, grain: 0 },
    E: { name: '棱彩', bevel: 20, refraction: 1.42, ior: 1.50, dispersion: .14, blur: .8, diffusion: .10, saturation: 1.12, tint: [.76, .82, .95, .008], reflection: .20, highlight: .12, shadow: .12, grain: 0 },
    F: { name: '轻乳白', bevel: 15, refraction: .72, ior: 1.39, dispersion: .008, blur: 14, diffusion: .94, saturation: .86, tint: [.94, .95, .98, .17], reflection: .16, highlight: .08, shadow: .08, grain: 0 },
    G: { name: '磨砂', bevel: 10, refraction: .60, ior: 1.40, dispersion: .005, blur: 18, diffusion: .96, saturation: 1.02, tint: [.88, .91, .95, .035], reflection: .12, highlight: .08, shadow: .09, grain: .025 },
    H: { name: '暖晶', bevel: 16, refraction: 1.10, ior: 1.48, dispersion: .035, blur: 1.1, diffusion: .16, saturation: 1.04, tint: [1., .67, .31, .065], reflection: .20, highlight: .12, shadow: .12, grain: 0 },
    I: { name: '经典液态', bevel: 30, refraction: 1, ior: 1.46, dispersion: .065, blur: 2.2, diffusion: .5, saturation: 1.16, tint: [.8, .86, .94, 0], reflection: .38, highlight: .13, shadow: .1, grain: 0 },
    J: { name: 'Glasswork', model: 'glasswork', bevel: 40, refraction: 1, ior: 1.46, dispersion: .15, blur: 2, diffusion: 1, saturation: 1.8, brightness: 1.06, contrast: 1, tint: [1, 1, 1, 0], reflection: .18, highlight: .20, shadow: .12, grain: 0, edge: 1.1, power: 2.6, adaptive: 1, density: 1, tintMin: .06, tintMax: .22 }
  };
  var ranges = {
    refraction: { min: 0, max: 2.5, step: .01, label: '折射强度' }, bevel: { min: 2, max: 50, step: 1, label: '折射边宽' },
    ior: { min: 1.05, max: 2, step: .01, label: '折射率' }, dispersion: { min: 0, max: .3, step: .005, label: '色散' },
    edge: { min: 0, max: 2.5, step: .01, label: '边缘位移' }, power: { min: 1, max: 6, step: .1, label: '边缘衰减' },
    blur: { min: 0, max: 30, step: .1, label: '模糊半径' }, diffusion: { min: 0, max: 1, step: .01, label: '漫散射' }, grain: { min: 0, max: .06, step: .001, label: '细粒' },
    saturation: { min: 0, max: 4, step: .01, label: '饱和度' }, brightness: { min: .25, max: 2.5, step: .01, label: '亮度' }, contrast: { min: .25, max: 2.5, step: .01, label: '对比度' },
    tintOpacity: { min: 0, max: .5, step: .005, label: '基础染色' }, tintR: { min: 0, max: 1, step: .001, label: '染色红' }, tintG: { min: 0, max: 1, step: .001, label: '染色绿' }, tintB: { min: 0, max: 1, step: .001, label: '染色蓝' },
    reflection: { min: 0, max: .6, step: .01, label: '环境反射' }, highlight: { min: 0, max: .4, step: .01, label: '边缘高光' }, shadow: { min: 0, max: .3, step: .01, label: '边缘阴影' },
    opacity: { min: 0, max: 1, step: .01, label: '材质浓度' }, adaptive: { min: 0, max: 1, step: .01, label: '背景自适应' }, density: { min: 0, max: 1, step: .01, label: '附加染色密度' },
    tintMin: { min: 0, max: .5, step: .005, label: '附加染色下限' }, tintMax: { min: 0, max: 1, step: .005, label: '附加染色上限' }
  };
  var keys = Object.keys(materials), fields = Object.keys(ranges), owns = Object.prototype.hasOwnProperty;
  keys.forEach(function (key) {
    var value = materials[key];
    value.key = value.baseKey = key; value.model = value.model || 'rounded'; value.legacy = key === 'I';
    value.brightness = value.brightness === undefined ? 1 : value.brightness; value.contrast = value.contrast === undefined ? 1 : value.contrast;
    value.opacity = 1; value.edge = value.edge === undefined ? 1 : value.edge; value.power = value.power === undefined ? 2.6 : value.power;
    value.adaptive = value.adaptive || 0; value.density = value.density || 0; value.tintMin = value.tintMin || 0; value.tintMax = value.tintMax === undefined ? .22 : value.tintMax;
    value.tintR = value.tint[0]; value.tintG = value.tint[1]; value.tintB = value.tint[2]; value.tintOpacity = value.tint[3];
    Object.freeze(value.tint); Object.freeze(value);
  });
  fields.forEach(function (field) { Object.freeze(ranges[field]); }); Object.freeze(ranges);
  function record(value) { return value && typeof value === 'object' && !Array.isArray(value); }
  function parameters(input) {
    var result = {}; if (!record(input)) return result;
    fields.forEach(function (field) { var value = input[field], range = ranges[field]; if (owns.call(input, field) && typeof value === 'number' && isFinite(value)) result[field] = Math.max(range.min, Math.min(range.max, value)); });
    return result;
  }
  function normalizeOverrides(input) {
    var result = {}; if (!record(input)) return result;
    keys.forEach(function (key) { if (!owns.call(input, key)) return; var row = parameters(input[key]); Object.keys(row).forEach(function (field) { if (row[field] === materials[key][field]) delete row[field]; }); if (Object.keys(row).length) result[key] = row; });
    return result;
  }
  function resolve(key, overrides, presets) {
    var custom = null, baseKey = typeof key === 'string' && /^[A-J]$/.test(key) ? key : 'I';
    if (typeof key === 'string' && /^user-[0-9a-f]{32}$/.test(key) && Array.isArray(presets)) {
      for (var i = 0; i < Math.min(64, presets.length); i++) { var item = presets[i]; if (record(item) && item.id === key && typeof item.baseStyle === 'string' && /^[A-J]$/.test(item.baseStyle)) { custom = item; baseKey = item.baseStyle; break; } }
    }
    var base = materials[baseKey], result = {}, changed = false;
    Object.keys(base).forEach(function (field) { result[field] = base[field]; });
    var row = parameters(custom ? custom.parameters : record(overrides) && owns.call(overrides, baseKey) ? overrides[baseKey] : null);
    Object.keys(row).forEach(function (field) { result[field] = row[field]; if (row[field] !== base[field]) changed = true; });
    result.key = custom ? custom.id : baseKey; result.baseKey = baseKey; result.customized = changed; result.legacy = baseKey === 'I' && !changed;
    if (result.tintMin > result.tintMax) { var lower = result.tintMax; result.tintMax = result.tintMin; result.tintMin = lower; }
    if (custom && typeof custom.name === 'string' && custom.name.trim()) result.name = custom.name.trim().slice(0, 48);
    result.tint = [result.tintR, result.tintG, result.tintB, result.tintOpacity];
    return result;
  }
  Object.defineProperties(materials, { ranges: { value: ranges }, resolve: { value: resolve }, normalizeOverrides: { value: normalizeOverrides } });
  root.PrismGlassMaterials = Object.freeze(materials);
}(window));
