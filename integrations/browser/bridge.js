(function (root) {
  'use strict';
  const core=root.BuddyBrowserCore;
  const HOST='com.buddy.browser_context';
  const SCRIPTS=['core.js','dom-driver.js','providers/chatgpt.js','providers/registry.js','content-controller.js','content.js'];
  const fail=code=>{const e=new Error(code);e.code=code;throw e;};
  function createBridge(chrome, options={}) {
    let current=null,busy=false;
    const schedule=options.schedule??setTimeout,cancelTimer=options.cancelTimer??clearTimeout;
    async function foreground(s) {
      const window=await chrome.windows.getLastFocused();
      const active=await chrome.tabs.query({active:true,windowId:s.windowId});
      if(current!==s || s.closed || !window.focused || window.id!==s.windowId || active.length!==1 || active[0].id!==s.tabId){close(s);fail('DESTINATION_CHANGED');}
    }
    async function content(s,op,payload) {
      if(current!==s || s.closed) fail('SESSION_INVALIDATED');
      await foreground(s);
      const tab=await chrome.tabs.get(s.tabId);
      if(!tab.active || tab.windowId!==s.windowId || tab.url!==s.url) { close(s);fail('DESTINATION_CHANGED'); }
      const reply=await chrome.tabs.sendMessage(s.tabId,{channel:'buddy-browser-v1',op,payload},{documentId:s.documentId});
      await foreground(s);
      if(current!==s || s.closed) fail('SESSION_INVALIDATED');
      if(!reply || reply.ok!==true) fail(reply?.code==='ACCOUNT_IDENTITY_UNAVAILABLE'?'ACCOUNT_IDENTITY_UNAVAILABLE':'CONTENT_REFUSED');
      return reply.value;
    }
    function close(s=current) {
      if(!s || s.closed)return;
      s.closed=true;
      try{chrome.tabs.sendMessage(s.tabId,{channel:'buddy-browser-v1',op:'invalidate'},{documentId:s.documentId}).catch(()=>{});}catch{}
      if(s.pending){s.pending.reject(Object.assign(new Error('NATIVE_DISCONNECTED'),{code:'NATIVE_DISCONNECTED'}));cancelTimer(s.pending.timer);s.pending=null;}
      try{s.port?.disconnect();}catch{}
      s.port=null;s.binding=null;s.offer=null;s.applied=null;
      if(current===s)current=null;
    }
    async function characterize() {
      close();
      const window=await chrome.windows.getLastFocused();if(!window.focused)fail('DESTINATION_CHANGED');
      const tabs=await chrome.tabs.query({active:true,windowId:window.id});
      if(tabs.length!==1 || !Number.isInteger(tabs[0].id) || tabs[0].windowId!==window.id || new URL(tabs[0].url).origin!=='https://chatgpt.com')fail('PROVIDER_UNSUPPORTED');
      const tab=tabs[0];
      const results=await chrome.scripting.executeScript({target:{tabId:tab.id,frameIds:[0]},world:'ISOLATED',files:SCRIPTS});
      if(results.length!==1 || results[0].frameId!==0 || typeof results[0].documentId!=='string' || !results[0].result?.generation)fail('DOCUMENT_UNAVAILABLE');
      const result=results[0].result;
      const s={tabId:tab.id,windowId:tab.windowId,url:tab.url,documentId:results[0].documentId,generation:result.generation,observation:result.observation,
        closed:false,port:null,pending:null,connectionId:null,nonce:null,sequence:0,binding:null,offer:null,applied:null};
      current=s;
      // Re-read from the exact browser-provided document before reporting even
      // structural readiness. No account, prompt, title or file text is returned.
      s.observation=await content(s,'characterize');
      return publicState(s);
    }
    function publicState(s=current) {
      if(!s)return {status:'disconnected'};
      return {status:s.connectionId?'awaiting-host-review':'readiness-only',pairingChallenge:s.challenge??null,
        observation:s.observation};
    }
    function native(s,message,registration=false) {
      if(current!==s || s.closed || s.pending)fail('SESSION_INVALIDATED');
      core.wire(message);
      return new Promise((resolve,reject)=>{
        const timer=schedule(()=>{if(s.pending){s.pending=null;reject(Object.assign(new Error('NATIVE_TIMEOUT'),{code:'NATIVE_TIMEOUT'}));close(s);}},15000);
        s.pending={resolve,reject,timer,registration,sequence:message.sequence,kind:message.kind};
        try{s.port.postMessage(message);}catch{cancelTimer(timer);s.pending=null;close(s);reject(Object.assign(new Error('NATIVE_UNAVAILABLE'),{code:'NATIVE_UNAVAILABLE'}));}
      });
    }
    function listen(s) {
      s.port.onDisconnect.addListener(()=>close(s));
      s.port.onMessage.addListener(reply=>{
        const pending=s.pending;
        try{
          core.wire(reply);
          if(current!==s || s.closed || !pending || reply.version!==1)fail('NATIVE_REPLY_INVALID');
          if(pending.registration){
            if(reply.kind!=='registered' || reply.status!=='awaiting-pairing' || !/^[a-f0-9]{32}$/.test(reply.connectionId) || !/^[a-f0-9]{64}$/.test(reply.nonce) || !/^[a-f0-9]{16}$/.test(reply.pairingChallenge))fail('NATIVE_REPLY_INVALID');
          }else if(reply.connectionId!==s.connectionId || reply.sequence!==pending.sequence || reply.kind!==pending.kind || !['ok','waiting','refused'].includes(reply.status))fail('NATIVE_REPLY_INVALID');
          s.pending=null;cancelTimer(pending.timer);pending.resolve(reply);
        }catch{close(s);}
      });
    }
    async function request(s,kind,payload={},value=s.binding) {
      if(!s.connectionId || ++s.sequence>core.LIMITS.sequence)fail('SESSION_INVALIDATED');
      const ready=value??{providerId:'chatgpt',tabId:s.tabId,documentId:s.documentId,generation:s.generation,accountId:'',workspaceId:'',conversationId:''};
      const reply=await native(s,{version:1,connectionId:s.connectionId,nonce:s.nonce,sequence:s.sequence,kind,binding:core.binding(ready,!value),payload});
      if(reply.status==='refused')fail('HOST_REFUSED');return reply;
    }
    function readiness(observation) {
      return {composerCount:observation.editorCandidates,renderedUserCount:observation.renderedUserCount,
        renderedAssistantCount:observation.renderedAssistantCount,identitySignalAvailable:observation.accountIdentity==='verified',
        completeHistorySignalAvailable:observation.capabilities.history,attachmentReceiptSignalAvailable:observation.capabilities.confirmedAttachments};
    }
    async function pair() {
      if(!current)await characterize(); const s=current;
      if(!s.port){
        try{s.port=chrome.runtime.connectNative(HOST);}catch{fail('NATIVE_UNAVAILABLE');}
        listen(s);
        const reply=await native(s,{version:1,kind:'register',providerId:'chatgpt',origin:'https://chatgpt.com',tabId:s.tabId,frameId:0,documentId:s.documentId,generation:s.generation},true);
        s.connectionId=reply.connectionId;s.nonce=reply.nonce;s.challenge=reply.pairingChallenge;
      }
      const observation=await content(s,'characterize');s.observation=observation;
      await request(s,'readiness.report',readiness(observation));return publicState(s);
    }
    async function admit() {
      const s=current;if(!s?.connectionId)fail('SESSION_UNPAIRED');
      const observed=await content(s,'identity');
      const value=core.binding({providerId:'chatgpt',tabId:s.tabId,documentId:s.documentId,generation:s.generation,...observed.identity});
      if(s.binding){if(!core.sameBinding(s.binding,value)){close(s);fail('DESTINATION_CHANGED');}return s;}
      const bound=await content(s,'bind',value);
      await request(s,'identity.bind',{evidence:bound.evidence},value);s.binding=value;return s;
    }
    async function capture() {
      const s=await admit(),captured=await content(s,'capture');
      const bytes=new TextEncoder().encode(JSON.stringify(captured.document));
      if(bytes.length>core.LIMITS.historyBytes)fail('HISTORY_LIMIT');
      const digest=await core.sha256(bytes),transferId=crypto.randomUUID();
      await content(s,'identity');
      const c=captured.coverage;
      await request(s,'history.begin',{transferId,byteLength:bytes.length,sha256:digest,pairCount:captured.document.pairs.length,
        coverage:{complete:c.complete,hasEarlier:c.hasEarlier,hasLater:c.hasLater,signal:c.signal,firstMessageId:c.firstId,lastMessageId:c.lastId}});
      for(let offset=0,index=0;offset<bytes.length;offset+=core.LIMITS.chunk,index++){
        await content(s,'identity');
        await request(s,'history.chunk',{transferId,index,dataBase64:core.base64(bytes.slice(offset,offset+core.LIMITS.chunk))});
      }
      await content(s,'identity');await request(s,'history.end',{transferId});bytes.fill(0);
      return {status:'captured',pairs:captured.document.pairs.length};
    }
    async function stage() {
      const s=await admit();if(s.offer || s.applied)fail('REVIEW_CONSUMED');
      const response=await request(s,'draft.poll');if(response.status==='waiting')return {status:'waiting-for-review'};
      const offer=core.frozen(JSON.parse(JSON.stringify(response.payload)));s.offer=offer; // Host approval is consumed now.
      try{
        await content(s,'prepare',offer);
        for(const item of offer.contents){
          const count=Math.max(1,Math.ceil(item.byteLength/core.LIMITS.chunk));
          for(let index=0;index<count;index++){
            await content(s,'identity');
            const reply=await request(s,'draft.chunk.read',{reviewId:offer.reviewId,contentId:item.contentId,index});
            const chunk=reply.payload;
            if(chunk.reviewId!==offer.reviewId || chunk.contentId!==item.contentId || chunk.index!==index || chunk.last!==(index===count-1))fail('NATIVE_REPLY_INVALID');
            await content(s,'chunk',chunk);
          }
        }
        await content(s,'identity');
        await request(s,'draft.state',{reviewId:offer.reviewId,digest:offer.digest,draftSha256:offer.contents.find(c=>c.contentId==='draft').sha256,
          composerId:offer.composerId,state:'pending',attachments:[]});
        const outcome=await content(s,'commit');
        const draft=offer.contents.find(c=>c.contentId==='draft');
        const attachments=outcome.status==='staged'?offer.contents.filter(c=>c.contentId!=='draft').map(c=>{
          const receipt=outcome.receipt.confirmed.find(r=>r.contentId===c.contentId);
          return {contentId:c.contentId,name:c.name,mimeType:c.mimeType,byteLength:c.byteLength,sha256:c.sha256,state:'ready',providerAttachmentId:receipt.providerAttachmentId};
        }):[];
        const state={reviewId:offer.reviewId,digest:offer.digest,draftSha256:draft.sha256,composerId:offer.composerId,
          state:outcome.status==='staged'?'staged':'partial-or-unknown',attachments};
        const acknowledged=await request(s,'draft.state',state);
        if(outcome.status==='staged'){
          const receipt=acknowledged.payload;
          if(receipt?.reviewId!==offer.reviewId || receipt.digest!==offer.digest || receipt.state!=='staged' || !/^[a-f0-9]{64}$/.test(receipt.receiptDigest))fail('NATIVE_REPLY_INVALID');
          s.applied={offer,attachments,receipt:core.frozen(JSON.parse(JSON.stringify(receipt)))};
        }
        return {status:outcome.status,reason:outcome.reason??null,sent:false};
      }catch(error){
        // After poll, failure is conservatively uncertain. Never claim that an
        // input/files event was undone because a later receipt could not arrive.
        try{await request(s,'draft.state',{reviewId:offer.reviewId,digest:offer.digest,draftSha256:offer.contents.find(c=>c.contentId==='draft').sha256,composerId:offer.composerId,state:'partial-or-unknown',attachments:[]});}catch{}
        close(s);return {status:'partial-or-unknown',reason:core.safeReason(error),sent:false};
      }
    }
    async function undo() {
      const s=await admit();if(!s.applied)fail('UNDO_UNAVAILABLE');
      const response=await request(s,'undo.poll');if(response.status==='waiting')return {status:'waiting-for-undo-review'};
      const offer=core.frozen(JSON.parse(JSON.stringify(response.payload)));
      try{
        if(offer.reviewId!==s.applied.offer.reviewId || offer.receiptDigest!==s.applied.receipt.receiptDigest || offer.composerId!==s.applied.offer.composerId || offer.originalDraft.contentId!=='undo-original' ||
          offer.originalDraft.sha256!==s.applied.offer.originalDraftSha256 ||
          offer.currentDraftSha256!==s.applied.offer.contents.find(c=>c.contentId==='draft').sha256 ||
          JSON.stringify(offer.attachments)!==JSON.stringify(s.applied.attachments))fail('UNDO_UNAVAILABLE');
        const assembly=new core.ChunkAssembly(offer.originalDraft.byteLength,offer.originalDraft.sha256,core.LIMITS.draftBytes);
        const count=Math.max(1,Math.ceil(offer.originalDraft.byteLength/core.LIMITS.chunk));
        for(let index=0;index<count;index++){
          await content(s,'identity');
          const reply=await request(s,'draft.chunk.read',{reviewId:offer.reviewId,contentId:'undo-original',index});
          if(reply.payload.reviewId!==offer.reviewId || reply.payload.contentId!=='undo-original' || reply.payload.index!==index || reply.payload.last!==(index===count-1))fail('NATIVE_REPLY_INVALID');
          assembly.add(index,reply.payload.dataBase64);
        }
        const bytes=await assembly.finish(()=>{if(current!==s || s.closed)fail('SESSION_INVALIDATED');});bytes.fill(0);
        await content(s,'undo',{...offer,verifiedOriginalSha256:offer.originalDraft.sha256});
        await request(s,'undo.state',{reviewId:offer.reviewId,receiptDigest:offer.receiptDigest,draftSha256:offer.originalDraft.sha256,composerId:offer.composerId,attachments:offer.attachments});
        s.applied=null;return {status:'undone',sent:false};
      }catch(error){close(s);return {status:'partial-or-unknown',reason:core.safeReason(error),sent:false};}
    }
    async function run(op) {
      if(op==='disconnect'){close();return {status:'disconnected'};}
      if(busy)fail('REQUEST_REFUSED');busy=true;
      try { if(op==='readiness')return await characterize();if(op==='pair')return await pair();if(op==='capture')return await capture();if(op==='stage')return await stage();if(op==='undo')return await undo();fail('REQUEST_REFUSED'); }
      finally{busy=false;}
    }
    const changed=(tabId,info)=>{if(current?.tabId===tabId && (info.status==='loading' || info.url))close();};
    chrome.tabs.onUpdated.addListener(changed);
    chrome.tabs.onRemoved.addListener(tabId=>{if(current?.tabId===tabId)close();});
    chrome.tabs.onActivated.addListener(info=>{if(current && info.tabId!==current.tabId)close();});
    chrome.windows.onFocusChanged.addListener(id=>{
      // Idle review may move to Buddy. A pending operation loses authority on
      // any focus loss; another browser window always invalidates the session.
      if(current && (id>=0 && id!==current.windowId || id<0 && busy))close();
    });
    return Object.freeze({run,disconnect:()=>close(),state:publicState});
  }
  root.BuddyBrowserBridge=Object.freeze({createBridge,HOST,SCRIPTS:Object.freeze(SCRIPTS)});
  if(typeof module!=='undefined')module.exports=root.BuddyBrowserBridge;
})(globalThis);
