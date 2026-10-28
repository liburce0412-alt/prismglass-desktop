(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var video = $('screen-video'), surface = $('pointer-surface'), allowed = false, csrf = '', activePage = false;
  var peer = null, lease = '', generation = 0, heartbeat = 0, timeout = 0, controller = null, busy = false, monitorsLoaded = false, metadataVersion = 0, viewOpened = false;
  function status(message) { $('screen-status').textContent = message; }
  function buttons() { $('screen-start').disabled = !allowed || !activePage || !monitorsLoaded || busy || !!peer; $('screen-stop').disabled = !busy && !peer; $('screen-monitor').disabled = $('screen-quality').disabled = busy || !!peer; $('screen-expand').disabled = !peer; }
  function tools(value) { document.body.dataset.screenTools = String(value); $('screen-tools-toggle').setAttribute('aria-expanded', String(value)); $('screen-tools-toggle').setAttribute('aria-label', value ? '收起投屏控制面板' : '展开投屏控制面板'); }
  function expand(value) { document.body.dataset.screenExpanded = String(value); $('screen-expand').textContent = value ? '退出全屏' : '铺满屏幕'; $('screen-expand').setAttribute('aria-pressed', String(value)); tools(!value); if (window.PrismGlassOptics) window.PrismGlassOptics.invalidate(); }
  function end(id, token) {
    if (!id) return;
    fetch('/api/screen/end', { method: 'POST', credentials: 'same-origin', cache: 'no-store', keepalive: true, headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': token }, body: JSON.stringify({ lease: id }) }).catch(function () {});
  }
  function stop(message) {
    generation++; clearTimeout(heartbeat); clearTimeout(timeout); if (controller) controller.abort(); controller = null;
    if (peer) { peer.ontrack = peer.oniceconnectionstatechange = null; peer.close(); } peer = null;
    if (video.srcObject) video.srcObject.getTracks().forEach(function (track) { track.stop(); });
    video.pause(); video.srcObject = null; video.hidden = true; surface.dataset.screen = 'false'; busy = false; viewOpened = false; expand(false);
    var oldLease = lease; lease = ''; end(oldLease, csrf); buttons();
    status(message || '画面已停止。移动、轻点和滚动仍按触控板方式操作。');
    if (window.PrismGlassOptics) window.PrismGlassOptics.invalidate();
  }
  async function api(path, body, signal) {
    var options = { credentials: 'same-origin', cache: 'no-store', signal: signal };
    if (body) { options.method = 'POST'; options.headers = { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf }; options.body = JSON.stringify(body); }
    var response = await fetch(path, options), data = await response.json();
    if (!response.ok) throw new Error(data.message || '投屏连接失败，请重新开始。');
    return data;
  }
  async function loadMonitors() {
    var version = ++metadataVersion; monitorsLoaded = false; buttons(); status('正在读取电脑显示器…');
    try {
      var info = await api('/api/screen'); if (version !== metadataVersion) return;
      if (!info.available || !info.monitors || !info.monitors.length) throw new Error(info.message || '电脑尚未配置投屏。');
      var selected = $('screen-monitor').value; $('screen-monitor').textContent = '';
      info.monitors.forEach(function (monitor) { var option = document.createElement('option'); option.value = String(monitor.id); option.textContent = monitor.name; $('screen-monitor').appendChild(option); });
      $('screen-monitor').value = info.monitors.some(function (monitor) { return String(monitor.id) === selected; }) ? selected : String(info.monitors[0].id);
      monitorsLoaded = true; status('选择画面与清晰度，再轻点“开始投屏”。');
    } catch (error) { if (version === metadataVersion) status(error.message || '无法读取显示器，请重新进入此页。'); }
    if (version === metadataVersion) buttons();
  }
  function gather(connection) {
    return new Promise(function (resolve, reject) {
      if (connection.iceGatheringState === 'complete') { resolve(); return; }
      var deadline = setTimeout(function () { connection.removeEventListener('icegatheringstatechange', changed); reject(new Error('局域网连接准备超时，请重试。')); }, 5000);
      function changed() { if (connection.iceGatheringState === 'complete') { clearTimeout(deadline); connection.removeEventListener('icegatheringstatechange', changed); resolve(); } }
      connection.addEventListener('icegatheringstatechange', changed);
    });
  }
  function pulse(version) {
    heartbeat = setTimeout(async function () {
      if (version !== generation || !lease) return;
      try { await api('/api/screen/ping', { lease: lease }, controller.signal); if (version === generation) pulse(version); }
      catch (error) { if (version === generation) stop(error.message); }
    }, 4000);
  }
  async function start() {
    if (!allowed || !activePage || !monitorsLoaded || busy || peer || document.hidden) return;
    if (!window.RTCPeerConnection) { status('当前浏览器不支持视频投屏，请使用 Safari。'); return; }
    var version = ++generation, token = csrf; busy = true; controller = new AbortController(); buttons(); status('正在准备局域网画面…');
    timeout = setTimeout(function () { if (version === generation) stop('投屏连接超时，请检查电脑与 iPad 是否在同一网络。'); }, 20000);
    try {
      var connection = new RTCPeerConnection({ iceServers: [] }); peer = connection;
      connection.addTransceiver('video', { direction: 'recvonly' });
      connection.ontrack = function (event) {
        if (version !== generation) return;
        video.srcObject = event.streams && event.streams[0] || new MediaStream([event.track]); video.hidden = false; surface.dataset.screen = 'true';
        video.play().catch(function () { if (version === generation) stop('Safari 未能播放画面，请轻点“开始投屏”重试。'); });
      };
      connection.oniceconnectionstatechange = function () {
        if (version !== generation) return;
        if (connection.iceConnectionState === 'failed' || connection.iceConnectionState === 'disconnected') stop('画面连接已中断，已停止采集。请重新开始。');
      };
      await connection.setLocalDescription(await connection.createOffer()); await gather(connection);
      if (version !== generation) return;
      var result = await api('/api/screen/begin', { monitor: Number($('screen-monitor').value), quality: $('screen-quality').value, offer: connection.localDescription.sdp }, controller.signal);
      if (version !== generation) { end(result.lease, token); return; }
      lease = result.lease; pulse(version); await connection.setRemoteDescription({ type: 'answer', sdp: result.answer });
      if (version !== generation) return;
      busy = false; buttons();
    } catch (error) { if (version === generation) stop(error.message || '投屏启动失败，请重试。'); }
  }
  video.onplaying = function () { if (!peer) return; clearTimeout(timeout); if (!viewOpened) { viewOpened = true; expand(true); } status('正在投屏 · ' + (video.videoWidth || '—') + ' × ' + (video.videoHeight || '—') + ' · 局域网 · 无声音'); };
  $('screen-start').onclick = start; $('screen-stop').onclick = function () { stop(); };
  $('screen-expand').onclick = function () { if (peer) expand(document.body.dataset.screenExpanded !== 'true'); };
  $('screen-tools-toggle').onclick = function () { tools(document.body.dataset.screenTools !== 'true'); };
  $('screen-tools-close').onclick = function () { tools(false); $('screen-tools-toggle').focus(); };
  $('screen-home').onclick = function () { $('home-tab').click(); };
  window.PrismCompanionScreen = {
    render: function (grants, token, visible) {
      var next = !!(grants && grants.screen), refresh = next && visible && (!allowed || !activePage || csrf !== token); activePage = visible;
      if ((!next || !visible || csrf && csrf !== token) && (peer || busy)) stop();
      allowed = next; csrf = token || ''; buttons();
      if (!allowed || !visible) { metadataVersion++; monitorsLoaded = false; buttons(); }
      if (!allowed) status('在电脑为此设备勾选“屏幕画面（投屏）”；鼠标控制需另行允许。');
      else if (refresh) loadMonitors();
    },
    stop: stop,
    clear: function () { stop(); metadataVersion++; allowed = false; csrf = ''; monitorsLoaded = false; $('screen-monitor').textContent = ''; buttons(); }
  };
  document.addEventListener('visibilitychange', function () { if (document.hidden) stop(); });
  window.addEventListener('pagehide', function () { stop(); });
  window.addEventListener('offline', function () { stop('网络已断开，投屏已停止。'); });
  buttons();
}());
