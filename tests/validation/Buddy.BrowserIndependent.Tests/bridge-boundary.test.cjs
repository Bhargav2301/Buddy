'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const root=process.env.BUDDY_BROWSER_SOURCE_ROOT;
if(!root||!path.isAbsolute(root))throw new Error('Explicit absolute BUDDY_BROWSER_SOURCE_ROOT required');
const names=['core.js','content-controller.js','bridge.js'];
const hash=p=>crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex');
const before=Object.fromEntries(names.map(n=>[n,hash(path.join(root,n))]));
const core=require(path.join(root,'core.js')),{createController}=require(path.join(root,'content-controller.js')),{createBridge}=require(path.join(root,'bridge.js'));
const event=()=>{const callbacks=[];return {addListener:f=>callbacks.push(f),emit:(...a)=>callbacks.forEach(f=>f(...a))};};
const pending=()=>{let resolve;const promise=new Promise(r=>resolve=r);return {promise,resolve};};
const flush=()=>new Promise(r=>setImmediate(r));

// Independent browser/native endpoint doubles. No browser, IPC, upload or account.
// The production controller, bridge, chunk assembler and reviewed-stage owner run.
async function fixture(){
 const state={draft:'Owned original Ω',writes:0,undos:0,sends:0,active:true,window:12,focused:true,queryCount:1,closed:false,
  url:'https://chatgpt.com/c/qa-conversation',identity:{accountId:'qa-account',workspaceId:'qa-workspace',conversationId:'qa-conversation'},
  turns:[{id:'u1',role:'user',text:'Keep 4 numbered checks.',complete:true},{id:'a1',role:'assistant',text:'Do not publish without review.',complete:true}],
  hold:null,replyPatch:null,beforeContent:null,afterContent:null,stageHook:null,native:[],content:[],timers:[],documentId:'doc-qa'};
 const original=state.draft,rewritten='Reviewed prompt Ω\r\nKeep 4 numbered checks.';
 const observation={editorCandidates:1,renderedUserCount:1,renderedAssistantCount:1,accountIdentity:'verified',capabilities:{history:true,confirmedAttachments:true}};
 const location={get href(){return state.url;}};
 const driver={characterize:()=>observation,observe:()=>({identity:{...state.identity},composerId:'qa-composer',evidence:{source:'synthetic'},capabilities:{draft:true,originalAssets:true,confirmedAttachments:true}}),
  guard:expected=>assert.deepEqual(state.identity,expected.identity),readDraft:()=>state.draft,
  captureTurns:()=>({turns:structuredClone(state.turns),coverage:{complete:true,hasEarlier:false,hasLater:false,scope:'rendered-completed-pairs',signal:'synthetic-complete',conversationId:state.identity.conversationId,firstId:'u1',lastId:'a1'}}),
  readAttachments:()=>[],stageDraft:async(expected,old,next)=>{assert.equal(state.draft,old);state.writes++;state.draft=next;await state.stageHook?.();return {exact:true};},
  undoDraft:()=>{state.undos++;state.draft=original;return {exact:true};}};
 const controller=createController(driver,location,'qa-generation');
 const bytes=new TextEncoder().encode(rewritten),oldBytes=new TextEncoder().encode(original);
 const offer={reviewId:'qa-review',digest:'d'.repeat(64),originalDraftSha256:await core.sha256(oldBytes),composerId:'qa-composer',contents:[{contentId:'draft',role:'draft',name:'Reviewed draft',mimeType:'text/plain',byteLength:bytes.length,sha256:await core.sha256(bytes)}]};
 const onMessage=event(),onDisconnect=event();
 const port={onMessage,onDisconnect,disconnect(){if(!state.closed){state.closed=true;onDisconnect.emit();}},postMessage(message){
  state.native.push(structuredClone(message));
  if(state.hold===message.kind)return;
  queueMicrotask(()=>{if(state.closed)return;
   let reply=message.kind==='register'?{version:1,kind:'registered',status:'awaiting-pairing',connectionId:'a'.repeat(32),nonce:'b'.repeat(64),pairingChallenge:'c'.repeat(16)}:
    {version:1,connectionId:'a'.repeat(32),sequence:message.sequence,kind:message.kind,status:'ok',payload:null};
   if(message.kind==='draft.poll')reply.payload=offer;
   if(message.kind==='draft.chunk.read'){const data=message.payload.contentId==='undo-original'?oldBytes:bytes;const index=message.payload.index;
    reply.payload={...message.payload,last:(index+1)*core.LIMITS.chunk>=data.length,dataBase64:core.base64(data.slice(index*core.LIMITS.chunk,(index+1)*core.LIMITS.chunk))};}
   if(message.kind==='draft.state'&&message.payload.state==='staged')reply.payload={reviewId:offer.reviewId,digest:offer.digest,state:'staged',receiptDigest:'e'.repeat(64)};
   if(message.kind==='undo.poll')reply.payload={reviewId:offer.reviewId,receiptDigest:'e'.repeat(64),currentDraftSha256:offer.contents[0].sha256,composerId:offer.composerId,
     originalDraft:{contentId:'undo-original',role:'undo-original',name:'Exact original draft',mimeType:'text/plain',byteLength:oldBytes.length,sha256:offer.originalDraftSha256},attachments:[]};
   if(state.replyPatch)reply=state.replyPatch(reply,message);onMessage.emit(reply);
  });
 }};
 const tab=()=>({id:41,windowId:12,url:state.url,active:state.active});
 const chrome={runtime:{connectNative:name=>{assert.equal(name,'com.buddy.browser_context');return port;}},
  windows:{getLastFocused:async()=>({id:state.window,focused:state.focused}),onFocusChanged:event()},
  tabs:{query:async query=>{assert.equal(query.windowId,12);return Array.from({length:state.queryCount},(_,i)=>({...tab(),id:41+i}));},get:async()=>tab(),onUpdated:event(),onRemoved:event(),onActivated:event(),
   sendMessage:async(id,message,target)=>{assert.equal(id,41);assert.equal(target.documentId,'doc-qa');state.content.push(message.op);
    if(message.op==='invalidate'){controller.invalidate();return {ok:true};}
    await state.beforeContent?.(message.op);let reply;
    try{reply={ok:true,value:await controller[message.op](message.payload)};}catch(e){reply={ok:false,code:core.safeReason(e)};}
    await state.afterContent?.(message.op);return reply;}},
  scripting:{executeScript:async request=>{assert.deepEqual(request.target,{tabId:41,frameIds:[0]});assert.equal(request.world,'ISOLATED');return [{frameId:0,documentId:state.documentId,result:{generation:'qa-generation',observation}}];}}};
 const bridge=createBridge(chrome,{schedule:fn=>{state.timers.push(fn);return fn;},cancelTimer:fn=>{state.timers=state.timers.filter(x=>x!==fn);}});
 return {state,bridge,chrome,controller,port,offer,onMessage,async start(){await bridge.run('pair');await bridge.run('capture');},cleanup(){bridge.disconnect();}};
}

