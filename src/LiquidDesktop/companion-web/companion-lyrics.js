/* iPad adapter for the shared PrismLyrics core. No word timing is inferred. */
(function () {
  'use strict';
  var container = document.getElementById('ipad-lyrics'), current = document.getElementById('lyric-current'), next = document.getElementById('lyric-next');
  if (!container || !current || !next) return;
  var media = window.matchMedia ? window.matchMedia('(prefers-reduced-motion: reduce)') : null;
  var currentText = null, nextText = null, appearance = null, palette = null, styleKey = '', backgroundKey = '', appliedStill = null, explicitStill = false, motionPaused = false, wash = null;
  var fonts = {
    sans: '-apple-system,BlinkMacSystemFont,"PingFang SC","Helvetica Neue",sans-serif',
    humanist: '"Avenir Next","PingFang SC",-apple-system,sans-serif',
    hei: '"PingFang SC","Hiragino Sans GB","Microsoft YaHei UI",sans-serif',
    song: '"Songti SC","STSong","Hiragino Mincho ProN",serif',
    kai: '"Kaiti SC","STKaiti","KaiTi",serif',
    round: '"Hiragino Maru Gothic ProN","PingFang SC",-apple-system,sans-serif',
    mono: '"SFMono-Regular",Menlo,Monaco,monospace',
    display: '-apple-system,"PingFang SC","Helvetica Neue",sans-serif'
  };
  function animations(node) { return typeof node.getAnimations === 'function' ? node.getAnimations({ subtree: true }) : []; }
  function cancelEffects() {
    var core = window.PrismLyrics;
    if (core && typeof container.getAnimations === 'function') core.cancel(container);
    else [current, next].forEach(function (node) { if (node.children.length) node.textContent = node.textContent; });
  }
  function isStill() {
    return explicitStill || motionPaused || document.hidden || document.body.dataset.reduced === 'true' || !!(media && media.matches) ||
      (typeof container.getClientRects === 'function' && !container.getClientRects().length) ||
      typeof container.getAnimations !== 'function' || typeof current.parentElement.animate !== 'function';
  }
  function copyPalette(value) {
    var result = {};
    ['album', 'wallpaper'].forEach(function (key) { if (value && Array.isArray(value[key])) result[key] = value[key].slice(0, 3).map(function (color) { return typeof color === 'string' && /^#[0-9a-f]{6}$/i.test(color) ? color : ''; }); });
    return result;
  }
  function paint(still, force) {
    var core = window.PrismLyrics; if (!core || !appearance) return;
    if (force || still !== appliedStill) {
      core.apply(container, appearance, still);
      container.style.fontFamily = fonts[appearance.font] || fonts.sans;
      // Desktop fixed widths belong to the Dock. The tablet owns its column.
      container.style.setProperty('--copy-width', '100%');
      if (still || force) cancelEffects();
    }
    if (!wash) { wash = document.createElement('div'); wash.className = 'companion-lyric-wash'; wash.setAttribute('aria-hidden', 'true'); container.prepend(wash); }
    var key = JSON.stringify([appearance.backgroundTheme, appearance.backgroundOpacity, appearance.backgroundMotion, palette, still]);
    if (key !== backgroundKey) { core.background(wash, appearance, palette, still); backgroundKey = key; }
    appliedStill = still;
  }
  function render(music, still) {
    if (document.hidden || !music || music.available !== true) { clear(); return; }
    var line = typeof music.current === 'string' ? music.current : '', following = typeof music.next === 'string' ? music.next : '';
    if (!line && !following) { clear(); return; }
    var changed = line !== currentText, followingChanged = following !== nextText;
    if (changed) { cancelEffects(); current.textContent = line; currentText = line; }
    if (followingChanged) { next.textContent = following; nextText = following; }
    container.hidden = false; current.parentElement.hidden = !line; next.parentElement.hidden = !following;
    container.classList.toggle('interlude', !!music.gap);
    explicitStill = !!still; motionPaused = !!music.motionPaused;
    var core = window.PrismLyrics;
    if (!core) { container.classList.add('reduced'); return; }
    appearance = core.normalize(music.appearance); palette = copyPalette(music.palette);
    var key = JSON.stringify(appearance), changedStyle = key !== styleKey; styleKey = key;
    var stopped = isStill(); paint(stopped, changedStyle);
    // Polls, next-line corrections, style updates and play-position updates do
    // not replay the current line. This is an entrance, not karaoke progress.
    if (changed && line && !stopped) core.animate(container, appearance, false);
  }
  function clear() {
    cancelEffects(); animations(container).forEach(function (animation) { animation.cancel(); });
    current.textContent = ''; next.textContent = ''; current.parentElement.hidden = true; next.parentElement.hidden = true;
    container.hidden = true; container.classList.add('reduced'); container.classList.remove('interlude');
    container.removeAttribute('style');
    ['layout', 'texture', 'effect', 'background', 'backgroundFlow'].forEach(function (key) { delete container.dataset[key]; });
    // Retire the background host as well as its pixels: the shared core keeps
    // transition palettes in a WeakMap keyed by this short-lived element.
    if (wash) { wash.remove(); wash = null; }
    currentText = nextText = appearance = palette = null; styleKey = backgroundKey = ''; appliedStill = null; explicitStill = motionPaused = false;
  }
  function refreshMotion() { if (appearance && !container.hidden) paint(isStill(), false); }
  document.addEventListener('visibilitychange', function () { if (document.hidden) clear(); });
  window.addEventListener('pagehide', clear);
  if (media) { if (media.addEventListener) media.addEventListener('change', refreshMotion); else if (media.addListener) media.addListener(refreshMotion); }
  if (typeof MutationObserver === 'function') new MutationObserver(refreshMotion).observe(document.body, { attributes: true, attributeFilter: ['data-reduced', 'data-page'] });
  window.PrismCompanionLyrics = { render: render, clear: clear };
}());
