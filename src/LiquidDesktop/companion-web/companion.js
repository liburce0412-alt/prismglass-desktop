(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var session = null, sessionAt = 0, state = null, apps = null, epoch = 0, requests = [], pollTimer = 0, polling = false, versionChanged = false;
  var ticket = '', pairing = false, pairTimer = 0, pairDeadline = 0, category = '', folder = '', appsAt = 0, calendarDay = '', clockOffset = 0;
  var noteDirty = false, noteRevision = null, noteConflict = false, offlineDraft = null, taskListKey = '', pending = {}, volumeTail = Promise.resolve();
  var appGridKey = '', cooldownUntil = 0, cooldownCode = '', feedbackKind = '', taskFilter = 'all', writesBlocked = false;
  var page = 'home', appPage = 0, pageSize = 24, composing = false, searchTimer = 0, appNodes = Object.create(null);
  var assets = Object.create(null), wantedAssets = Object.create(null), assetActive = 0, scene = 'auto', touchKey = '', rememberedContext = {};
  var pointerLease = '', pointerSeq = 0, pointerExpires = 0, pointerBusy = false, pointerTimer = 0, pointerGeneration = 0, pointerDelta = null, fingers = {}, gesture = null;
  var pointerSamples = [], pointerActions = [], pointerHeld = false, pointerDragWanted = false, pointerLastSent = 0, pointerLastTap = null, pointerSpeed = 1, pointerNatural = true, wheelContact = null;
  var powerTicket = null, powerBusy = false, powerError = '', controlsVersion = 0, resumePage = '', resumeScene = 'auto', volumeEditing = false;
  var powerNames = { lock: '锁定', sleep: '睡眠', restart: '重启', shutdown: '关机' };
  var keyboardComposing = false, keyboardSending = false;
  var remoteKeys = Array.prototype.slice.call(document.querySelectorAll('[data-remote-key]'));
  var actionButtons = Array.prototype.slice.call(document.querySelectorAll('[data-action]'));
  var privateViews = Array.prototype.slice.call(document.querySelectorAll('.private-view'));

  function text(id, value) { var node = $(id), next = value == null ? '' : String(value); if (node.textContent !== next) node.textContent = next; }
  function visible(id, show) { $(id).hidden = !show; }
  function feedback(message, error, kind) { feedbackKind = kind || ''; text('feedback', message); $('feedback').dataset.error = String(!!error); visible('feedback', !!message); }
  function connection(message, online) { text('connection-status', message); $('connection-dot').dataset.online = String(!!online); }
  function requestId() { var bytes = new Uint8Array(16); crypto.getRandomValues(bytes); return Array.prototype.map.call(bytes, function (b) { return ('0' + b.toString(16)).slice(-2); }).join(''); }
  function safeAsset(url) { return typeof url === 'string' && /^\/api\/assets\/[a-zA-Z0-9_-]{1,96}$/.test(url) ? url : ''; }
  function number(value) { return typeof value === 'number' && isFinite(value) ? value : null; }
  function percent(value) { return number(value) === null || value < 0 || value > 100 ? '—' : Math.round(value) + '%'; }
  function rate(value) { return number(value) === null ? '—' : value >= 1048576 ? (value / 1048576).toFixed(1) + ' MB/s' : Math.round(value / 1024) + ' KB/s'; }
  function time(value) { return new Date(value).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' }); }
  function duration(seconds) { seconds = Math.max(0, Math.floor(seconds)); return ('0' + Math.floor(seconds / 60)).slice(-2) + ':' + ('0' + seconds % 60).slice(-2); }
  function permission(name) { return !!(session && session.grants && session.grants[name]); }
  function cooling() { return Date.now() < cooldownUntil; }
  function blocked() { return cooling() || writesBlocked; }
  function rateNotice() { return (cooldownCode === 'busy' ? '电脑暂时繁忙' : '请求较多') + '，暂停同步 ' + Math.max(1, Math.ceil((cooldownUntil - Date.now()) / 1000)) + ' 秒后自动恢复。编辑会保留，操作不会自动重放。'; }
  function responseError(response, data, isolated) {
    var error = new Error(data.message || '请求未完成，请稍后重试。'); error.status = response.status; error.code = data.error;
    if (response.status === 429 && !isolated) {
      var header = response.headers && response.headers.get('Retry-After'), seconds = Number(header || data.retryAfter);
      if (!isFinite(seconds) || seconds <= 0) seconds = data.error === 'busy' ? 1 : 60;
      cooldownCode = data.error || 'rate_limited'; cooldownUntil = Math.max(cooldownUntil, Date.now() + Math.min(300, Math.max(1, seconds)) * 1000);
    }
    return error;
  }
  function cancelled() { var error = new Error('请求已取消'); error.cancelled = true; return error; }
  function checkVersion(value) { if (value.protocol !== 1) { var error = new Error('页面与电脑版本不一致。请先复制未保存的内容，再重新载入页面。'); error.versionChanged = true; throw error; } }

  async function request(path, body, isolated) {
    if (cooling() && !isolated) { var limited = new Error(rateNotice()); limited.status = 429; throw limited; }
    var generation = epoch, controller = new AbortController(), timedOut = false;
    requests.push(controller);
    var timeout = setTimeout(function () { timedOut = true; controller.abort(); }, 6500);
    try {
      var headers = { Accept: 'application/json' };
      if (body) { headers['Content-Type'] = 'application/json'; if (session) headers['X-CSRF-Token'] = session.csrf; }
      var response = await fetch(path, { method: body ? 'POST' : 'GET', credentials: 'same-origin', cache: 'no-store', headers: headers, body: body ? JSON.stringify(body) : undefined, signal: controller.signal });
      if (generation !== epoch || document.hidden) throw cancelled();
      var data;
      try { data = await response.json(); } catch (_) { throw new Error('电脑返回了无法读取的内容，请重新连接。'); }
      if (generation !== epoch || document.hidden) throw cancelled();
      if (!response.ok) throw responseError(response, data, isolated);
      return data;
    } catch (error) {
      if (generation !== epoch || document.hidden) throw cancelled();
      if (timedOut) { var timeoutError = new Error('连接超时，私人内容已隐藏。'); timeoutError.network = true; throw timeoutError; }
      if (!error.status && !error.cancelled) { error.network = true; if (error.name === 'TypeError') error.message = '连接中断，私人内容已隐藏。请检查电脑和局域网连接。'; }
      throw error;
    } finally { clearTimeout(timeout); var index = requests.indexOf(controller); if (index >= 0) requests.splice(index, 1); }
  }

  function clearPrivate(keepDraft, keepPairInput) {
    $('screen-text').value = ''; keyboardComposing = false; keyboardSending = false; keyboardPanel(false);
    if (window.PrismCompanionScan) window.PrismCompanionScan.stop();
    volumeEditing = false;
    if (session && state) { resumePage = page; resumeScene = scene; }
    stopPointer(false); powerTicket = null; powerBusy = false; powerError = ''; if (window.PrismCompanionWidgets) window.PrismCompanionWidgets.clear();
    if (window.PrismCompanionLyrics) window.PrismCompanionLyrics.clear();
    if (window.PrismCompanionSettings) window.PrismCompanionSettings.clear();
    if (window.PrismGlassOptics) window.PrismGlassOptics.setActive(false);
    ['power-confirm', 'power-pending'].forEach(function (id) { visible(id, false); });
    // Network loss hides drafts; only a fresh authenticated state may restore them.
    if (keepDraft === true) {
      if (noteDirty || $('task-text').value) {
        offlineDraft = offlineDraft || {};
        if (noteDirty) offlineDraft.note = { text: $('note-text').value, revision: noteRevision };
        if ($('task-text').value) offlineDraft.task = $('task-text').value;
      }
    } else offlineDraft = null;
    if (window.PrismCompanionScreen) window.PrismCompanionScreen.clear();
    epoch++; clearTimeout(pollTimer); clearTimeout(pairTimer); clearTimeout(searchTimer); requests.forEach(function (c) { c.abort(); }); requests = [];
    Object.keys(assets).forEach(function (key) { var asset = assets[key]; if (asset.controller) asset.controller.abort(); if (asset.blob) URL.revokeObjectURL(asset.blob); });
    assets = Object.create(null); wantedAssets = Object.create(null); assetActive = 0; appNodes = Object.create(null); appPage = 0; composing = false; rememberedContext = {}; touchKey = ''; scene = 'auto'; $('touch-scene').value = 'auto';
    session = null; state = null; apps = null; ticket = ''; pairing = false; pairDeadline = 0; versionChanged = false; pending = {}; category = ''; folder = ''; appsAt = 0;
    noteDirty = false; noteConflict = false; noteRevision = null; taskListKey = ''; appGridKey = ''; taskFilter = 'all'; volumeTail = Promise.resolve();
    setPresentation(false); $('presentation-toggle').disabled = true;
    ['all', 'open', 'done'].forEach(function (filter) { var button = $('task-filter-' + filter); button.disabled = true; button.setAttribute('aria-pressed', String(filter === taskFilter)); });
    privateViews.forEach(function (node) { node.hidden = true; });
    ['task-list', 'quick-apps', 'app-grid', 'app-categories', 'app-folders', 'touch-actions', 'touch-extra'].forEach(function (id) { $(id).textContent = ''; });
    ['track-title', 'track-artist', 'lyric-current', 'lyric-next', 'music-source', 'weather-main', 'weather-detail', 'countdown', 'apps-status', 'focus-completed', 'home-context', 'touch-context', 'asset-status', 'data-status', 'home-task-count', 'home-task-first', 'home-task-second', 'home-countdown', 'music-elapsed', 'music-duration'].forEach(function (id) { text(id, ''); });
    visible('music-timeline', false); $('music-progress').value = 0;
    ['cpu-value', 'memory-value', 'disk-value', 'network-value', 'volume-value'].forEach(function (id) { text(id, '—'); });
    ['task-text', 'note-text', 'app-search'].forEach(function (id) { $(id).value = ''; });
    if (keepPairInput !== true) $('pair-code').value = '';
    $('cover').removeAttribute('src'); $('cover').hidden = true;
    text('focus-time', '--:--'); text('focus-status', '等待电脑同步'); text('note-status', '未共享'); text('task-count', '未共享'); text('last-sync', '私人内容已隐藏');
    $('task-text').disabled = true; $('note-text').disabled = true; $('task-add').disabled = true; $('note-save').disabled = true;
    $('theme-toggle').disabled = true; $('motion-toggle').disabled = true; $('apps-tab').disabled = true; $('work-tab').disabled = true; $('notes-tab').disabled = true; $('logout').disabled = true;
    $('pair-submit').disabled = false; $('pair-code').disabled = false; $('device-name').disabled = false; text('pair-submit', '请求配对');
    visible('note-reload', false); visible('pair-form', false); visible('pair-panel', true);
    text('pair-hint', '正在恢复已记住的设备…'); text('pair-status', '连接成功后会自动进入，无需重复扫码。');
    actionButtons.forEach(function (button) { button.disabled = true; });
    document.body.dataset.music = 'quiet'; feedback('', false); showPage('home');
  }
  function restoreDraft() {
    if (!offlineDraft) return;
    if (!permission('notes')) delete offlineDraft.note;
    else if (offlineDraft.note && (state.notes || {}).available) {
      $('note-text').value = offlineDraft.note.text; noteRevision = offlineDraft.note.revision; noteDirty = true; noteConflict = false; delete offlineDraft.note;
    }
    if (!permission('tasks')) delete offlineDraft.task;
    else if (offlineDraft.task != null && (state.tasks || {}).available) { $('task-text').value = offlineDraft.task; delete offlineDraft.task; }
    if (!offlineDraft.note && offlineDraft.task == null) offlineDraft = null;
  }
  function unavailable(error, mutation, anonymousProbe) {
    if (error.cancelled) return;
    if (error.code === 'session_budget') { writesBlocked = true; feedback(error.message || '本次会话操作额度已用尽，请在电脑解除配对后重新配对。同步仍可继续。', true); renderState(); updateAppButtons(); return; }
    if (error.status === 429) { setPresentation(false); connection('同步稍后恢复', false); feedback(rateNotice(), true, 'cooldown'); renderState(); updateAppButtons(); schedule(0); return; }
    if (error.versionChanged) {
      versionChanged = true; clearTimeout(pollTimer); setPresentation(false); $('presentation-toggle').disabled = true; connection('需要重新载入', false);
      actionButtons.forEach(function (button) { button.disabled = true; });
      ['task-add', 'note-save', 'theme-toggle', 'motion-toggle', 'apps-tab', 'all-apps'].forEach(function (id) { $(id).disabled = true; });
      feedback(error.message, true); return;
    }
    if (error.status === 401 || error.status === 423 || error.network) {
      if (!(anonymousProbe && !session && !state && !resumePage && !offlineDraft)) clearPrivate(error.network === true && error.status !== 401 && error.status !== 423, anonymousProbe);
      if (error.status === 401) { resumePage = ''; resumeScene = 'auto'; }
      visible('pair-form', error.status === 401);
      if (anonymousProbe) {
        connection('等待配对', false);
        text('pair-hint', '首次连接，在这里扫码并由电脑确认。以后直接打开此入口即可。');
        text('pair-status', $('pair-code').value ? '配对码已填入，点击“请求配对”后还需在电脑确认。' : '主屏幕与 Safari 分别记住配对；请在日常使用的窗口内完成。'); return;
      }
      connection(error.status === 423 ? '电脑已锁定' : error.status === 401 ? '需要配对' : '连接中断', false);
      text('pair-hint', error.status === 423 ? '电脑解锁后会自动重新同步。' : error.status === 401 ? '请在电脑开启配对，输入新的配对码。' : '请检查电脑是否运行 PrismGlass，以及两台设备是否在同一局域网。');
      text('pair-status', error.status === 423 ? '电脑锁定期间不能读取个人内容或执行操作。' : '个人内容由电脑端选择共享。');
    }
    feedback(mutation && error.network ? '连接中断，本次操作的结果未知。请在电脑核对后再操作；页面不会自动重试。' : error.message, true, !mutation && (error.network || error.status === 401 || error.status === 423) ? 'connection' : '');
  }
  function schedule(delay) { clearTimeout(pollTimer); if (!document.hidden && !pairing && !versionChanged) pollTimer = setTimeout(refresh, Math.max(delay, cooldownUntil - Date.now())); }
  async function refresh() {
    if (document.hidden || pairing || versionChanged) return;
    if (cooling()) { schedule(0); return; }
    if (polling) { schedule(250); return; }
    polling = true; var anonymousProbe = !session;
    try {
      if (!session || Date.now() - sessionAt >= 86400000) { session = await request('/api/session'); checkVersion(session); sessionAt = Date.now(); anonymousProbe = false; }
      var controlVersion = controlsVersion, next = await request('/api/state');
      checkVersion(next);
      if (next.locked) { var locked = new Error('电脑已锁定，个人内容已隐藏。'); locked.status = 423; throw locked; }
      if (controlVersion !== controlsVersion && state && state.controls && next.controls) next.controls.power = state.controls.power;
      state = next; if (next.permissions) session.grants = next.permissions;
      clockOffset = number(state.serverTime) === null ? 0 : state.serverTime - Date.now();
      if (window.PrismGlassOptics) window.PrismGlassOptics.setActive(true);
      restoreDraft();
      if (resumePage) { var restored = resumePage; resumePage = ''; scene = resumeScene; $('touch-scene').value = scene; showPage(restored); }
      privateViews.forEach(function (node) { node.hidden = false; }); renderState(); connection(cooling() ? '同步稍后恢复' : '已连接电脑', !cooling()); text('last-sync', '最近同步 ' + time(Date.now()));
      visible('pair-panel', false); $('logout').disabled = false; $('pair-code').value = '';
      if (permission('apps') && (!apps || state.appsRevision != null && String(state.appsRevision) !== String(apps.revision) || Date.now() - appsAt > 10000)) await loadApps();
      if (!cooling() && (feedbackKind === 'cooldown' || feedbackKind === 'connection')) feedback('', false);
      syncAssets();
    } catch (error) { unavailable(error, false, anonymousProbe && error.status === 401); }
    finally { polling = false; schedule(session ? 2000 : 5000); }
  }

  function weatherLabel(code) { if (code === 0) return '晴'; if (code <= 3) return '多云'; if (code <= 48) return '雾'; if (code >= 95) return '雷雨'; if (code >= 71 && code <= 77 || code >= 85 && code <= 86) return '雪'; return '雨'; }
  function renderState() {
    if (!state || !session || versionChanged) return;
    pruneAssets();
    $('presentation-toggle').disabled = cooling();
    if (document.body.dataset.presentation === 'true' && document.body.dataset.presentationMode === 'lyrics' && !permission('music')) setPresentation(true);
    document.body.dataset.theme = state.theme === 'Astro' ? 'Astro' : 'Sky';
    var w = state.weather || {}, wd = w.data || {}, s = state.system || {}, sd = s.data || {}, v = state.volume || {};
    text('weather-main', w.available && number(wd.temp) !== null ? Math.round(wd.temp) + '° · ' + (number(wd.code) !== null ? weatherLabel(wd.code) : '天气') : '天气未获取');
    text('weather-detail', w.available ? (number(wd.low) !== null && number(wd.high) !== null ? Math.round(wd.low) + '° / ' + Math.round(wd.high) + '° · ' : '') + (w.updatedAt ? time(w.updatedAt) + ' 更新' : '来自电脑') : '电脑天气组件未提供有效数据');
    visible('weather', !!w.available); $('work-tab').disabled = false; $('notes-tab').disabled = false;
    text('cpu-value', s.available ? percent(sd.cpu) : '—'); text('memory-value', s.available ? percent(sd.ram) : '—');
    var disk = state.disk || {}; text('disk-value', disk.available && number(disk.free) !== null ? Math.round(disk.free / 1073741824) + ' GB 可用' : disk.reason || '未提供'); visible('disk-metric', !!disk.available);
    text('network-value', s.available && (number(sd.down) !== null || number(sd.up) !== null) ? '↓ ' + rate(sd.down) + '  ↑ ' + rate(sd.up) : '未获取');
    text('system-stamp', s.available ? '电脑状态 · ' + (s.updatedAt ? time(s.updatedAt) : '已更新') : s.reason || '电脑暂未提供性能数据');
    text('volume-value', v.available ? percent((v.data || {}).value) : '未获取');
    var m = permission('music') ? state.music || {} : {};
    text('track-title', m.available ? m.title || '未提供歌名' : permission('music') ? m.reason || '音乐响起时，在这里相遇。' : '音乐尚未共享');
    text('track-artist', m.available ? m.artist || '' : ''); text('music-source', m.available ? m.source || '系统媒体' : '未获取');
    text('playback-state', m.available ? m.playing === true ? '正在播放' : m.playing === false ? '已暂停' : '播放状态未提供' : '');
    if (window.PrismCompanionLyrics) window.PrismCompanionLyrics.render(m, $('reduce-motion').checked);
    else { text('lyric-current', m.available ? m.current || '此刻，听音乐。' : ''); text('lyric-next', m.available ? m.next || '' : ''); }
    text('play-button', m.available && m.playing === true ? '暂停' : m.available && m.playing === false ? '播放' : '播放 / 暂停');
    document.body.dataset.music = m.available ? m.playing === false ? 'paused' : 'active' : 'quiet';
    if (!m.available || !safeAsset(m.cover)) { $('cover').removeAttribute('src'); $('cover').hidden = true; $('cover').dataset.asset = ''; }
    var context = state.context || {}; text('home-context', context.available && context.appName ? '电脑正在使用 · ' + context.appName : '');
    var missing = []; if (!w.available) missing.push('天气：' + (w.reason || '电脑暂未提供数据')); if (permission('music') && !m.available) missing.push('音乐：' + (m.reason || '没有可用的媒体会话')); if (!(state.focus || {}).available) missing.push('专注：' + ((state.focus || {}).reason || '电脑暂未提供计时')); text('data-status', missing.join('；'));
    renderTasks(); renderNotes(); renderFocus(); renderMusicProgress();
    var settings = state.settings || {}; text('theme-toggle', '切换到 ' + (settings.theme === 'Astro' ? 'Sky' : 'Astro')); text('motion-toggle', settings.manualPaused ? '恢复电脑动效' : '暂停电脑动效');
    $('theme-toggle').disabled = !permission('control') || !!pending.settings || blocked(); $('motion-toggle').disabled = !permission('control') || !!pending.settings || blocked();
    $('apps-tab').disabled = !permission('apps'); $('all-apps').disabled = !permission('apps');
    actionButtons.forEach(function (button) { var action = button.dataset.action; button.disabled = blocked() || !permission(button.dataset.permission) || !!pending[action] || action.indexOf('focus.') === 0 && !(state.focus || {}).available || action.indexOf('volume.') === 0 && action !== 'volume.mute' && !v.available; });
    if (!permission('apps')) { apps = null; $('quick-apps').textContent = ''; text('quick-empty', '电脑尚未共享应用入口。'); visible('quick-empty', true); }
    visible('focus-card', !!(state.focus || {}).available); visible('quick-card', permission('apps')); renderTouchBar(); renderControls(); renderControlCenter(); renderWidgets(); syncAssets();
    if (window.PrismCompanionSettings) window.PrismCompanionSettings.render(state, session.grants || {}, pending, blocked());
    if (window.PrismGlassOptics) window.PrismGlassOptics.invalidate();
  }
  function renderTasks() {
    var tasks = state.tasks || {}, allowed = permission('tasks') && tasks.available;
    $('task-text').maxLength = 120; $('task-text').disabled = !allowed; $('task-add').disabled = !allowed || !!pending.tasks || blocked();
    var items = allowed && Array.isArray(tasks.items) ? tasks.items : [];
    var open = items.filter(function (item) { return !item.done; });
    text('home-task-count', allowed ? open.length ? open.length + ' 件待办' : '暂时都完成了' : permission('tasks') ? '等待电脑同步' : '待办尚未共享');
    text('home-task-first', allowed ? open.length ? open[0].text : '空一点时间，做喜欢的事。' : '在电脑开启共享后，在这里查看。');
    text('home-task-second', open.length > 1 ? open[1].text : ''); visible('home-task-second', open.length > 1);
    var filtered = items.filter(function (item) { return taskFilter === 'all' || (taskFilter === 'done') === !!item.done; }), listKey = JSON.stringify([!!allowed, tasks.revision, taskFilter, filtered]);
    ['all', 'open', 'done'].forEach(function (filter) { var button = $('task-filter-' + filter); button.disabled = !allowed; button.setAttribute('aria-pressed', String(filter === taskFilter)); });
    text('task-count', allowed ? items.filter(function (t) { return !t.done; }).length + ' 项未完成' : permission('tasks') ? '暂不可用' : '未共享');
    text('tasks-empty', !allowed ? permission('tasks') ? tasks.reason || '电脑暂未提供待办，稍后会自动同步。' : '请在电脑允许此设备共享待办。' : taskFilter === 'open' ? '没有未完成的待办。' : taskFilter === 'done' ? '还没有已完成的待办。' : '手边的事情，都已记好。'); visible('tasks-empty', !filtered.length);
    if (listKey !== taskListKey) {
      taskListKey = listKey; $('task-list').textContent = '';
      filtered.forEach(function (item) {
      var li = document.createElement('li'), toggle = document.createElement('button'), mark = document.createElement('span'), label = document.createElement('span'), remove = document.createElement('button');
      toggle.type = 'button'; toggle.className = 'task-toggle'; toggle.setAttribute('aria-pressed', String(!!item.done)); toggle.disabled = !!pending.tasks;
      mark.className = 'check'; mark.textContent = item.done ? '✓' : ''; mark.setAttribute('aria-hidden', 'true'); label.className = 'task-text'; label.textContent = item.text;
      toggle.appendChild(mark); toggle.appendChild(label); toggle.onclick = function () { write('tasks', '/api/tasks', { expectedRevision: tasks.revision, command: 'toggle', id: item.id }, '待办已保存到电脑'); };
      remove.type = 'button'; remove.className = 'task-remove'; remove.textContent = '×'; remove.setAttribute('aria-label', '删除待办：' + item.text); remove.disabled = !!pending.tasks;
      remove.onclick = function () { write('tasks', '/api/tasks', { expectedRevision: tasks.revision, command: 'delete', id: item.id }, '待办已删除'); };
      li.appendChild(toggle); li.appendChild(remove); $('task-list').appendChild(li);
      });
    }
    Array.prototype.forEach.call($('task-list').children, function (row) { row.children[0].disabled = row.children[1].disabled = !!pending.tasks || blocked(); });
    visible('countdown', !!(allowed && tasks.countdownTitle && tasks.countdownDate));
    if (allowed && tasks.countdownTitle && tasks.countdownDate) { var date = new Date(tasks.countdownDate + 'T00:00:00'), today = new Date(Date.now() + clockOffset); today.setHours(0, 0, 0, 0); var days = Math.round((date - today) / 86400000); text('countdown', tasks.countdownTitle + ' · ' + (isFinite(days) ? days > 0 ? '还有 ' + days + ' 天' : days === 0 ? '就是今天' : '已过 ' + Math.abs(days) + ' 天' : tasks.countdownDate)); }
    text('home-countdown', $('countdown').hidden ? '' : $('countdown').textContent); visible('home-countdown', !$('countdown').hidden);
  }
  function renderMusicProgress() {
    var music = state && permission('music') ? state.music || {} : {};
    var available = music.available && number(music.position) !== null && number(music.duration) !== null && music.duration > 0;
    visible('music-timeline', !!available);
    if (!available) { $('music-progress').value = 0; text('music-elapsed', ''); text('music-duration', ''); return; }
    var elapsed = Math.min(music.duration, Math.max(0, music.position + (music.playing && number(state.serverTime) !== null ? Math.min(5, Math.max(0, (Date.now() + clockOffset - state.serverTime) / 1000)) : 0)));
    $('music-progress').max = music.duration; $('music-progress').value = elapsed;
    text('music-elapsed', duration(elapsed)); text('music-duration', duration(music.duration));
  }
  function renderNotes() {
    var notes = state.notes || {}, allowed = permission('notes') && notes.available;
    $('note-text').maxLength = 32768; $('note-text').disabled = !allowed; $('note-save').disabled = !allowed || !noteDirty || !!pending.notes || noteConflict;
    if (!allowed) { if ($('note-text').value) $('note-text').value = ''; noteDirty = false; noteRevision = null; noteConflict = false; text('note-status', permission('notes') ? notes.reason || '电脑暂未提供笔记' : '未共享'); visible('note-reload', false); return; }
    if (!noteDirty) { if ($('note-text').value !== (notes.text || '')) $('note-text').value = notes.text || ''; noteRevision = notes.revision; text('note-status', '已与电脑同步'); }
    else if (String(notes.revision) !== String(noteRevision)) { noteConflict = true; text('note-status', '电脑内容已更新，请先复制编辑内容再重新载入'); }
    else text('note-status', pending.notes ? '正在保存…' : '尚未保存');
    visible('note-reload', noteConflict); $('note-save').disabled = !noteDirty || !!pending.notes || noteConflict || versionChanged || blocked();
  }
  function renderFocus() {
    var f = state && state.focus || {};
    if (!f.available) { text('focus-time', '--:--'); text('focus-status', '电脑专注组件未运行'); text('focus-completed', '由电脑计时'); text('home-focus-time', '留一段专注'); text('home-focus-label', '给喜欢的事，一点时间'); return; }
    var running = f.deadline > 0, remaining = running ? Math.max(0, f.deadline - (Date.now() + clockOffset) / 1000) : f.remaining;
    text('focus-time', number(remaining) === null ? '--:--' : duration(remaining)); text('focus-status', running ? remaining > 0 ? '给眼前的事情一点时间' : '等待电脑确认完成' : '准备好时，就开始');
    text('focus-toggle', running ? '暂停' : '开始'); text('focus-completed', number(f.completed) !== null ? '电脑已记录 ' + f.completed + ' 轮专注' : '由电脑计时');
    text('home-focus-time', number(remaining) === null ? '留一段专注' : duration(remaining)); text('home-focus-label', running ? '正在专注 · 与电脑同步' : '给喜欢的事，一点时间');
  }

  async function loadApps() { var result = await request('/api/apps'); apps = result; appsAt = Date.now(); pruneAssets(); renderApps(); }
  function appButton(item) {
    var cached = appNodes[item.id];
    if (!cached) {
      var button = document.createElement('button'), icon = document.createElement('span'), initial = document.createElement('span'), image = document.createElement('img'), copy = document.createElement('span'), title = document.createElement('strong'), caption = document.createElement('small');
      button.type = 'button'; button.className = 'app-button'; button.dataset.appId = item.id; icon.className = 'app-icon'; icon.setAttribute('aria-hidden', 'true'); image.alt = ''; image.hidden = true; image.dataset.appId = item.id;
      icon.appendChild(initial); icon.appendChild(image); copy.className = 'app-copy'; copy.appendChild(title); copy.appendChild(caption); button.appendChild(icon); button.appendChild(copy);
      cached = appNodes[item.id] = { button: button, image: image, initial: initial, title: title, caption: caption, item: item };
      button.onclick = function () { if (apps && permission('apps')) write('app.open', '/api/action', { action: 'app.open', targetId: cached.item.id, expectedRevision: apps.revision }, '已向电脑发送打开请求'); };
    }
    cached.item = item; cached.initial.textContent = (item.name || '?').slice(0, 1); cached.title.textContent = item.name; cached.caption.textContent = item.exists === false ? '入口已失效' : '在电脑打开';
    cached.button.dataset.unavailable = String(item.exists === false); cached.button.setAttribute('aria-label', (item.exists === false ? '入口已失效：' : '在电脑打开：') + item.name);
    var asset = safeAsset(item.icon); if (cached.image.dataset.asset !== asset) { cached.image.dataset.asset = asset; cached.image.removeAttribute('src'); cached.image.hidden = true; }
    return cached.button;
  }
  function filterButton(label, selected, callback) { var button = document.createElement('button'); button.type = 'button'; button.textContent = label; button.setAttribute('aria-pressed', String(selected)); button.onclick = callback; return button; }
  function updateAppButtons() { Object.keys(appNodes).forEach(function (id) { var button = appNodes[id].button; button.disabled = button.dataset.unavailable === 'true' || !!pending['app.open'] || !permission('apps') || versionChanged || blocked(); }); }
  function mountApps(id, items) {
    var container = $(id), buttons = items.map(appButton), same = container.children.length === buttons.length && buttons.every(function (button, index) { return container.children[index] === button; });
    if (!same) { container.textContent = ''; buttons.forEach(function (button) { container.appendChild(button); }); }
  }
  function renderApps() {
    if (!apps || !permission('apps')) return;
    var items = Array.isArray(apps.items) ? apps.items : [], categories = Array.isArray(apps.categories) ? apps.categories : [], folders = Array.isArray(apps.folders) ? apps.folders : [];
    var live = Object.create(null); items.forEach(function (item) { live[item.id] = true; }); Object.keys(appNodes).forEach(function (id) { if (!live[id]) { appNodes[id].button.remove(); delete appNodes[id]; } });
    if (page === 'work') mountApps('quick-apps', items.slice(0, 6));
    visible('quick-empty', !items.length); text('quick-empty', '在电脑选择希望共享的应用入口。');
    if (page !== 'apps') { updateAppButtons(); syncAssets(); return; }
    if (category && !categories.some(function (c) { return c.id === category; })) category = '';
    if (folder && !folders.some(function (f) { return f.id === folder; })) folder = '';
    var query = $('app-search').value.trim().toLocaleLowerCase(), gridKey = JSON.stringify([categories, folders, category, folder]);
    if (gridKey !== appGridKey) {
      appGridKey = gridKey;
      $('app-categories').textContent = ''; $('app-categories').appendChild(filterButton('全部', !category, function () { category = ''; folder = ''; appPage = 0; renderApps(); }));
      categories.forEach(function (c) { $('app-categories').appendChild(filterButton(c.name, category === c.id, function () { category = c.id; folder = ''; appPage = 0; renderApps(); })); });
      $('app-folders').textContent = ''; if (folder) $('app-folders').appendChild(filterButton('所有文件夹', false, function () { folder = ''; appPage = 0; renderApps(); }));
      folders.filter(function (f) { return !category || f.category === category; }).forEach(function (f) { $('app-folders').appendChild(filterButton('▱ ' + f.name, folder === f.id, function () { folder = folder === f.id ? '' : f.id; appPage = 0; renderApps(); })); });
    }
    var filtered = items.filter(function (item) { return (!category || item.category === category) && (!folder || item.folder === folder) && (!query || String(item.name).toLocaleLowerCase().indexOf(query) >= 0 || String(item.search || '').toLocaleLowerCase().indexOf(query) >= 0); });
    var pages = Math.max(1, Math.ceil(filtered.length / pageSize)); appPage = Math.max(0, Math.min(appPage, pages - 1));
    mountApps('app-grid', filtered.slice(appPage * pageSize, (appPage + 1) * pageSize)); text('apps-status', filtered.length ? filtered.length + ' 个入口 · 在电脑打开' : query ? '没有匹配的应用。' : '此分类暂无已授权的入口。');
    text('apps-page-label', (appPage + 1) + ' / ' + pages); $('apps-prev').disabled = appPage === 0; $('apps-next').disabled = appPage >= pages - 1;
    updateAppButtons(); syncAssets();
  }
  function showPage(page) {
    if ((!session || !state) && page !== 'widgets' || page === 'apps' && !permission('apps')) page = 'home';
    changePage(page);
  }
  function changePage(next) {
    if ($('feedback').dataset.error !== 'true') feedback('', false);
    if (page === 'pointer' && next !== 'pointer') stopPointer(true);
    page = next; document.body.dataset.page = page;
    if (window.PrismCompanionScreen) window.PrismCompanionScreen.render(session && session.grants, session && session.csrf, page === 'pointer');
    ['home', 'work', 'notes', 'apps', 'pointer', 'widgets', 'computer', 'appearance'].forEach(function (name) { visible(name + '-page', page === name); if (page === name) $(name + '-tab').setAttribute('aria-current', 'page'); else $(name + '-tab').removeAttribute('aria-current'); });
    if (page !== 'widgets' && window.PrismCompanionWidgets) window.PrismCompanionWidgets.pause();
    $('main-content').scrollTop = 0; if (apps) renderApps(); else syncAssets();
    if ((page === 'apps' || page === 'work') && permission('apps') && (!apps || Date.now() - appsAt > 10000)) schedule(0);
    if (window.PrismGlassOptics) window.PrismGlassOptics.invalidate();
  }
  function demandImage(image, asset) {
    if (!asset) return;
    if (image.dataset.asset !== asset) { image.dataset.asset = asset; image.removeAttribute('src'); image.hidden = true; }
    wantedAssets[asset] = true;
    var entry = assets[asset] || (assets[asset] = { path: asset, status: 'idle', nodes: [], retries: 0, blob: '' });
    if (entry.nodes.indexOf(image) < 0) entry.nodes.push(image);
    if (entry.blob && image.getAttribute('src') !== entry.blob) { image.src = entry.blob; image.hidden = false; }
  }
  function pruneAssets() {
    var valid = Object.create(null), ids = Object.create(null), music = state && state.music || {}, cover = permission('music') && music.available ? safeAsset(music.cover) : '';
    if (cover) valid[cover] = true;
    if (permission('apps') && apps && Array.isArray(apps.items)) apps.items.forEach(function (item) { ids[item.id] = safeAsset(item.icon); if (ids[item.id]) valid[ids[item.id]] = true; });
    Object.keys(assets).forEach(function (key) {
      var entry = assets[key];
      entry.nodes = entry.nodes.filter(function (node) {
        var keep = node === $('cover') ? cover === key : ids[node.dataset.appId] === key;
        if (!keep && node.dataset.asset === key) { node.removeAttribute('src'); node.hidden = true; }
        return keep;
      });
      if (!valid[key]) { if (entry.controller) entry.controller.abort(); if (entry.blob) URL.revokeObjectURL(entry.blob); delete assets[key]; delete wantedAssets[key]; }
    });
  }
  function syncAssets() {
    wantedAssets = Object.create(null);
    if (!session || !state || document.hidden) return;
    var music = state.music || {};
    if (page === 'home' && permission('music') && music.available) demandImage($('cover'), safeAsset(music.cover));
    var container = page === 'apps' ? $('app-grid') : page === 'work' ? $('quick-apps') : null;
    if (container && permission('apps')) Array.prototype.forEach.call(container.children, function (button) { var node = appNodes[button.dataset.appId]; if (node) demandImage(node.image, safeAsset(node.item.icon)); });
    Object.keys(assets).forEach(function (key) { var entry = assets[key]; if (!wantedAssets[key] && entry.controller) entry.controller.abort(); });
    pumpAssets();
  }
  function pumpAssets() {
    if (cooling() || !session || !state || document.hidden) return;
    Object.keys(wantedAssets).some(function (key) { if (assetActive >= 3) return true; var entry = assets[key]; if (entry && entry.status === 'idle') fetchAsset(entry); return false; });
  }
  async function fetchAsset(entry) {
    var generation = epoch, controller = new AbortController(); entry.controller = controller; entry.status = 'loading'; assetActive++;
    var timeout = setTimeout(function () { controller.abort(); }, 6500);
    try {
      var response = await fetch(entry.path, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'image/*' }, signal: controller.signal });
      if (generation !== epoch || document.hidden || assets[entry.path] !== entry) return;
      if (!response.ok) { var data = {}; try { data = await response.json(); } catch (_) {} throw responseError(response, data); }
      var blob = await response.blob(); if (generation !== epoch || document.hidden || assets[entry.path] !== entry) return;
      entry.blob = URL.createObjectURL(blob); entry.status = 'loaded';
      entry.nodes.forEach(function (node) { if (node.dataset.asset === entry.path) { node.src = entry.blob; node.hidden = false; node.onerror = function () { node.hidden = true; }; } });
    } catch (error) {
      if (generation !== epoch || document.hidden || assets[entry.path] !== entry) return;
      if (controller.signal.aborted && !wantedAssets[entry.path]) entry.status = 'idle';
      else if (error.status === 429) { entry.status = entry.retries++ < 1 ? 'idle' : 'failed'; unavailable(error, false); }
      else if (error.status === 401 || error.status === 423) unavailable(error, false);
      else { entry.status = 'failed'; text('asset-status', '部分图标暂不可用，应用仍可打开。'); }
    } finally {
      clearTimeout(timeout); if (generation === epoch) { entry.controller = null; assetActive--; pumpAssets(); }
    }
  }
  function renderTouchBar() {
    if (!session || !state) return;
    var context = state.context || {}, currentScene = ['general', 'browser', 'editor', 'music', 'focus'].indexOf(context.scene) >= 0 ? context.scene : 'general';
    if (context.available && (currentScene === 'browser' || currentScene === 'editor')) rememberedContext[currentScene] = Array.isArray(context.actions) ? context.actions : [];
    var selected = scene === 'auto' ? currentScene : scene, specific = selected === 'browser' || selected === 'editor', matched = !!(context.available && currentScene === selected), definitions;
    $('touch-pin').setAttribute('aria-pressed', String(scene !== 'auto')); text('touch-pin', scene === 'auto' ? '固定此场景' : '恢复跟随');
    if (specific) definitions = (matched && Array.isArray(context.actions) ? context.actions : rememberedContext[selected] || []).map(function (item) { return { id: item.id, label: item.label, permission: 'control', contextId: matched ? context.id : null }; });
    else if (selected === 'music') definitions = [{ id: 'media.previous', label: '上一首', permission: 'music' }, { id: 'media.toggle', label: (state.music || {}).playing ? '暂停' : '播放 / 暂停', permission: 'music' }, { id: 'media.next', label: '下一首', permission: 'music' }];
    else if (selected === 'focus') definitions = [{ id: 'focus.25', label: '25 分钟', permission: 'control' }, { id: 'focus.5', label: '5 分钟', permission: 'control' }, { id: 'focus.toggle', label: (state.focus || {}).deadline > 0 ? '暂停专注' : '开始专注', permission: 'control' }, { id: 'focus.reset', label: '重置', permission: 'control' }];
    else definitions = [{ id: 'pointer', label: '触控板', permission: 'pointer' }, { id: 'windows', label: '任务视图', permission: 'control' }, { id: 'media.toggle', label: (state.music || {}).playing ? '暂停音乐' : '播放音乐', permission: 'music' }, { id: 'focus.toggle', label: (state.focus || {}).deadline > 0 ? '暂停专注' : '开始专注', permission: 'control' }];
    if (selected === 'browser') { var order = ['previous-tab','next-tab','new-tab','back','forward','reload']; definitions.sort(function (a,b) { return order.indexOf(a.id) - order.indexOf(b.id); }); }
    var names = { general: '通用控制', browser: '浏览器', editor: '编辑器', music: '音乐', focus: '专注' };
    text('touch-context', specific && !matched ? '请先在电脑切到' + names[selected] + '，此处不会切换电脑窗口。' : (scene === 'auto' ? '自动跟随 · ' : '已固定 · ') + (specific && context.appName ? context.appName : names[selected]) + (permission(specific || selected !== 'music' ? 'control' : 'music') ? '' : ' · 未授权控制'));
    var key = JSON.stringify([selected, specific ? context.id : '', definitions]);
    if (key !== touchKey) {
      touchKey = key; $('touch-actions').textContent = ''; $('touch-extra').textContent = ''; visible('touch-extra', false); visible('touch-more', definitions.length > 4); $('touch-more').setAttribute('aria-expanded', 'false');
      definitions.forEach(function (definition, index) {
        var button = document.createElement('button'); button.type = 'button'; button.textContent = definition.label; button.dataset.touchId = definition.id; button.dataset.permission = definition.permission;
        if (['next-tab','palette','media.toggle','focus.toggle'].indexOf(definition.id) >= 0) button.className = 'touch-primary';
        button.onclick = function () {
          if (!permission(definition.permission)) return;
          if (specific) {
            var live = state && state.context || {};
            if (!matched || !live.available || live.id !== definition.contextId || live.scene !== selected) { feedback('电脑前台已改变，等待场景更新后再操作。', true); schedule(0); return; }
            write('context.execute', '/api/action', { action: 'context.execute', contextId: definition.contextId, command: definition.id }, '已向当前电脑窗口发送操作');
          } else if (definition.id === 'pointer') showPage('pointer');
          else if (definition.id === 'windows') $('desktop-overview').onclick();
          else if (definition.id === 'theme') $('theme-toggle').onclick();
          else executeAction(definition.id);
        };
        $(index < 4 ? 'touch-actions' : 'touch-extra').appendChild(button);
      });
    }
    Array.prototype.slice.call($('touch-actions').children).concat(Array.prototype.slice.call($('touch-extra').children)).forEach(function (button) {
      var id = button.dataset.touchId;
      button.disabled = blocked() || versionChanged || !permission(button.dataset.permission) || specific && !matched || !!pending[specific ? 'context.execute' : id === 'windows' ? 'desktop.execute' : id === 'theme' ? 'settings' : id.indexOf('volume.') === 0 ? 'volume' : id] || id.indexOf('focus.') === 0 && !(state.focus || {}).available || id === 'windows' && !context.available || id.indexOf('volume.') === 0 && id !== 'volume.mute' && !(state.volume || {}).available;
    });
  }
  function setPresentation(active) {
    if (active && (!session || !state || document.hidden || versionChanged || cooling())) return;
    document.body.dataset.presentation = String(active); $('presentation-toggle').setAttribute('aria-pressed', String(active));
    if (active) showPage('home');
    var mode = ['balanced', 'lyrics', 'clock'].indexOf($('presentation-mode').value) >= 0 ? $('presentation-mode').value : 'balanced';
    if (mode === 'lyrics' && !permission('music')) { mode = 'clock'; $('presentation-mode').value = mode; }
    document.body.dataset.presentationMode = mode;
    if (window.PrismGlassOptics) window.PrismGlassOptics.invalidate();
  }

  function controlError(error, id) {
    if (error.cancelled) return;
    if (id === 'power-status') powerError = error.message;
    if (error.network || error.status === 401 || error.status === 423) unavailable(error, true);
    else text(id, error.message || '操作未完成，请重新操作。');
  }
  function stopPointer(notify) {
    keyboardSending = false;
    wheelContact = null; $('pointer-wheel').dataset.active = 'false'; $('pointer-wheel').setAttribute('aria-disabled', 'true'); $('wheel-up').disabled = $('wheel-down').disabled = true;
    var lease = pointerLease; ++pointerGeneration; pointerLease = ''; pointerSeq = 0; pointerExpires = 0; pointerBusy = false; fingers = {}; gesture = null; pointerDelta = null; pointerSamples = []; pointerActions = []; pointerHeld = false; pointerDragWanted = false; pointerLastTap = null; clearTimeout(pointerTimer); pointerTimer = 0;
    $('pointer-surface').dataset.active = 'false'; $('pointer-enable').disabled = !permission('pointer'); $('pointer-stop').disabled = $('pointer-left').disabled = $('pointer-right').disabled = true;
    text('pointer-status', permission('pointer') ? '触控已停止，点击启用后继续。' : '在电脑选中此设备，勾选“触控板”并更新权限后即可使用。');
    // Release also survives privacy clearing, pagehide and cancellation of ordinary requests.
    if (lease && session) releasePointerLease(lease, session.csrf);
    renderKeyboard();
  }
  function keyboardPanel(open) { if (!open) $('screen-text').blur(); document.body.dataset.screenKeyboard = String(open); $('screen-keyboard').hidden = !open; $('screen-keyboard-toggle').setAttribute('aria-expanded', String(open)); text('screen-keyboard-toggle', open ? '收起键盘' : '键盘'); }
  function renderKeyboard() {
    var allowed = permission('keyboard'), ready = allowed && !!pointerLease && permission('pointer') && page === 'pointer' && !document.hidden;
    $('screen-keyboard-toggle').disabled = !ready; $('screen-text').disabled = !ready; $('screen-text-send').disabled = !ready || keyboardSending;
    remoteKeys.forEach(function (button) { button.disabled = !ready || keyboardSending; });
    if (!allowed) { $('screen-text').value = ''; keyboardPanel(false); text('keyboard-status', '在电脑为此设备勾选“键盘输入”，文字与按键才会启用。'); }
    else if (!ready) text('keyboard-status', '启用触控后，可展开键盘向电脑输入。');
    else if (!keyboardSending && $('keyboard-status').textContent.indexOf('启用') >= 0) text('keyboard-status', '先选中电脑输入框，再输入文字或使用按键。');
  }
  function releasePointerLease(lease, csrf) {
    try { fetch('/api/pointer/end', { method: 'POST', credentials: 'same-origin', cache: 'no-store', keepalive: true, headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf }, body: JSON.stringify({ leaseId: lease }) }).catch(function () {}); } catch (_) {}
  }
  async function beginPointer() {
    if (!permission('pointer') || pointerBusy || pointerLease || !state) return;
    var generation = ++pointerGeneration, authGeneration = epoch; pointerBusy = true; $('pointer-enable').disabled = true;
    try {
      var result = await request('/api/pointer/begin', {}, true);
      if (generation !== pointerGeneration || page !== 'pointer') { if (authGeneration === epoch) request('/api/pointer/end', { leaseId: result.leaseId }, true).catch(function () {}); return; }
      pointerLease = result.leaseId; pointerExpires = result.expiresAt; pointerSeq = 0; pointerLastSent = Date.now(); queuePointer();
      $('pointer-wheel').dataset.active = 'true'; $('pointer-wheel').setAttribute('aria-disabled', 'false'); $('wheel-up').disabled = $('wheel-down').disabled = false;
      $('pointer-surface').dataset.active = 'true'; $('pointer-stop').disabled = $('pointer-left').disabled = $('pointer-right').disabled = false;
      text('pointer-status', '已启用 · 单指移动，双击按住拖拽，双指轻点右键、上下滚动。');
    } catch (error) { if (generation === pointerGeneration) { stopPointer(false); controlError(error, 'pointer-status'); } }
    finally { if (generation === pointerGeneration) { pointerBusy = false; $('pointer-enable').disabled = !!pointerLease || !permission('pointer'); renderKeyboard(); } }
  }
  async function sendPointer(kind, values) {
    if ((kind === 'text' || kind === 'key') && !permission('keyboard')) return false;
    if (!pointerLease || pointerBusy || !permission('pointer') || document.hidden || page !== 'pointer') return;
    if (Date.now() + clockOffset >= pointerExpires) { stopPointer(false); return; }
    var generation = pointerGeneration, body = { leaseId: pointerLease, seq: ++pointerSeq, kind: kind }; Object.keys(values).forEach(function (key) { body[key] = values[key]; }); pointerBusy = true; pointerLastSent = Date.now();
    if (kind === 'drag' && values.phase === 'start') pointerHeld = true;
    if (kind === 'drag' && values.phase === 'end') pointerHeld = false;
    try { var result = await request('/api/pointer', body, true); if (generation === pointerGeneration) { pointerExpires = result.expiresAt; return true; } }
    catch (error) { if (generation === pointerGeneration) { stopPointer(false); controlError(error, 'pointer-status'); } }
    finally { if (generation === pointerGeneration) { pointerBusy = false; queuePointer(); } }
  }
  function pointerAction(kind, values) {
    if (!pointerLease) return;
    if (pointerActions.length >= 4) { stopPointer(true); return; }
    pointerActions.push({ kind: kind, values: values, at: Date.now() }); queuePointer();
  }
  function endPointerDrag() {
    pointerDragWanted = false;
    pointerActions = pointerActions.filter(function (action) { return action.kind !== 'drag' || action.values.phase !== 'start'; });
    if (pointerHeld && !pointerActions.some(function (action) { return action.kind === 'drag' && action.values.phase === 'end'; })) pointerAction('drag', { phase: 'end' });
  }
  function clickPointer(button) { pointerDelta = null; pointerSamples = []; if (pointerHeld || pointerDragWanted) endPointerDrag(); pointerAction('click', { button: button }); }
  function addPointerDelta(kind, dx, dy) {
    var last = pointerSamples[pointerSamples.length - 1];
    if (last && last.kind !== kind || pointerDelta && pointerDelta.kind !== kind) { pointerSamples = []; pointerDelta = null; }
    if (pointerSamples.length >= 64) pointerSamples.shift();
    pointerSamples.push({ kind: kind, dx: dx, dy: dy, at: Date.now() }); queuePointer();
  }
  function takePointerDelta() {
    var now = Date.now(), delta = pointerDelta && now - pointerDelta.at <= 200 ? pointerDelta : null; pointerDelta = null;
    pointerSamples.forEach(function (sample) { if (now - sample.at > 200) return; if (!delta || delta.kind !== sample.kind) delta = { kind: sample.kind, dx: 0, dy: 0, at: sample.at }; delta.dx += sample.dx; delta.dy += sample.dy; delta.at = sample.at; }); pointerSamples = [];
    if (!delta) return null;
    var x = 0, y = Math.max(-5, Math.min(5, Math.trunc(delta.dy / 24)));
    if (delta.kind === 'move') { x = Math.max(-500, Math.min(500, Math.trunc(delta.dx))); y = Math.max(-500, Math.min(500, Math.trunc(delta.dy))); delta.dx -= x; delta.dy -= y; }
    else delta.dy -= y * 24;
    pointerDelta = delta;
    return delta.kind === 'scroll' ? y ? { kind: 'scroll', values: { steps: y } } : null : x || y ? { kind: 'move', values: { dx: x, dy: y } } : null;
  }
  function queuePointer() {
    if (pointerTimer || !pointerLease) return;
    pointerTimer = setTimeout(function () {
      pointerTimer = 0; if (!pointerLease) return; queuePointer();
      if (pointerBusy || Date.now() - pointerLastSent < 17) return;
      if (Date.now() + clockOffset >= pointerExpires) { stopPointer(true); return; }
      var action = pointerActions[0];
      while (action && Date.now() - action.at > 200 && !(action.kind === 'drag' && action.values.phase === 'end')) {
        pointerActions.shift(); if (action.kind === 'drag') { stopPointer(true); return; } action = pointerActions[0];
      }
      // Finish the latest movement before releasing a drag; no stale movement is replayed.
      var finishingDrag = action && action.kind === 'drag' && action.values.phase === 'end', scrolling = pointerDelta && pointerDelta.kind === 'scroll' || pointerSamples.length && pointerSamples[0].kind === 'scroll';
      var delta = !action || finishingDrag && !scrolling ? takePointerDelta() : null;
      if (delta) sendPointer(delta.kind, delta.values);
      else if (action) { pointerActions.shift(); sendPointer(action.kind, action.values); }
      else if (pointerHeld && pointerDragWanted && Date.now() - pointerLastSent >= 250) sendPointer('drag', { phase: 'hold' });
    }, 17);
  }
  function pointerMove(event) {
    var finger = fingers[event.pointerId]; if (!pointerLease || !finger) return; event.preventDefault();
    var dx = event.clientX - finger.x, dy = event.clientY - finger.y; finger.x = event.clientX; finger.y = event.clientY;
    gesture.distance += Math.abs(dx) + Math.abs(dy); var count = Object.keys(fingers).length, kind = count > 1 ? 'scroll' : 'move';
    if (gesture.multi && count < 2) return;
    addPointerDelta(kind, dx * 1.5 * pointerSpeed, kind === 'scroll' ? (pointerNatural ? 1 : -1) * dy / count : dy * 1.5 * pointerSpeed);
  }
  function pointerEnd(event, cancel) {
    if (!fingers[event.pointerId]) return; delete fingers[event.pointerId];
    if (cancel) { stopPointer(true); return; }
    if (!Object.keys(fingers).length) {
      var tap = gesture && gesture.distance < 7 && Date.now() - gesture.at < 350;
      if (gesture && gesture.drag) { var started = pointerHeld; endPointerDrag(); if (!started && tap) clickPointer('left'); pointerLastTap = null; }
      else if (tap) { clickPointer(gesture.multi ? 'right' : 'left'); pointerLastTap = gesture.multi ? null : { at: Date.now(), x: event.clientX, y: event.clientY }; }
      else pointerLastTap = null;
      gesture = null;
    }
  }
  function renderControls() {
    renderKeyboard();
    if (window.PrismCompanionScreen) window.PrismCompanionScreen.render(session && session.grants, session && session.csrf, page === 'pointer');
    if (!permission('pointer') && pointerLease) stopPointer(false);
    $('pointer-enable').disabled = !permission('pointer') || !!pointerLease || pointerBusy;
    if (!pointerLease && !pointerBusy) text('pointer-status', permission('pointer') ? '点击启用触控板，再用手指移动电脑鼠标。' : '在电脑选中此设备，勾选“触控板”并更新权限后即可使用。');
    if (!permission('power')) powerTicket = null;
    var power = state && state.controls && state.controls.power || {}, waiting = power.status === 'pending', busy = waiting || power.status === 'executing' || power.status === 'busy';
    Object.keys(powerNames).forEach(function (action) { $('power-' + action).disabled = !permission('power') || powerBusy || busy; });
    visible('power-confirm', !!powerTicket); visible('power-pending', permission('power') && waiting);
    $('power-commit').disabled = powerBusy || !powerTicket || Date.now() + clockOffset >= powerTicket.expiresAt; $('power-cancel').disabled = powerBusy || !waiting;
    if (!permission('power')) text('power-status', '在电脑选中此设备，勾选“电源控制”并更新权限后即可使用。');
    else text('power-status', powerError || power.message || '选择操作后再次确认；倒计时结束前可以取消。');
    if (waiting) text('power-countdown', Math.max(0, Math.ceil((power.executeAt - Date.now() - clockOffset) / 1000)) + ' 秒后' + (powerNames[power.action] || '执行'));
    if (powerTicket && Date.now() + clockOffset >= powerTicket.expiresAt) { powerTicket = null; visible('power-confirm', false); text('power-status', '确认已过期，请重新选择操作。'); }
  }
  async function preparePower(action) {
    if (!permission('power') || powerBusy || !state || !powerNames[action]) return;
    powerBusy = true; powerTicket = null; powerError = ''; renderControls();
    try {
      var result = await request('/api/power/prepare', { action: action, requestId: requestId() }, true);
      powerTicket = { id: result.ticketId, expiresAt: result.expiresAt, action: action };
      text('power-confirm-title', '确认' + powerNames[action] + '这台电脑？');
      text('power-confirm-detail', action === 'lock' ? '确认后 3 秒锁屏，私人内容会隐藏。' : action === 'sleep' ? '确认后 5 秒进入睡眠，伴随屏会断开连接。' : '请先保存电脑上的工作。确认后 15 秒' + powerNames[action] + '，不会强制关闭未保存的应用。');
      text('power-commit', '确认' + powerNames[action]);
    } catch (error) { controlError(error, 'power-status'); }
    finally { powerBusy = false; renderControls(); }
  }
  async function submitPower(cancel) {
    var power = state && state.controls && state.controls.power || {}, prepared = powerTicket;
    if (!permission('power') || powerBusy || cancel && (!power.id || power.status !== 'pending') || !cancel && (!prepared || Date.now() + clockOffset >= prepared.expiresAt)) return;
    powerBusy = true; powerTicket = null; powerError = ''; controlsVersion++; renderControls();
    try {
      var result = await request('/api/power/' + (cancel ? 'cancel' : 'commit'), cancel ? { jobId: power.id } : { ticketId: prepared.id, requestId: requestId() }, true);
      controlsVersion++; if (state) { state.controls = state.controls || {}; state.controls.power = result; }
    } catch (error) { controlError(error, 'power-status'); }
    finally { powerBusy = false; renderControls(); schedule(0); }
  }
  function renderWidgets() { if (window.PrismCompanionWidgets) window.PrismCompanionWidgets.render(state, session && session.grants || {}); }
  function renderControlCenter() {
    var v = state && state.volume || {}, context = state && state.context || {}, focus = state && state.focus || {};
    if (!volumeEditing && !pending.volume && v.available) $('control-volume').value = Math.round(v.data.value);
    $('control-volume').disabled = !permission('control') || !v.available || !!pending.volume || blocked();
    text('control-volume-value', volumeEditing ? $('control-volume').value + '%' : v.available ? percent(v.data.value) : '等待电脑音量');
    ['desktop-next','desktop-overview','desktop-show','desktop-maximize','desktop-restore','desktop-previous-space','desktop-next-space'].forEach(function (id) { $(id).disabled = !permission('control') || !context.available || !!pending['desktop.execute'] || blocked(); });
    text('desktop-control-status', context.available ? '当前 · ' + context.appName : '等待电脑前台窗口');
    text('control-focus-time', focus.available ? duration(focus.deadline > 0 ? Math.max(0, focus.deadline - (Date.now() + clockOffset) / 1000) : focus.remaining) : '--:--');
    text('control-focus-toggle', focus.deadline > 0 ? '暂停' : '开始');
  }

  async function write(key, path, data, message, onSuccess) {
    if (writesBlocked) { feedback('本次会话操作额度已用尽，请在电脑解除配对后重新配对。同步仍可继续。', true); return; }
    if (cooling()) { feedback(rateNotice(), true, 'cooldown'); schedule(0); return; }
    if (!session || !state || document.hidden || versionChanged || pending[key]) return;
    var generation = epoch; pending[key] = true; data.requestId = requestId(); renderState(); if (apps) renderApps(); feedback('', false);
    try { var result = await request(path, data); if (generation !== epoch) return; if (key === 'settings' && result.revision != null) state.settings = result; if (key === 'lyricsStyle' && result.revision != null) state.lyricsSettings = result; if (onSuccess) onSuccess(result); feedback(result.message || message, false); }
    catch (error) { if (error.status === 409 && error.code === 'conflict' && key === 'notes') { noteConflict = true; visible('note-reload', true); } unavailable(error, true); }
    finally { if (generation === epoch) { delete pending[key]; if (state) renderState(); if (apps) renderApps(); } schedule(0); }
  }
  function executeAction(action) {
      if (action === 'volume.up' || action === 'volume.down') {
        var generation = epoch;
        volumeTail = volumeTail.then(function () { if (generation === epoch && session && !document.hidden) return write('volume', '/api/action', { action: action }, '音量请求已发送，等待电脑读回'); });
      } else write(action, '/api/action', { action: action }, '请求已发送，等待电脑读回状态');
  }
  actionButtons.forEach(function (button) { button.onclick = function () { executeAction(button.dataset.action); }; });
  $('task-form').onsubmit = function (event) { event.preventDefault(); var value = $('task-text').value.trim(); if (!value || !state || !(state.tasks || {}).available) return; write('tasks', '/api/tasks', { command: 'add', text: value, expectedRevision: state.tasks.revision }, '待办已保存到电脑', function () { if ($('task-text').value.trim() === value) $('task-text').value = ''; }); };
  $('note-text').oninput = function () { noteDirty = true; if (state) renderNotes(); };
  $('note-save').onclick = function () { var value = $('note-text').value; write('notes', '/api/notes', { text: value, expectedRevision: noteRevision }, '随手记已保存到电脑', function (result) { if ($('note-text').value === value) { noteDirty = false; noteConflict = false; state.notes.text = value; if (result.revision != null) state.notes.revision = result.revision; } }); };
  $('note-reload').onclick = function () { noteDirty = false; noteConflict = false; renderNotes(); };
  $('theme-toggle').onclick = function () { if (state) write('settings', '/api/settings', { expectedRevision: state.settings.revision, changes: { theme: state.settings.theme === 'Astro' ? 'Sky' : 'Astro' } }, '电脑主题已更新'); };
  $('motion-toggle').onclick = function () { if (state) write('settings', '/api/settings', { expectedRevision: state.settings.revision, changes: { manualPaused: !state.settings.manualPaused } }, '电脑动效设置已更新'); };
  $('apps-tab').onclick = $('all-apps').onclick = function () { showPage('apps'); };
  $('home-tab').onclick = $('apps-back').onclick = function () { showPage('home'); };
  $('work-tab').onclick = function () { showPage('work'); }; $('notes-tab').onclick = function () { showPage('notes'); };
  ['pointer', 'widgets', 'computer'].forEach(function (name) { $(name + '-tab').onclick = function () { showPage(name); }; });
    if (window.PrismCompanionSettings) window.PrismCompanionSettings.init(write);
  $('screen-open').onclick = function () { showPage('pointer'); };
  $('appearance-tab').onclick = function () { showPage('appearance'); };
  $('appearance-back').onclick = $('touch-computer').onclick = function () { showPage('computer'); };
  $('local-widgets').onclick = function () { showPage('widgets'); };
  $('widgets-back').onclick = function () { showPage('home'); };
  $('touch-more').onclick = function () { var open = $('touch-extra').hidden; visible('touch-extra', open); this.setAttribute('aria-expanded', String(open)); if (window.PrismGlassOptics) window.PrismGlassOptics.invalidate(); };
  $('control-volume').oninput = function () { volumeEditing = true; text('control-volume-value', this.value + '%'); };
  $('control-volume').onchange = function () { volumeEditing = false; write('volume','/api/action',{action:'volume.set',value:Number(this.value)},'音量已发送，等待电脑读回'); };
  [['desktop-next','next-window'],['desktop-overview','task-view'],['desktop-show','show-desktop'],['desktop-maximize','maximize'],['desktop-restore','restore'],['desktop-previous-space','previous-desktop'],['desktop-next-space','next-desktop']].forEach(function (entry) { $(entry[0]).onclick = function () { var context = state && state.context || {}; if (!context.available || !permission('control')) return; write('desktop.execute','/api/action',{action:'desktop.execute',contextId:context.id,command:entry[1]},'窗口操作已发送'); }; });
  window.addEventListener('companion-widget-navigate', function (event) { if (event.detail && event.detail.page === 'work') showPage('work'); });
  window.addEventListener('companion-widget-immerse', function () { setPresentation(true); });
  $('home-focus').onclick = function () { showPage('work'); };
  $('pointer-enable').onclick = beginPointer; $('pointer-stop').onclick = function () { stopPointer(true); };
  $('screen-keyboard-toggle').onclick = function () { if (!permission('keyboard') || !pointerLease) return; var open = $('screen-keyboard').hidden; keyboardPanel(open); if (open) $('screen-text').focus(); };
  $('screen-text').oncompositionstart = function () { keyboardComposing = true; };
  $('screen-text').oncompositionend = function () { keyboardComposing = false; };
  $('screen-text').oninput = function () { if (pointerLease && Date.now() - pointerLastSent > 5000) pointerAction('move', { dx: 0, dy: 0 }); };
  $('screen-text-form').onsubmit = async function (event) {
    event.preventDefault(); if (keyboardComposing || keyboardSending || !permission('keyboard') || !pointerLease) return;
    var input = $('screen-text'), value = input.value;
    if (!value || value.length > 256 || /[\x00-\x1f\x7f-\x9f]/.test(value)) { text('keyboard-status', '请输入 1–256 个字符；回车请用下方按键。'); return; }
    if (pointerBusy || pointerHeld || pointerDragWanted) { text('keyboard-status', '请先结束当前触控操作，再发送文字。'); return; }
    var generation = pointerGeneration, authGeneration = epoch; keyboardSending = true; renderKeyboard(); text('keyboard-status', '正在发送文字…');
    var sent = await sendPointer('text', { text: value });
    if (generation !== pointerGeneration || authGeneration !== epoch) return;
    keyboardSending = false; if (sent && input.value === value) input.value = ''; renderKeyboard();
    if (permission('keyboard') && pointerLease) text('keyboard-status', sent ? '文字已发送。' : '未确认发送，请查看电脑后再操作。');
  };
  remoteKeys.forEach(function (button) { button.onclick = function () { if (!permission('keyboard') || keyboardSending) return; endPointerDrag(); pointerAction('key', { key: button.getAttribute('data-remote-key') }); }; });
  $('pointer-speed').oninput = function () { var value = Number(this.value); if (!isFinite(value)) value = 100; value = Math.max(50, Math.min(200, value)); pointerSpeed = value / 100; text('pointer-speed-value', value + '%'); pointerDelta = null; pointerSamples = []; };
  $('pointer-natural').checked = true; $('pointer-natural').onchange = function () { pointerNatural = this.checked; pointerDelta = null; pointerSamples = []; };
  ['left', 'right'].forEach(function (button) { $('pointer-' + button).onclick = function () { clickPointer(button); }; });
  $('pointer-surface').onpointerdown = function (event) {
    if (!pointerLease || wheelContact) return; event.preventDefault();
    fingers[event.pointerId] = { x: event.clientX, y: event.clientY };
    if (Object.keys(fingers).length > 2) { stopPointer(true); return; }
    if (!gesture) {
      var doubleTap = pointerLastTap && Date.now() - pointerLastTap.at <= 300 && Math.abs(event.clientX - pointerLastTap.x) + Math.abs(event.clientY - pointerLastTap.y) < 24;
      gesture = { at: Date.now(), distance: 0, multi: false, drag: !!doubleTap }; pointerLastTap = null;
      if (doubleTap) { pointerDragWanted = true; pointerAction('drag', { phase: 'start' }); }
    }
    if (Object.keys(fingers).length > 1) { gesture.multi = true; gesture.drag = false; pointerLastTap = null; endPointerDrag(); pointerDelta = null; pointerSamples = []; }
    if (this.setPointerCapture) try { this.setPointerCapture(event.pointerId); } catch (_) { stopPointer(true); }
  };
  $('pointer-surface').onpointermove = pointerMove;
  $('pointer-surface').onpointerup = function (event) { pointerEnd(event, false); };
  $('pointer-surface').onpointercancel = function (event) { pointerEnd(event, true); };
  $('pointer-surface').onlostpointercapture = function (event) { if (fingers[event.pointerId]) stopPointer(true); };
  window.addEventListener('blur', function () { if (pointerLease) stopPointer(true); });
  $('pointer-surface').oncontextmenu = function (event) { event.preventDefault(); };
  $('pointer-surface').onkeydown = function (event) {
    if (event.key === 'Escape') { stopPointer(true); return; }
    var moves = { ArrowLeft: [-20, 0], ArrowRight: [20, 0], ArrowUp: [0, -20], ArrowDown: [0, 20] }, move = moves[event.key];
    if (move) { event.preventDefault(); addPointerDelta('move', move[0], move[1]); }
    else if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); clickPointer('left'); }
  };
  $('pointer-wheel').onpointerdown = function (event) {
    if (!pointerLease || wheelContact || Object.keys(fingers).length) return;
    event.preventDefault(); endPointerDrag(); pointerSamples = []; pointerDelta = null; wheelContact = { id: event.pointerId, y: event.clientY };
    if (this.setPointerCapture) try { this.setPointerCapture(event.pointerId); } catch (_) { stopPointer(true); }
  };
  $('pointer-wheel').onpointermove = function (event) {
    if (!pointerLease || !wheelContact || wheelContact.id !== event.pointerId) return;
    event.preventDefault(); addPointerDelta('scroll', 0, (event.clientY - wheelContact.y) * (pointerNatural ? 1 : -1)); wheelContact.y = event.clientY;
  };
  $('pointer-wheel').onpointerup = function (event) { if (wheelContact && wheelContact.id === event.pointerId) wheelContact = null; };
  $('pointer-wheel').onpointercancel = $('pointer-wheel').onlostpointercapture = function (event) { if (wheelContact && wheelContact.id === event.pointerId) stopPointer(true); };
  $('pointer-wheel').oncontextmenu = function (event) { event.preventDefault(); };
  $('pointer-wheel').onkeydown = function (event) {
    if (event.key === 'Escape') { stopPointer(true); return; }
    var delta = { ArrowUp: 24, ArrowDown: -24, PageUp: 120, PageDown: -120 }[event.key];
    if (pointerLease && delta) { event.preventDefault(); addPointerDelta('scroll', 0, delta); }
  };
  $('wheel-up').onclick = function () { if (pointerLease) addPointerDelta('scroll', 0, 24); };
  $('wheel-down').onclick = function () { if (pointerLease) addPointerDelta('scroll', 0, -24); };
  Object.keys(powerNames).forEach(function (action) { $('power-' + action).onclick = function () { preparePower(action); }; });
  $('power-commit').onclick = function () { submitPower(false); }; $('power-cancel').onclick = function () { submitPower(true); };
  $('power-dismiss').onclick = function () { powerTicket = null; renderControls(); };
  function searchApps() { clearTimeout(searchTimer); if (!composing) searchTimer = setTimeout(function () { appPage = 0; renderApps(); }, 180); }
  $('app-search').oncompositionstart = function () { composing = true; clearTimeout(searchTimer); };
  $('app-search').oncompositionend = function () { composing = false; searchApps(); };
  $('app-search').oninput = function (event) { if (!event || !event.isComposing) searchApps(); };
  $('apps-prev').onclick = function () { if (appPage > 0) { appPage--; renderApps(); $('main-content').scrollTop = 0; } };
  $('apps-next').onclick = function () { if (!$('apps-next').disabled) { appPage++; renderApps(); $('main-content').scrollTop = 0; } };
  $('touch-scene').onchange = function () { var next = this.value; if (['auto', 'general', 'browser', 'editor', 'music', 'focus'].indexOf(next) >= 0) { scene = next; renderTouchBar(); } };
  $('touch-pin').onclick = function () { if (!session || !state) return; var next = (state.context || {}).scene; scene = scene !== 'auto' ? 'auto' : ['general', 'browser', 'editor', 'music', 'focus'].indexOf(next) >= 0 ? next : 'general'; $('touch-scene').value = scene; renderTouchBar(); };
  ['all', 'open', 'done'].forEach(function (filter) { $('task-filter-' + filter).onclick = function () { if (state) { taskFilter = filter; renderTasks(); } }; });
  $('presentation-toggle').onclick = function () { setPresentation(document.body.dataset.presentation !== 'true'); };
  $('music-immerse').onclick = function () { $('presentation-mode').value = 'lyrics'; setPresentation(true); };
  $('home-tasks').onclick = function () { showPage('work'); };
  $('home-tools').onclick = function () { showPage('widgets'); };
  $('presentation-exit').onclick = function () { setPresentation(false); };
  $('presentation-mode').onchange = function () { setPresentation(document.body.dataset.presentation === 'true'); };
  document.querySelector('.brand').onclick = function (event) { event.preventDefault(); showPage('home'); };
  $('manage-button').onclick = function () { var show = $('connection-settings').hidden; visible('connection-settings', show); $('manage-button').setAttribute('aria-expanded', String(show)); };
  $('settings-close').onclick = function () { visible('connection-settings', false); $('manage-button').setAttribute('aria-expanded', 'false'); };
  $('reduce-motion').checked = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  $('reduce-motion').onchange = function () { document.body.dataset.reduced = String($('reduce-motion').checked); if (state) renderState(); };
  $('reduce-motion').onchange();
  $('reconnect').onclick = function () { if (cooling()) { feedback(rateNotice(), true, 'cooldown'); schedule(0); return; } clearPrivate(true); feedback('', false); connection('正在连接', false); schedule(0); };
  $('logout').onclick = async function () {
    if (!session || $('logout').disabled) return;
    $('logout').disabled = true;
    try { await request('/api/logout', { requestId: requestId() }); clearPrivate(); resumePage = ''; resumeScene = 'auto'; connection('已解除配对', false); feedback('此设备已解除配对。', false); }
    catch (error) { clearPrivate(); connection('连接已关闭', false); feedback(error.cancelled ? '' : '尚未确认电脑已撤销会话，请在电脑端撤销此设备。', true); }
  };

  function pairingFailed(error) {
    if (error.cancelled) return;
    ticket = ''; pairing = false; pairDeadline = 0; clearTimeout(pairTimer); unavailable(error, false);
    $('pair-submit').disabled = false; $('pair-code').disabled = false; $('device-name').disabled = false; text('pair-submit', '请求配对'); text('pair-status', error.message);
    $('pair-scan').disabled = false; visible('pair-form', !error.network && error.status !== 423);
    if (!error.network && error.status !== 423) connection('配对未完成', false);
  }
  async function pollPair() {
    if (!ticket || document.hidden) return;
    try {
      var result = await request('/api/pair', { ticket: ticket });
      if (result.status === 'paired') { ticket = ''; pairing = false; pairDeadline = 0; writesBlocked = false; $('pair-code').value = ''; text('pair-status', '电脑已批准，正在同步…'); schedule(0); }
      else if (result.status === 'pending') {
        if (Date.now() >= pairDeadline) throw new Error('等待电脑批准已超时。请在电脑重新开启配对，使用新的二维码或配对码。');
        if (Date.now() >= pairDeadline - 90000) text('pair-status', '仍在等待电脑批准：请回到电脑的伴随屏窗口，选择共享内容并批准此设备。本次请求从发送起两分钟有效。'); pairTimer = setTimeout(pollPair, 2000);
      }
      else throw new Error('配对状态无效，请重新输入配对码。');
    } catch (error) { pairingFailed(error); }
  }
  $('pair-code').oninput = function () {
    var value = this.value, normalized = value.replace(/\s/g, ''), start = this.selectionStart, end = this.selectionEnd;
    if (value !== normalized) { this.value = normalized; if (typeof start === 'number') this.setSelectionRange(value.slice(0, start).replace(/\s/g, '').length, value.slice(0, end).replace(/\s/g, '').length); }
  };
  $('pair-form').onsubmit = async function (event) {
    event.preventDefault(); if ($('pair-submit').disabled || document.hidden) return;
    var code = $('pair-code').value.replace(/\s/g, ''), name = $('device-name').value.trim(); if (!/^\d{6}$/.test(code) || !name) { text('pair-status', '请输入六位数字配对码和设备名称，可直接粘贴带空格的配对码。'); return; }
    clearPrivate(); pairing = true; visible('pair-form', true); $('pair-scan').disabled = true; $('pair-submit').disabled = true; $('pair-code').disabled = true; $('device-name').disabled = true; text('pair-hint', '确认一次，日常自动连接。'); text('pair-status', '正在发送配对请求…'); feedback('', false);
    try { var result = await request('/api/pair', { code: code, name: name }); if (result.status !== 'pending' || typeof result.ticket !== 'string') throw new Error('配对请求未建立，请重试。'); ticket = result.ticket; pairDeadline = Date.now() + 120000; $('pair-code').value = ''; text('pair-submit', '等待批准'); text('pair-status', '请在两分钟内回到电脑的伴随屏窗口，选择共享内容并批准此设备。'); connection('等待电脑批准', false); pairTimer = setTimeout(pollPair, 2000); }
    catch (error) { pairingFailed(error); if (!error.cancelled && error.code === 'pair_code') $('pair-code').value = code; }
  };

  $('pair-scan').onclick = function () {
    if (!window.PrismCompanionScan) { text('pair-status', '扫码组件未加载，请重新载入页面或输入配对码。'); return; }
    window.PrismCompanionScan.start(function (code) { $('pair-code').value = code; $('pair-form').onsubmit({ preventDefault: function () {} }); });
  };

  function prefillPairCode() {
    var fragment = window.location.hash, match = /^#pair=(\d{6})$/.exec(fragment);
    if (fragment) window.history.replaceState(null, '', window.location.pathname + window.location.search);
    if (match && !document.hidden) { $('pair-code').value = match[1]; text('pair-status', '扫码已填入配对码。确认设备名称后点击“请求配对”，再到电脑批准。'); }
  }

  function tick() {
    if (document.hidden) return;
    if (cooling() && feedbackKind === 'cooldown') feedback(rateNotice(), true, 'cooldown');
    var now = new Date(Date.now() + clockOffset), day = now.toDateString(); text('clock', ('0' + now.getHours()).slice(-2) + ':' + ('0' + now.getMinutes()).slice(-2));
    if (calendarDay !== day) {
      calendarDay = day; text('today-label', now.toLocaleDateString('zh-CN', { month: 'long', day: 'numeric', weekday: 'long' })); text('calendar-month', now.getFullYear() + ' / ' + ('0' + (now.getMonth() + 1)).slice(-2));
      var days = new Date(now.getFullYear(), now.getMonth() + 1, 0).getDate(), offset = (new Date(now.getFullYear(), now.getMonth(), 1).getDay() + 6) % 7;
      $('calendar-days').textContent = '';
      for (var i = 0; i < offset + days; i++) { var cell = document.createElement('span'); if (i >= offset) { var date = i - offset + 1; cell.textContent = date; cell.setAttribute('role', 'listitem'); cell.setAttribute('aria-label', (now.getMonth() + 1) + '月' + date + '日'); if (date === now.getDate()) { cell.className = 'today'; cell.setAttribute('aria-current', 'date'); } else if (i % 7 >= 5) cell.className = 'weekend'; } else cell.setAttribute('aria-hidden', 'true'); $('calendar-days').appendChild(cell); }
    }
    if (state) { renderFocus(); renderControls(); renderMusicProgress(); }
    if (pointerLease && Date.now() + clockOffset >= pointerExpires) stopPointer(true);
    renderWidgets();
  }
  document.addEventListener('visibilitychange', function () { if (document.hidden) { clearPrivate(); connection('页面已隐藏', false); } else { connection('正在重新连接', false); tick(); schedule(0); } });
  window.addEventListener('pagehide', clearPrivate);
  window.addEventListener('pageshow', function () { if (!document.hidden) schedule(0); });
  window.addEventListener('online', function () { if (!document.hidden) schedule(0); });
  window.addEventListener('offline', function () { clearPrivate(true); connection('网络已断开', false); text('pair-hint', '网络恢复后会重新连接，未完成操作不会自动重试。'); });
  window.addEventListener('beforeunload', function (event) { if (noteDirty || $('task-text').value.trim()) { event.preventDefault(); event.returnValue = ''; } });
  function sizeViewport() { document.body.style.setProperty('--viewport-height', (window.visualViewport ? window.visualViewport.height : window.innerHeight) + 'px'); }
  window.addEventListener('resize', sizeViewport); if (window.visualViewport) window.visualViewport.addEventListener('resize', sizeViewport);
  sizeViewport(); clearPrivate(); prefillPairCode(); tick(); setInterval(tick, 1000); schedule(0);
}());