test('exact staged and undone text traverse controller and native protocol with no Send operation',async()=>{
 const f=await fixture();await f.start();assert.equal((await f.bridge.run('stage')).status,'staged');assert.equal(f.state.writes,1);
 assert.equal((await f.bridge.run('undo')).status,'undone');assert.equal(f.state.draft,'Owned original Ω');assert.equal(f.state.undos,1);
 assert.equal(f.state.native.some(x=>/send|submit/i.test(x.kind)),false);assert.equal(f.state.sends,0);f.cleanup();
});
for(const mutate of [f=>f.state.window=13,f=>f.state.focused=false,f=>f.state.queryCount=2,f=>f.state.active=false])
 test('active status alone cannot authorize a different or ambiguous foreground destination '+mutate,async()=>{
  const f=await fixture();await f.start();mutate(f);await assert.rejects(f.bridge.run('stage'));assert.equal(f.state.writes,0);assert.equal(f.bridge.state().status,'disconnected');f.cleanup();
 });
test('focus change during readback refuses even if original tab remains active',async()=>{
 const f=await fixture();await f.start();f.state.afterContent=op=>{if(op==='identity')f.state.window=13;};await assert.rejects(f.bridge.run('stage'));
 assert.equal(f.state.native.some(x=>x.kind==='draft.poll'),false);assert.equal(f.state.writes,0);f.cleanup();
});
test('idle Buddy review may suspend browser focus, but return to same browser must be fresh',async()=>{
 const f=await fixture();await f.start();f.state.focused=false;f.chrome.windows.onFocusChanged.emit(-1);assert.notEqual(f.bridge.state().status,'disconnected');
 f.state.focused=true;assert.equal((await f.bridge.run('stage')).status,'staged');f.cleanup();
});
test('different browser-window event permanently invalidates idle pairing',async()=>{
 const f=await fixture();await f.start();f.chrome.windows.onFocusChanged.emit(13);await assert.rejects(f.bridge.run('stage'));assert.equal(f.state.writes,0);f.cleanup();
});
test('pending request loses ownership on non-browser focus and cannot use late native response',async()=>{
 const f=await fixture();await f.start();f.state.hold='draft.poll';const task=f.bridge.run('stage');await flush();f.chrome.windows.onFocusChanged.emit(-1);
 await assert.rejects(task);assert.equal(f.state.writes,0);assert.equal(f.bridge.state().status,'disconnected');f.cleanup();
});
test('explicit popup disconnect cancels a busy native request immediately',async()=>{
 const f=await fixture();await f.start();f.state.hold='draft.poll';const task=f.bridge.run('stage');const result=task.then(()=>null,e=>e);await flush();
 let error;try{assert.equal((await f.bridge.run('disconnect')).status,'disconnected');assert.equal(f.bridge.state().status,'disconnected');}catch(e){error=e;}finally{f.cleanup();}
 assert.ok(await result);assert.equal(f.state.writes,0);if(error)throw error;
});
test('native timeout closes owner without a new poll or effect',async()=>{
 const f=await fixture();await f.start();f.state.hold='draft.poll';const task=f.bridge.run('stage');const observed=assert.rejects(task);await flush();
 assert.equal(f.state.timers.length,1);f.state.timers[0]();await observed;assert.equal(f.state.writes,0);assert.equal(f.state.native.filter(x=>x.kind==='draft.poll').length,1);f.cleanup();
});
for(const field of ['connectionId','sequence','kind'])test('native reply '+field+' substitution refuses before mutation',async()=>{
 const f=await fixture();await f.start();f.state.replyPatch=(reply,request)=>request.kind==='draft.poll'?{...reply,[field]:field==='sequence'?999:'unrelated'}:reply;
 await assert.rejects(f.bridge.run('stage'));assert.equal(f.state.writes,0);assert.equal(f.bridge.state().status,'disconnected');f.cleanup();
});
for(const change of [f=>f.state.identity.accountId='other',f=>f.state.identity.workspaceId='other',f=>f.state.identity.conversationId='other',f=>f.state.turns[1].text='Replaced complete answer'])
 test('same URL cannot reuse review after identity or captured history changes '+change,async()=>{
  const f=await fixture();await f.start();change(f);const outcome=await f.bridge.run('stage').then(value=>({value}),error=>({error}));
  assert.ok(outcome.error||outcome.value.status!=='staged');assert.equal(f.state.writes,0);f.cleanup();
 });
