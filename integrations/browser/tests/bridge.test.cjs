'use strict';
const test=require('node:test');const assert=require('node:assert/strict');
const core=require('../core.js');const {createController}=require('../content-controller.js');
const {createBridge}=require('../bridge.js');const {fixture}=require('./dom-fixture.cjs');
function event(){const listeners=[];return {addListener:f=>listeners.push(f),emit:(...args)=>listeners.forEach(f=>f(...args))};}
async function setup({original='Original draft',draft='Reviewed prompt 🌙',asset=false}={}){
  const f=fixture();f.editor._text=original;
  const observation={editorCandidates:1,renderedUserCount:1,renderedAssistantCount:1,accountIdentity:'verified',capabilities:{history:true,confirmedAttachments:true}};
  const adapter={...f.driver,characterize:()=>observation};
  let generation=0,controller=null;
  const bytes=new TextEncoder().encode(draft),originalBytes=new TextEncoder().encode(original);
  const contents=[{contentId:'draft',role:'draft',name:'Reviewed draft',mimeType:'text/plain',byteLength:bytes.length,sha256:await core.sha256(bytes)}];
  const data={draft:bytes,'undo-original':originalBytes};
  if(asset){const b=new Uint8Array([11,22,33]);data['asset-0']=b;contents.push({contentId:'asset-0',role:'original',name:'image.png',mimeType:'image/png',byteLength:b.length,sha256:await core.sha256(b)});
    f.input.handlers.change=()=>f.receipts.push({id:'receipt-1',name:'image.png',size:3,sha256:contents[1].sha256,state:'complete',visible:true});}
  const offer={reviewId:'review-one',digest:'d'.repeat(64),originalDraftSha256:await core.sha256(originalBytes),composerId:'composer-1',contents};
  const messages=[],nativeMessages=event(),disconnected=event();let sequence=0,closed=false,approved=true,staged=null,polls=0;
  const tab={id:7,windowId:3,url:'https://chatgpt.com/c/11111111-1111-1111-1111-111111111111',active:true};
  const focused={id:3,focused:true};
  const port={onMessage:nativeMessages,onDisconnect:disconnected,disconnect(){if(!closed){closed=true;disconnected.emit();}},postMessage(message){
    messages.push(structuredClone(message));core.wire(message);
    queueMicrotask(()=>{
      if(closed)return;
      if(message.kind==='register'){nativeMessages.emit({version:1,kind:'registered',connectionId:'a'.repeat(32),nonce:'b'.repeat(64),pairingChallenge:'c'.repeat(16),status:'awaiting-pairing'});return;}
      assert.equal(message.sequence,++sequence);assert.equal(message.nonce,'b'.repeat(64));
      const reply={version:1,connectionId:'a'.repeat(32),sequence,kind:message.kind,status:'ok',code:null,payload:null};
      if(message.kind==='draft.poll'){polls++;if(!approved){reply.status='waiting';}else{approved=false;reply.payload=offer;}}
      if(message.kind==='draft.chunk.read'){
        const p=message.payload,b=data[p.contentId];assert.ok(b);const start=p.index*core.LIMITS.chunk;
        assert.ok(start<b.length || (start===0&&b.length===0));
        reply.payload={...p,dataBase64:core.base64(b.slice(start,start+core.LIMITS.chunk)),last:start+core.LIMITS.chunk>=b.length};
      }
      if(message.kind==='draft.state'){
        const p=message.payload;assert.ok(['pending','staged','partial-or-unknown'].includes(p.state));
        if(p.state==='staged'){assert.equal(p.draftSha256,contents[0].sha256);assert.ok(p.attachments.every(x=>x.state==='ready'));staged=p;
          reply.payload={reviewId:offer.reviewId,digest:offer.digest,state:'staged',originalDraftSha256:offer.originalDraftSha256,draftSha256:contents[0].sha256,contents,receiptDigest:'e'.repeat(64)};}
      }
      if(message.kind==='undo.poll'){assert.ok(staged);reply.payload={reviewId:offer.reviewId,receiptDigest:'e'.repeat(64),currentDraftSha256:contents[0].sha256,composerId:offer.composerId,
        originalDraft:{contentId:'undo-original',role:'undo-original',name:'Exact original draft',mimeType:'text/plain',byteLength:originalBytes.length,sha256:offer.originalDraftSha256},attachments:staged.attachments};}
      nativeMessages.emit(reply);
    });
  }};
  const chrome={runtime:{connectNative(name){assert.equal(name,'com.buddy.browser_context');return port;}},
    windows:{getLastFocused:async()=>focused,onFocusChanged:event()},
    tabs:{query:async()=>[tab],get:async()=>tab,onUpdated:event(),onRemoved:event(),onActivated:event(),
      sendMessage:async(id,msg,opts)=>{assert.equal(id,7);assert.equal(opts.documentId,'document-one');
        if(msg.op==='invalidate'){controller.invalidate();return {ok:true};}
        try{return {ok:true,value:await controller[msg.op](msg.payload)};}catch(e){return {ok:false,code:core.safeReason(e)};}}},
    scripting:{executeScript:async options=>{assert.deepEqual(options.target,{tabId:7,frameIds:[0]});assert.equal(options.world,'ISOLATED');controller?.invalidate();const id='generation-'+ ++generation;controller=createController(adapter,f.location,id);return [{frameId:0,documentId:'document-one',result:{generation:id,observation}}];}}};
  const bridge=createBridge(chrome);return {bridge,f,chrome,messages,tab,focused,port,offer,get controller(){return controller;},get polls(){return polls;},get staged(){return staged;}};
}
test('readiness injects only selected top document; no host, history or prompt payload',async()=>{
  const s=await setup();const r=await s.bridge.run('readiness');assert.equal(r.status,'readiness-only');assert.equal(s.messages.length,0);
  assert.equal(JSON.stringify(r).includes('Original draft'),false);s.bridge.disconnect();
});
test('v1 bridge uses exact chunks, pending gate, staged ready receipt and text Undo without Send',async()=>{
  const s=await setup({asset:true});await s.bridge.run('pair');await s.bridge.run('capture');
  assert.equal((await s.bridge.run('stage')).status,'staged');assert.equal(s.f.editor.innerText,'Reviewed prompt 🌙');
  assert.equal(s.staged.attachments[0].providerAttachmentId,'receipt-1');assert.equal(s.polls,1);
  assert.equal((await s.bridge.run('undo')).status,'undone');assert.equal(s.f.editor.innerText,'Original draft');assert.equal(s.f.input.files.length,1);
  assert.deepEqual(s.f.editor.events,['input','input']);assert.deepEqual(s.f.input.events,['input','change']);
  assert.equal(s.messages.some(m=>/send|submit/i.test(m.kind)),false);s.bridge.disconnect();
});
test('empty draft and empty Undo-original each explicitly fetch one zero-length chunk',async()=>{
  const s=await setup({original:'',draft:''});await s.bridge.run('pair');await s.bridge.run('capture');
  assert.equal((await s.bridge.run('stage')).status,'staged');assert.equal((await s.bridge.run('undo')).status,'undone');
  const chunks=s.messages.filter(m=>m.kind==='draft.chunk.read');assert.equal(chunks.length,2);assert.deepEqual(chunks.map(x=>x.payload.index),[0,0]);s.bridge.disconnect();
});
test('navigation, tab change and disconnect permanently invalidate pinned content',async()=>{
  for(const change of [s=>s.chrome.tabs.onUpdated.emit(7,{url:'https://chatgpt.com/c/other'}),s=>s.chrome.tabs.onActivated.emit({tabId:9}),s=>s.port.disconnect()]){
    const s=await setup();await s.bridge.run('pair');change(s);await assert.rejects(s.bridge.run('capture'));assert.equal(s.f.editor.writes,0);assert.equal(s.bridge.state().status,'disconnected');
  }
});
test('account loss refuses and cannot recover the previous reviewed operation',async()=>{
  const s=await setup();await s.bridge.run('pair');await s.bridge.run('capture');s.f.identity.attrs.account='';
  await assert.rejects(s.bridge.run('stage'));s.f.identity.attrs.account='account-one';await assert.rejects(s.bridge.run('stage'));assert.equal(s.f.editor.writes,0);s.bridge.disconnect();
});
test('disconnect during a file input event reports possible partial effects, never retries or Send',async()=>{
  const s=await setup({asset:true});await s.bridge.run('pair');await s.bridge.run('capture');
  s.f.input.handlers.input=()=>s.port.disconnect();const result=await s.bridge.run('stage');
  assert.equal(result.status,'partial-or-unknown');assert.equal(s.f.input.files.length,1);assert.deepEqual(s.f.input.events,['input']);assert.equal(s.polls,1);
});
test('old active tab in an unfocused browser window is not a current destination',async()=>{
  const s=await setup();await s.bridge.run('pair');await s.bridge.run('capture');
  s.focused.id=5; // tab.active remains true in its original window; no tab event.
  await assert.rejects(s.bridge.run('stage'));assert.equal(s.f.editor.writes,0);assert.equal(s.bridge.state().status,'disconnected');
  const other=await setup();await other.bridge.run('pair');other.chrome.windows.onFocusChanged.emit(5);
  assert.equal(other.bridge.state().status,'disconnected');
});
test('same-chat completed history edit after capture invalidates prior review',async()=>{
  const s=await setup();await s.bridge.run('pair');await s.bridge.run('capture');
  s.f.assistant.children[0]._text='Changed assistant response';
  const result=await s.bridge.run('stage');assert.equal(result.status,'partial-or-unknown');assert.equal(s.f.editor.writes,0);
  assert.equal(s.bridge.state().status,'disconnected');
});
test('absent native host is fixed failure and readiness remains available without registration',async()=>{
  const s=await setup();s.chrome.runtime.connectNative=()=>{throw new Error('private-machine-error');};
  await assert.rejects(s.bridge.run('pair'),e=>e.code==='NATIVE_UNAVAILABLE');assert.equal(s.messages.length,0);
  assert.equal((await s.bridge.run('readiness')).status,'readiness-only');assert.equal(s.f.editor.writes,0);s.bridge.disconnect();
});
test('explicit disconnect is accepted during pending staging and late completion cannot resume authority',async()=>{
  const s=await setup();await s.bridge.run('pair');await s.bridge.run('capture');
  const original=s.chrome.tabs.sendMessage;let release,entered;
  const arrived=new Promise(resolve=>entered=resolve);
  s.chrome.tabs.sendMessage=async(id,message,options)=>{
    if(message.op==='commit'){entered();await new Promise(resolve=>release=resolve);}
    return await original(id,message,options);
  };
  const staging=s.bridge.run('stage');await arrived;
  assert.equal((await s.bridge.run('disconnect')).status,'disconnected');release();
  assert.equal((await staging).status,'partial-or-unknown');assert.equal(s.f.editor.writes,0);assert.equal(s.polls,1);assert.equal(s.bridge.state().status,'disconnected');
});
