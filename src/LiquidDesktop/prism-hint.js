/* One accessible hint per host. Native desktop hosts use the shared DockLabel glass. */
(() => {
  'use strict';
  const bridge = window.chrome?.webview, generatedNames = new WeakMap();
  const portal = document.createElement('div'), tip = document.createElement('div');
  portal.className = 'prism-hint-portal'; portal.hidden = true;
  tip.id = 'prism-hint'; tip.className = 'prism-hint'; tip.setAttribute('role', 'tooltip');
  tip.setAttribute('aria-hidden', 'true'); portal.append(tip); document.body.append(portal);
  let target = null, pending = null, timer = 0, native = false, keyboard = false, keyboardMode = true, touchUntil = 0, pointerFocusUntil = 0, layer = null, context = null;
  const post = value => bridge?.postMessage('hint:' + (typeof value === 'string' ? value : JSON.stringify(value)));
  function migrate(node) {
    if (!node.hasAttribute('title')) return;
    const text = node.getAttribute('title') || '', previous = generatedNames.get(node);
    if (text) node.dataset.prismHint = text; else delete node.dataset.prismHint;
    if (previous && node.getAttribute('aria-label') === previous) {
      if (text) node.setAttribute('aria-label', text); else node.removeAttribute('aria-label');
      generatedNames.set(node, text);
    } else if (text && node.matches('button,a,[role="button"]') && !node.hasAttribute('aria-label') && !node.hasAttribute('aria-labelledby') && !/[a-z0-9\u3400-\u9fff]/i.test(node.textContent)) {
      node.setAttribute('aria-label', text); generatedNames.set(node, text);
    }
    node.removeAttribute('title');
  }
  function scan(root) {
    if (root.nodeType !== 1 || portal.contains(root)) return;
    if (root.hasAttribute('title')) migrate(root);
    root.querySelectorAll('[title]').forEach(migrate);
  }
  function describe(node, add) {
    const ids = (node.getAttribute('aria-describedby') || '').split(/\s+/).filter(id => id && id !== tip.id);
    if (add) ids.push(tip.id);
    if (ids.length) node.setAttribute('aria-describedby', ids.join(' ')); else node.removeAttribute('aria-describedby');
  }
  function hide() {
    const visible = !portal.hidden;
    clearTimeout(timer); pending = null;
    if (target) describe(target, false);
    target = null; portal.hidden = true; tip.setAttribute('aria-hidden', 'true');
    if (visible && native) post('hide');
    if (visible) window.PrismGlassOptics?.invalidate();
  }
  function show(node, fromKeyboard) {
    clearTimeout(timer); pending = null;
    if (!node?.isConnected || !node.dataset.prismHint || !node.getClientRects().length || node.closest('[inert],[hidden]')) { hide(); return; }
    if (target && target !== node) describe(target, false);
    target = node; keyboard = !!fromKeyboard; tip.textContent = node.dataset.prismHint;
    portal.classList.toggle('prism-hint-native', native); portal.hidden = false; tip.setAttribute('aria-hidden', 'false'); describe(node, true);
    const rect = node.getBoundingClientRect();
    if (native) {
      tip.removeAttribute('data-optical');
      post({ text: tip.textContent, x: rect.x, y: rect.y, w: rect.width, h: rect.height, viewportWidth: innerWidth, viewportHeight: innerHeight, keyboard });
      return;
    }
    if (window.PrismGlassOptics) tip.setAttribute('data-optical', '');
    const viewport = window.visualViewport, left = viewport?.offsetLeft || 0, top = viewport?.offsetTop || 0;
    const width = viewport?.width || innerWidth, height = viewport?.height || innerHeight, margin = 8;
    tip.style.maxWidth = Math.max(1, Math.min(440, width - margin * 2)) + 'px';
    tip.style.maxHeight = Math.max(1, height - margin * 2) + 'px';
    const box = tip.getBoundingClientRect();
    const x = Math.max(left + margin, Math.min(left + width - box.width - margin, rect.left + (rect.width - box.width) / 2));
    let y = rect.bottom + 9;
    if (y + box.height > top + height - margin) y = rect.top - box.height - 9;
    y = Math.max(top + margin, Math.min(top + height - box.height - margin, y));
    tip.style.left = x + 'px'; tip.style.top = y + 'px';
    window.PrismGlassOptics?.invalidate();
  }
  function find(node) { return node?.closest?.('[data-prism-hint]') || null; }
  document.addEventListener('pointerover', event => {
    if (event.pointerType === 'touch' || Date.now() < touchUntil) return;
    const next = find(event.target); if (next === target || next === pending) return;
    hide(); if (next) { pending = next; timer = setTimeout(() => show(next, false), 280); }
  });
  document.addEventListener('pointerout', event => {
    if (keyboard) return;
    const next = find(event.relatedTarget); if (next !== target && next !== pending) hide();
  });
  document.addEventListener('focusin', event => {
    if (Date.now() < Math.max(touchUntil, pointerFocusUntil)) return;
    const next = find(event.target);
    if (!next) { hide(); return; }
    if (keyboardMode) show(next, true);
  });
  document.addEventListener('focusout', event => { if (find(event.relatedTarget) !== target) hide(); });
  document.addEventListener('pointerdown', event => { keyboardMode = false; pointerFocusUntil = Date.now() + 120; if (event.pointerType === 'touch') touchUntil = Date.now() + 700; hide(); }, true);
  document.addEventListener('keydown', event => { keyboardMode = true; if (event.key === 'Escape') hide(); }, true);
  document.addEventListener('scroll', hide, true);
  document.addEventListener('visibilitychange', () => { if (document.hidden) hide(); });
  for (const name of ['blur', 'pagehide', 'resize', 'prism-panel-change']) window.addEventListener(name, hide);
  window.visualViewport?.addEventListener('resize', hide); window.visualViewport?.addEventListener('scroll', hide);
  const observer = new MutationObserver(records => {
    for (const record of records) {
      if (record.type === 'childList') record.addedNodes.forEach(scan);
      else if (record.attributeName === 'title') migrate(record.target);
    }
    if (target && (!target.isConnected || !target.getClientRects().length || !target.dataset.prismHint || target.closest('[hidden],[inert]'))) hide();
    else if (target && target.dataset.prismHint !== tip.textContent) show(target, keyboard);
  });
  scan(document.body); observer.observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['title', 'data-prism-hint', 'hidden', 'inert'] });
  bridge?.addEventListener('message', event => {
    if (event.data?.type === 'hint-host') { hide(); native = event.data.native === true; }
    else if (event.data?.type === 'dismiss' || event.data?.type === 'visibility' && !event.data.visible) hide();
  });
  post('ready');
  window.PrismHint = {
    hide,
    // Copy only this small completed optical surface above DOM content. No second WebGL scene.
    paintOptics(source) {
      if (native || portal.hidden || !target || !tip.hasAttribute('data-optical-surface')) return;
      if (!layer) { layer = document.createElement('canvas'); layer.className = 'prism-hint-surface'; layer.setAttribute('aria-hidden', 'true'); portal.prepend(layer); context = layer.getContext('2d'); }
      const r = tip.getBoundingClientRect(), bounds = source.getBoundingClientRect(), sx = source.width / bounds.width, sy = source.height / bounds.height;
      if (!context || !r.width || !r.height || !isFinite(sx) || !isFinite(sy)) return;
      const w = Math.ceil(r.width * sx), h = Math.ceil(r.height * sy);
      if (layer.width !== w) layer.width = w; if (layer.height !== h) layer.height = h;
      Object.assign(layer.style, { left: r.left + 'px', top: r.top + 'px', width: r.width + 'px', height: r.height + 'px' });
      context.clearRect(0, 0, w, h); context.drawImage(source, (r.left - bounds.left) * sx, (r.top - bounds.top) * sy, r.width * sx, r.height * sy, 0, 0, w, h);
      tip.dataset.prismOptics = 'ready';
    }
  };
})();
