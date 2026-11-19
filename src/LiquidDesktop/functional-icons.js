/* Animate the existing artwork; its viewBox, strokes and live <use> state belong to the host. */
(() => {
  'use strict';
  const live = new Map(), media = window.matchMedia('(prefers-reduced-motion: reduce)');
  const partPoses = {
    rays: 'rotate(18deg)', hand: 'rotate(28deg)', note: 'translateY(-1.25px)',
    pen: 'translate(1px,-1px)', plus: 'rotate(90deg)', leaf: 'rotate(-8deg)',
    barLow: 'scaleY(.8)', barHigh: 'scaleY(1.12)', flap: 'translateY(-1px)',
    arrow: 'translateY(1.5px)', tick: 'translateX(1px)', date: 'translateY(-1px)',
    link: 'translate(1px,-1px)', lid: 'translateY(-.5px) rotate(-8deg)',
    cursor: 'translate(1px,1px)', dial: 'translateX(2px)'
  };
  let settingsPaused = false, runtimePaused = false;
  const still = () => document.hidden || settingsPaused || runtimePaused || media.matches || document.body.dataset.reduced === 'true' || ['reduced-motion', 'prism-reduced-motion', 'motion-paused'].some(name => document.body.classList.contains(name));
  function bind(svg) {
    if (live.has(svg) || !svg.firstElementChild) return;
    const target = svg.closest('button,a,[role="button"],label');
    if (!target) return;
    let hovered = false, focused = false, pressed = false, timer = 0;
    const parts = [...svg.querySelectorAll('[data-prism-part]')].filter(part => partPoses[part.dataset.prismPart]);
    const enabled = () => !still() && !target.disabled && target.getAttribute('aria-disabled') !== 'true' && !target.classList.contains('busy');
    function pose() {
      const use = svg.querySelector('use'), name = svg.dataset.prismIcon || (use && (use.getAttribute('href') || '').split('#')[1]) || '';
      if (parts.length) return 'translateY(-.5px)';
      if (name === 'settings') return 'rotate(24deg)';
      if (['updates', 'reset', 'refresh'].includes(name)) return 'rotate(32deg)';
      if (['weather', 'appearance'].includes(name)) return 'rotate(18deg)';
      if (name === 'glass') return 'rotate(-5deg) skewY(-4deg)';
      if (['music', 'lyrics', 'life', 'welcome'].includes(name)) return 'rotate(-7deg)';
      if (name === 'previous' || name === 'next') return `translateX(${name === 'previous' ? -2 : 2}px)`;
      if (['play', 'exit', 'taskview'].includes(name)) return 'translateX(1.5px)';
      if (name === 'pause') return 'scaleX(.9)';
      if (name === 'close') return 'rotate(90deg)';
      if (['tray', 'dock', 'home'].includes(name)) return 'translateY(-2px)';
      if (['components', 'companion', 'shortcuts', 'startup'].includes(name)) return 'rotate(-4deg)';
      if (['capture', 'expand'].includes(name)) return 'scale(1.08)';
      if (name === 'bluetooth') return 'rotate(9deg)';
      if (['npu', 'scan', 'downloads'].includes(name)) return 'translateY(1.5px)';
      if (name === 'performance') return 'translateX(1px)';
      return 'translateY(-1.5px)';
    }
    function update() {
      const active = enabled();
      svg.style.transformOrigin = '50% 50%';
      svg.style.transition = active ? 'transform 220ms cubic-bezier(.2,.8,.2,1)' : 'none';
      svg.style.transform = active && pressed ? 'translateY(1px) scale(.93)' : active && (hovered || focused) ? pose() : '';
      // Only the meaningful detail moves; the original outline and live <use> stay intact.
      for (const part of parts) {
        part.style.transformBox = 'view-box';
        part.style.transformOrigin = part.dataset.prismOrigin || '12px 12px';
        part.style.transition = active ? 'transform 260ms cubic-bezier(.2,.8,.2,1)' : 'none';
        part.style.transform = active && !pressed && (hovered || focused) ? partPoses[part.dataset.prismPart] : '';
      }
    }
    function stop() {
      clearTimeout(timer); hovered = focused = pressed = false;
      for (const node of [svg, ...parts]) { node.style.transition = 'none'; node.style.transform = ''; }
    }
    const handlers = {
      pointerenter(e) { if (!e.pointerType || e.pointerType === 'mouse') { hovered = true; update(); } },
      pointerleave() { hovered = pressed = false; update(); },
      pointerdown(e) { if (e.button === 0) { pressed = true; update(); } },
      pointerup(e) { pressed = false; if (e.pointerType && e.pointerType !== 'mouse' && enabled()) { hovered = true; clearTimeout(timer); timer = setTimeout(() => { hovered = false; update(); }, 220); } update(); },
      pointercancel() { stop(); },
      focusin() { try { focused = target.matches(':focus-visible'); } catch { focused = true; } update(); },
      focusout() { focused = pressed = false; update(); },
      keydown(e) { if (e.key === ' ' || e.key === 'Enter') { pressed = true; update(); } },
      keyup(e) { if (e.key === ' ' || e.key === 'Enter') { pressed = false; update(); } }
    };
    for (const [event, handler] of Object.entries(handlers)) target.addEventListener(event, handler);
    svg.dataset.prismMotion = 'ready';
    live.set(svg, { stop, update, destroy() { stop(); for (const [event, handler] of Object.entries(handlers)) target.removeEventListener(event, handler); } });
  }
  function scan() {
    for (const [svg, item] of live) if (!svg.isConnected) { item.destroy(); live.delete(svg); }
    document.querySelectorAll('svg.control-icon,svg[data-prism-icon],.view-tabs svg').forEach(bind);
  }
  function suspend() { if (still()) for (const item of live.values()) item.stop(); }
  const observer = new MutationObserver(records => {
    // Text clocks and glass animation classes must not rescan the entire icon tree each frame.
    if (records.some(r => r.type === 'childList' && [...r.addedNodes, ...r.removedNodes].some(n => n.nodeType === 1))) scan();
    for (const r of records) if (r.type === 'attributes' && r.target !== document.body) {
      const svg = r.target.closest('svg') || r.target.querySelector('svg[data-prism-motion]');
      if (live.has(svg)) live.get(svg).update();
    }
    suspend();
  });
  scan(); observer.observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['data-reduced', 'class', 'href', 'disabled', 'aria-disabled'] });
  document.addEventListener('visibilitychange', suspend);
  for (const event of ['prism-settings-applied', 'prism-preferences']) window.addEventListener(event, e => { settingsPaused = !!(e.detail && (e.detail.manualPaused || e.detail.reducedMotion)); suspend(); });
  window.chrome?.webview?.addEventListener('message', e => {
    const value = e.data;
    if (value?.type === 'motion' || value?.type === 'layout') runtimePaused = !!value.paused;
    if (value?.type === 'preferences') settingsPaused = !!(value.snapshot?.manualPaused || value.snapshot?.reducedMotion);
    suspend();
  });
  for (const event of ['blur', 'pagehide']) window.addEventListener(event, () => { for (const item of live.values()) item.stop(); });
  if (media.addEventListener) media.addEventListener('change', suspend); else media.addListener(suspend);
  window.PrismFunctionalIcons = { refresh: scan, suspend() { for (const item of live.values()) item.stop(); } };
})();
