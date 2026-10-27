/* The application library owns metadata only; launches always use host-resolved IDs. */
(function () {
  'use strict';
  let root, send, snapshot = { entries: [], categories: [], folders: [] }, category = 'all', folder = '', query = '', page = 0, size = 18;
  let search, tabs, grid, footer, status, breadcrumb, modal, modalReturn, composing = false, timer, visible = [], dragging = null, hoverTimer, hoverId = '', groupReady = false, edgeTimer, edgeLock = false, configuredColumns = 0, resizeGrid, deferredSnapshot;
  const el = (tag, cls, text) => { const node = document.createElement(tag); if (cls) node.className = cls; if (text !== undefined) node.textContent = text; return node; };
  const button = (text, fn, cls) => { const node = el('button', cls, text); node.type = 'button'; node.addEventListener('click', fn); return node; };
  function emit(command, data) { send(Object.assign({ action: 'apps', command, revision: snapshot.revision || 0 }, data || {})); }
  function interaction() { send({ action: 'interaction', active: !!modal || !!dragging }); }
  function nameOf(id) { return (snapshot.categories.find(c => c.id === id) || {}).name || '未分类'; }
  function itemOf(id) { return snapshot.entries.find(e => e.id === id) || snapshot.folders.find(f => f.id === id); }
  function rank(entry, value) {
    const name = (entry.name || '').toLowerCase(), words = value.toLowerCase().trim().split(/\s+/).filter(Boolean);
    const text = (entry.search || name + ' ' + (entry.alias || '')).toLowerCase(), initials = (entry.initials || '').toLowerCase();
    let score = 0;
    for (const word of words) {
      if (name === word) score += 1000;
      else if (name.startsWith(word)) score += 700;
      else if (initials.startsWith(word)) score += 500;
      else if (text.includes(word)) score += 350;
      else { let at = 0; for (const ch of text) if (ch === word[at]) at++; if (at < word.length) return -1; score += 100; }
    }
    return score;
  }
  function items() {
    if (query) return snapshot.entries.map(e => ({ e, score: rank(e, query) })).filter(x => x.score >= 0).sort((a, b) => b.score - a.score || a.e.order - b.e.order).map(x => x.e);
    const entries = snapshot.entries.filter(e => e.kind !== 'action' && (folder ? e.folder === folder : !e.folder) && (category === 'all' || e.category === category));
    const folders = folder ? [] : snapshot.folders.filter(f => category === 'all' || f.category === category).map(f => Object.assign({ kind: 'folder', exists: true }, f));
    return entries.concat(folders).sort((a, b) => a.order - b.order || a.name.localeCompare(b.name, 'zh-CN'));
  }
  function icon(entry, small) {
    const box = el('span', small ? 'apps-icon small' : 'apps-icon');
    if (entry.kind === 'folder') {
      box.classList.add('apps-folder-preview');
      snapshot.entries.filter(e => e.folder === entry.id).sort((a, b) => a.order - b.order).slice(0, 4).forEach(e => box.append(icon(e, true)));
      if (!box.children.length) box.append(el('span', '', '▧'));
    } else if (/^(data:image\/(png|jpeg|webp);base64,|https?:\/\/|file:\/\/)/i.test(entry.icon || '')) {
      const image = el('img'); image.src = entry.icon; image.alt = ''; image.loading = 'lazy'; image.draggable = false; image.onerror = () => { image.remove(); box.textContent = entry.kind === 'directory' ? '▰' : '◇'; }; box.append(image);
    } else box.textContent = entry.kind === 'action' ? '⚙' : entry.kind === 'directory' ? '▰' : entry.kind === 'file' ? '▤' : '◇';
    return box;
  }
  function activate(entry) {
    if (entry.kind === 'folder') { folder = entry.id; page = 0; render(); search.focus(); }
    else if (entry.exists === false) details(entry);
    else emit('open', { id: entry.id });
  }
  function categoryTab(id, label) {
    const node = button(label, () => { category = id; folder = ''; page = 0; render(); }, 'apps-tab' + (category === id ? ' selected' : ''));
    node.setAttribute('aria-pressed', String(category === id));
    node.addEventListener('dragover', event => { if (!dragging) return; event.preventDefault(); node.classList.add('drop-target'); });
    node.addEventListener('dragleave', () => node.classList.remove('drop-target'));
    node.addEventListener('drop', event => { node.classList.remove('drop-target'); if (!dragging || id === 'all') return; event.preventDefault(); const source = dragging.id; finishDrag(true); emit('move', { id: source, target: id }); });
    if (id !== 'all') node.addEventListener('contextmenu', event => { event.preventDefault(); editCategory(id); });
    return node;
  }
  function renderTabs() {
    tabs.replaceChildren(categoryTab('all', '全部'));
    snapshot.categories.slice().sort((a, b) => a.order - b.order).forEach(c => tabs.append(categoryTab(c.id, c.name)));
    tabs.append(button('＋', () => nameDialog('新建分类', '', name => emit('categoryCreate', { name })), 'apps-tab apps-new'));
    tabs.lastChild.setAttribute('aria-label', '新建分类');
    tabs.append(button('编辑', () => editCategory(category === 'all' ? 'uncategorized' : category), 'apps-tab apps-edit'));
  }
  function render() {
    if (!root) return;
    if (category !== 'all' && !snapshot.categories.some(c => c.id === category)) category = 'all';
    if (folder && !snapshot.folders.some(f => f.id === folder)) folder = '';
    renderTabs();
    breadcrumb.replaceChildren();
    if (folder) { breadcrumb.append(button('‹ 返回 ' + (category === 'all' ? '全部' : nameOf(category)), () => { folder = ''; page = 0; render(); }, 'apps-back')); breadcrumb.append(el('span', '', (snapshot.folders.find(f => f.id === folder) || {}).name)); }
    else breadcrumb.textContent = query ? '应用、收纳的文件和系统操作' : '应用、文件与文件夹 · 右键或 Shift+F10 管理';
    const all = items(), pages = Math.max(1, Math.ceil(all.length / size)); page = Math.min(page, pages - 1); visible = all.slice(page * size, (page + 1) * size);
    const focused = document.activeElement && document.activeElement.dataset.appId;
    grid.replaceChildren();
    visible.forEach(entry => {
      const tile = button('', () => activate(entry), 'apps-tile' + (entry.exists === false ? ' unavailable' : ''));
      tile.dataset.appId = entry.id; tile.title = entry.name + (entry.path ? '\n' + entry.path : '') + (entry.exists === false ? '\n入口不可用 · 点击修复' : '');
      tile.append(icon(entry), el('span', 'apps-name', entry.name));
      const duplicate = snapshot.entries.filter(e => e.name === entry.name).length > 1;
      let detail = entry.exists === false ? '不可用 · 可修复' : entry.kind === 'folder' ? snapshot.entries.filter(e => e.folder === entry.id).length + ' 个项目' : entry.kind === 'action' ? '系统操作' : query ? (entry.kind === 'file' ? '文件' : entry.kind === 'directory' ? '文件夹' : nameOf(entry.category)) : '';
      if (duplicate && entry.path) detail = entry.path;
      if (detail) tile.append(el('span', 'apps-detail', detail));
      tile.setAttribute('aria-label', entry.name + (detail ? '，' + detail : ''));
      tile.addEventListener('contextmenu', event => { event.preventDefault(); details(entry); });
      tile.addEventListener('keydown', event => { if ((event.key === 'F10' && event.shiftKey) || event.key === 'ContextMenu') { event.preventDefault(); details(entry); } });
      if (!query && entry.kind !== 'action') {
        tile.draggable = true;
        tile.addEventListener('dragstart', event => { dragging = { id: entry.id }; event.dataTransfer.setData('text/plain', entry.id); event.dataTransfer.effectAllowed = 'copyMove'; tile.classList.add('dragging'); root.classList.add('apps-dragging'); status.textContent = '拖到分类或图标；中心停留可建文件夹。Esc 取消，拖到 Dock 可固定。'; interaction(); emit('dragBegin', { id: entry.id }); });
        tile.addEventListener('dragend', () => finishDrag(false));
        tile.addEventListener('dragover', event => dragOver(event, entry, tile));
        tile.addEventListener('dragleave', event => { if (!tile.contains(event.relatedTarget)) clearHover(); });
        tile.addEventListener('drop', event => drop(event, entry));
      }
      grid.append(tile);
    });
    if (!visible.length) { const empty = el('div', 'apps-empty'); empty.append(el('strong', '', snapshot.loading ? '正在读取应用…' : query ? '没有找到匹配项目' : '这里还没有项目'), el('p', '', query ? '试试名称、拼音、首字母或别名。' : '添加应用、文件或文件夹，或将项目移入此分类。')); grid.append(empty); }
    footer.replaceChildren();
    const prev = button('‹', () => changePage(-1), 'apps-page-button'), next = button('›', () => changePage(1), 'apps-page-button');
    prev.disabled = page === 0; next.disabled = page + 1 >= pages; prev.setAttribute('aria-label', '上一页'); next.setAttribute('aria-label', '下一页');
    [prev, next].forEach((node, i) => { node.addEventListener('dragover', event => { if (!dragging || node.disabled) return; event.preventDefault(); if (!edgeTimer && !edgeLock) { node.classList.add('drop-target'); status.textContent = '停留以翻到' + (i ? '下一页' : '上一页'); edgeTimer = setTimeout(() => { edgeTimer = null; edgeLock = true; changePage(i ? 1 : -1); }, 600); } }); node.addEventListener('dragleave', clearEdge); });
    footer.append(prev, el('span', 'apps-page-count', (page + 1) + ' / ' + pages), next, el('span', 'apps-total', all.length + ' 个项目'));
    if (!dragging) status.textContent = snapshot.error || '';
    status.classList.toggle('error', !!snapshot.error);
    if (focused) { const restored = Array.from(grid.querySelectorAll('[data-app-id]')).find(n => n.dataset.appId === focused); if (restored) restored.focus(); }
  }
  function changePage(delta) { clearHover(); page = Math.max(0, page + delta); render(); }
  function clearHover() { clearTimeout(hoverTimer); hoverTimer = null; hoverId = ''; groupReady = false; root.querySelectorAll('.drop-target,.group-preview').forEach(n => n.classList.remove('drop-target', 'group-preview')); }
  function clearEdge() { clearTimeout(edgeTimer); edgeTimer = null; edgeLock = false; }
  function dragOver(event, target, tile) {
    if (!dragging || dragging.id === target.id) return; event.preventDefault(); clearEdge();
    const source = itemOf(dragging.id), rect = tile.getBoundingClientRect(), centered = Math.abs(event.clientX - rect.left - rect.width / 2) < rect.width * .28 && Math.abs(event.clientY - rect.top - rect.height / 2) < rect.height * .3;
    const canGroup = source && !source.folder && !target.folder && source.category === target.category && source.kind !== 'folder' && !snapshot.folders.some(f => f.id === source.id) && target.kind !== 'folder' && centered;
    if (hoverId === target.id + (canGroup ? ':group' : ':move')) return;
    clearHover(); hoverId = target.id + (canGroup ? ':group' : ':move'); tile.classList.add('drop-target');
    if (canGroup) hoverTimer = setTimeout(() => { groupReady = true; tile.classList.add('group-preview'); status.textContent = '松开后为新文件夹命名；Esc 取消'; }, 550);
    else status.textContent = target.kind === 'folder' ? '松开移入“' + target.name + '”，Esc 取消' : '松开移到此项目之前，Esc 取消';
  }
  function drop(event, target) {
    if (!dragging || dragging.id === target.id) return; event.preventDefault();
    const id = dragging.id, group = groupReady; finishDrag(true);
    if (group) nameDialog('创建文件夹', '新建文件夹', name => emit('group', { id, target: target.id, name }));
    else if (target.kind === 'folder') emit('move', { id, target: target.id });
    else emit('reorder', { id, target: target.id });
  }
  function finishDrag(cancelled) {
    if (!dragging) return;
    emit('dragEnd', { id: dragging.id, cancelled: !!cancelled }); dragging = null; clearHover(); clearEdge(); root.classList.remove('apps-dragging'); root.querySelectorAll('.dragging').forEach(n => n.classList.remove('dragging')); status.textContent = snapshot.error || ''; interaction(); if (deferredSnapshot) { snapshot = deferredSnapshot; deferredSnapshot = null; render(); }
  }
  function closeDialog() { if (!modal) return false; modal.remove(); modal = null; if (modalReturn && modalReturn.isConnected) modalReturn.focus(); interaction(); return true; }
  function dialog(title) {
    closeDialog(); modalReturn = document.activeElement;
    modal = el('div', 'apps-dialog-shade'); const panel = el('div', 'apps-dialog'); panel.setAttribute('role', 'dialog'); panel.setAttribute('aria-modal', 'true'); panel.setAttribute('aria-label', title); panel.append(el('h3', '', title)); modal.append(panel); root.append(modal); interaction();
    modal.addEventListener('click', event => { if (event.target === modal) closeDialog(); });
    panel.addEventListener('keydown', event => { if (event.key !== 'Tab') return; const controls = Array.from(panel.querySelectorAll('button,input,select')).filter(n => !n.disabled); if (!controls.length) return; const at = controls.indexOf(document.activeElement); if (event.shiftKey && at <= 0) { event.preventDefault(); controls[controls.length - 1].focus(); } else if (!event.shiftKey && at === controls.length - 1) { event.preventDefault(); controls[0].focus(); } });
    return panel;
  }
  function confirm(title, text, yes) { const panel = dialog(title); panel.append(el('p', '', text)); const actions = el('div', 'apps-dialog-actions'); actions.append(button('取消', closeDialog), button('确认', () => { closeDialog(); yes(); }, 'primary')); panel.append(actions); actions.firstChild.focus(); }
  function nameDialog(title, value, save, maxLength) {
    const panel = dialog(title), input = el('input', 'apps-name-input'); input.value = value; input.maxLength = maxLength || 80; input.setAttribute('aria-label', title); panel.append(input);
    const error = el('p', 'apps-dialog-error'); error.setAttribute('role', 'alert'); panel.append(error);
    const submit = () => { if (!input.value.trim() && maxLength !== 256) { error.textContent = '请输入名称'; return; } const value = input.value.trim(); closeDialog(); save(value); };
    input.addEventListener('keydown', e => { if (e.key === 'Enter' && !e.isComposing && e.keyCode !== 229) { e.preventDefault(); submit(); } });
    const actions = el('div', 'apps-dialog-actions'); actions.append(button('取消', closeDialog), button('保存', submit, 'primary')); panel.append(actions); input.focus(); input.select();
  }
  function confirmMove(id, target) { const dest = itemOf(target) || snapshot.categories.find(c => c.id === target); if (!dest) return; confirm('移动收纳位置', '将“' + (itemOf(id) || {}).name + '”移入“' + dest.name + '”？原文件保持原位。', () => emit('move', { id, target })); }
  function chooseMove(entry) {
    const panel = dialog('移动“' + entry.name + '”'), select = el('select', 'apps-select');
    snapshot.categories.slice().sort((a, b) => a.order - b.order).forEach(c => { const option = el('option', '', c.name); option.value = c.id; select.append(option); if (entry.kind !== 'folder') snapshot.folders.filter(f => f.category === c.id).forEach(f => { const child = el('option', '', c.name + ' / ' + f.name); child.value = f.id; select.append(child); }); });
    select.value = entry.folder || entry.category; panel.append(select, el('p', '', '仅更改收纳位置，不移动原文件。')); const actions = el('div', 'apps-dialog-actions'); actions.append(button('取消', closeDialog), button('移动', () => { const target = select.value; closeDialog(); emit('move', { id: entry.id, target }); }, 'primary')); panel.append(actions); select.focus();
  }
  function details(entry) {
    if (entry.kind === 'action') return;
    const panel = dialog(entry.name), menu = el('div', 'apps-menu'); panel.append(el('p', 'apps-location', entry.path || nameOf(entry.category)), menu);
    const action = (label, fn) => menu.append(button(label, () => { closeDialog(); fn(); }));
    if (entry.kind === 'folder') { action('打开文件夹', () => activate(entry)); action('重命名', () => nameDialog('文件夹名称', entry.name, name => emit('folderRename', { id: entry.id, name }))); }
    else { action(entry.exists === false ? '重新定位入口…' : '打开', () => entry.exists === false ? emit('repair', { id: entry.id }) : activate(entry)); action('固定到 Dock', () => emit('pin', { id: entry.id })); action('打开所在位置', () => emit('reveal', { id: entry.id })); action('编辑搜索别名', () => nameDialog('搜索别名（可补充多音字读音）', entry.alias || '', name => emit('alias', { id: entry.id, name }), 256)); }
    action('移动到分类或文件夹…', () => chooseMove(entry));
    if (entry.folder) action('移出文件夹', () => confirmMove(entry.id, entry.category));
    action(entry.kind === 'folder' ? '解散文件夹' : '移除收纳记录', () => confirm(entry.kind === 'folder' ? '解散文件夹' : '移除收纳记录', entry.kind === 'folder' ? '其中项目将回到所属分类，原文件保持原位。' : '此项目将从应用库移除，刷新时也不会重新加入。可通过“添加”恢复；原文件保持原位。', () => emit(entry.kind === 'folder' ? 'folderDelete' : 'remove', { id: entry.id })));
    menu.append(button('关闭', closeDialog)); menu.firstChild.focus();
  }
  function editCategory(id) {
    const selected = snapshot.categories.find(c => c.id === id); if (!selected) return;
    const panel = dialog('管理分类'), select = el('select', 'apps-select'); snapshot.categories.slice().sort((a, b) => a.order - b.order).forEach(c => { const option = el('option', '', c.name); option.value = c.id; select.append(option); }); select.value = id; panel.append(select); select.onchange = () => editCategory(select.value);
    const menu = el('div', 'apps-menu'); panel.append(menu);
    menu.append(button('在此分类新建文件夹', () => { closeDialog(); nameDialog('新建文件夹', '', name => emit('folderCreate', { target: id, name })); }));
    if (id !== 'uncategorized') { menu.append(button('重命名分类', () => nameDialog('分类名称', selected.name, name => emit('categoryRename', { id, name })))); menu.append(button('移除分类', () => confirm('移除分类', '分类中的项目将回到“未分类”，原文件保持原位。', () => emit('categoryDelete', { id })))); }
    const ordered = snapshot.categories.slice().sort((a, b) => a.order - b.order), at = ordered.findIndex(c => c.id === id);
    if (at > 0) menu.append(button('向前移动分类', () => { closeDialog(); emit('categoryMove', { id, target: ordered[at - 1].id }); }));
    if (at + 1 < ordered.length) menu.append(button('向后移动分类', () => { closeDialog(); emit('categoryMove', { id: ordered[at + 1].id, target: id }); }));
    menu.append(button('完成', closeDialog)); select.focus();
  }
  function cancel() {
    clearTimeout(timer); timer = null;
    if (dragging) { finishDrag(true); return true; }
    if (closeDialog()) return true;
    if (query || search.value) { query = ''; search.value = ''; page = 0; render(); search.focus(); return true; }
    if (folder) { folder = ''; page = 0; render(); search.focus(); return true; }
    return false;
  }
  function init(container, sender) {
    root = container; send = sender; root.classList.add('apps-center'); root.replaceChildren();
    const tools = el('div', 'apps-toolbar'); search = el('input', 'apps-search'); search.type = 'search'; search.placeholder = '搜索应用、收纳文件和系统操作'; search.setAttribute('aria-label', '搜索应用、收纳文件和系统操作'); search.autocomplete = 'off'; search.spellcheck = false;
    tools.append(search, button('＋ 添加', () => { const panel = dialog('添加到应用中心'), menu = el('div', 'apps-menu'); panel.append(menu); menu.append(button('应用或文件…', () => { closeDialog(); emit('pick'); }), button('文件夹…', () => { closeDialog(); emit('pickFolder'); }), button('刷新已安装应用', () => { closeDialog(); emit('refresh'); }), button('取消', closeDialog)); menu.firstChild.focus(); }, 'apps-add'));
    tabs = el('nav', 'apps-tabs'); tabs.setAttribute('aria-label', '应用分类'); breadcrumb = el('div', 'apps-breadcrumb'); grid = el('div', 'apps-grid'); grid.setAttribute('aria-label', '应用项目'); footer = el('div', 'apps-pagination'); status = el('div', 'apps-status'); status.setAttribute('role', 'status'); root.append(tools, tabs, breadcrumb, grid, footer, status);
    const schedule = () => { if (composing) return; clearTimeout(timer); timer = setTimeout(() => { query = search.value.trim().toLowerCase(); page = 0; render(); timer = null; }, 100); };
    search.addEventListener('input', schedule); search.addEventListener('compositionstart', () => { composing = true; clearTimeout(timer); }); search.addEventListener('compositionend', () => { composing = false; schedule(); });
    root.addEventListener('keydown', event => {
      if (event.isComposing || composing || event.keyCode === 229) return;
      if (event.key === 'Escape') { if (cancel()) { event.preventDefault(); event.stopPropagation(); } return; }
      if (modal) return;
      if (event.key.toLowerCase() === 'k' && event.ctrlKey) { event.preventDefault(); search.focus(); search.select(); return; }
      if (!['ArrowDown', 'ArrowUp', 'ArrowLeft', 'ArrowRight', 'Enter', 'PageDown', 'PageUp'].includes(event.key)) return;
      const tiles = Array.from(grid.querySelectorAll('.apps-tile')); if (!tiles.length) return;
      let at = tiles.indexOf(document.activeElement); const fromInput = document.activeElement === search;
      if (fromInput && ['ArrowLeft', 'ArrowRight', 'ArrowUp'].includes(event.key)) return;
      if (!fromInput && at < 0) return;
      if (event.key === 'Enter') { if (fromInput) { clearTimeout(timer); query = search.value.trim().toLowerCase(); render(); if (visible[0]) activate(visible[0]); event.preventDefault(); } return; }
      event.preventDefault();
      if (event.key === 'PageDown' || event.key === 'PageUp') { changePage(event.key === 'PageDown' ? 1 : -1); const first = grid.querySelector('.apps-tile'); if (first) first.focus(); return; }
      const cols = Number(grid.style.getPropertyValue('--apps-columns')) || 6; at += event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : event.key === 'ArrowDown' ? (at < 0 ? 1 : cols) : -cols;
      tiles[Math.max(0, Math.min(tiles.length - 1, at))].focus();
    });
    const resize = resizeGrid = () => { if (!root.clientWidth || dragging) return; const cols = configuredColumns || Math.max(2, Math.floor(grid.clientWidth / 112)), rows = Math.max(2, Math.min(4, Math.floor((root.clientHeight - 205) / 112))), next = cols * rows; grid.style.setProperty('--apps-columns', cols); if (next !== size) { const first = page * size; size = next; page = Math.floor(first / size); render(); } };
    if (window.ResizeObserver) new ResizeObserver(resize).observe(root); render(); resize();
  }
  window.PrismApps = { init, update(value) { const next = Object.assign({ entries: [], categories: [], folders: [] }, value || {}); if (dragging) { deferredSnapshot = next; return; } snapshot = next; render(); }, configure(value) { configuredColumns = value && Number(value.columns) >= 3 && Number(value.columns) <= 10 ? Math.round(Number(value.columns)) : 0; if (root) root.classList.toggle('apps-hide-labels', !!value && value.showLabels === false); if (resizeGrid) resizeGrid(); }, focus() { if (search) { clearTimeout(timer); timer = null; const next = search.value.trim().toLowerCase(); if (next !== query) { query = next; page = 0; render(); } search.focus(); search.select(); } }, cancel, suspend() { clearTimeout(timer); timer = null; finishDrag(true); closeDialog(); }, rank };
}());
