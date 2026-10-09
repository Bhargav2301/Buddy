'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const {createProbe, inspectDocument, diagnostic, describe} = require('../probe.js');
const observed = () => ({editorCandidates:1, renderedRoleNodes:2, renderedUserCount:1,
  renderedAssistantCount:1, fileInputCandidates:1, streamingIndicatorPresent:false, stableConversationRoute:true});
function event() {
  const listeners = new Set();
  return {addListener:f=>listeners.add(f), removeListener:f=>listeners.delete(f),
    emit:(...args)=>{for(const fn of [...listeners])fn(...args);}, size:()=>listeners.size};
}
function dom() {
  const events = new Map();
  return {addEventListener(name,fn){if(!events.has(name))events.set(name,event());events.get(name).addListener(fn);},
    removeEventListener(name,fn){events.get(name)?.removeListener(fn);},emit(name){events.get(name)?.emit();}};
}
function fixture(options={}) {
  const calls=[];let resume, clock=0, seq=0, pausedResolve;
  const paused=new Promise(r=>pausedResolve=r),timers=new Map();
  const doc=Object.assign(dom(),{visibilityState:'visible', focused:true,hasFocus(){return this.focused;}});
  const popup=Object.assign(dom(),{document:doc,location:{href:'chrome-extension://owned/popup.html'}});
  const state={views:[popup],current:{id:3,type:'normal',state:'normal',focused:false},
    last:{id:3,type:'normal',state:'normal',focused:false},
    tab:{id:7,windowId:3,url:'https://chatgpt.com/c/11111111-1111-1111-1111-111111111111',active:true,status:'complete'}, rows:1};
  function call(name,answer) {
    calls.push(name);const index=calls.length;
    if(options.rejectAt===index)return Promise.reject(new Error('PRIVATE_browser_error_with_url_or_account'));
    if(options.pauseAt===index){pausedResolve();return new Promise(resolve=>{resume=()=>resolve(answer());});}
    return Promise.resolve(answer());
  }
  const chrome={runtime:{getURL:path=>'chrome-extension://owned/'+path},extension:{getViews:({type})=>{assert.equal(type,'popup');return state.views;}},
    windows:{getCurrent:()=>call('current',()=>({...state.current})),getLastFocused:()=>call('last',()=>({...state.last})),onRemoved:event(),onFocusChanged:event()},
    tabs:{query:query=>call('tabs',()=>{assert.deepEqual(query,{active:true,windowId:3});return state.tabs??[{...state.tab}];}),onActivated:event(),onUpdated:event(),onRemoved:event()},
    scripting:{executeScript:input=>call(input.args[2]?'read':'bind',()=>{
      assert.equal(input.world,'ISOLATED');assert.equal(input.func,inspectDocument);assert.equal(input.args[0],state.tab.url);
      assert.deepEqual(input.target,input.args[2]?{tabId:7,documentIds:['doc-one']}:{tabId:7,frameIds:[0]});
      let row={frameId:0,documentId:'doc-one',result:{ok:true,nonce:input.args[1],...(input.args[2]?{observation:observed()}:{})}};
      if(options.result)row=options.result(row,input.args[2]);
      return Array.from({length:state.rows},()=>row);
    })}};
  const probe=createProbe(chrome,popup,{now:()=>clock,schedule:(fn,ms)=>{const id=++seq;timers.set(id,{fn,at:clock+ms});return id;},unschedule:id=>timers.delete(id)});
  return {probe,chrome,popup,doc,state,calls,paused,resume:()=>resume(),timers,
    expire(){clock=10000;for(const {fn} of [...timers.values()])fn();}};
}
const flush=()=>new Promise(r=>setImmediate(r));
test('focused authentic popup can read its exact parent/tab when the parent focused flag is false',async()=>{
  const f=fixture();assert.deepEqual(await f.probe.run(),{ok:true,observation:observed()});
  assert.equal(f.calls.filter(x=>x==='read').length,1);assert.equal(f.calls.length,14);assert.equal(f.timers.size,0);f.probe.dispose();
});
test('parent focused=true still requires popup focus',async()=>{
  const f=fixture();f.state.current.focused=f.state.last.focused=true;f.doc.focused=false;
  assert.equal((await f.probe.run()).diagnostic,'R02');assert.equal(f.calls.length,0);
});
for(const [name,change,code] of [
  ['unfocused popup',f=>f.doc.focused=false,'R02'],['hidden popup',f=>f.doc.visibilityState='hidden','R02'],
  ['not a toolbar popup',f=>f.state.views=[],'R31'],['duplicate owner view',f=>f.state.views=[f.popup,f.popup],'R31'],
  ['other popup only',f=>f.state.views=[{}],'R31'],['wrong extension URL',f=>f.popup.location.href='chrome-extension://other/popup.html','R31'],
  ['replaced popup document',f=>f.popup.document={},'R31'],['other last-focused window',f=>f.state.last.id=4,'R15'],
  ['missing owner window id',f=>delete f.state.current.id,'R01'],['non-normal owner',f=>f.state.current.type='popup','R01'],
  ['minimized owner',f=>f.state.current.state='minimized','R01'],['malformed focus metadata',f=>f.state.current.focused='false','R01'],
  ['no selected tab',f=>f.state.tabs=[],'R04'],['two selected tabs',f=>f.state.tabs=[f.state.tab,f.state.tab],'R04'],
  ['wrong tab owner',f=>f.state.tab.windowId=4,'R04'],['inactive tab',f=>f.state.tab.active=false,'R04'],
  ['pending navigation',f=>f.state.tab.pendingUrl='https://chatgpt.com/','R04'],['still loading',f=>f.state.tab.status='loading','R04'],
  ['another origin',f=>f.state.tab.url='https://example.com/','R06'],['lookalike origin',f=>f.state.tab.url='https://chatgpt.com.example.com/','R06'],
  ['credential URL',f=>f.state.tab.url='https://secret@chatgpt.com/','R06'],['invalid URL',f=>f.state.tab.url='invalid','R06']
])test(name+' refuses before injection',async()=>{const f=fixture();change(f);assert.equal((await f.probe.run()).diagnostic,code);assert.ok(!f.calls.includes('bind'));assert.equal(f.timers.size,0);});
for(let index=1;index<=14;index++) {
  test('Disconnect at awaited API '+index+' settles before the API and prevents follow-on dispatch',async()=>{
    const f=fixture({pauseAt:index});const task=f.probe.run();await f.paused;
    const stopped=f.probe.stop();assert.equal(stopped.readDispatched,index>=11);
    assert.equal((await task).diagnostic,'R29');assert.equal(f.calls.length,index);assert.equal(f.timers.size,0);
    f.resume();await flush();assert.equal(f.calls.length,index);
  });
  test('deadline at awaited API '+index+' cannot resurrect later',async()=>{
    const f=fixture({pauseAt:index});const task=f.probe.run();await f.paused;f.expire();
    assert.equal((await task).diagnostic,'R30');f.resume();await flush();assert.equal(f.calls.length,index);assert.equal(f.timers.size,0);
  });
  test('browser rejection at API '+index+' produces only a fixed diagnostic',async()=>{
    const f=fixture({rejectAt:index});const reply=await f.probe.run();assert.equal(reply.ok,false);
    assert.match(reply.diagnostic,/^R\d\d$/);assert.ok(!JSON.stringify(reply).includes('PRIVATE'));assert.equal(f.calls.length,index);
  });
}
for(const [name,stop] of [
  ['blur',f=>{f.doc.focused=false;f.popup.emit('blur');}],['popup close',f=>f.popup.emit('pagehide')],
  ['hidden',f=>{f.doc.visibilityState='hidden';f.doc.emit('visibilitychange');}],
  ['tab changed',f=>f.chrome.tabs.onActivated.emit({windowId:3,tabId:8})],
  ['navigation',f=>f.chrome.tabs.onUpdated.emit(7,{status:'loading'})],
  ['same-document URL change',f=>f.chrome.tabs.onUpdated.emit(7,{url:'https://chatgpt.com/'})],
  ['tab closed',f=>f.chrome.tabs.onRemoved.emit(7)],['owner window closed',f=>f.chrome.windows.onRemoved.emit(3)]
])test(name+' cancels pending injection',async()=>{
  const f=fixture({pauseAt:7});const task=f.probe.run();await f.paused;stop(f);
  assert.equal((await task).diagnostic,'R29');f.resume();await flush();assert.ok(!f.calls.includes('read'));
});
test('a cancelled API completing after a fresh successful check cannot replace its result',async()=>{
  const f=fixture({pauseAt:1});const old=f.probe.run();await f.paused;f.probe.stop();assert.equal((await old).diagnostic,'R29');
  assert.deepEqual(await f.probe.run(),{ok:true,observation:observed()});const count=f.calls.length;
  f.resume();await flush();assert.equal(f.calls.length,count);assert.equal(f.timers.size,0);
});
test('busy second click does not dispatch or steal ownership',async()=>{
  const f=fixture({pauseAt:1});const old=f.probe.run();await f.paused;
  assert.equal((await f.probe.run()).diagnostic,'R24');assert.equal(f.calls.length,1);f.probe.stop();await old;f.resume();
});
for(const index of [7,11,14])test('changed target after API '+index+' is rejected',async()=>{
  const f=fixture({pauseAt:index});const task=f.probe.run();await f.paused;f.state.last.id=4;f.chrome.windows.onFocusChanged.emit(4);f.resume();
  assert.equal((await task).ok,false);
});
for(const [name,mutate,code] of [
  ['subframe',r=>({...r,frameId:1}),'R09'],['missing document',r=>({...r,documentId:''}),'R10'],
  ['wrong nonce',r=>({...r,result:{...r.result,nonce:'old'}}),'R11'],
  ['refused document',r=>({...r,result:{ok:false}}),'R23'],
  ['bounded scan refused',r=>({...r,result:{ok:false,reason:'limit'}}),'R32'],
  ['changed document',r=>({...r,documentId:'replacement'}),'R10'],
  ['extra observation field',r=>({...r,result:{...r.result,observation:{...observed(),privateText:'SECRET'}}}),'R23'],
  ['negative count',r=>({...r,result:{...r.result,observation:{...observed(),renderedRoleNodes:-1}}}),'R23']
])test(name+' cannot be displayed',async()=>{
  const f=fixture({result:(row,read)=>read?mutate(row):row});assert.equal((await f.probe.run()).diagnostic,code);
});
for(const rows of [0,2])test(rows+' injection results are rejected',async()=>{const f=fixture();f.state.rows=rows;assert.equal((await f.probe.run()).diagnostic,'R08');});
test('dispose removes handlers and refuses future use',async()=>{
  const f=fixture();f.probe.dispose();assert.equal(f.chrome.tabs.onActivated.size(),0);assert.equal(f.chrome.windows.onRemoved.size(),0);
  assert.equal((await f.probe.run()).diagnostic,'R31');assert.equal(f.calls.length,0);
});
test('UI uses explicit version and no raw errors or observation fields',()=>{
  assert.match(describe(observed()),/probe 0.1.6/);assert.ok(!diagnostic('private browser error').includes('private browser error'));
  assert.match(describe({...observed(),privateText:'SECRET'}),/R23/);assert.ok(!describe({...observed(),privateText:'SECRET'}).includes('SECRET'));
});
function inspectFixture(nodes,options={}) {
  let index=0,readText=0;
  const href=options.href ?? 'https://chatgpt.com/c/11111111-1111-1111-1111-111111111111';
  for(const node of nodes) {
    node.isConnected=true;node.isContentEditable=!!node.editor;node.getClientRects=()=>node.invisible?[]:[{width:100,height:30}];
    node.closest=()=>null;
    node.matches=selector=>selector==='#prompt-textarea'?!!node.editor:selector===':disabled'?false:selector.startsWith('form')?!!node.file:!!node.streaming;
    node.hasAttribute=name=>name==='data-message-author-role'&&node.role!==undefined;
    node.getAttribute=name=>name==='data-message-author-role'?node.role:null;
    for(const name of ['textContent','innerText','value','files'])Object.defineProperty(node,name,{get(){readText++;throw Error('PRIVATE');}});
  }
  const document={visibilityState:'visible',documentElement:nodes[0],createTreeWalker:()=>({currentNode:nodes[0],nextNode:()=>nodes[++index]??null})};
  const window={};window.top=window;
  const globals={window,document,location:{href},URL,NodeFilter:{SHOW_ELEMENT:1},
    getComputedStyle:()=>({display:'block',visibility:'visible'}),performance:{now:()=>options.expired?index*101:0}};
  if(options.hidden)document.visibilityState='hidden';if(options.frame)window.top={};
  const output=vm.runInNewContext('('+inspectDocument.toString()+')('+JSON.stringify(href)+', "nonce", '+(options.bind?'false':'true')+')',globals);
  return {output:JSON.parse(JSON.stringify(output)),readText};
}
test('structural function reads only attributes, not text/value/files',()=>{
  const f=inspectFixture([{editor:true},{role:'user'},{role:'assistant'},{file:true},{streaming:true}]);
  assert.deepEqual(f.output,{ok:true,nonce:'nonce',observation:observedWithStreaming()});assert.equal(f.readText,0);
  function observedWithStreaming(){return {...observed(),streamingIndicatorPresent:true};}
});
test('binding stage never traverses DOM',()=>{assert.deepEqual(inspectFixture([],{bind:true}).output,{ok:true,nonce:'nonce'});});
test('hidden document and subframes refuse',()=>{for(const options of [{hidden:true},{frame:true}])assert.equal(inspectFixture([] ,options).output.ok,false);});
test('caps preserve the seven structural fields',()=>{
  const f=inspectFixture(Array.from({length:600},()=>({role:'user',editor:true,file:true})));
  assert.equal(f.output.observation.editorCandidates,2);assert.equal(f.output.observation.renderedRoleNodes,513);
  assert.equal(f.output.observation.renderedUserCount,513);assert.equal(f.output.observation.fileInputCandidates,2);
});
test('node/time limits refuse instead of returning partial counts',()=>{
  assert.deepEqual(inspectFixture(Array.from({length:8001},()=>({}))).output,{ok:false,reason:'limit'});
  assert.deepEqual(inspectFixture([{},{}],{expired:true}).output,{ok:false,reason:'limit'});
});
test('package has no background worker, content listener, native host or broad permissions',()=>{
  const manifest=JSON.parse(fs.readFileSync(require.resolve('../manifest.json'),'utf8'));
  assert.equal(manifest.version,'0.1.6');assert.deepEqual(manifest.permissions,['activeTab','scripting']);
  for(const key of ['background','content_scripts','host_permissions','externally_connectable'])assert.equal(manifest[key],undefined);
  const source=fs.readFileSync(require.resolve('../probe.js'),'utf8')+fs.readFileSync(require.resolve('../popup.js'),'utf8');
  assert.doesNotMatch(source,/connectNative|sendNativeMessage|sendMessage\(|\.fetch\(|fetch\(|localStorage|\.submit\(|\.click\(|dispatchEvent\(/);
});

function popupFixture() {
  const elements=Object.fromEntries(['status','check','disconnect'].map(name=>[name,{disabled:false,textContent:'',handlers:{},addEventListener(type,fn){this.handlers[type]=fn;}}]));
  let resolve, stops=0, disposed=0;
  const probe={run:()=>new Promise(r=>resolve=r),stop:()=>{stops++;return {readDispatched:true};},dispose:()=>disposed++};
  const window=dom();const api=require('../probe.js');
  vm.runInNewContext(fs.readFileSync(require.resolve('../popup.js'),'utf8'),{
    document:{getElementById:id=>elements[id]},window,chrome:{},BuddyReadiness:{...api,createProbe:()=>probe}});
  return {elements,window,settle:reply=>resolve(reply),stops:()=>stops,disposed:()=>disposed};
}
test('popup Stop feedback is immediate and a late successful result cannot overwrite it',async()=>{
  const f=popupFixture();const task=f.elements.check.handlers.click();assert.equal(f.elements.check.disabled,true);
  f.elements.disconnect.handlers.click();assert.equal(f.stops(),1);assert.match(f.elements.status.textContent,/already-dispatched/);
  const stopped=f.elements.status.textContent;f.settle({ok:true,observation:observed()});await task;
  assert.equal(f.elements.status.textContent,stopped);assert.equal(f.elements.check.disabled,false);
});
test('popup close disposes its owner and suppresses the pending result',async()=>{
  const f=popupFixture();const task=f.elements.check.handlers.click();f.window.emit('pagehide');assert.equal(f.disposed(),1);
  const before=f.elements.status.textContent;f.settle({ok:true,observation:observed()});await task;assert.equal(f.elements.status.textContent,before);
});
test('popup failure shows a versioned fixed code and re-enables a deliberate new check',async()=>{
  const f=popupFixture();const task=f.elements.check.handlers.click();f.settle({ok:false,diagnostic:'R02',privateError:'SECRET'});await task;
  assert.match(f.elements.status.textContent,/R02 \(probe 0.1.6\)/);assert.ok(!f.elements.status.textContent.includes('SECRET'));assert.equal(f.elements.check.disabled,false);
});

for(const [count, expected] of [[0,/does not prove the message box is absent/],[1,/draft access is not verified/],[2,/composer is ambiguous/]])
  test('reported editor count '+count+' does not grant a provider capability',()=>{
    const text=describe({...observed(),editorCandidates:count,fileInputCandidates:2,stableConversationRoute:false});
    assert.match(text,expected);assert.match(text,/controls, not attachments/);assert.match(text,/Recognized URL shapes/);
    assert.match(text,/capabilities remain unavailable/);
  });


const routeUuid='12345678-1234-4321-abcd-123456789abc';
for(const [name,path,expected] of [
  ['standard conversation','/c/'+routeUuid,true],
  ['standard trailing slash','/c/'+routeUuid+'/',true],
  ['project conversation','/g/g-p-0123456789abcdef-example-project/c/'+routeUuid,true],
  ['GPT conversation','/g/g-Example123-example/c/'+routeUuid,true],
  ['project trailing slash','/g/g-p-0123456789abcdef-example/c/'+routeUuid+'/',true],
  ['query and fragment','/g/g-p-example/c/'+routeUuid+'?view=example#section',true],
  ['new chat','/',false],
  ['project home','/g/g-p-example/project',false],
  ['GPT home','/g/g-example',false],
  ['missing conversation UUID','/g/g-p-example/c/',false],
  ['malformed UUID','/g/g-p-example/c/1234',false],
  ['unsupported prefix','/unrelated/c/'+routeUuid,false],
  ['missing scoped ID prefix','/g/example/c/'+routeUuid,false],
  ['extra suffix','/g/g-p-example/c/'+routeUuid+'/settings',false],
  ['encoded slash','/g/g-p-example%2Fextra/c/'+routeUuid,false],
  ['empty scoped ID','/g/g-/c/'+routeUuid,false]
])test('route shape: '+name,()=>{
  const f=inspectFixture([{editor:true}],{href:'https://chatgpt.com'+path});
  assert.equal(f.output.ok,true);assert.equal(f.output.observation.stableConversationRoute,expected);
  assert.equal(f.output.observation.renderedRoleNodes,0);assert.equal(f.readText,0);
  assert.equal(Object.keys(f.output.observation).length,7);
  assert.ok(!JSON.stringify(f.output).includes(routeUuid),'URL/IDs are not returned');
});
for(const href of ['https://chatgpt.com.example.com/c/'+routeUuid,'https://example.com/g/g-p-example/c/'+routeUuid,'https://private@chatgpt.com/c/'+routeUuid])
  test('route shape never admits a different or credential-bearing origin: '+new URL(href).hostname,()=>{
    assert.equal(inspectFixture([],{href}).output.ok,false);
  });
test('unrecognized message roles are explained even with a recognized route and editor',()=>{
  const result=describe({...observed(),renderedRoleNodes:0,renderedUserCount:0,renderedAssistantCount:0});
  assert.match(result,/visible messages may still be present/);
  assert.match(result,/draft access is not verified/);
  assert.match(result,/capabilities remain unavailable/);
});

test('loaded message counts never imply complete virtualized history',()=>{assert.match(describe(observed()),/loaded DOM only; virtualized turns may be absent/);});