test('disconnect after draft effect is partial and never a successful receipt or Undo',async()=>{
 const f=await fixture();await f.start();f.state.stageHook=()=>f.port.disconnect();const r=await f.bridge.run('stage');
 assert.equal(r.status,'partial-or-unknown');assert.equal(f.state.writes,1);assert.equal(f.state.native.some(x=>x.kind==='draft.state'&&x.payload.state==='staged'),false);
 await assert.rejects(f.bridge.run('undo'));assert.equal(f.state.undos,0);f.cleanup();
});
test('history mutation during pending commit invalidates staged result after an honest partial write',async()=>{
 const f=await fixture();await f.start();const gate=pending(),entered=pending();f.state.stageHook=()=>{entered.resolve();return gate.promise;};
 const task=f.bridge.run('stage');await entered.promise;f.state.turns[0].text='A newer question';gate.resolve();const r=await task;
 assert.equal(r.status,'partial-or-unknown');assert.equal(f.state.writes,1);assert.equal(f.state.native.some(x=>x.kind==='draft.state'&&x.payload.state==='staged'),false);f.cleanup();
});
test('no stale concurrent stage can consume another grant',async()=>{
 const f=await fixture();await f.start();f.state.hold='draft.poll';const task=f.bridge.run('stage');const observed=assert.rejects(task);await flush();
 await assert.rejects(f.bridge.run('stage'));assert.equal(f.state.native.filter(x=>x.kind==='draft.poll').length,1);f.cleanup();await observed;
});
test('dependency files stayed unchanged through independent tests',()=>{assert.deepEqual(Object.fromEntries(names.map(n=>[n,hash(path.join(root,n))])),before);console.log(JSON.stringify({sourceRoot:root,hashes:before,scope:'injected browser/native endpoints; no actual browser, IPC, upload or Send'}));});
