(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var video = $('screen-video'), surface = $('pointer-surface'), allowed = false, csrf = '', activePage = false;
  var peer = null, lease = '', generation = 0, heartbeat = 0, timeout = 0, controller = null, busy = false, monitorsLoaded = false, metadataVersion = 0, viewOpened = false;
  var audioPeer = null, audioAllowed = false, audioBusy = false, audioPlaying = false, audioGeneration = 0, audioTimeout = 0, audioError = false;
  var rememberedMonitor = null, serverInstance = '', recovery = 0, recovering = false;
  var mode = 'mirror', switching = false, available = false, extensionAvailable = false, extensionMessage = '', streamMessage = '', monitorCount = 0;
  function status(message) { $('screen-status').textContent = message; }
  function ready() { return monitorsLoaded && available && (mode === 'extend' ? extensionAvailable : monitorCount > 0 && $('screen-monitor').value !== ''); }
  function buttons() {
    $('screen-audio-toggle').disabled = !audioAllowed || !lease || audioBusy;
    $('screen-audio-toggle').textContent = audioBusy ? '声音连接中…' : audioPeer ? audioPlaying ? '关闭声音' : '点按播放声音' : audioError ? '重试声音' : '开启声音';
    $('screen-start').disabled = !allowed || !activePage || !ready() || switching || busy || !!peer;
    $('screen-start').textContent = mode === 'extend' ? '打开副屏' : '开始投屏';
    $('screen-stop').disabled = !busy && !peer; $('screen-monitor').disabled = $('screen-quality').disabled = switching || busy || !!peer; $('screen-expand').disabled = !peer;
    $('screen-monitor-field').hidden = mode === 'extend';
    $('screen-mode-mirror').setAttribute('aria-pressed', String(mode === 'mirror')); $('screen-mode-extend').setAttribute('aria-pressed', String(mode === 'extend'));
    $('screen-mode-mirror').disabled = $('screen-mode-extend').disabled = !allowed || !activePage || switching || busy;
    $('screen-mode-description').textContent = mode === 'extend' ? '把 iPad 用作独立桌面；停止或断开后关闭副屏。' : '显示主屏幕或其他有线屏幕的画面。';
  }
  function readyMessage() {
    if (!available) return streamMessage || '电脑尚未配置投屏。';
    if (mode === 'extend') return extensionAvailable ? '轻点“打开副屏”，在 iPad 使用独立桌面。' : extensionMessage || '电脑尚未安装可用的虚拟显示驱动，请先在电脑配置副屏。';
    return monitorCount ? '选择主屏幕或其他有线屏幕，再轻点“开始投屏”。' : '没有可投屏的主屏幕或有线屏幕，请检查电脑显示器连接。';
  }
  function tools(value) { document.body.dataset.screenTools = String(value); $('screen-tools-toggle').setAttribute('aria-expanded', String(value)); $('screen-tools-toggle').setAttribute('aria-label', value ? '收起投屏控制面板' : '展开投屏控制面板'); }
  function layout() {
    var body = document.body, keyboard = body.dataset.screenKeyboard === 'true', active = body.dataset.screenExpanded === 'true' || keyboard;
    var viewport = window.visualViewport, width = viewport ? viewport.width : window.innerWidth, height = viewport ? viewport.height : window.innerHeight;
    body.dataset.screenLayout = String(active);
    if (active) height = Math.max(0, height - (parseFloat(getComputedStyle($('pointer-page')).paddingTop) || 0));
    var ratio = !video.hidden && video.videoHeight > 0 ? video.videoWidth / video.videoHeight : 0, gap = ratio ? width - height * ratio : 0;
    var stretch = $('screen-fit').value === 'fill'; body.dataset.screenFit = stretch ? 'fill' : 'contain';
    var side = active && keyboard && !stretch && gap >= 260;
    body.dataset.screenInput = side ? 'side' : 'bottom';
    body.style.setProperty('--screen-input-width', (side ? Math.min(gap, 360) : 0) + 'px');
    body.style.setProperty('--screen-letterbox-height', Math.max(0, ratio ? height - Math.min(height, width / ratio) - 16 : 0) + 'px');
    body.dataset.screenControls = active && mode === 'mirror' && !keyboard && !stretch && ratio && height - Math.min(height, width / ratio) >= 60 ? 'bottom' : 'top';
    if (window.PrismGlassOptics) window.PrismGlassOptics.invalidate();
  }
  function expand(value) { document.body.dataset.screenExpanded = String(value); $('screen-expand').textContent = value ? '退出全屏' : '全屏观看'; $('screen-expand').setAttribute('aria-pressed', String(value)); tools(!value); layout(); }
  function end(id, token) {
    if (!id) return Promise.resolve('');
    return new Promise(function (resolve) {
      var deadline = setTimeout(function () { done('尚未确认电脑端关闭，请稍后重试；失联会话将由电脑超时清理。'); }, 5000);
      function done(message) { clearTimeout(deadline); resolve(message || ''); }
      fetch('/api/screen/end', { method: 'POST', credentials: 'same-origin', cache: 'no-store', keepalive: true, headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': token }, body: JSON.stringify({ lease: id }) }).then(async function (response) {
        if (response.ok) { done(''); return; }
        try { var result = await response.json(); done(result.message || '电脑端尚未完成关闭，请稍后重试。'); }
        catch (error) { done('无法确认电脑端关闭，请稍后重试。'); }
      }, function () { done('未能联系电脑关闭会话，将等待电脑超时清理。'); });
    });
  }
  function stop(message) {
    if ((peer || busy || lease) && window.PrismCompanionInput) window.PrismCompanionInput.stop(true);
    clearTimeout(recovery); recovery = 0; recovering = false; surface.dataset.recovering = 'false';
    stopAudio(false); generation++; clearTimeout(heartbeat); clearTimeout(timeout); if (controller) controller.abort(); controller = null;
    if (peer) { peer.ontrack = peer.oniceconnectionstatechange = null; peer.close(); } peer = null;
    if (video.srcObject) video.srcObject.getTracks().forEach(function (track) { track.stop(); });
    video.pause(); video.srcObject = null; video.hidden = true; surface.dataset.screen = 'false'; busy = false; viewOpened = false; expand(false);
    var oldLease = lease, stoppedGeneration = generation; lease = ''; var ending = end(oldLease, csrf).then(function (error) { if (error && generation === stoppedGeneration && allowed && activePage) status(error); return error; }); buttons();
    status(message || '画面已停止。再次控制电脑前，请重新启用触控。');
    if (window.PrismGlassOptics) window.PrismGlassOptics.invalidate();
    return ending;
  }
  async function api(path, body, signal, token) {
    var options = { credentials: 'same-origin', cache: 'no-store', signal: signal };
    if (body) { options.method = 'POST'; options.headers = { 'Content-Type': 'application/json', 'X-CSRF-Token': token === undefined ? csrf : token }; options.body = JSON.stringify(body); }
    var response = await fetch(path, options), data = await response.json();
    if (!response.ok) { var error = new Error(data.message || '投屏连接失败，请重新开始。'); error.code = data.error; throw error; }
    return data;
  }
  async function loadMonitors() {
    var version = ++metadataVersion; monitorsLoaded = false; buttons(); status('正在读取电脑显示器…');
    try {
      var info = await api('/api/screen'); if (version !== metadataVersion) return;
      available = !!info.available; extensionAvailable = !!info.extensionAvailable; extensionMessage = info.extensionMessage || ''; streamMessage = info.message || '';
      var monitors = info.monitors || []; monitorCount = monitors.length;
      var sameInstance = !!info.instance && serverInstance === info.instance;
      var selected = rememberedMonitor !== null ? rememberedMonitor : $('screen-monitor').value;
      var requireSelection = rememberedMonitor !== null && (!sameInstance || !monitors.some(function (monitor) { return String(monitor.id) === selected; }));
      serverInstance = info.instance || ''; $('screen-monitor').textContent = '';
      if (requireSelection) { var missing = document.createElement('option'); missing.value = ''; missing.textContent = '来源已变化，请重新选择'; $('screen-monitor').appendChild(missing); }
      monitors.forEach(function (monitor) { var option = document.createElement('option'); option.value = String(monitor.id); option.textContent = monitor.name; $('screen-monitor').appendChild(option); });
      $('screen-monitor').value = requireSelection ? '' : monitors.some(function (monitor) { return String(monitor.id) === selected; }) ? selected : monitors.length ? String(monitors[0].id) : '';
      monitorsLoaded = true; status(requireSelection && mode === 'mirror' ? '原投屏来源无法确认，请重新选择屏幕。' : readyMessage());
    } catch (error) { if (version === metadataVersion) status(error.message || '无法读取显示器，请重新进入此页。'); }
    if (version === metadataVersion) buttons();
  }
  async function changeMode(next) {
    if (next === mode || !allowed || !activePage || busy || switching) return;
    mode = next; switching = true; monitorsLoaded = false; var version = ++metadataVersion;
    var stopError = await stop('正在切换画面模式…'); switching = false;
    if (version !== metadataVersion || !allowed || !activePage) { buttons(); return; }
    await loadMonitors();
    if (stopError && metadataVersion === version + 1 && allowed && activePage) status(stopError);
  }
  function gather(connection) {
    return new Promise(function (resolve, reject) {
      if (connection.iceGatheringState === 'complete') { resolve(); return; }
      var deadline = setTimeout(function () { connection.removeEventListener('icegatheringstatechange', changed); reject(new Error('局域网连接准备超时，请重试。')); }, 5000);
      function changed() { if (connection.iceGatheringState === 'complete') { clearTimeout(deadline); connection.removeEventListener('icegatheringstatechange', changed); resolve(); } }
      connection.addEventListener('icegatheringstatechange', changed);
    });
  }
  function audioStatus(message, error) { audioError = !!error; $('screen-audio-status').textContent = message; $('screen-audio-toggle').title = message; buttons(); }
  function stopAudio(notify) {
    audioGeneration++; clearTimeout(audioTimeout); audioTimeout = 0; audioBusy = false; audioPlaying = false;
    if (audioPeer) { audioPeer.ontrack = audioPeer.oniceconnectionstatechange = null; audioPeer.close(); } audioPeer = null;
    var player = $('screen-audio'); player.pause(); if (player.srcObject) player.srcObject.getTracks().forEach(function (track) { track.stop(); }); player.srcObject = null;
    var version = audioGeneration;
    if (notify && lease) api('/api/screen/audio/end', {lease:lease,sequence:version}).catch(function () { if (version === audioGeneration) audioStatus('声音关闭尚未确认，停止投屏可结束整个会话。', true); });
    audioStatus('系统声音未开启'); buttons();
  }
  async function playAudio() {
    var version = audioGeneration;
    try { await $('screen-audio').play(); if (version === audioGeneration && audioPeer) { audioPlaying = true; audioStatus('正在播放电脑系统声音'); buttons(); } }
    catch (_) { if (version === audioGeneration) { audioPlaying = false; audioStatus('Safari 尚未允许播放，请点按“播放声音”。'); buttons(); } }
  }
  async function toggleAudio() {
    if (!audioAllowed || !lease || audioBusy) return;
    if (audioPeer) { if (!audioPlaying) await playAudio(); else stopAudio(true); return; }
    var version = ++audioGeneration, screenLease = lease, token = csrf; audioBusy = true; buttons(); audioStatus('正在连接电脑系统声音…');
    audioTimeout = setTimeout(function () { if (version === audioGeneration) { stopAudio(true); audioStatus('声音连接超时，画面继续播放。', true); } }, 12000);
    try {
      var connection = new RTCPeerConnection({iceServers:[]}); audioPeer = connection; connection.addTransceiver('audio',{direction:'recvonly'});
      connection.ontrack = function (event) { if (version !== audioGeneration) return; clearTimeout(audioTimeout); audioTimeout = 0; $('screen-audio').srcObject = event.streams && event.streams[0] || new MediaStream([event.track]); playAudio(); };
      connection.oniceconnectionstatechange = function () { if (version === audioGeneration && connection.iceConnectionState === 'failed') { stopAudio(true); audioStatus('声音连接中断，画面继续播放。', true); } };
      await connection.setLocalDescription(await connection.createOffer()); await gather(connection);
      if (version !== audioGeneration) return;
      var result = await api('/api/screen/audio/begin',{lease:screenLease,sequence:version,offer:connection.localDescription.sdp}, undefined, token);
      if (version !== audioGeneration || lease !== screenLease) { api('/api/screen/audio/end',{lease:screenLease,sequence:version+1}, undefined, token).catch(function () {}); return; }
      await connection.setRemoteDescription({type:'answer',sdp:result.answer});
    } catch (error) { if (version === audioGeneration) { stopAudio(true); audioStatus(error.message || '声音未能开启，画面继续播放。', true); } }
    finally { if (version === audioGeneration) { audioBusy = false; buttons(); } }
  }
  $('screen-audio-toggle').onclick = toggleAudio;
  function pulse(version, retry) {
    heartbeat = setTimeout(async function () {
      if (version !== generation || !lease) return;
      var ping = new AbortController(), parent = controller.signal, cancel = function () { ping.abort(); };
      var deadline = setTimeout(cancel, 2500); parent.addEventListener('abort', cancel);
      var soundVersion = audioGeneration;
      try { var pingResult = await api('/api/screen/ping', { lease: lease, audioSequence: soundVersion, audioEnabled: !!audioPeer }, ping.signal); if (version === generation && soundVersion === audioGeneration && audioPeer && !audioBusy && pingResult.audio === false) { stopAudio(true); audioStatus('系统声音已停止，请重新开启；画面继续播放。', true); } if (version === generation) { pulse(version, false); if (retry && viewOpened && !recovering) video.onplaying(); } }
      catch (error) { if (version === generation) { if (!retry && !error.code) { status('正在重新确认画面连接…'); pulse(version, true); } else stop(error.message || '连接恢复失败，请重试投屏。'); } }
      finally { clearTimeout(deadline); parent.removeEventListener('abort', cancel); }
    }, retry ? 1000 : 4000);
  }
  async function start() {
    if (!allowed || !activePage || !ready() || switching || busy || peer || document.hidden) return;
    if (!window.RTCPeerConnection) { status('当前浏览器不支持视频投屏，请使用 Safari。'); return; }
    if (mode === 'mirror') rememberedMonitor = $('screen-monitor').value;
    var version = ++generation, token = csrf, selectedMode = mode; busy = true; controller = new AbortController(); buttons(); status(mode === 'extend' ? '正在打开 iPad 副屏…' : '正在准备局域网画面…');
    timeout = setTimeout(function () { if (version === generation) stop('投屏连接超时，请检查电脑与 iPad 是否在同一网络。'); }, 20000);
    try {
      var connection = new RTCPeerConnection({ iceServers: [] }); peer = connection;
      connection.addTransceiver('video', { direction: 'recvonly' });
      connection.ontrack = function (event) {
        if (version !== generation) return;
        video.srcObject = event.streams && event.streams[0] || new MediaStream([event.track]); video.hidden = false; surface.dataset.screen = 'true';
        video.play().catch(function () { if (version === generation) stop('Safari 未能播放画面，请重新开始。'); });
      };
      connection.oniceconnectionstatechange = function () {
        if (version !== generation) return;
        if (connection.iceConnectionState === 'failed') stop('画面连接失败，请重试投屏。');
        else if (connection.iceConnectionState === 'disconnected') {
          if (!recovery) { recovering = true; surface.dataset.recovering = 'true'; status('正在恢复画面…');
            recovery = setTimeout(function () { if (version === generation) stop('画面恢复超时，请重试投屏。'); }, 6000);
          }
        } else if (connection.iceConnectionState === 'connected' || connection.iceConnectionState === 'completed') {
          clearTimeout(recovery); recovery = 0; recovering = false; surface.dataset.recovering = 'false'; if (viewOpened) video.onplaying();
        }
      };
      await connection.setLocalDescription(await connection.createOffer()); await gather(connection);
      if (version !== generation) return;
      // Keep an already submitted start readable so cancellation can close its returned lease.
      var request = { mode: selectedMode, portrait: selectedMode === 'extend' && window.innerHeight > window.innerWidth, monitor: selectedMode === 'extend' ? -1 : Number($('screen-monitor').value), quality: $('screen-quality').value, instance: serverInstance, offer: connection.localDescription.sdp }, result, waitingForApproval = false;
      while (version === generation) {
        try { result = await api('/api/screen/begin', request); break; }
        catch (error) {
          if (error.code !== 'screen_network_pending') throw error;
          if (version !== generation) return;
          if (!waitingForApproval) { waitingForApproval = true; clearTimeout(timeout); timeout = setTimeout(function () { if (version === generation) stop('等待电脑确认已超时，请重新开始投屏。'); }, 120000); }
          status(error.message); await new Promise(function (resolve) { setTimeout(resolve, 1500); });
        }
      }
      if (!result) return;
      if (version !== generation) { var cancelledGeneration = generation, closeError = await end(result.lease, token); if (closeError && generation === cancelledGeneration && !peer && allowed && activePage) status(closeError); return; }
      clearTimeout(timeout); timeout = setTimeout(function () { if (version === generation) stop('投屏连接超时，请检查电脑与 iPad 是否在同一网络。'); }, 20000);
      lease = result.lease; pulse(version); await connection.setRemoteDescription({ type: 'answer', sdp: result.answer });
      if (version !== generation) return;
      busy = false; buttons();
    } catch (error) { if (version === generation) { await stop(error.message || '投屏启动失败，请重试。'); if (error.code === 'screen_source_changed' && allowed && activePage) loadMonitors(); } }
  }
  video.onloadedmetadata = video.onresize = layout;
  video.onplaying = function () { if (!peer || recovering) return; clearTimeout(timeout); if (!viewOpened) { viewOpened = true; expand(true); } status((mode === 'extend' ? '副屏已打开 · ' : '正在投屏 · ') + (video.videoWidth || '—') + ' × ' + (video.videoHeight || '—') + ' · 局域网'); };
  $('screen-monitor').onchange = function () { rememberedMonitor = this.value; buttons(); if (ready() && !peer && !busy) status(readyMessage()); };
  $('screen-fit').onchange = layout;
  $('screen-tools').onkeydown = function (event) { if (event.key === 'Escape' && !event.defaultPrevented && !event.isComposing && event.keyCode !== 229) { event.preventDefault(); tools(false); $('screen-tools-toggle').focus(); } };
  $('screen-start').onclick = start; $('screen-stop').onclick = function () { stop(); };
  $('screen-mode-mirror').onclick = function () { return changeMode('mirror'); }; $('screen-mode-extend').onclick = function () { return changeMode('extend'); };
  $('screen-expand').onclick = function () { if (peer) expand(document.body.dataset.screenExpanded !== 'true'); };
  $('screen-tools-toggle').onclick = function () { tools(document.body.dataset.screenTools !== 'true'); };
  $('screen-tools-close').onclick = function () { tools(false); $('screen-tools-toggle').focus(); };
  $('screen-home').onclick = function () { $('home-tab').click(); };
  window.PrismCompanionScreen = {
    point: function (x, y) {
      if (!peer || !lease || recovering || video.hidden || video.paused || video.readyState < 2 || !video.videoWidth || !video.videoHeight) return null;
      var rect = video.getBoundingClientRect(), style = getComputedStyle(video), w = rect.width, h = rect.height;
      if (!w || !h) return null;
      if (style.objectFit !== 'fill') { var scale = Math.min(w / video.videoWidth, h / video.videoHeight); w = video.videoWidth * scale; h = video.videoHeight * scale; }
      var position = style.objectPosition.split(' '), left = rect.left + (rect.width - w) * (parseFloat(position[0]) || 0) / 100, top = rect.top + (rect.height - h) * (parseFloat(position[1]) || 0) / 100;
      if (x < left || y < top || x >= left + w || y >= top + h) return null;
      return { geometry: [left, top, w, h, lease].join('|'), command: { screenLease: lease, x: Math.round((x - left) / w * 65535), y: Math.round((y - top) / h * 65535) } };
    },
    layout: layout,
    closeTools: function () { tools(false); },
    render: function (grants, token, visible) {
      audioAllowed = !!(grants && grants.audio); if (!audioAllowed && audioPeer) stopAudio(true);
      var next = !!(grants && grants.screen), refresh = next && visible && (!allowed || !activePage || csrf !== token); activePage = visible;
      if ((!next || !visible || csrf && csrf !== token) && (peer || busy)) stop();
      allowed = next; csrf = token || ''; buttons();
      if (!allowed || !visible) { metadataVersion++; monitorsLoaded = false; buttons(); }
      if (!allowed) status('在电脑为此设备勾选“屏幕画面（投屏）”；鼠标控制需另行允许。');
      else if (refresh) loadMonitors();
    },
    stop: stop,
    clear: function () { audioAllowed = false; stop(); metadataVersion++; allowed = false; csrf = ''; monitorsLoaded = false; $('screen-monitor').textContent = ''; buttons(); }
  };
  document.addEventListener('visibilitychange', function () { if (document.hidden) stop(); });
  window.addEventListener('pagehide', function () { stop(); });
  window.addEventListener('offline', function () { stop('网络已断开，投屏已停止。'); });
  tools(true); buttons();
}());
