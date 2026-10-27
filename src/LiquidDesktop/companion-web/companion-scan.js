(function () {
  'use strict';
  var panel = document.getElementById('pair-camera'), video = document.getElementById('pair-video');
  var status = document.getElementById('pair-status'), button = document.getElementById('pair-scan');
  var canvas = document.createElement('canvas'), stream = null, timer = 0, limitTimer = 0, generation = 0, decoder = null;

  function stop() {
    generation++; clearTimeout(timer); clearTimeout(limitTimer);
    if (stream) stream.getTracks().forEach(function (track) { track.stop(); });
    stream = null; video.pause(); video.srcObject = null; canvas.width = canvas.height = 0;
    panel.hidden = true; button.disabled = false;
  }
  function loadDecoder() {
    if (window.jsQR) return Promise.resolve();
    if (!decoder) decoder = new Promise(function (resolve, reject) {
      var script = document.createElement('script'); script.src = '/jsqr.js';
      script.onload = function () { if (window.jsQR) resolve(); else { decoder = null; script.remove(); reject(new Error()); } };
      script.onerror = function () { decoder = null; script.remove(); reject(new Error()); };
      document.head.appendChild(script);
    });
    return decoder;
  }
  async function start(onCode) {
    if (button.disabled || document.hidden) return;
    stop(); var current = generation;
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) { status.textContent = '当前窗口无法使用相机，可在下方输入电脑显示的配对码。'; return; }
    panel.hidden = false; button.disabled = true; status.textContent = '请允许使用相机，对准电脑上的配对二维码。画面只在此 iPad 处理。';
    limitTimer = setTimeout(function () { stop(); status.textContent = '扫描已暂停。可重新扫码，或输入电脑显示的配对码。'; }, 60000);
    try {
      var captured = await navigator.mediaDevices.getUserMedia({ audio: false, video: { facingMode: { ideal: 'environment' }, width: { ideal: 640 }, height: { ideal: 480 }, frameRate: { ideal: 15, max: 15 } } });
      if (current !== generation || document.hidden) { captured.getTracks().forEach(function (track) { track.stop(); }); return; }
      stream = captured; video.srcObject = stream; video.muted = true; video.playsInline = true;
      await Promise.all([video.play(), loadDecoder()]);
      if (current !== generation || document.hidden) return;
      status.textContent = '对准电脑的配对二维码，识别后自动请求连接，仍由电脑确认。';
      var context = canvas.getContext('2d');
      function frame() {
        if (current !== generation || document.hidden) return;
        try {
          if (video.readyState >= 2 && video.videoWidth && video.videoHeight) {
            var scale = Math.min(1, 640 / Math.max(video.videoWidth, video.videoHeight));
            canvas.width = Math.max(1, Math.round(video.videoWidth * scale)); canvas.height = Math.max(1, Math.round(video.videoHeight * scale));
            context.drawImage(video, 0, 0, canvas.width, canvas.height);
            var pixels = context.getImageData(0, 0, canvas.width, canvas.height);
            var code = window.jsQR(pixels.data, pixels.width, pixels.height, { inversionAttempts: 'dontInvert' });
            if (code && typeof code.data === 'string' && code.data.length <= 256) {
              var url; try { url = new URL(code.data); } catch (_) {}
              var match = url && /^#pair=(\d{6})$/.exec(url.hash);
              if (match && url.protocol === 'https:' && url.origin === window.location.origin && !url.username && !url.password && !url.search && (url.pathname === '/' || url.pathname === '/index.html')) { stop(); onCode(match[1]); return; }
              status.textContent = '请扫描当前这台电脑的配对二维码；普通入口二维码不含配对码。';
            }
          }
          timer = setTimeout(frame, 160);
        } catch (_) { stop(); status.textContent = '相机识别未完成，可重新扫码或输入配对码。'; }
      }
      frame();
    } catch (error) {
      if (current !== generation) return;
      stop(); status.textContent = error.name === 'NotAllowedError' ? '未获得相机权限。可重新允许相机，或在下方输入配对码。' : '相机暂时无法打开，可重试或输入配对码。';
    }
  }
  document.getElementById('pair-scan-stop').onclick = function () { stop(); status.textContent = '已停止扫描，相机已关闭。'; button.focus(); };
  document.addEventListener('visibilitychange', function () { if (document.hidden) stop(); });
  window.addEventListener('pagehide', stop);
  window.PrismCompanionScan = { start: start, stop: stop };
}());
