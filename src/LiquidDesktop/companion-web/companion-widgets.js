/* Local tools plus explicitly shared computer summaries. No offline actions. */
(function () {
  'use strict';
  var gallery = document.getElementById('widget-gallery'), customize = document.getElementById('widget-customize');
  if (!gallery || !customize) return;
  var ids = ['timer', 'stopwatch', 'calculator', 'date', 'world', 'breath', 'weather', 'tasks', 'immerse', 'hourglass', 'sunpath', 'yeardots', 'tide', 'orrery', 'bonsai'];
  var titles = { timer: '计时器', stopwatch: '秒表', calculator: '计算器', date: '日期计算', world: '世界时钟', breath: '呼吸练习', weather: '天气', tasks: '待办', immerse: '沉浸展示', hourglass: '专注沙漏', sunpath: '日光窗', yeardots: '值得期待', tide: '潮汐休息', orrery: '专注星仪', bonsai: '待办盆景' };
  var zones = [['Asia/Shanghai', '北京'], ['Asia/Tokyo', '东京'], ['Europe/London', '伦敦'], ['Europe/Paris', '巴黎'], ['America/New_York', '纽约'], ['Australia/Sydney', '悉尼'], ['UTC', '世界标准时间']];
  var storageKey = 'prismglass.companion.widgets.v1', preferences = { version: 1, order: ids.slice(), visible: ids.slice(0, 9), duration: 180, sandMinutes: 5, zone: 'Asia/Shanghai' }, storageAvailable = true;
  try {
    var saved = JSON.parse(window.localStorage.getItem(storageKey) || 'null');
    if (saved && saved.version === 1) {
      if (Array.isArray(saved.order)) preferences.order = saved.order.filter(function (id, at, list) { return ids.indexOf(id) >= 0 && list.indexOf(id) === at; }).concat(ids.filter(function (id) { return saved.order.indexOf(id) < 0; }));
      if (Array.isArray(saved.visible)) preferences.visible = ids.filter(function (id) { return saved.visible.indexOf(id) >= 0; });
      if ([5, 15, 25, 50].indexOf(saved.sandMinutes) >= 0) preferences.sandMinutes = saved.sandMinutes;
      if (Number.isInteger(saved.duration) && saved.duration >= 60 && saved.duration <= 10800 && saved.duration % 60 === 0) preferences.duration = saved.duration;
      if (zones.some(function (zone) { return zone[0] === saved.zone; })) preferences.zone = saved.zone;
    }
  } catch (error) { storageAvailable = false; }
  var cards = {}, rows = {}, tick = 0, lastSecond = -1, weatherKey = '', tasksKey = '', authenticated = false, pageHidden = false;
  var timer = { remaining: preferences.duration * 1000, started: 0, running: false }, stopwatch = { elapsed: 0, started: 0, running: false }, breath = { started: 0, running: false };
  var reduced = window.matchMedia ? window.matchMedia('(prefers-reduced-motion: reduce)') : null;
  var clockFormat, dateFormat;
  function el(tag, className, value) { var node = document.createElement(tag); if (className) node.className = className; if (value != null) node.textContent = value; return node; }
  function set(node, value) { value = String(value); if (node.textContent !== value) node.textContent = value; }
  function named(node, id) { node.id = id; return node; }
  function button(id, label, action) { var node = named(el('button', '', label), id); node.type = 'button'; node.onclick = action; return node; }
  function icon(kind) {
    var paths = { calculator: ['M6 3h12v18H6zM9 6h6M9 10h1m4 0h1m-6 4h1m4 0h1m-6 4h1m4 0h1'], date: ['M4 5h16v16H4zM8 3v4m8-4v4M4 10h16m-12 4h2m4 0h2m-8 3h2'], timer: ['M9 3h6m-3 0v3', 'M12 10v4l3 2'], stopwatch: ['M9 3h6m-3 0v3m5 2 2-2', 'M12 10v4'], world: ['M3 12h18M12 3c6 5 6 13 0 18-6-5-6-13 0-18'], breath: ['M4 14c0-5 5-9 8-11 3 2 8 6 8 11a8 8 0 0 1-16 0Z'], weather: ['M5 15a4 4 0 1 1 3-7 5 5 0 0 1 9 3 3 3 0 0 1 0 6H7'], tasks: ['M8 6h12M8 12h12M8 18h12M3 6h1m-1 6h1m-1 6h1'], immerse: ['M9 3H4v5m11-5h5v5M4 16v5h5m11-5v5h-5'], up: ['m6 14 6-6 6 6'], down: ['m6 10 6 6 6-6'] };
    paths.hourglass = ['M6 3h12M6 21h12M7 3c0 5 10 13 10 18M17 3C17 8 7 16 7 21M9 7h6m-6 10h6'];
    paths.sunpath = ['M3 19h18M4 16a8 8 0 0 1 16 0M12 3v2m-8 2 2 2m14-2-2 2'];
    paths.date = ['M4 5h16v16H4zM8 3v4m8-4v4M4 10h16M7 15h4m-2-2v4m5-2h3'];
    paths.yeardots = ['M4 5h16v16H4zM8 3v4m8-4v4M4 10h16M7 13h1m3 0h1m3 0h1m-9 4h1', 'M13 15h4v4h-4z'];
    paths.tide = ['M2 8c4-5 6 5 10 0s6 5 10 0M2 13c4-5 6 5 10 0s6 5 10 0M2 18c4-5 6 5 10 0s6 5 10 0'];
    paths.orrery = ['M10 12a2 2 0 1 0 4 0 2 2 0 1 0-4 0M3 12a9 5 0 1 0 18 0 9 5 0 1 0-18 0M9 3l6 18'];
    paths.bonsai = ['M8 21h8l1-4H7ZM12 17V7M12 10C4 10 4 3 4 3s8 0 8 7ZM12 13c8 0 8-7 8-7s-8 0-8 7Z'];
    var svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg'); svg.setAttribute('viewBox', '0 0 24 24'); svg.setAttribute('aria-hidden', 'true'); svg.setAttribute('data-prism-icon', kind); svg.classList.add('ui-icon');
    if (['timer', 'stopwatch', 'world'].indexOf(kind) >= 0) { var circle = document.createElementNS('http://www.w3.org/2000/svg', 'circle'); circle.setAttribute('cx', '12'); circle.setAttribute('cy', kind === 'world' ? '12' : '14'); circle.setAttribute('r', kind === 'world' ? '9' : '8'); svg.appendChild(circle); }
    (paths[kind] || []).forEach(function (d) { var path = document.createElementNS('http://www.w3.org/2000/svg', 'path'); path.setAttribute('d', d); svg.appendChild(path); }); return svg;
  }
  function card(id, caption) {
    var node = named(el('section', 'companion-widget widget-' + id), 'widget-card-' + id), header = el('div', 'widget-card-heading'), title = named(el('h2', '', titles[id]), 'widget-heading-' + id);
    node.setAttribute('aria-labelledby', title.id); header.appendChild(icon(id)); header.appendChild(title); node.appendChild(header); node.appendChild(el('p', 'widget-caption', caption)); cards[id] = node; gallery.appendChild(node); return node;
  }
  function status(id, parent, value) { var node = named(el('p', 'widget-status', value), id); node.setAttribute('role', 'status'); parent.appendChild(node); return node; }
  function controls(parent) { var node = el('div', 'widget-controls'); parent.appendChild(node); return node; }
  function emit(type, detail) { window.dispatchEvent(new CustomEvent(type, { detail: detail || {} })); }
  function save() { try { window.localStorage.setItem(storageKey, JSON.stringify(preferences)); storageAvailable = true; } catch (error) { storageAvailable = false; } set(settingsNote, storageAvailable ? '仅保存小组件偏好，不保存电脑内容。' : '当前浏览器无法保存偏好，本次调整仍可使用。'); }
  function activePage() { return !pageHidden && !document.hidden && document.body.dataset.page === 'widgets'; }
  function shown(id) { return preferences.visible.indexOf(id) >= 0; }
  function elapsedClock(ms) { var seconds = Math.max(0, Math.floor(ms / 1000)), hours = Math.floor(seconds / 3600), minutes = Math.floor(seconds % 3600 / 60); return (hours ? hours + ':' : '') + String(minutes).padStart(2, '0') + ':' + String(seconds % 60).padStart(2, '0'); }
  function timerRemaining() { return Math.max(0, timer.remaining - (timer.running ? Date.now() - timer.started : 0)); }
  function stopwatchElapsed() { return stopwatch.elapsed + (stopwatch.running ? Date.now() - stopwatch.started : 0); }

  var timerCard = card('timer', ''), timerTime = named(el('strong', 'widget-number', elapsedClock(timer.remaining)), 'widget-timer'), timerLabel = el('label', 'widget-duration-label', '分钟'), timerMinutes = named(el('input'), 'widget-timer-minutes');
  timerCard.appendChild(timerTime); timerMinutes.type = 'number'; timerMinutes.min = '1'; timerMinutes.max = '180'; timerMinutes.step = '1'; timerMinutes.inputMode = 'numeric'; timerMinutes.value = String(preferences.duration / 60); timerLabel.appendChild(timerMinutes); timerCard.appendChild(timerLabel);
  var timerPresets = el('div', 'widget-presets'); timerPresets.setAttribute('role', 'group'); timerPresets.setAttribute('aria-label', '快捷计时时长'); timerCard.appendChild(timerPresets);
  var timerPresetButtons = [1, 3, 5, 15].map(function (minutes) { var preset = button('widget-timer-preset-' + minutes, minutes + ' 分钟', function () { if (timer.running) return; timerMinutes.value = String(minutes); timerMinutes.onchange(); }); preset.value = String(minutes); timerPresets.appendChild(preset); return preset; });
  var timerStatus = status('widget-timer-status', timerCard, '仅在此页前台计时'), timerActions = controls(timerCard);
  var timerToggle = button('widget-timer-toggle', '开始', function () {
    if (timer.running) pauseOne('timer', '已暂停，可继续');
    else if (activePage() && shown('timer')) { if (timer.remaining <= 0) timer.remaining = preferences.duration * 1000; timer.started = Date.now(); timer.running = true; set(timerStatus, '计时中 · 切页后继续计时'); }
    updateTools(); syncTicker();
  });
  var timerReset = button('widget-timer-reset', '重置', function () { timer.running = false; timer.remaining = preferences.duration * 1000; set(timerStatus, '已重置'); updateTools(); }); timerActions.appendChild(timerToggle); timerActions.appendChild(timerReset);
  timerMinutes.onchange = function () { var value = Number(timerMinutes.value); if (timer.running || !Number.isInteger(value) || value < 1 || value > 180) { timerMinutes.value = String(preferences.duration / 60); set(timerStatus, '请输入 1 到 180 的整数分钟'); return; } preferences.duration = value * 60; timer.remaining = preferences.duration * 1000; save(); set(timerStatus, '时长已更新，点击开始'); updateTools(); };

  var watchCard = card('stopwatch', ''), watchTime = named(el('strong', 'widget-number', '00:00'), 'widget-stopwatch'); watchCard.appendChild(watchTime);
  var watchStatus = status('widget-stopwatch-status', watchCard, '切页后继续计时'), watchActions = controls(watchCard);
  var watchToggle = button('widget-stopwatch-toggle', '开始', function () { if (stopwatch.running) pauseOne('stopwatch', '已暂停，可继续'); else if (activePage() && shown('stopwatch')) { stopwatch.running = true; stopwatch.started = Date.now(); set(watchStatus, '计时中 · 切页后继续计时'); } updateTools(); syncTicker(); });
  var lapCount = 0, lapAt = 0, watchLaps = named(el('ol', 'widget-laps'), 'widget-stopwatch-laps'); watchLaps.hidden = true; watchLaps.setAttribute('aria-label', '最近五圈');
  var watchLap = button('widget-stopwatch-lap', '记一圈', function () {
    if (!stopwatch.running || !activePage() || !shown('stopwatch')) return;
    var elapsed = stopwatchElapsed(), lapTime = elapsedClock(elapsed - lapAt), row = el('li'); lapAt = elapsed; lapCount++;
    row.appendChild(el('span', '', '第 ' + lapCount + ' 圈')); row.appendChild(el('strong', '', lapTime)); watchLaps.appendChild(row);
    if (watchLaps.children.length > 5) watchLaps.children[0].remove(); watchLaps.hidden = false; set(watchStatus, '第 ' + lapCount + ' 圈 · 本圈 ' + lapTime + '，保留最近五圈');
  });
  watchActions.appendChild(watchToggle); watchActions.appendChild(watchLap); watchActions.appendChild(button('widget-stopwatch-reset', '归零', function () { stopwatch.running = false; stopwatch.elapsed = 0; lapCount = 0; lapAt = 0; watchLaps.textContent = ''; watchLaps.hidden = true; set(watchStatus, '已归零'); updateTools(); })); watchCard.appendChild(watchLaps);

  var worldCard = card('world', ''), worldTime = named(el('strong', 'widget-number', '—'), 'widget-world-time'), worldDate = el('p', 'widget-world-date'), worldZone = named(el('select'), 'widget-zone');
  worldZone.setAttribute('aria-label', '世界时钟城市'); zones.forEach(function (zone) { var option = el('option', '', zone[1]); option.value = zone[0]; worldZone.appendChild(option); }); worldZone.value = preferences.zone; worldCard.appendChild(worldTime); worldCard.appendChild(worldDate); worldCard.appendChild(worldZone);
  function configureClock() { try { clockFormat = new Intl.DateTimeFormat('zh-CN', { timeZone: preferences.zone, hour: '2-digit', minute: '2-digit', hour12: false }); dateFormat = new Intl.DateTimeFormat('zh-CN', { timeZone: preferences.zone, month: 'long', day: 'numeric', weekday: 'short' }); } catch (error) { clockFormat = dateFormat = null; } lastSecond = -1; }
  worldZone.onchange = function () { if (!zones.some(function (zone) { return zone[0] === worldZone.value; })) return; preferences.zone = worldZone.value; configureClock(); save(); updateTools(); };

  var breathCard = card('breath', ''), orb = named(el('div', 'widget-breath-orb'), 'widget-breath-orb'); orb.setAttribute('aria-hidden', 'true'); orb.dataset.running = 'false'; breathCard.appendChild(orb);
  var breathStatus = status('widget-breath-status', breathCard, '吸气 4 秒 · 呼气 4 秒'), breathActions = controls(breathCard), breathToggle = button('widget-breath-toggle', '开始练习', function () { if (breath.running) pauseOne('breath', '已暂停'); else if (activePage() && shown('breath')) { breath.started = performance.now(); breath.running = true; } updateTools(); syncTicker(); }); breathActions.appendChild(breathToggle);

  var weatherCard = card('weather', ''), weatherLocation = named(el('p', 'widget-weather-location', '电脑天气位置'), 'widget-weather-location'), temperature = named(el('strong', 'widget-number', '—'), 'widget-weather-temp'), weatherSummary = named(el('p', 'widget-summary', '连接电脑后同步天气'), 'widget-weather-summary'); weatherCard.appendChild(weatherLocation); weatherCard.appendChild(temperature); weatherCard.appendChild(weatherSummary); var weatherStamp = status('widget-weather-status', weatherCard, '尚未连接'), weatherReason = named(el('p', 'widget-weather-reason'), 'widget-weather-reason'); weatherReason.hidden = true; weatherCard.appendChild(weatherReason);
  var tasksCard = card('tasks', ''), taskCount = named(el('strong', 'widget-task-count', '—'), 'widget-task-count'), taskList = named(el('ul', 'widget-task-list'), 'widget-task-list'); tasksCard.appendChild(taskCount); tasksCard.appendChild(taskList); var taskStatus = status('widget-tasks-status', tasksCard, '连接并允许共享待办后显示');
  var taskOpen = button('widget-tasks-open', '打开待办', function () { if (!taskOpen.disabled) emit('companion-widget-navigate', { page: 'work' }); }); taskOpen.disabled = true; controls(tasksCard).appendChild(taskOpen);
  var immerseCard = card('immerse', ''); immerseCard.appendChild(el('div', 'widget-immersion-orbit')); var immerseStatus = status('widget-immerse-status', immerseCard, '连接电脑后可用'), immerseButton = button('widget-immerse', '开始展示', function () { if (authenticated) emit('companion-widget-immerse'); }); immerseButton.disabled = true; controls(immerseCard).appendChild(immerseButton);

  var calculatorCard = card('calculator', ''), calculatorDisplay = named(el('output', 'widget-number calculator-display', '0'), 'widget-calculator-result'); calculatorDisplay.setAttribute('aria-live', 'polite'); calculatorCard.appendChild(calculatorDisplay);
  var calculatorKeys = el('div', 'calculator-keys'), calculatorValue = '0', calculatorStored = null, calculatorOperator = '', calculatorFresh = true;
  calculatorCard.appendChild(calculatorKeys);
  function calculate(key) {
    if (key === 'AC' || calculatorValue === '错误') { calculatorValue = '0'; calculatorStored = null; calculatorOperator = ''; calculatorFresh = true; }
    if (/^[0-9]$/.test(key)) { if (calculatorFresh || calculatorValue === '0') calculatorValue = key; else if (calculatorValue.replace(/[-.]/g, '').length < 12) calculatorValue += key; calculatorFresh = false; }
    else if (key === '.') { if (calculatorFresh) calculatorValue = '0'; if (calculatorValue.indexOf('.') < 0) calculatorValue += '.'; calculatorFresh = false; }
    else if (key === '⌫') { calculatorValue = calculatorFresh ? '0' : calculatorValue.slice(0, -1) || '0'; if (calculatorValue === '-') calculatorValue = '0'; }
    else if (key === '±') calculatorValue = String(-Number(calculatorValue));
    else if (key === '%') calculatorValue = String(Number(calculatorValue) / 100);
    else if (['+', '−', '×', '÷', '='].indexOf(key) >= 0) {
      var right = Number(calculatorValue), result = right;
      if (calculatorOperator && !calculatorFresh) {
        if (calculatorOperator === '+') result = calculatorStored + right;
        if (calculatorOperator === '−') result = calculatorStored - right;
        if (calculatorOperator === '×') result = calculatorStored * right;
        if (calculatorOperator === '÷') result = right === 0 ? NaN : calculatorStored / right;
      }
      calculatorValue = Number.isFinite(result) ? String(Number(result.toPrecision(12))) : '错误';
      calculatorStored = result; calculatorOperator = key === '=' || !Number.isFinite(result) ? '' : key; calculatorFresh = true;
    }
    set(calculatorDisplay, calculatorValue);
  }
  ['AC', '±', '%', '÷', '7', '8', '9', '×', '4', '5', '6', '−', '1', '2', '3', '+', '⌫', '0', '.', '='].forEach(function (key, index) { var item = button('widget-calc-' + index, key, function () { calculate(key); }); if (['+', '−', '×', '÷', '='].indexOf(key) >= 0) item.className = 'calculator-operator'; item.setAttribute('aria-label', { AC: '清空计算', '±': '切换正负', '%': '百分比', '÷': '除以', '×': '乘以', '−': '减去', '+': '加上', '⌫': '删除一位', '.': '小数点', '=': '等于' }[key] || key); calculatorKeys.appendChild(item); });

  var dateCard = card('date', ''), dateDistance = named(el('strong', 'widget-number', '—'), 'widget-date-distance'), dateInput = named(el('input'), 'widget-date-target'); dateInput.type = 'date'; dateInput.setAttribute('aria-label', '选择目标日期'); dateInput.min = '1900-01-01'; dateInput.max = '2100-12-31'; dateCard.appendChild(dateDistance); dateCard.appendChild(dateInput); var dateStatus = status('widget-date-status', dateCard, '选择日期');
  function updateDateDistance() {
    var match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(dateInput.value), target = match && new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])));
    if (!target || Number(match[1]) < 1900 || Number(match[1]) > 2100 || target.getUTCFullYear() !== Number(match[1]) || target.getUTCMonth() !== Number(match[2]) - 1 || target.getUTCDate() !== Number(match[3])) { set(dateDistance, '—'); set(dateStatus, dateInput.value ? '请选择有效日期' : '选择日期'); return; }
    var now = new Date(), days = Math.round((target.getTime() - Date.UTC(now.getFullYear(), now.getMonth(), now.getDate())) / 86400000);
    set(dateDistance, Math.abs(days) + ' 天'); set(dateStatus, days === 0 ? '就是今天' : days > 0 ? '距离那一天' : '已经过去');
  }
  dateInput.onchange = updateDateDistance;

  // Shared work summaries and the same transparent artwork as the desktop.
  // Local timers reuse the existing foreground ticker; private content is never persisted.
  var artNodes = {}, artCaptions = {}, sunSnapshot = null, artSecond = -1, artFrame = 0, artReduced = false, leafChangedAt = -Infinity, leafSignature = '', artParents = null;
  var sand = { remaining: preferences.sandMinutes * 60000, started: 0, running: false }, sandToggle, sandPresets = [];
  var rest = { elapsed: 0, started: 0, running: false }, work = { tasks: null, focus: null, control: false }, workDetails = {}, workLabels = {}, workActions = {}, workKeys = {};
  function restElapsed() { return Math.min(60000, rest.elapsed + (rest.running ? performance.now() - rest.started : 0)); }
  function focusRemaining() { var f = work.focus; return f && f.available ? Math.max(0, f.deadline > 0 ? f.deadline * 1000 - Date.now() - (f.offset || 0) : f.remaining * 1000) : null; }
  function countdownDays(value) { var m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value || ''), d = m && new Date(Date.UTC(+m[1], +m[2] - 1, +m[3])); if (!d || +m[1] < 1900 || +m[1] > 2100 || d.getUTCFullYear() !== +m[1] || d.getUTCMonth() !== +m[2] - 1 || d.getUTCDate() !== +m[3]) return null; var now = new Date(); return Math.round((d - Date.UTC(now.getFullYear(), now.getMonth(), now.getDate())) / 86400000); }
  // Retain the SVG tree: time changes only geometry attributes, never replaces
  // the whole scene. Static/data/visual cadence follows ModularClocks and eww.
  function shape(parent, tag, attributes) {
    if (!parent._artChildren) parent._artChildren = [];
    if (parent._artCursor == null) { parent._artCursor = 0; artParents.push(parent); }
    var index = parent._artCursor++, node = parent._artChildren[index];
    if (node && node._artTag !== tag) { node.remove(); node = null; }
    if (!node) { node = document.createElementNS('http://www.w3.org/2000/svg', tag); node._artTag = tag; node._artAttributes = {}; parent._artChildren[index] = node; var next = parent._artChildren[index + 1]; if (next && next.parentNode === parent) parent.insertBefore(node, next); else parent.appendChild(node); }
    Object.keys(node._artAttributes).forEach(function (name) { if (!(name in attributes)) { node.removeAttribute(name); delete node._artAttributes[name]; } });
    Object.keys(attributes).forEach(function (name) { var value = String(attributes[name]); if (node._artAttributes[name] !== value) { node.setAttribute(name, value); node._artAttributes[name] = value; } }); return node;
  }
  function dot(parent, x, y, rx, ry, color, opacity) { return shape(parent, 'ellipse', { cx: x, cy: y, rx: rx, ry: ry, fill: color, opacity: opacity == null ? 1 : opacity }); }
  function stroke(parent, x, y, x2, y2, color, width) { return shape(parent, 'line', { x1: x, y1: y, x2: x2, y2: y2, stroke: color, 'stroke-width': width || 2, 'stroke-linecap': 'round' }); }
  function artColor(id) { return id === 'hourglass' || id === 'sunpath' ? '#ffd3a6' : id === 'tide' || id === 'bonsai' ? '#b3ebcd' : '#bcd7ff'; }
  function sandRemaining() { return Math.max(0, sand.remaining - (sand.running ? Date.now() - sand.started : 0)); }
  function sunPosition(data) {
    if (!data || data.stale || !Number.isFinite(data.fetchedAt) || Date.now() - data.fetchedAt > 7200000 || data.fetchedAt > Date.now() + 60000 || !Number.isFinite(data.utcOffsetSeconds) || Math.abs(data.utcOffsetSeconds) > 50400) return null;
    var pattern = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/;
    if (!pattern.test(data.sunrise) || !pattern.test(data.sunset)) return null;
    var rise = Date.parse(data.sunrise + ':00Z') - data.utcOffsetSeconds * 1000, sunset = Date.parse(data.sunset + ':00Z') - data.utcOffsetSeconds * 1000;
    if (!Number.isFinite(rise) || !Number.isFinite(sunset) || sunset <= rise || sunset - rise > 86400000 || Math.floor((rise + data.utcOffsetSeconds * 1000) / 86400000) !== Math.floor((Date.now() + data.utcOffsetSeconds * 1000) / 86400000)) return null;
    return Math.max(0, Math.min(1, (Date.now() - rise) / (sunset - rise)));
  }
  function drawArt(id) {
    var svg = artNodes[id], color = artColor(id); artParents = [];
    var defs = shape(svg, 'defs', {}), prefix = 'prism-art-' + id + '-';
    function gradient(name, stops, radial, attrs) {
      var item = shape(defs, radial ? 'radialGradient' : 'linearGradient', Object.assign({ id: prefix + name }, radial ? { cx: '30%', cy: '24%', r: '78%' } : { x1: '0%', y1: '0%', x2: '70%', y2: '100%' }, attrs || {}));
      stops.forEach(function (stop) { shape(item, 'stop', { offset: stop[0], 'stop-color': stop[1], 'stop-opacity': stop[2] == null ? 1 : stop[2] }); }); return 'url(#' + prefix + name + ')';
    }
    var pearl = gradient('pearl', [[0, '#fff'], [.48, color, .95], [1, '#7789c6', .6]], true), quiet = gradient('quiet', [[0, '#fff', .32], [.5, '#bcd0f1', .14], [1, '#899ac1', .08]], true);
    var warm = gradient('warm', [[0, '#fffff4'], [.4, '#ffe09d', .98], [1, '#eb9975', .9]], true), cool = gradient('cool', [[0, '#f9ffff'], [.4, '#a6e1f8', .95], [1, '#718fda', .8]], true), rose = gradient('rose', [[0, '#fffaff'], [.45, '#e1c4f0', .9], [1, '#a783d3', .8]], true);
    var glow = gradient('glow', [[0, color, .25], [1, color, 0]], true, { cx: '50%', cy: '50%', r: '50%' }), glass = gradient('glass', [[0, '#fff', .5], [.45, '#d9e8ff', .06], [1, '#b9d0fc', .3]]), prism = gradient('prism', [[0, '#c1eeff', .75], [.34, '#fff6ff', .9], [.68, '#f1c6e5', .6], [1, '#bdadf9', .4]]);
    function sphere(x, y, r, fill) { shape(svg, 'circle', { cx: x, cy: y, r: r, fill: fill, stroke: '#fff', 'stroke-opacity': .5, 'stroke-width': .6 }); dot(svg, x - r * .28, y - r * .32, r * .25, r * .16, '#fff', .6); }
    function path(d, fill, edge, width) { return shape(svg, 'path', { d: d, fill: fill || 'none', stroke: edge || 'none', 'stroke-width': width || 0 }); }
    if (id === 'hourglass') {
      dot(svg, 122, 157, 46, 9, glow);
      path('M82,24H162C162,62 135,75 127,88C135,103 162,116 162,154H82C82,116 109,103 117,88C109,75 82,62 82,24Z', glass, prism, 1.3);
      var fraction = sandRemaining() / (preferences.sandMinutes * 60000), top = 87 - 57 * Math.sqrt(fraction), sandPaint = gradient('sand', [[0, '#fff0c8', .95], [.4, '#e8be8f', .95], [1, '#d39e7b', .9]]);
      function half(y) { return Math.max(1, Math.min(30, (87 - y) * .68)); }
      if (fraction > 0) {
        var d = 'M' + (122 - half(top)) + ',' + top;
        for (var row = 0; row <= 18; row++) { var y = top + (87 - top) * row / 18; d += 'L' + (122 - half(y)) + ',' + y; }
        for (row = 18; row >= 0; row--) { y = top + (87 - top) * row / 18; d += 'L' + (122 + half(y)) + ',' + y; }
        path(d + 'Z', sandPaint); dot(svg, 122, top, half(top), 3, '#ffe6b6', .95);
        for (var grain = 1; grain <= 14; grain++) { y = top + (87 - top) * grain / 15; dot(svg, 122 + Math.sin(grain * 2.4) * half(y) * .7, y, .5, .4, '#fffae1', .6); }
      }
      if (fraction < 1) { var height = 48 * (1 - fraction); path('M92,151C101,151 114,' + (153 - height) + ' 122,' + (151 - height) + 'C131,' + (153 - height) + ' 143,151 152,151Z', sandPaint); dot(svg, 122, 151, 29, 3, '#f8daad', .85); }
      var floor = 142 - 42 * (1 - fraction), moving = sand.running && !artStill(), flow = (preferences.sandMinutes * 60000 - sandRemaining()) / 1000;
      stroke(svg, 122, 87, 122, floor, sand.running ? '#ffe7b866' : '#ffe7b800', .6);
      for (var grainIndex = 0; grainIndex < 4; grainIndex++) { var travel = ((moving ? flow : 0) * 1.6 + grainIndex * .25) % 1; dot(svg, 122 + Math.sin(grainIndex * 2.4) * .6, 89 + (floor - 89) * travel, .8, 1.3, '#fff8dc', sand.running ? .9 : 0); }
      path('M88,32C88,60 112,73 119,85', 'none', '#ffffffb4', 1.4); path('M133,103C143,117 154,130 156,145', 'none', '#ffebd382', .8);
      [22, 156].forEach(function (y) { shape(svg, 'ellipse', { cx: 122, cy: y, rx: 43, ry: 4, fill: glass, stroke: prism, 'stroke-width': 1 }); stroke(svg, 90, y - 1, 150, y - 1, '#ffffff96', .7); });
      set(artCaptions[id], sand.remaining === 0 ? '时间到了' : elapsedClock(Math.ceil(sandRemaining() / 1000) * 1000));
    } else if (id === 'sunpath') {
      var dawn = gradient('dawn', [[0, '#ffebd2', .3], [.42, '#e8d3eb', .07], [1, '#abd5f4', .22]]), halo = gradient('halo', [[0, '#ffd78d', .4], [.45, '#ffbf8d', .14], [1, '#ffc09f', 0]], true, { cx: '50%', cy: '50%', r: '50%' });
      shape(svg, 'ellipse', { cx: 122, cy: 142, rx: 97, ry: 17, fill: glass, stroke: '#fff', 'stroke-opacity': .4, 'stroke-width': .6 });
      path('M34,136C43,11 201,11 210,136Z', dawn, '#fff8ec', .7); path('M36,135C45,17 199,17 208,135', 'none', prism, 1.5); path('M35,141C89,131 155,131 209,141', 'none', '#ffffff90', .7);
      for (var step = 0; step <= 8; step++) stroke(svg, 46 + step * 19, 141, 46 + step * 19, 143 + (step % 4 === 0 ? 3 : 0), '#ebf4ff66', .7);
      var progress = sunPosition(sunSnapshot);
      if (progress != null) { var x = 36 + 172 * progress, sy = 135 - 88 * Math.sin(Math.PI * progress); dot(svg, x, sy, 33, 33, halo); sphere(x, sy, 12, warm); }
      [progress != null ? sunSnapshot.sunrise.slice(11) : '—', progress != null ? sunSnapshot.sunset.slice(11) : '—'].forEach(function (value, index) { var timeLabel = shape(svg, 'text', { x: index ? 208 : 36, y: 164, fill: 'currentColor', opacity: .65, 'font-size': 10, 'text-anchor': index ? 'end' : 'start' }); set(timeLabel, value); });
      set(artCaptions[id], sunSnapshot && Number.isFinite(sunSnapshot.temp) ? Math.round(sunSnapshot.temp) + '°' : '日光');
    } else if (id === 'yeardots') {
      var date = new Date(), first = new Date(date.getFullYear(), date.getMonth(), 1), count = new Date(date.getFullYear(), date.getMonth() + 1, 0).getDate(), offset = (first.getDay() + 6) % 7, target = work.tasks && /^(\d{4})-(\d{2})-(\d{2})$/.exec(work.tasks.countdownDate), targetDay = target && +target[1] === date.getFullYear() && +target[2] === date.getMonth() + 1 ? +target[3] : 0;
      ['一','二','三','四','五','六','日'].forEach(function (label, index) { var node = shape(svg, 'text', { x: 35 + index * 29, y: 23, fill: 'currentColor', opacity: .55, 'font-size': 9, 'text-anchor': 'middle' }); node.textContent = label; });
      for (var day = 1; day <= count; day++) { var index = offset + day - 1, x = 35 + index % 7 * 29, y = 44 + Math.floor(index / 7) * 26; if (day === date.getDate()) { dot(svg, x, y, 13, 13, glow); shape(svg, 'circle', { cx: x, cy: y, r: 12, fill: glass, stroke: '#f4d8ae', 'stroke-width': 1 }); } var label = shape(svg, 'text', { x: x, y: y + 3.5, fill: 'currentColor', opacity: day < date.getDate() ? .55 : 1, 'font-size': 11, 'font-family': 'inherit', 'text-anchor': 'middle' }); set(label, day); if (day === targetDay) dot(svg, x, y + 10, 2, 1.5, '#b3ebcd'); }
      if (work.tasks && work.tasks.items.length) {
        var todayIndex = offset + date.getDate() - 1, cx = 35 + todayIndex % 7 * 29, cy = 44 + Math.floor(todayIndex / 7) * 26;
        var completed = work.tasks.items.filter(function(item){return item.done;}).length;
        if (completed) shape(svg, 'circle', {cx:cx,cy:cy,r:12,fill:'none',stroke:'#b3ebcd','stroke-width':2,pathLength:100,'stroke-dasharray':(100*completed/work.tasks.items.length)+' 100',transform:'rotate(-90 '+cx+' '+cy+')'});
      }
      set(artCaptions[id], (date.getMonth() + 1) + ' / ' + date.getDate());
    } else if (id === 'tide') {
      dot(svg, 122, 153, 94, 11, glow);
      for (var ribbon = 1; ribbon <= 4; ribbon++) {
        var shift = (ribbon - 1) * 17, wobble = Math.sin((artStill() ? 0 : restElapsed() / 1000) * Math.PI / 4 + ribbon * .45) * 15, ribbonPaint = gradient('ribbon' + ribbon, [[0, '#f9f9ff', .6], [.26, ribbon % 2 === 0 ? '#dcbeee' : color, .38], [.5, '#94c9eb', .12], [.78, '#e5f0ff', .8], [1, '#c4b2ed', .4]]);
        var curve = 'M18,' + (63 + shift) + 'C81,' + (17 + shift + wobble) + ' 154,' + (111 + shift - wobble) + ' 224,' + (44 + shift);
        path(curve + 'L224,' + (62 + shift) + 'C154,' + (131 + shift - wobble) + ' 81,' + (37 + shift + wobble) + ' 18,' + (83 + shift) + 'Z', ribbonPaint, '#ecf7ff44', .5); path(curve, 'none', prism, 1.1);
      }
    } else if (id === 'orrery') {
      dot(svg, 122, 150, 70, 12, glow); shape(svg, 'ellipse', { cx: 122, cy: 145, rx: 55, ry: 10, fill: glass, stroke: '#f5f7ff', 'stroke-opacity': .45, 'stroke-width': .7 });
      stroke(svg, 122, 138, 122, 92, '#f3debfaa', 3); stroke(svg, 121, 137, 121, 94, '#fff8e5aa', .7);
      for (var ring = 1; ring <= 3; ring++) shape(svg, 'ellipse', { cx: 122, cy: 87, rx: 43 + ring * 13, ry: 20 + ring * 5, fill: 'none', stroke: prism, 'stroke-width': ring === 2 ? 1.5 : .8, transform: 'rotate(' + (-34 + ring * 21) + ' 122 87)' });
      dot(svg, 122, 87, 34, 34, glow); sphere(122, 87, 19, warm);
      for (ring = 1; ring <= 3; ring++) { var rx = 43 + ring * 13, ry = 20 + ring * 5, tilt = (-34 + ring * 21) * Math.PI / 180, total = work.focus ? work.focus.total : 1500, elapsed = Math.max(0, total - (focusRemaining() == null ? total : focusRemaining() / 1000)), period = ring === 1 ? 60 : ring === 2 ? 300 : Math.max(1, total), a = ring * 2.15 + elapsed / period * Math.PI * 2, ox = Math.cos(a) * rx, oy = Math.sin(a) * ry; sphere(122 + ox * Math.cos(tilt) - oy * Math.sin(tilt), 87 + ox * Math.sin(tilt) + oy * Math.cos(tilt), 5 + ring * 1.3, [cool, rose, warm][ring - 1]); }
      [[49, 50], [194, 45], [181, 126]].forEach(function (p) { stroke(svg, p[0] - 2, p[1], p[0] + 2, p[1], '#fff8e9aa', .6); stroke(svg, p[0], p[1] - 2, p[0], p[1] + 2, '#fff8e9aa', .6); });
    } else if (id === 'bonsai') {
      dot(svg, 122, 157, 71, 14, glow);
      var tasks = work.tasks && work.tasks.items || [], done = tasks.filter(function (item) { return item.done; }).length, count = Math.max(8, Math.min(12, tasks.length)), complete = tasks.length ? done / tasks.length : 0;
      var leaves = [[102,72,-1,117,108],[133,64,1,121,99],[111,50,-1,122,84],[129,39,1,124,68],[90,89,-1,117,119],[153,85,1,125,114],[143,105,1,122,133],[106,111,-1,119,135],[86,60,-1,116,96],[150,45,1,124,80],[116,29,-1,125,56],[162,104,1,129,129]];
      var leaf = gradient('leaf', [[0, '#f1fff8', .82], [.28, '#a5e4cf', .43], [.65, '#5faba7', .22], [1, '#d5fbe6', .76]]), leafDone = gradient('leaf-done', [[0, '#fff9d3', .96], [.35, '#f1d397', .7], [.7, '#a9c5b1', .25], [1, '#ffefbe', .86]]);
      path('M117,151C135,117 110,92 125,53', 'none', prism, 3.2);
      for (var leafIndex = 1; leafIndex <= count; leafIndex++) {
        var branch = leaves[leafIndex - 1], side = branch[2], bx = branch[3], by = branch[4], settle = artStill() ? 0 : Math.max(0, 1 - (performance.now() - leafChangedAt) / 1200), sway = Math.sin((1 - settle) * 12 + leafIndex * .7) * settle * 3, lx = branch[0] + sway, ly = branch[1], paint = leafIndex <= Math.round(complete * count) ? leafDone : work.tasks ? leaf : quiet;
        path('M' + bx + ',' + by + 'C' + (bx + side * 9) + ',' + (by - 2) + ' ' + (lx - side * 5) + ',' + (ly + 10) + ' ' + lx + ',' + ly, 'none', prism, 1.1);
        path('M' + lx + ',' + ly + 'C' + (lx + side * 2) + ',' + (ly - 19) + ' ' + (lx + side * 17) + ',' + (ly - 22) + ' ' + (lx + side * 24) + ',' + (ly - 15) + 'C' + (lx + side * 26) + ',' + (ly - 1) + ' ' + (lx + side * 11) + ',' + (ly + 3) + ' ' + lx + ',' + ly + 'Z', paint, '#e4fff096', .6); stroke(svg, lx, ly, lx + side * 20, ly - 13, '#f2fff496', .6);
      }
      path('M87,145H157L151,166C137,173 108,173 93,166Z', glass, prism, .9); shape(svg, 'ellipse', { cx: 122, cy: 145, rx: 35, ry: 6, fill: glass, stroke: '#f3fcffaa', 'stroke-width': .7 });
    }
    artParents.forEach(function (parent) { while (parent._artChildren.length > parent._artCursor) parent._artChildren.pop().remove(); parent._artCursor = null; }); artParents = null;
  }
  function describeArt(id) {
    var label = '', detail = '', action = workActions[id], seconds, days, items, pending;
    if (id === 'hourglass') { label = '留一点专注'; detail = sand.running ? '正在计时 · 切页后继续' : sand.remaining === 0 ? '慢慢回到手边的事' : '选好时长，慢慢开始'; set(sandToggle, sand.running ? '暂停' : sand.remaining < preferences.sandMinutes * 60000 && sand.remaining > 0 ? '继续' : '开始'); sandPresets.forEach(function (button) { button.setAttribute('aria-pressed', String(Number(button.value) === preferences.sandMinutes)); }); }
    if (id === 'sunpath') { label = sunSnapshot && sunSnapshot.location || '今天的天光'; var progress = sunPosition(sunSnapshot); if (progress == null) detail = '天气待同步'; else { var sunset = Date.parse(sunSnapshot.sunset + ':00Z') - sunSnapshot.utcOffsetSeconds * 1000, sunrise = Date.parse(sunSnapshot.sunrise + ':00Z') - sunSnapshot.utcOffsetSeconds * 1000, minutes = Math.ceil((sunset - Date.now()) / 60000); detail = Date.now() < sunrise ? '日出 ' + sunSnapshot.sunrise.slice(11) : minutes > 0 ? '日落还有 ' + Math.floor(minutes / 60) + '时' + minutes % 60 + '分' : '日光已落 · 休息一下'; } set(action, '查看天气'); }
    if (id === 'yeardots') { label = new Date().getFullYear() + ' · 今天'; days = work.tasks && countdownDays(work.tasks.countdownDate); detail = !work.tasks ? '允许共享待办后显示倒数日' : days == null ? '尚未设置倒数日' : work.tasks.countdownTitle + ' · ' + (days === 0 ? '就是今天' : days > 0 ? '还有 ' + days + ' 天' : '已过 ' + Math.abs(days) + ' 天'); action.disabled = !work.tasks; if(work.tasks && work.tasks.items.length) detail += ' · 待办已完成 ' + work.tasks.items.filter(function(item){return item.done;}).length + '/' + work.tasks.items.length; }
    if (id === 'tide') { var elapsed = restElapsed(), phase = Math.floor(elapsed / 1000) % 8; label = '给自己一分钟'; set(artCaptions[id], elapsedClock(Math.ceil((60000 - elapsed) / 1000) * 1000)); detail = elapsed >= 60000 ? '慢慢回到手边的事' : rest.running ? phase < 4 ? '吸气  ' + (4 - phase) : '呼气  ' + (8 - phase) : '放松肩膀，跟随潮汐'; set(action, rest.running ? '暂停' : elapsed > 0 && elapsed < 60000 ? '继续' : '休息 1 分钟'); }
    if (id === 'orrery') { seconds = focusRemaining(); label = '只做眼前这一件'; set(artCaptions[id], seconds == null ? '--:--' : elapsedClock(Math.ceil(seconds / 1000) * 1000)); detail = seconds == null ? '电脑专注待同步' : '已完成 ' + (work.focus.completed || 0) + ' 轮专注'; set(action, work.focus && work.focus.deadline > 0 ? '暂停' : '开始专注'); action.disabled = !work.control || seconds == null; document.getElementById('widget-orrery-reset').disabled = action.disabled; }
    if (id === 'bonsai') { items = work.tasks && work.tasks.items || []; pending = items.filter(function (item) { return !item.done; }); label = '手边的事'; set(artCaptions[id], !work.tasks ? '小小待办' : pending.length ? pending.length + ' 件待办' : items.length ? '都做好了' : '暂无待办'); detail = !work.tasks ? '允许共享待办后显示' : pending.length ? pending[0].text : '留一点空白给自己'; set(workLabels.bonsai, label); action.disabled = !work.tasks; var progress = document.getElementById('widget-bonsai-progress'); set(progress, work.tasks && items.length ? (items.length - pending.length) + ' / ' + items.length + ' 已完成' : ''); }
    set(workLabels[id], label); set(workDetails[id], detail);
  }
  function artStill() { return document.body.dataset.reduced === 'true' || !!(reduced && reduced.matches); }
  function movingArt() {
    if (!activePage() || artStill()) return [];
    return ['hourglass', 'tide', 'orrery', 'bonsai'].filter(function (id) {
      return shown(id) && (id === 'hourglass' ? sand.running && sandRemaining() > 0 : id === 'tide' ? rest.running && restElapsed() < 60000 : id === 'orrery' ? work.focus && work.focus.available && work.focus.deadline > 0 && focusRemaining() > 0 : performance.now() - leafChangedAt < 1200);
    });
  }
  function syncArtMotion() {
    if (leafChangedAt !== -Infinity && performance.now() - leafChangedAt >= 1200) { leafChangedAt = -Infinity; drawArt('bonsai'); }
    var moving = movingArt();
    if (!moving.length) { if (artFrame && window.cancelAnimationFrame) window.cancelAnimationFrame(artFrame); artFrame = 0; return; }
    if (artFrame || !window.requestAnimationFrame) return;
    artFrame = window.requestAnimationFrame(function () {
      artFrame = 0; movingArt().forEach(drawArt); syncArtMotion();
    });
  }
  ['hourglass', 'sunpath', 'yeardots', 'tide', 'orrery', 'bonsai'].forEach(function (id) {
    var panel = card(id, ''); panel.classList.add('work-sculpture'); var scene = el('div', 'widget-work-scene'), svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg'); svg.setAttribute('viewBox', '0 0 244 196'); svg.setAttribute('aria-hidden', 'true'); svg.classList.add('widget-art'); scene.appendChild(svg); artNodes[id] = svg;
    workLabels[id] = el('p', 'widget-work-label', ''); scene.appendChild(workLabels[id]); artCaptions[id] = status('widget-' + id + '-status', scene, ''); panel.appendChild(scene); workDetails[id] = status('widget-' + id + '-detail', panel, ''); workDetails[id].classList.add('widget-work-detail');
    if (id === 'bonsai') scene.appendChild(named(el('span', 'widget-work-progress', ''), 'widget-bonsai-progress'));
    if (id === 'hourglass') { var presets = el('div', 'widget-presets'); presets.setAttribute('aria-label', '沙漏快捷分钟'); panel.appendChild(presets); [5, 15, 25, 50].forEach(function (minutes) { var b = button('widget-hourglass-preset-' + minutes, minutes + ' 分', function () { preferences.sandMinutes = minutes; sand.running = false; sand.remaining = minutes * 60000; artSecond = -1; save(); updateTools(); }); b.value = String(minutes); presets.appendChild(b); sandPresets.push(b); }); }
    var actions = controls(panel);
    workActions[id] = button('widget-' + id + '-toggle', '打开', function () {
      if (id === 'hourglass') { if (sand.running) pauseOne('hourglass'); else if (activePage() && shown(id)) { if (sand.remaining <= 0) sand.remaining = preferences.sandMinutes * 60000; sand.started = Date.now(); sand.running = true; } }
      else if (id === 'tide') { if (rest.running) pauseOne('tide'); else if (activePage() && shown(id)) { if (rest.elapsed >= 60000) rest.elapsed = 0; rest.started = performance.now(); rest.running = true; } }
      else if (id === 'orrery') { if (work.control && work.focus && work.focus.available) emit('companion-widget-action', { action: 'focus-toggle' }); }
      else if (id === 'sunpath') emit('companion-widget-navigate', { page: 'home' });
      else emit('companion-widget-navigate', { page: 'work' });
      artSecond = -1; updateTools();
    }); actions.appendChild(workActions[id]); if (id === 'hourglass') sandToggle = workActions[id];
    if (id === 'hourglass' || id === 'tide' || id === 'orrery') actions.appendChild(button('widget-' + id + '-reset', '重置', function () { if (id === 'hourglass') { sand.running = false; sand.remaining = preferences.sandMinutes * 60000; } else if (id === 'tide') { rest.running = false; rest.elapsed = 0; } else if (work.control && work.focus && work.focus.available) emit('companion-widget-action', { action: 'focus-reset' }); artSecond = -1; updateTools(); }));
    if (id === 'yeardots') set(workActions[id], '打开倒数日'); if (id === 'bonsai') set(workActions[id], '打开待办');
    drawArt(id); describeArt(id);
  });
  function updateArt() {
    var still = artStill(); if (artReduced !== still) { artReduced = still; artSecond = -1; workKeys = {}; }
    syncArtMotion();
    var second = Math.floor(performance.now() / 1000); if (second === artSecond) return; artSecond = second;
    if (sand.running && sandRemaining() <= 0) { sand.running = false; sand.remaining = 0; }
    if (rest.running && restElapsed() >= 60000) { rest.running = false; rest.elapsed = 60000; }
    ['hourglass', 'sunpath', 'yeardots', 'tide', 'orrery', 'bonsai'].forEach(function (id) { if (!shown(id)) return; var key = id === 'hourglass' ? Math.ceil(sandRemaining() / 1000) + ':' + sand.running + ':' + preferences.sandMinutes : id === 'tide' ? Math.floor(restElapsed() / 1000) + ':' + rest.running : id === 'orrery' ? Math.ceil((focusRemaining() || 0) / 1000) : id === 'sunpath' ? Math.floor(Date.now() / 60000) + JSON.stringify(sunSnapshot) : id === 'yeardots' ? new Date().toDateString() + ':' + (work.tasks && work.tasks.countdownDate || '') + ':' + leafSignature : leafSignature; if (workKeys[id] !== key) { workKeys[id] = key; drawArt(id); } describeArt(id); });
    syncArtMotion();
  }

  var settings = named(el('section', 'widget-settings glass'), 'widget-settings'); settings.hidden = true; settings.setAttribute('aria-label', '小组件显示与顺序');
  var settingsHeading = el('div', 'section-heading'); settingsHeading.appendChild(el('h2', '', '小组件设置')); settingsHeading.appendChild(button('widget-settings-close', '完成', function () { settings.hidden = true; customize.setAttribute('aria-expanded', 'false'); })); settings.appendChild(settingsHeading);
  var settingsList = el('div', 'widget-settings-list'); settings.appendChild(settingsList); var settingsNote = el('p', 'muted', '仅保存小组件偏好，不保存电脑内容。'); settings.appendChild(settingsNote); gallery.parentNode.insertBefore(settings, gallery);
  var empty = el('p', 'widget-empty', '未显示小组件，可点“整理小组件”选择。'); gallery.appendChild(empty);
  customize.setAttribute('aria-controls', 'widget-settings'); customize.setAttribute('aria-expanded', 'false'); customize.onclick = function () { settings.hidden = !settings.hidden; customize.setAttribute('aria-expanded', String(!settings.hidden)); };
  function move(id, direction, control) { var at = preferences.order.indexOf(id), target = at + direction; if (target < 0 || target >= ids.length) return; preferences.order.splice(at, 1); preferences.order.splice(target, 0, id); applyOrder(); save(); if (control.focus) control.focus(); }
  ids.forEach(function (id) {
    var row = el('div', 'widget-setting-row'), label = el('label'), checkbox = el('input'); checkbox.type = 'checkbox'; checkbox.checked = shown(id); checkbox.setAttribute('aria-label', '显示' + titles[id]); label.appendChild(checkbox); label.appendChild(el('span', '', titles[id])); row.appendChild(label);
    checkbox.onchange = function () { preferences.visible = ids.filter(function (key) { return key === id ? checkbox.checked : shown(key); }); if (!checkbox.checked) pauseOne(id, '卡片已收起，返回后可继续'); applyOrder(); save(); };
    var up = button('widget-up-' + id, '', function () { move(id, -1, up); }), down = button('widget-down-' + id, '', function () { move(id, 1, down); }); up.setAttribute('aria-label', '上移' + titles[id]); down.setAttribute('aria-label', '下移' + titles[id]); up.appendChild(icon('up')); down.appendChild(icon('down')); row.appendChild(up); row.appendChild(down); rows[id] = { node: row, checkbox: checkbox, up: up, down: down };
  });
  function applyOrder() { preferences.order.forEach(function (id, index) { cards[id].hidden = !shown(id); gallery.appendChild(cards[id]); settingsList.appendChild(rows[id].node); rows[id].checkbox.checked = shown(id); rows[id].up.disabled = index === 0; rows[id].down.disabled = index === ids.length - 1; }); empty.hidden = preferences.visible.length !== 0; gallery.appendChild(empty); updateTools(); }
  function pauseOne(id, reason) {
    if (id === 'timer' && timer.running) { timer.remaining = timerRemaining(); timer.running = false; set(timerStatus, reason); }
    if (id === 'stopwatch' && stopwatch.running) { stopwatch.elapsed = stopwatchElapsed(); stopwatch.running = false; set(watchStatus, reason); }
    if (id === 'breath' && breath.running) { breath.running = false; set(breathStatus, reason); }
    if (id === 'tide' && rest.running) { rest.elapsed = restElapsed(); rest.running = false; artSecond = -1; }
    if (id === 'hourglass' && sand.running) { sand.remaining = sandRemaining(); sand.running = false; artSecond = -1; }
    syncArtMotion();
  }
  function pauseTools(reason) { pauseOne('breath', reason); pauseOne('tide', reason); if (artFrame && window.cancelAnimationFrame) window.cancelAnimationFrame(artFrame); artFrame = 0; updateTools(); }
  function updateTools() {
    updateArt();
    var remaining = timerRemaining(); if (timer.running && remaining <= 0) { timer.running = false; timer.remaining = 0; set(timerStatus, '时间到了'); }
    set(timerTime, elapsedClock(Math.ceil(remaining / 1000) * 1000)); set(timerToggle, timer.running ? '暂停' : timer.remaining < preferences.duration * 1000 && timer.remaining > 0 ? '继续' : '开始'); timerMinutes.disabled = timer.running;
    timerPresetButtons.forEach(function (preset) { preset.disabled = timer.running; preset.setAttribute('aria-pressed', String(Number(preset.value) * 60 === preferences.duration)); });
    set(watchTime, elapsedClock(stopwatchElapsed())); set(watchToggle, stopwatch.running ? '暂停' : stopwatch.elapsed > 0 ? '继续' : '开始');
    watchLap.disabled = !stopwatch.running || !activePage() || !shown('stopwatch');
    var still = document.body.dataset.reduced === 'true' || !!(reduced && reduced.matches); orb.dataset.running = String(breath.running && !still); set(breathToggle, breath.running ? '暂停练习' : '开始练习');
    if (breath.running) set(breathStatus, still ? '减少动态 · 吸气 4 秒，呼气 4 秒' : Math.floor((performance.now() - breath.started) / 1000) % 8 < 4 ? '吸气' : '呼气');
    var second = Math.floor(Date.now() / 1000); if (second !== lastSecond) { lastSecond = second; var date = new Date(); set(worldTime, clockFormat ? clockFormat.format(date) : '—'); set(worldDate, dateFormat ? dateFormat.format(date) : '此浏览器不支持所选时区'); if (dateInput) updateDateDistance(); }
  }
  function syncTicker() { if (activePage()) { updateTools(); if (!tick) tick = setInterval(updateTools, 250); } else { if (tick) clearInterval(tick); tick = 0; pauseTools('离开页面已暂停，点击继续'); } }
  function weatherName(code) { if (!Number.isFinite(code)) return '天气状况未提供'; if (code === 0) return '晴'; if (code === 1) return '晴间少云'; if (code === 2) return '多云'; if (code === 3) return '阴'; if (code === 45 || code === 48) return '雾'; if (code >= 51 && code <= 67) return '雨'; if (code >= 71 && code <= 77 || code === 85 || code === 86) return '雪'; if (code >= 80 && code <= 82) return '阵雨'; if (code >= 95 && code <= 99) return '雷雨'; return '天气状况未提供'; }
  function weatherTimestamp(value) { if (!Number.isFinite(value) || value <= 0) return '数据时间未提供'; var date = new Date(value), today = date.toDateString() === new Date().toDateString(); return date.toLocaleString('zh-CN', { month: today ? undefined : '2-digit', day: today ? undefined : '2-digit', hour: '2-digit', minute: '2-digit', hour12: false }) + ' 数据'; }
  function render(state, permission) {
    if (document.hidden) { clear(); return; }
    // Anonymous one-second UI refreshes must not stop tools that the user
    // deliberately started without a connection. Auth loss pauses only once.
    if (!state) { if (authenticated) clear(); else syncTicker(); return; }
    authenticated = true; immerseButton.disabled = false; set(immerseStatus, '');
    var weather = state.weather || {}, data = weather.data || {}, key = JSON.stringify([weather.available, weather.updatedAt, weather.reason, data.temp, data.code, data.high, data.low, data.feelsLike, data.location, data.source, data.kind, data.stale, data.reason, new Date().toDateString()]);
    var nextSun = weather.available === true ? { sunrise: data.sunrise, sunset: data.sunset, fetchedAt: data.fetchedAt, utcOffsetSeconds: data.utcOffsetSeconds, stale: data.stale, temp: data.temp, location: data.location } : null;
    if (JSON.stringify(nextSun) !== JSON.stringify(sunSnapshot)) { sunSnapshot = nextSun; drawArt('sunpath'); }
    if (key !== weatherKey) {
      weatherKey = key; var available = weather.available === true && Number.isFinite(data.temp), details = [weatherName(data.code)], stamp = [];
      if (Number.isFinite(data.feelsLike)) details.push('体感 ' + Math.round(data.feelsLike) + '°'); if (Number.isFinite(data.low) && Number.isFinite(data.high)) details.push(Math.round(data.low) + '° / ' + Math.round(data.high) + '°');
      if (data.stale === true) stamp.push('上次天气'); stamp.push(weatherTimestamp(weather.updatedAt)); if (data.source) stamp.push(data.source); if (data.kind === 'model') stamp.push('数值预报'); else if (data.kind === 'observation') stamp.push('观测数据');
      set(weatherLocation, data.location || '电脑天气位置'); set(temperature, available ? Math.round(data.temp) + '°' : '—'); set(weatherSummary, available ? details.join(' · ') : weather.reason || '请在电脑中检查天气位置与网络。'); set(weatherStamp, available ? stamp.join(' · ') : '等待电脑提供有效数据');
      set(weatherReason, available && data.stale === true ? weather.reason || data.reason || '正在等待新的天气数据。' : ''); weatherReason.hidden = !available || data.stale !== true; weatherCard.dataset.state = !available ? 'unavailable' : data.stale === true ? 'stale' : 'ready';
    }
    var allowed = !!(permission && permission.tasks), tasks = state.tasks || {}, availableTasks = allowed && tasks.available === true, items = availableTasks && Array.isArray(tasks.items) ? tasks.items.filter(function (item) { return !item.done && typeof item.text === 'string'; }) : [];
    key = JSON.stringify([allowed, availableTasks, items.map(function (item) { return [item.id, item.text]; })]);
    if (key !== tasksKey) { tasksKey = key; taskList.textContent = ''; set(taskCount, availableTasks ? items.length + ' 件未完成' : '—'); items.slice(0, 3).forEach(function (item) { taskList.appendChild(el('li', '', item.text)); }); set(taskStatus, !allowed ? '请在电脑允许此设备共享待办' : !availableTasks ? '电脑待办暂不可用' : items.length ? items.length > 3 ? '还有 ' + (items.length - 3) + ' 件待办' : '' : '没有未完成待办'); }
    taskOpen.disabled = !availableTasks; work.tasks = availableTasks ? { items: tasks.items || [], countdownTitle: tasks.countdownTitle || '', countdownDate: tasks.countdownDate || '' } : null;
    var nextLeaves = JSON.stringify(work.tasks && work.tasks.items.map(function (item) { return [item.id, !!item.done]; })); if (leafSignature !== nextLeaves) { leafChangedAt = leafSignature && work.tasks ? performance.now() : -Infinity; leafSignature = nextLeaves; }
    work.focus = state.focus && state.focus.available ? Object.assign({}, state.focus, { offset: Number.isFinite(state.serverTime) ? state.serverTime - Date.now() : 0 }) : null; work.control = !!(permission && permission.control); artSecond = -1; syncTicker();
  }
  function clear() { authenticated = false; work.tasks = null; work.focus = null; work.control = false; leafSignature = ''; leafChangedAt = -Infinity; artSecond = -1; ['yeardots','orrery','bonsai'].forEach(function (id) { drawArt(id); describeArt(id); }); weatherKey = tasksKey = ''; sunSnapshot = null; drawArt('sunpath'); set(weatherLocation, '电脑天气位置'); set(temperature, '—'); set(weatherSummary, '连接电脑后同步天气'); set(weatherStamp, '尚未连接'); set(weatherReason, ''); weatherReason.hidden = true; weatherCard.dataset.state = 'unavailable'; set(taskCount, '—'); taskList.textContent = ''; set(taskStatus, '连接并允许共享待办后显示'); taskOpen.disabled = true; immerseButton.disabled = true; set(immerseStatus, '连接电脑后可用'); pauseTools('已暂停，点击继续'); syncTicker(); }
  function lifecycle() { if (document.hidden) clear(); syncTicker(); }
  configureClock(); applyOrder(); syncTicker(); if (!storageAvailable) set(settingsNote, '当前浏览器无法保存偏好，本次调整仍可使用。');
  document.addEventListener('visibilitychange', lifecycle);
  function pause() { pauseTools('离开页面已暂停，点击继续'); if (tick) clearInterval(tick); tick = 0; if (artFrame && window.cancelAnimationFrame) window.cancelAnimationFrame(artFrame); artFrame = 0; }
  window.addEventListener('pagehide', function () { pageHidden = true; clear(); pause(); });
  window.addEventListener('pageshow', function () { pageHidden = false; syncTicker(); });
  if (typeof MutationObserver === 'function') new MutationObserver(syncTicker).observe(document.body, { attributes: true, attributeFilter: ['data-page', 'data-reduced'] });
  if (reduced) { if (reduced.addEventListener) reduced.addEventListener('change', updateTools); else if (reduced.addListener) reduced.addListener(updateTools); }
  window.PrismCompanionWidgets = { render: render, clear: clear, pause: pause };
}());
