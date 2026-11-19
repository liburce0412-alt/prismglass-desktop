'use strict';
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),assert=require('node:assert/strict');
const root=path.join(__dirname,'../src/LiquidDesktop'),html=fs.readFileSync(path.join(root,'sidedock.html'),'utf8');
const listeners=new Map(),received=[],snapshot={glassStyle:'J',glassOpacity:100,glassOverrides:{J:{refraction:1.37}},textScale:100};
const window={chrome:{webview:{addEventListener:(type,fn)=>{if(type==='message'){const list=listeners.get(type)||[];list.push(fn);listeners.set(type,list);}}}},addEventListener(){},dispatchEvent:e=>{if(e.type==='prism-preferences')received.push(e.detail);}};
const document={getElementById:()=>null,documentElement:{style:{setProperty(){}}},body:{classList:{toggle(){}}}};
const context={window,document,WeakMap,MutationObserver:class{observe(){} disconnect(){}},requestAnimationFrame:()=>1,cancelAnimationFrame(){},CustomEvent:class{constructor(type,args){this.type=type;this.detail=args.detail;}}};
for(const [,src] of html.matchAll(/<script src="([^"]+)"/g)){
 if(src==='desktop-preferences.js')vm.runInNewContext(fs.readFileSync(path.join(root,src),'utf8'),context);
 // The host is allowed to reply as soon as sidedock.js posts ready, before later scripts load.
 if(src==='sidedock.js')for(const listener of listeners.get('message')||[])listener({data:{type:'preferences',snapshot}});
}
assert.equal(received.length,1,'the initial material preferences must not be lost before the bridge loads');
assert.equal(received[0].glassStyle,'J');assert.equal(received[0].glassOverrides.J.refraction,1.37);
console.log('PASS initial SideDock material preferences reach the preferences bridge');
