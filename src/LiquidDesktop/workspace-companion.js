'use strict';
(()=>{
 const $=id=>document.getElementById(id),post=value=>window.chrome?.webview?.postMessage(JSON.stringify({action:'companion',...value}));
 const grants={apps:'共享应用',music:'音乐与歌词',control:'外观、音量与快捷控制',tasks:'私人待办',notes:'私人笔记',screen:'屏幕画面',pointer:'鼠标触控板',keyboard:'键盘输入',power:'电脑电源'};
 let state=null,selected='',drafts=new Map(),appDraft=null,devicesKey='',appsKey='',request=0,pending=new Map(),lastResult='',mounted=false,collapsedQr='';
 const key=(item,isPending)=>(isPending?'pending:':'device:')+item.id;
 const normalized=value=>Object.fromEntries(Object.keys(grants).map(k=>[k,!!value?.[k]]));
 const signature=value=>JSON.stringify(normalized(value));
 function node(tag,text,props={}){const el=document.createElement(tag);if(text!==undefined)el.textContent=text;Object.assign(el,props);return el;}
 function button(text,command,props={}){const el=node('button',text,{type:'button',...props});el.addEventListener('click',command);return el;}
 function selection(){for(const isPending of [false,true])for(const item of state?.service?.[isPending?'pending':'devices']||[])if(key(item,isPending)===selected)return{item,isPending};return null;}
 function send(command,extra={}){if(state?.busy&&command!=='stop')return;const token=String(++request);pending.set(token,{command,selected});if(state&&command!=='stop'){state={...state,busy:true};render();}post({command,request:token,...extra});}
 function connection(command){collapsedQr='';send(command,{address:$('companion-address').value,port:Number($('companion-port').value)});}
 function select(value){selected=value;renderGrants();renderDevices();}
 function renderDevices(){
  const service=state.service||{},next=JSON.stringify([service.devices,service.pending]);
  if(next!==devicesKey){devicesKey=next;for(const isPending of [false,true]){const list=$(isPending?'companion-pending':'companion-devices');list.replaceChildren();const items=service[isPending?'pending':'devices']||[];
    if(!items.length)list.append(node('p',isPending?'暂无请求。新设备扫码后会出现在这里。':'尚无已授权设备。',{className:'companion-empty'}));
    for(const item of items){const el=button(item.name,()=>select(key(item,isPending)),{className:'companion-device'});el.dataset.device=key(item,isPending);el.append(node('small',isPending?'等待电脑批准':'已配对'));list.append(el);}
   }
  }
  for(const el of document.querySelectorAll('[data-device]'))el.setAttribute('aria-pressed',String(el.dataset.device===selected));
 }
 function renderGrants(){
  const current=selection(),value=current?(drafts.get(selected)||normalized(current.item.grants)):normalized(null),dirty=current&&signature(value)!==signature(current.item.grants);
  $('companion-permission-title').textContent=current?current.item.name+' 的共享权限':'选择一台设备';
  $('companion-permission-hint').textContent=!current?'在左侧选择设备或待批准请求。':current.isPending?'核对设备名称，只勾选需要开放的功能。':dirty?'有未保存的更改，点击保存后才生效。':'已保存';
  for(const input of document.querySelectorAll('[data-grant]')){input.checked=value[input.dataset.grant];input.disabled=!current||state.busy;}
  $('companion-grant-save').hidden=!!current?.isPending;$('companion-grant-save').disabled=!current||!dirty||state.busy;
  $('companion-approve').hidden=!current?.isPending;$('companion-approve').disabled=!current||state.busy;
  $('companion-reject').hidden=!current?.isPending;$('companion-reject').disabled=!current||state.busy;
  $('companion-revoke').hidden=!current||current.isPending;$('companion-revoke').disabled=state.busy;
 }
 function permission(command){const current=selection();if(!current)return;send(command,{id:current.item.id,grants:drafts.get(selected)||normalized(current.item.grants)});}
 function renderApps(){
  const entries=state.apps||[],next=JSON.stringify(entries);
  if(next!==appsKey){appsKey=next;const list=$('companion-apps');list.replaceChildren();
   for(const entry of entries){const label=node('label',undefined,{className:'companion-app'}),input=node('input',undefined,{type:'checkbox'});input.dataset.sharedApp=entry.id;input.addEventListener('change',()=>{if(appDraft===null)appDraft=new Set(entries.filter(e=>e.allowed).map(e=>e.id));input.checked?appDraft.add(entry.id):appDraft.delete(entry.id);renderApps();});label.append(input,node('span',entry.name+(entry.exists?'':'（已失效）')));label.dataset.search=entry.name.toLocaleLowerCase();list.append(label);}
  }
  const byId=new Map(entries.map(e=>[e.id,e])),chosen=appDraft||new Set(entries.filter(e=>e.allowed).map(e=>e.id));
  for(const input of document.querySelectorAll('[data-shared-app]')){const entry=byId.get(input.dataset.sharedApp);input.checked=chosen.has(entry.id);input.disabled=state.busy||!entry.exists&&!entry.allowed;input.parentElement.hidden=!input.parentElement.dataset.search.includes($('companion-app-search').value.toLocaleLowerCase());}
  $('companion-app-count').textContent='已选 '+chosen.size+' 个应用'+(appDraft?' · 未保存':'');$('companion-app-save').disabled=state.busy||appDraft===null||chosen.size>500;
  const matches=[...$('companion-apps').children].some(el=>!el.hidden);$('companion-app-empty').hidden=matches;$('companion-app-empty').textContent=entries.length?'没有匹配的应用。':state.appsLoaded?'没有可共享的应用，可先到应用中心添加入口。':'正在读取应用列表…';
 }
 function render(){if(!mounted||!state)return;
  const service=state.service||{};$('companion-service-status').textContent=state.status;$('companion-service-status').dataset.enabled=String(!!service.enabled);
  $('companion-url').textContent=service.enabled?service.address:'同一局域网 · 每次手动开启';
  $('companion-notice').textContent=state.notice||service.error||'';$('companion-notice').hidden=!$('companion-notice').textContent;$('companion-notice').dataset.error=String(!!state.error||!!service.error);
  for(const el of document.querySelectorAll('[data-companion-command]'))el.disabled=!!state.busy;
  $('companion-pair').textContent=service.enabled?'生成配对二维码':'连接并生成配对二维码';$('companion-connect').textContent=state.busy?'正在处理…':service.enabled?'检查连接':'检查并连接';$('companion-stop').disabled=!service.enabled&&!state.busy;
  const address=$('companion-address');if(document.activeElement!==address){const value=address.value||state.address;address.replaceChildren();for(const network of state.networks||[])address.append(node('option',network.label,{value:network.address}));if(!address.children.length)address.append(node('option',state.readingNetwork?'正在读取网络…':'暂无局域网连接',{value:''}));address.value=[...address.options].some(o=>o.value===value)?value:state.address;}
  address.disabled=!!state.busy;$('companion-port').disabled=!!state.busy;if(document.activeElement!==$('companion-port')&&!$('companion-port').dataset.edited)$('companion-port').value=state.port;
  const qr=$('companion-qr');if(qr.getAttribute('src')!==state.qr){if(state.qr)qr.src=state.qr;else qr.removeAttribute('src');}
  $('companion-pairing').hidden=!state.qr||state.qr===collapsedQr;qr.alt=state.entryQr?'固定入口二维码':'配对二维码';$('companion-pair-code').textContent=state.code||'';$('companion-pair-note').textContent=state.entryQr?'已配对设备可直接扫码打开；新设备请生成配对二维码。':'用 iPad 扫码或输入配对码，回到下方核对设备并批准。';
  if(!selection()){selected='';const items=service.devices||[],requests=service.pending||[];if(requests.length===1)selected=key(requests[0],true);else if(items.length===1)selected=key(items[0],false);}
  const valid=new Set([...(service.devices||[]).map(i=>key(i,false)),...(service.pending||[]).map(i=>key(i,true))]);for(const id of drafts.keys())if(!valid.has(id))drafts.delete(id);
  renderDevices();renderGrants();renderApps();
 }
 function mount(){if(mounted)return;mounted=true;const root=$('settings-companion');root.innerHTML=`
  <div class="companion-connection-head"><div><strong id="companion-service-status">正在读取状态…</strong><p id="companion-url"></p></div><div class="companion-actions"><button type="button" id="companion-connect" data-companion-command>检查并连接</button><button type="button" id="companion-stop" class="quiet">关闭连接</button></div></div>
  <p id="companion-notice" role="status" hidden></p>
  <div class="companion-actions"><button type="button" id="companion-pair" data-companion-command>生成配对二维码</button><button type="button" id="companion-help-link" class="quiet">首次连接指南 ↓</button></div>
  <section id="companion-pairing" hidden aria-label="连接二维码"><img id="companion-qr" alt="配对二维码"><div><strong id="companion-pair-code"></strong><p id="companion-pair-note"></p><button type="button" id="companion-collapse-qr" class="quiet">收起二维码</button></div></section>
  <details id="companion-network"><summary>连接设置</summary><div class="companion-network-fields"><label>电脑网络<select id="companion-address"></select></label><label>端口<input id="companion-port" type="number" min="1024" max="65535"></label></div><p class="life-note">换网后重新检查连接。涉及证书、网络类别或防火墙的改动，会逐项说明并请求确认。</p></details>
  <div class="companion-section-head"><h2>设备与权限</h2><span>仅在保存或批准后生效</span></div>
  <div class="companion-device-layout"><div class="companion-device-lists"><h3>已授权设备</h3><div id="companion-devices"></div><h3>待批准请求</h3><div id="companion-pending"></div></div><section class="companion-permissions"><h3 id="companion-permission-title">选择一台设备</h3><p id="companion-permission-hint" class="life-note"></p><div class="companion-actions"><button type="button" id="companion-grant-save">保存设备权限</button><button type="button" id="companion-approve" hidden>批准请求</button><button type="button" id="companion-reject" class="quiet" hidden>拒绝</button><button type="button" id="companion-revoke" class="quiet" hidden>撤销授权</button></div><div id="companion-grants"></div></section></div>
  <details id="companion-shared-apps"><summary>应用共享范围 <span id="companion-app-count"></span></summary><p class="life-note">获应用权限的设备共用此范围，最多 500 个入口。</p><input id="companion-app-search" type="search" placeholder="搜索要共享的应用" aria-label="搜索共享应用"><div id="companion-apps"></div><p id="companion-app-empty" class="life-note"></p><div class="companion-actions"><button type="button" id="companion-app-save">保存共享范围</button><button type="button" id="companion-app-refresh" class="quiet" data-companion-command>刷新列表</button><button type="button" id="companion-app-all" class="quiet" data-companion-command>全选可用入口</button></div></details>
  <details id="companion-help"><summary>首次连接指南</summary><ol><li>电脑与 iPad 连接同一家庭 Wi-Fi 或个人热点。在上方检查连接，核对系统改动后确认。</li><li>首次使用会创建专用证书。导出下方的公开证书，通过你信任的方式发送到 iPad。</li><li>在 iPad 安装描述文件，再到“设置 → 通用 → 关于本机 → 证书信任设置”，为 PrismGlass 证书开启完全信任。请勿跳过证书错误。</li><li>生成配对二维码，用 Safari 打开入口并添加到主屏幕。从主屏幕图标内配对，再在此处核对设备、选择权限并批准。</li></ol><p class="life-note">电脑重新启动 PrismGlass 后需手动连接，同一设备无需重新配对。已配置过旧版的用户可导入原 setup-record.json，沿用已信任的证书。</p><div class="companion-actions"><button type="button" id="companion-export" data-companion-command>导出 iPad 证书</button><button type="button" id="companion-import" class="quiet" data-companion-command>导入旧连接配置</button></div></details>`;
  for(const [name,title] of Object.entries(grants)){const label=node('label',title),input=node('input',undefined,{type:'checkbox',disabled:true});input.dataset.grant=name;input.addEventListener('change',()=>{const current=selection();if(!current)return;const value={...(drafts.get(selected)||normalized(current.item.grants)),[name]:input.checked};drafts.set(selected,value);renderGrants();});label.prepend(input);$('companion-grants').append(label);}
  $('companion-connect').onclick=()=>connection('connect');$('companion-pair').onclick=()=>connection('pair');$('companion-stop').onclick=()=>send('stop');$('companion-export').onclick=()=>send('export');$('companion-import').onclick=()=>send('import');
  $('companion-port').oninput=()=>{$('companion-port').dataset.edited='true';};$('companion-help-link').onclick=()=>{$('companion-help').open=true;$('companion-help').scrollIntoView({block:'start'});$('companion-help').querySelector('summary').focus();};$('companion-collapse-qr').onclick=()=>{collapsedQr=state.qr;$('companion-pairing').hidden=true;$('companion-pair').focus();};
  for(const [id,command] of [['companion-grant-save','grant'],['companion-approve','approve'],['companion-reject','reject'],['companion-revoke','revoke']])$(id).onclick=()=>permission(command);
  $('companion-app-search').oninput=renderApps;$('companion-app-save').onclick=()=>{if(appDraft)send('apps-save',{ids:[...appDraft]});};$('companion-app-refresh').onclick=()=>send('apps-refresh');$('companion-app-all').onclick=()=>{appDraft=new Set(state.apps.filter(e=>e.exists).map(e=>e.id));renderApps();};render();
 }
 function receive(value){state=value;const result=value.result;if(result&&result.request!==lastResult){lastResult=result.request;const action=pending.get(result.request);if(action&&result.ok){if(['grant','approve','revoke','reject'].includes(action.command))drafts.delete(action.selected);if(action.command==='apps-save')appDraft=null;}pending.delete(result.request);}render();}
 window.PrismCompanionSettings={open(){mount();post({command:'read'});},receive};
})();
