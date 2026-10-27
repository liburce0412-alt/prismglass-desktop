(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); }, write, models = {};
  var desktop = [
    ['theme','主题',{Sky:'Sky · 晴空',Astro:'Astro · 星夜'}], ['glassOpacity','玻璃浓度',60,100], ['textScale','文字大小',90,130],
    ['dockIconSize','Dock 图标大小',32,72], ['gridColumns','应用列数',4,10], ['showLabels','显示应用名称'], ['dockAutoHide','自动隐藏 Dock'],
    ['manualPaused','暂停电脑动效'], ['reducedMotion','减少电脑动画'], ['pauseOnBattery','电池供电时暂停'], ['pauseOnPowerSaver','节能模式时暂停'], ['pauseOnRemote','远程桌面时暂停']
  ];
  var lyrics = [
    ['font','字体风格',{sans:'现代',humanist:'人文',hei:'黑体',song:'宋体',kai:'楷体',round:'圆润',mono:'等宽',display:'展示'}],
    ['layout','排版',{duo:'双行',single:'单行',split:'左右错落',stack:'层叠'}], ['align','对齐',{left:'靠左',center:'居中',right:'靠右'}],
    ['effect','换句动效',{float:'轻浮',spring:'弹性',fade:'淡入',flip:'翻转',slide:'滑入',zoom:'缩放',blur:'聚焦',wave:'波浪',ripple:'涟漪',cascade:'依次浮现',stretch:'舒展',none:'无'}],
    ['texture','文字材质',{solid:'纯色',gradient:'渐变',shimmer:'流光',neon:'霓虹',outline:'描边'}],
    ['color','主色','color'], ['secondary','次行颜色','color'], ['accent','高光颜色','color'],
    ['fontSize','桌面字号',18,32], ['widthMode','桌面宽度模式',{adaptive:'自适应',fixed:'固定宽度'}], ['width','桌面固定宽度',420,1200,10], ['weight','字重',400,900,50], ['spacing','字距',-2,12], ['intensity','动效强度',0,100], ['speed','动效速度',50,180], ['glow','辉光',0,100], ['secondaryOpacity','次行透明度',30,100],
    ['backgroundTheme','歌词背景',{transparent:'透明',album:'跟随专辑',wallpaper:'跟随桌面',ocean:'海洋',pine:'松林',sand:'暖沙',violet:'紫雾'}], ['backgroundOpacity','背景浓度',0,100], ['backgroundMotion','背景缓动']
  ];
  var cards = { hiddenCards:{quota:'额度',desktop:'桌面入口',notes:'随手记',tasks:'待办',countdown:'倒计时',links:'链接',calculator:'计算器'}, hiddenLeftCards:{network:'网络',Weather:'天气',Music:'音乐',Focus:'专注',Folders:'文件夹',System:'系统状态',Tools:'工具',Life:'生活'} };
  function create(tag, label) { var node = document.createElement(tag); if (label) node.textContent = label; return node; }
  function addField(model, field) {
    var key = field[0], label = create('label'), caption = create('span', field[1]), input, output;
    label.className = 'appearance-field'; label.appendChild(caption);
    if (field[2] && typeof field[2] === 'object') {
      input = create('select'); Object.keys(field[2]).forEach(function (value) { var option = create('option', field[2][value]); option.value = value; input.appendChild(option); });
    } else {
      input = create('input'); input.type = field.length === 2 ? 'checkbox' : field[2] === 'color' ? 'color' : 'range';
      if (input.type === 'range') { input.min = field[2]; input.max = field[3]; input.step = field[4] || 1; output = create('output'); caption.appendChild(output); }
    }
    input.id = model.name + '-' + key; input.setAttribute('aria-label', field[1]); label.appendChild(input); model.container.appendChild(label);
    model.fields[key] = {input:input,output:output};
    input.oninput = input.onchange = function () { model.dirty = true; if (output) output.textContent = ' ' + input.value; status(model); };
  }
  function cardField(model, key, labels) {
    var group = create('fieldset'), legend = create('legend', key === 'hiddenCards' ? '桌面右侧组件' : '桌面左侧组件'); group.className = 'appearance-card-toggles'; group.appendChild(legend);
    model.fields[key] = {cards:{}};
    Object.keys(labels).forEach(function (id) { var label = create('label'), input = create('input'); input.type = 'checkbox'; input.setAttribute('aria-label', '显示' + labels[id]); label.appendChild(input); label.appendChild(create('span', labels[id])); group.appendChild(label); model.fields[key].cards[id] = input; input.onchange = function () { model.dirty = true; status(model); }; });
    model.container.appendChild(group);
  }
  function values(model) {
    var result = {}; Object.keys(model.fields).forEach(function (key) { var field = model.fields[key], input = field.input;
      result[key] = field.cards ? Object.keys(field.cards).filter(function (id) { return !field.cards[id].checked; }) : input.type === 'checkbox' ? input.checked : input.type === 'range' ? Number(input.value) : input.value;
    }); return result;
  }
  function load(model) {
    var data = model.data || {}, source = model.name === 'lyrics' ? data.appearance || {} : data;
    Object.keys(model.fields).forEach(function (key) { var field = model.fields[key], input = field.input;
      if (field.cards) Object.keys(field.cards).forEach(function (id) { field.cards[id].checked = !Array.isArray(source[key]) || source[key].indexOf(id) < 0; });
      else if (input.type === 'checkbox') input.checked = source[key] === true;
      else if (source[key] != null) { input.value = String(source[key]); if (field.output) field.output.textContent = ' ' + input.value; }
    }); model.revision = data.revision; model.dirty = false;
  }
  function status(model) {
    var conflict = model.dirty && String(model.revision) !== String((model.data || {}).revision), disabled = !model.allowed || model.busy || model.blocked;
    $(model.name + '-appearance-status').textContent = !model.allowed ? (model.data || {}).reason || '需要电脑授权共享与控制。' : conflict ? '电脑设置已更新；保留了你的调整，请载入电脑版本后再修改。' : model.busy ? '正在保存…' : model.dirty ? '调整尚未保存' : '已与电脑同步';
    $(model.name + '-appearance-save').disabled = disabled || !model.dirty || conflict;
    $(model.name + '-appearance-reload').disabled = disabled;
    Object.keys(model.fields).forEach(function (key) { var field = model.fields[key]; if (field.cards) Object.keys(field.cards).forEach(function (id) { field.cards[id].disabled = disabled; }); else field.input.disabled = disabled; });
  }
  function init(dispatch) {
    write = dispatch;
    ['desktop','lyrics'].forEach(function (name) {
      var model = models[name] = {name:name, fields:{},container:$(name + '-appearance-fields'),data:null,dirty:false,allowed:false};
      (name === 'desktop' ? desktop : lyrics).forEach(function (field) { addField(model, field); });
      if (name === 'desktop') Object.keys(cards).forEach(function (key) { cardField(model, key, cards[key]); });
      $(name + '-appearance-reload').onclick = function () { load(model); status(model); };
      $(name + '-appearance-form').onsubmit = function (event) {
        event.preventDefault(); if ($(name + '-appearance-save').disabled) return;
        var payload = {expectedRevision:model.revision}, changes = values(model); payload[name === 'desktop' ? 'changes' : 'appearance'] = changes;
        write(name === 'desktop' ? 'settings' : 'lyricsStyle', name === 'desktop' ? '/api/settings' : '/api/lyrics-style', payload, name === 'desktop' ? '桌面设置已同步' : '歌词效果已同步', function (result) { model.data = result; load(model); status(model); });
      }; status(model);
    });
  }
  function render(state, grants, pending, blocked) {
    ['desktop','lyrics'].forEach(function (name) { var model = models[name]; if (!model) return;
      model.data = state[name === 'desktop' ? 'settings' : 'lyricsSettings'] || {}; model.allowed = grants.control && model.data.available !== false && (name === 'desktop' || grants.music && model.data.editable);
      model.busy = !!pending[name === 'desktop' ? 'settings' : 'lyricsStyle']; model.blocked = blocked;
      if (!model.dirty) load(model); status(model);
    });
  }
  function clear() { Object.keys(models).forEach(function (name) { var model = models[name]; model.data = null; model.allowed = false; model.dirty = false; status(model); }); }
  window.PrismCompanionSettings = {init:init,render:render,clear:clear};
}());
