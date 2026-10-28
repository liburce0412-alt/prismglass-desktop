/* Local tools plus explicitly shared computer summaries. No offline actions. */
(function () {
  'use strict';
  var gallery = document.getElementById('widget-gallery'), customize = document.getElementById('widget-customize');
  if (!gallery || !customize) return;
  var ids = ['timer', 'stopwatch', 'calculator', 'date', 'world', 'breath', 'weather', 'tasks', 'immerse'];
  var titles = { timer: '留一小段时间', stopwatch: '记录这一刻', calculator: '随手算一算', date: '与那一天的距离', world: '另一座城市', breath: '慢慢呼吸', weather: '窗外天气', tasks: '手边的事', immerse: '让风景陪着你' };
  var zones = [['Asia/Shanghai', '北京'], ['Asia/Tokyo', '东京'], ['Europe/London', '伦敦'], ['Europe/Paris', '巴黎'], ['America/New_York', '纽约'], ['Australia/Sydney', '悉尼'], ['UTC', '世界标准时间']];
  var storageKey = 'prismglass.companion.widgets.v1', preferences = { version: 1, order: ids.slice(), visible: ids.slice(), duration: 180, zone: 'Asia/Shanghai' }, storageAvailable = true;
  try {
    var saved = JSON.parse(window.localStorage.getItem(storageKey) || 'null');
    if (saved && saved.version === 1) {
      if (Array.isArray(saved.order)) preferences.order = saved.order.filter(function (id, at, list) { return ids.indexOf(id) >= 0 && list.indexOf(id) === at; }).concat(ids.filter(function (id) { return saved.order.indexOf(id) < 0; }));
      if (Array.isArray(saved.visible)) preferences.visible = ids.filter(function (id) { return saved.visible.indexOf(id) >= 0; });
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
    var svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg'); svg.setAttribute('viewBox', '0 0 24 24'); svg.setAttribute('aria-hidden', 'true'); svg.classList.add('ui-icon');
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
  function save() { try { window.localStorage.setItem(storageKey, JSON.stringify(preferences)); storageAvailable = true; } catch (error) { storageAvailable = false; } set(settingsNote, storageAvailable ? '只保存卡片顺序、可见性、时长和时区；不会保存电脑内容。' : '当前浏览器无法保存偏好，本次调整仍可使用。'); }
  function activePage() { return !pageHidden && !document.hidden && document.body.dataset.page === 'widgets'; }
  function shown(id) { return preferences.visible.indexOf(id) >= 0; }
  function elapsedClock(ms) { var seconds = Math.max(0, Math.floor(ms / 1000)), hours = Math.floor(seconds / 3600), minutes = Math.floor(seconds % 3600 / 60); return (hours ? hours + ':' : '') + String(minutes).padStart(2, '0') + ':' + String(seconds % 60).padStart(2, '0'); }
  function timerRemaining() { return Math.max(0, timer.remaining - (timer.running ? performance.now() - timer.started : 0)); }
  function stopwatchElapsed() { return stopwatch.elapsed + (stopwatch.running ? performance.now() - stopwatch.started : 0); }

  var timerCard = card('timer', '一杯茶，一次拉伸，或一个短暂的停顿。'), timerTime = named(el('strong', 'widget-number', elapsedClock(timer.remaining)), 'widget-timer'), timerLabel = el('label', 'widget-duration-label', '分钟'), timerMinutes = named(el('input'), 'widget-timer-minutes');
  timerCard.appendChild(timerTime); timerMinutes.type = 'number'; timerMinutes.min = '1'; timerMinutes.max = '180'; timerMinutes.step = '1'; timerMinutes.inputMode = 'numeric'; timerMinutes.value = String(preferences.duration / 60); timerLabel.appendChild(timerMinutes); timerCard.appendChild(timerLabel);
  var timerPresets = el('div', 'widget-presets'); timerPresets.setAttribute('role', 'group'); timerPresets.setAttribute('aria-label', '快捷计时时长'); timerCard.appendChild(timerPresets);
  var timerPresetButtons = [1, 3, 5, 15].map(function (minutes) { var preset = button('widget-timer-preset-' + minutes, minutes + ' 分钟', function () { if (timer.running) return; timerMinutes.value = String(minutes); timerMinutes.onchange(); }); preset.value = String(minutes); timerPresets.appendChild(preset); return preset; });
  var timerStatus = status('widget-timer-status', timerCard, '仅在此页前台计时'), timerActions = controls(timerCard);
  var timerToggle = button('widget-timer-toggle', '开始', function () {
    if (timer.running) pauseOne('timer', '已暂停，可继续');
    else if (activePage() && shown('timer')) { if (timer.remaining <= 0) timer.remaining = preferences.duration * 1000; timer.started = performance.now(); timer.running = true; set(timerStatus, '计时中 · 离开此页会暂停'); }
    updateTools(); syncTicker();
  });
  var timerReset = button('widget-timer-reset', '重置', function () { timer.running = false; timer.remaining = preferences.duration * 1000; set(timerStatus, '已重置'); updateTools(); }); timerActions.appendChild(timerToggle); timerActions.appendChild(timerReset);
  timerMinutes.onchange = function () { var value = Number(timerMinutes.value); if (timer.running || !Number.isInteger(value) || value < 1 || value > 180) { timerMinutes.value = String(preferences.duration / 60); set(timerStatus, '请输入 1 到 180 的整数分钟'); return; } preferences.duration = value * 60; timer.remaining = preferences.duration * 1000; save(); set(timerStatus, '时长已更新，点击开始'); updateTools(); };

  var watchCard = card('stopwatch', '给投入的时间，留一个准确的刻度。'), watchTime = named(el('strong', 'widget-number', '00:00'), 'widget-stopwatch'); watchCard.appendChild(watchTime);
  var watchStatus = status('widget-stopwatch-status', watchCard, '页面隐藏时暂停，不在后台继续计时'), watchActions = controls(watchCard);
  var watchToggle = button('widget-stopwatch-toggle', '开始', function () { if (stopwatch.running) pauseOne('stopwatch', '已暂停，可继续'); else if (activePage() && shown('stopwatch')) { stopwatch.running = true; stopwatch.started = performance.now(); set(watchStatus, '计时中 · 离开此页会暂停'); } updateTools(); syncTicker(); });
  var lapCount = 0, lapAt = 0, watchLaps = named(el('ol', 'widget-laps'), 'widget-stopwatch-laps'); watchLaps.hidden = true; watchLaps.setAttribute('aria-label', '最近五圈');
  var watchLap = button('widget-stopwatch-lap', '记一圈', function () {
    if (!stopwatch.running || !activePage() || !shown('stopwatch')) return;
    var elapsed = stopwatchElapsed(), lapTime = elapsedClock(elapsed - lapAt), row = el('li'); lapAt = elapsed; lapCount++;
    row.appendChild(el('span', '', '第 ' + lapCount + ' 圈')); row.appendChild(el('strong', '', lapTime)); watchLaps.appendChild(row);
    if (watchLaps.children.length > 5) watchLaps.children[0].remove(); watchLaps.hidden = false; set(watchStatus, '第 ' + lapCount + ' 圈 · 本圈 ' + lapTime + '，保留最近五圈');
  });
  watchActions.appendChild(watchToggle); watchActions.appendChild(watchLap); watchActions.appendChild(button('widget-stopwatch-reset', '归零', function () { stopwatch.running = false; stopwatch.elapsed = 0; lapCount = 0; lapAt = 0; watchLaps.textContent = ''; watchLaps.hidden = true; set(watchStatus, '已归零'); updateTools(); })); watchCard.appendChild(watchLaps);

  var worldCard = card('world', '从这块屏幕，望向另一个时区。'), worldTime = named(el('strong', 'widget-number', '—'), 'widget-world-time'), worldDate = el('p', 'widget-world-date'), worldZone = named(el('select'), 'widget-zone');
  worldZone.setAttribute('aria-label', '世界时钟城市'); zones.forEach(function (zone) { var option = el('option', '', zone[1]); option.value = zone[0]; worldZone.appendChild(option); }); worldZone.value = preferences.zone; worldCard.appendChild(worldTime); worldCard.appendChild(worldDate); worldCard.appendChild(worldZone);
  function configureClock() { try { clockFormat = new Intl.DateTimeFormat('zh-CN', { timeZone: preferences.zone, hour: '2-digit', minute: '2-digit', hour12: false }); dateFormat = new Intl.DateTimeFormat('zh-CN', { timeZone: preferences.zone, month: 'long', day: 'numeric', weekday: 'short' }); } catch (error) { clockFormat = dateFormat = null; } lastSecond = -1; }
  worldZone.onchange = function () { if (!zones.some(function (zone) { return zone[0] === worldZone.value; })) return; preferences.zone = worldZone.value; configureClock(); save(); updateTools(); };

  var breathCard = card('breath', '不用赶路。跟着一呼一吸，慢下来。'), orb = named(el('div', 'widget-breath-orb'), 'widget-breath-orb'); orb.setAttribute('aria-hidden', 'true'); orb.dataset.running = 'false'; breathCard.appendChild(orb);
  var breathStatus = status('widget-breath-status', breathCard, '吸气 4 秒 · 呼气 4 秒'), breathActions = controls(breathCard), breathToggle = button('widget-breath-toggle', '开始呼吸', function () { if (breath.running) pauseOne('breath', '已暂停，按自己的节奏呼吸'); else if (activePage() && shown('breath')) { breath.started = performance.now(); breath.running = true; } updateTools(); syncTicker(); }); breathActions.appendChild(breathToggle);

  var weatherCard = card('weather', '来自电脑正在使用的天气位置。'), temperature = named(el('strong', 'widget-number', '—'), 'widget-weather-temp'), weatherSummary = el('p', 'widget-summary', '连接电脑后同步天气'); weatherCard.appendChild(temperature); weatherCard.appendChild(weatherSummary); var weatherStamp = status('widget-weather-status', weatherCard, '尚未连接');
  var tasksCard = card('tasks', '下一件想完成的小事。'), taskCount = named(el('strong', 'widget-task-count', '—'), 'widget-task-count'), taskList = named(el('ul', 'widget-task-list'), 'widget-task-list'); tasksCard.appendChild(taskCount); tasksCard.appendChild(taskList); var taskStatus = status('widget-tasks-status', tasksCard, '连接并允许共享待办后显示');
  var taskOpen = button('widget-tasks-open', '打开工作页', function () { if (!taskOpen.disabled) emit('companion-widget-navigate', { page: 'work' }); }); taskOpen.disabled = true; controls(tasksCard).appendChild(taskOpen);
  var immerseCard = card('immerse', '收起控制，留一片安静的光。'); immerseCard.appendChild(el('div', 'widget-immersion-orbit')); var immerseStatus = status('widget-immerse-status', immerseCard, '连接电脑后可进入沉浸展示'), immerseButton = button('widget-immerse', '进入沉浸', function () { if (authenticated) emit('companion-widget-immerse'); }); immerseButton.disabled = true; controls(immerseCard).appendChild(immerseButton);

  var calculatorCard = card('calculator', '一个结果，不必再拿起手机。'), calculatorDisplay = named(el('output', 'widget-number calculator-display', '0'), 'widget-calculator-result'); calculatorDisplay.setAttribute('aria-live', 'polite'); calculatorCard.appendChild(calculatorDisplay);
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

  var dateCard = card('date', '看看期待的日子，还有多久。'), dateDistance = named(el('strong', 'widget-number', '—'), 'widget-date-distance'), dateInput = named(el('input'), 'widget-date-target'); dateInput.type = 'date'; dateInput.setAttribute('aria-label', '选择目标日期'); dateInput.min = '1900-01-01'; dateInput.max = '2100-12-31'; dateCard.appendChild(dateDistance); dateCard.appendChild(dateInput); var dateStatus = status('widget-date-status', dateCard, '选择一天，查看相隔天数');
  function updateDateDistance() {
    var match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(dateInput.value), target = match && new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])));
    if (!target || Number(match[1]) < 1900 || Number(match[1]) > 2100 || target.getUTCFullYear() !== Number(match[1]) || target.getUTCMonth() !== Number(match[2]) - 1 || target.getUTCDate() !== Number(match[3])) { set(dateDistance, '—'); set(dateStatus, dateInput.value ? '请选择有效日期' : '选择一天，查看相隔天数'); return; }
    var now = new Date(), days = Math.round((target.getTime() - Date.UTC(now.getFullYear(), now.getMonth(), now.getDate())) / 86400000);
    set(dateDistance, Math.abs(days) + ' 天'); set(dateStatus, days === 0 ? '就是今天' : days > 0 ? '距离那一天' : '已经过去');
  }
  dateInput.onchange = updateDateDistance;

  var settings = named(el('section', 'widget-settings glass'), 'widget-settings'); settings.hidden = true; settings.setAttribute('aria-label', '小组件显示与顺序');
  var settingsHeading = el('div', 'section-heading'); settingsHeading.appendChild(el('h2', '', '留下你常用的')); settingsHeading.appendChild(button('widget-settings-close', '完成', function () { settings.hidden = true; customize.setAttribute('aria-expanded', 'false'); })); settings.appendChild(settingsHeading);
  var settingsList = el('div', 'widget-settings-list'); settings.appendChild(settingsList); var settingsNote = el('p', 'muted', '只保存卡片顺序、可见性、时长和时区；不会保存电脑内容。'); settings.appendChild(settingsNote); gallery.parentNode.insertBefore(settings, gallery);
  var empty = el('p', 'widget-empty', '小组件已全部收起，可通过“自定义”重新选择。'); gallery.appendChild(empty);
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
  }
  function pauseTools(reason) { pauseOne('timer', reason); pauseOne('stopwatch', reason); pauseOne('breath', reason); updateTools(); }
  function updateTools() {
    var remaining = timerRemaining(); if (timer.running && remaining <= 0) { timer.running = false; timer.remaining = 0; set(timerStatus, '时间到了 · 已在页面前台完成'); }
    set(timerTime, elapsedClock(Math.ceil(remaining / 1000) * 1000)); set(timerToggle, timer.running ? '暂停' : timer.remaining < preferences.duration * 1000 && timer.remaining > 0 ? '继续' : '开始'); timerMinutes.disabled = timer.running;
    timerPresetButtons.forEach(function (preset) { preset.disabled = timer.running; preset.setAttribute('aria-pressed', String(Number(preset.value) * 60 === preferences.duration)); });
    set(watchTime, elapsedClock(stopwatchElapsed())); set(watchToggle, stopwatch.running ? '暂停' : stopwatch.elapsed > 0 ? '继续' : '开始');
    watchLap.disabled = !stopwatch.running || !activePage() || !shown('stopwatch');
    var still = document.body.dataset.reduced === 'true' || !!(reduced && reduced.matches); orb.dataset.running = String(breath.running && !still); set(breathToggle, breath.running ? '暂停呼吸' : '开始呼吸');
    if (breath.running) set(breathStatus, still ? '减少动态已开启 · 按自己的节奏呼吸' : Math.floor((performance.now() - breath.started) / 1000) % 8 < 4 ? '慢慢吸气' : '缓缓呼气');
    var second = Math.floor(Date.now() / 1000); if (second !== lastSecond) { lastSecond = second; var date = new Date(); set(worldTime, clockFormat ? clockFormat.format(date) : '—'); set(worldDate, dateFormat ? dateFormat.format(date) : '此浏览器不支持所选时区'); if (dateInput) updateDateDistance(); }
  }
  function syncTicker() { if (activePage()) { updateTools(); if (!tick) tick = setInterval(updateTools, 250); } else { if (tick) clearInterval(tick); tick = 0; pauseTools('离开页面已暂停，点击继续'); } }
  function weatherName(code) { if (code === 0) return '晴'; if (code <= 3) return '多云'; if (code <= 48) return '雾'; if (code >= 95) return '雷雨'; if (code >= 71 && code <= 77 || code >= 85 && code <= 86) return '雪'; return '雨'; }
  function render(state, permission) {
    if (document.hidden) { clear(); return; }
    // Anonymous one-second UI refreshes must not stop tools that the user
    // deliberately started without a connection. Auth loss pauses only once.
    if (!state) { if (authenticated) clear(); else syncTicker(); return; }
    authenticated = true; immerseButton.disabled = false; set(immerseStatus, '时钟、真实音乐与安静的光');
    var weather = state.weather || {}, data = weather.data || {}, key = JSON.stringify([weather.available, weather.updatedAt, data.temp, data.code, data.high, data.low]);
    if (key !== weatherKey) { weatherKey = key; var available = weather.available === true && Number.isFinite(data.temp); set(temperature, available ? Math.round(data.temp) + '°' : '—'); set(weatherSummary, available ? (Number.isFinite(data.code) ? weatherName(data.code) : '当前天气') + (Number.isFinite(data.low) && Number.isFinite(data.high) ? ' · ' + Math.round(data.low) + '° / ' + Math.round(data.high) + '°' : '') : '电脑暂时没有可用的天气数据'); set(weatherStamp, available && Number.isFinite(weather.updatedAt) ? new Date(weather.updatedAt).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' }) + ' 更新' : '等待下一次有效同步'); }
    var allowed = !!(permission && permission.tasks), tasks = state.tasks || {}, availableTasks = allowed && tasks.available === true, items = availableTasks && Array.isArray(tasks.items) ? tasks.items.filter(function (item) { return !item.done && typeof item.text === 'string'; }) : [];
    key = JSON.stringify([allowed, availableTasks, items.map(function (item) { return [item.id, item.text]; })]);
    if (key !== tasksKey) { tasksKey = key; taskList.textContent = ''; set(taskCount, availableTasks ? items.length + ' 件未完成' : '—'); items.slice(0, 3).forEach(function (item) { taskList.appendChild(el('li', '', item.text)); }); set(taskStatus, !allowed ? '请在电脑允许此设备共享待办' : !availableTasks ? '电脑待办暂不可用' : items.length ? items.length > 3 ? '还有 ' + (items.length - 3) + ' 件，去工作页看看' : '一件一件，慢慢来' : '此刻没有未完成事项'); }
    taskOpen.disabled = !availableTasks; syncTicker();
  }
  function clear() { authenticated = false; weatherKey = tasksKey = ''; set(temperature, '—'); set(weatherSummary, '连接电脑后同步天气'); set(weatherStamp, '尚未连接'); set(taskCount, '—'); taskList.textContent = ''; set(taskStatus, '连接并允许共享待办后显示'); taskOpen.disabled = true; immerseButton.disabled = true; set(immerseStatus, '连接电脑后可进入沉浸展示'); pauseTools('已暂停，点击继续'); syncTicker(); }
  function lifecycle() { if (document.hidden) clear(); syncTicker(); }
  configureClock(); applyOrder(); syncTicker(); if (!storageAvailable) set(settingsNote, '当前浏览器无法保存偏好，本次调整仍可使用。');
  document.addEventListener('visibilitychange', lifecycle);
  function pause() { pauseTools('离开页面已暂停，点击继续'); if (tick) clearInterval(tick); tick = 0; }
  window.addEventListener('pagehide', function () { pageHidden = true; clear(); pause(); });
  window.addEventListener('pageshow', function () { pageHidden = false; syncTicker(); });
  if (typeof MutationObserver === 'function') new MutationObserver(syncTicker).observe(document.body, { attributes: true, attributeFilter: ['data-page', 'data-reduced'] });
  if (reduced) { if (reduced.addEventListener) reduced.addEventListener('change', updateTools); else if (reduced.addListener) reduced.addListener(updateTools); }
  window.PrismCompanionWidgets = { render: render, clear: clear, pause: pause };
}());
