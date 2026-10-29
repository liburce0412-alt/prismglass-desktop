/* Isolated UI data. No computer service, paired session, or native action is used. */
window.__errors = [];
window.addEventListener('error', function (event) { window.__errors.push(event.message); });
window.__fixture = {
  mode: 'connected', writes: [],
  grants: { apps: true, tasks: true, notes: true, music: true, control: true, pointer: true, screen: true, keyboard: true, power: false },
  state: {
    protocol: 1, theme: 'Sky', appsRevision: 'fixture-apps',
    settings: { available: true, revision: 'fixture-settings', theme: 'Sky', glassOpacity: 100, textScale: 100, reducedMotion: false, manualPaused: false, dockIconSize: 48, gridColumns: 6, showLabels: true, dockAutoHide: true, pauseOnBattery: false, pauseOnPowerSaver: true, pauseOnRemote: true, hiddenCards: [], hiddenLeftCards: [] },
    weather: { available: true, data: { temp: 21, code: 3, high: 24, low: 18 } },
    system: { available: true, data: { cpu: 12, ram: 45, down: 2048, up: 1024 } },
    volume: { available: true, data: { value: 45 } },
    music: { available: true, title: '界面验收 · 测试曲目', artist: '测试媒体会话', source: '系统媒体', playing: true, current: '让光停在歌声里', next: '下一句 · 风从身边经过', position: 42, duration: 180 },
    focus: { available: true, total: 1500, remaining: 1300, deadline: 0, completed: 2 },
    tasks: { available: true, revision: 'fixture-tasks', items: [{ id: 'one', text: '核对横屏与竖屏阅读体验', done: false }, { id: 'two', text: '检查触控与连接状态', done: false }], countdownTitle: '', countdownDate: '' },
    notes: { available: true, revision: 'fixture-notes', text: '界面验收用内容，不读取或写入用户笔记。' },
    context: { available: false }, controls: { pointer: { available: true, active: false }, keyboard: { available: true }, power: { available: false, status: 'idle' } }
  },
  apps: { revision: 'fixture-apps', categories: [{ id: 'work', name: '工作' }], folders: [], items: [{ id: 'explorer', name: '文件资源管理器', category: 'work', folder: '', exists: true, order: 0 }, { id: 'code', name: 'Visual Studio Code', category: 'work', folder: '', exists: true, order: 1 }, { id: 'missing', name: '已失效的测试入口', category: 'work', folder: '', exists: false, order: 2 }] }
};
window.fetch = async function (url, request) {
  var f = window.__fixture, status = 200, data;
  if (request && request.method === 'POST') { f.writes.push({ url: url, body: request.body }); status = 403; data = { error: 'fixture_read_only', message: '界面验收不会执行电脑操作。' }; }
  else if (f.mode === 'offline') throw new TypeError('Fixture disconnected');
  else if (f.mode === 'unpaired') { status = 401; data = { error: 'unpaired', message: '尚未配对。' }; }
  else if (f.mode === 'locked') { status = 423; data = { error: 'locked', message: '电脑已锁定，个人内容已隐藏。' }; }
  else if (url === '/api/session') data = { protocol: 1, csrf: 'fixture-only', grants: f.grants };
  else if (url === '/api/state') data = Object.assign({}, f.state, { serverTime: Date.now() });
  else if (url === '/api/apps') data = f.apps;
  else if (url === '/api/screen') data = { available: true, extensionAvailable: false, extensionMessage: '此测试不启用虚拟显示器。', monitors: [{ id: 0, name: '主屏幕 · 1920 × 1080' }] };
  else { status = 404; data = { message: '未配置的界面验收请求。' }; }
  return new Response(JSON.stringify(data), { status: status, headers: { 'Content-Type': 'application/json' } });
};
