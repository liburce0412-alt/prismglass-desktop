'use strict';
PrismGlassRenderer(document.getElementById('scene'),s=>window.chrome.webview.postMessage(s),receive=>window.chrome.webview.addEventListener('message',e=>receive(e.data)));
