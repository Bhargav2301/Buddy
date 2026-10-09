'use strict';
// Root-only run: actual test executable over anonymous stdin/stdout. No browser,
// extension, named pipe, network, account, model or installed state.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),{spawn}=require('node:child_process');
const options={};for(let i=2;i<process.argv.length;i+=2){const k=process.argv[i],v=process.argv[i+1];assert.ok(['--source-root','--fixture-dll','--fixture-sha256','--mode'].includes(k)&&v&&!options[k]);options[k]=v;}
const source=options['--source-root'],dll=options['--fixture-dll'],mode=options['--mode'];
assert.ok(source&&path.isAbsolute(source)&&dll&&path.isAbsolute(dll));assert.equal(path.basename(dll),'Buddy.BrowserContext.Tests.dll');
assert.ok(['normal','empty-original'].includes(mode));assert.match(options['--fixture-sha256']??'',/^[a-f0-9]{64}$/);
const sha=bytes=>crypto.createHash('sha256').update(bytes).digest('hex'),fileHash=p=>sha(fs.readFileSync(p));
assert.equal(fileHash(dll),options['--fixture-sha256']);
const sourceNames=['core.js','dom-driver.js','content-controller.js','bridge.js'];
const hashes=Object.fromEntries(sourceNames.map(n=>[n,fileHash(path.join(source,n))]));
const outputDir=path.dirname(dll),outputs=Object.fromEntries(fs.readdirSync(outputDir).filter(n=>/\.(dll|json)$/.test(n)).sort().map(n=>[n,fileHash(path.join(outputDir,n))]));
process.env.BUDDY_BROWSER_SOURCE_ROOT=source;
const core=require(path.join(source,'core.js')),{fixture}=require('./wire-synthetic-dom.cjs'),{createController}=require(path.join(source,'content-controller.js')),{createBridge}=require(path.join(source,'bridge.js'));
const event=()=>{const f=[];return {addListener:x=>f.push(x),emit:(...args)=>f.forEach(x=>x(...args))};};
const original=mode==='empty-original'?'':'Owned original Ω',replacement='Reviewed prompt Ω\r\nKeep 4 numbered checks.',assetBytes=new TextEncoder().encode('Original\r\nΩ🙂\0bytes');
const f=fixture();f.state.conversationId='qa-conversation';f.editor.value=original;
f.turns[0].body.value='Keep 4 numbered checks.';f.turns[1].body.value='Do not publish without review.';
const observed={editorCandidates:1,renderedUserCount:1,renderedAssistantCount:1,accountIdentity:'verified',capabilities:{history:true,confirmedAttachments:true}};
const adapter={...f.driver,characterize:()=>observed},controller=createController(adapter,f.location,'qa-generation');
// This fixture receipt is synthetic and explicitly correlates the staged File's
// original bytes to the expected hash; it does not claim server upload retention.
f.input.onEvent=e=>{if(e.type==='change')f.state.attachments=[{id:'qa-original-receipt',name:'owned.txt',size:assetBytes.length,sha256:sha(assetBytes),state:'complete',visible:true}];};
const onMessage=event(),onDisconnect=event(),frames=[];
let child,bridge,closed=false,buffer=Buffer.alloc(0),stderr='',parseError=null,exitResult=null;
let resolveExit;const settled=new Promise(r=>resolveExit=r);
const port={onMessage,onDisconnect,disconnect(){if(closed)return;closed=true;child?.stdin.end();onDisconnect.emit();},postMessage(message){
 const data=Buffer.from(core.wire(message));assert.ok(data.length>0&&data.length<=65536);const header=Buffer.alloc(4);header.writeUInt32LE(data.length);
 frames.push({direction:'to-broker',kind:message.kind,bytes:data.length,sha256:sha(data)});child.stdin.write(Buffer.concat([header,data]));
}};
const tab={id:41,windowId:12,url:f.location.href,active:true};
const chrome={runtime:{connectNative:n=>{assert.equal(n,'com.buddy.browser_context');return port;}},
 windows:{getLastFocused:async()=>({id:12,focused:true}),onFocusChanged:event()},
 tabs:{query:async q=>{assert.equal(q.windowId,12);return [tab];},get:async()=>tab,onUpdated:event(),onRemoved:event(),onActivated:event(),
  sendMessage:async(id,message,target)=>{assert.equal(id,41);assert.equal(target.documentId,'doc-qa');
   if(message.op==='invalidate'){controller.invalidate();return {ok:true};}
   try{return {ok:true,value:await controller[message.op](message.payload)};}catch(error){return {ok:false,code:core.safeReason(error)};}}},
 scripting:{executeScript:async request=>{assert.deepEqual(request.target,{tabId:41,frameIds:[0]});return [{frameId:0,documentId:'doc-qa',result:{generation:'qa-generation',observation:observed}}];}}};

(async()=>{
 let result=null,failure=null;
 try{
  const dotnet=path.join(process.env.ProgramFiles??'C:\\Program Files','dotnet','dotnet.exe');
  assert.ok(fs.existsSync(dotnet));
  child=spawn(dotnet,['--roll-forward','Major',dll,mode==='normal'?'--wire-fixture':'--wire-fixture-empty-original'],{shell:false,windowsHide:true,stdio:['pipe','pipe','pipe']});
  child.on('error',error=>{parseError=error;port.disconnect();if(!child.pid){exitResult={code:null,signal:null,startFailed:true};resolveExit();}});
  child.on('exit',(code,signal)=>{exitResult={code,signal};port.disconnect();resolveExit();});
  child.stdin.on('error',error=>{parseError??=error;port.disconnect();});
  child.stderr.on('data',data=>{stderr=(stderr+data.toString()).slice(0,16384);});
  child.stdout.on('data',data=>{
   try{buffer=Buffer.concat([buffer,data]);if(buffer.length>262144)throw new Error('Bounded fixture output exceeded');
    while(buffer.length>=4){const size=buffer.readUInt32LE(0);assert.ok(size>0&&size<=65536);if(buffer.length<size+4)break;
     const bytes=buffer.subarray(4,size+4),reply=JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(bytes));buffer=buffer.subarray(size+4);
     frames.push({direction:'from-broker',kind:reply.kind,status:reply.status,code:reply.code??null,bytes:size,sha256:sha(bytes)});onMessage.emit(reply);
    }
   }catch(error){parseError=error;port.disconnect();}
  });
  bridge=createBridge(chrome);await bridge.run('pair');const captured=await bridge.run('capture');assert.equal(captured.pairs,1);
  const staged=await bridge.run('stage');assert.equal(staged.status,'staged');assert.equal(staged.sent,false);assert.equal(f.editor.innerText,replacement);
  assert.equal(f.input.files.length,1);assert.deepEqual(new Uint8Array(await f.input.files[0].arrayBuffer()),assetBytes);
  assert.equal(f.input.files[0].name,'owned.txt');assert.equal(f.input.files[0].type,'text/plain');
  const undone=await bridge.run('undo');assert.equal(undone.status,'undone');assert.equal(f.editor.innerText,original);assert.equal(f.input.files.length,1);
  await assert.rejects(bridge.run('undo'));await assert.rejects(bridge.run('stage'));
  assert.equal(f.effects.send,0);assert.equal(frames.some(x=>/^(send|submit)$/i.test(x.kind)),false);
  const originalChunks=frames.filter(x=>x.direction==='to-broker'&&x.kind==='draft.chunk.read');assert.equal(originalChunks.length,3);
  result={capturedPairs:1,staged:true,exactOriginalBytes:true,undone:true,retainedOriginalAttachment:true,oneUseRefused:true,noSend:true,emptyOriginal:mode==='empty-original'};
 }catch(error){failure=error;}
 finally{
  bridge?.disconnect();port.disconnect();
  if(child){let notice=setTimeout(()=>process.stderr.write('Fixture settlement is still pending; no retry or success claim.\n'),20000);await settled;clearTimeout(notice);}
 }
 if(parseError)failure??=parseError;
 try{assert.equal(exitResult?.code,0);assert.equal(buffer.length,0);assert.equal(stderr,'');
  assert.deepEqual(Object.fromEntries(sourceNames.map(n=>[n,fileHash(path.join(source,n))])),hashes);
  assert.deepEqual(Object.fromEntries(Object.keys(outputs).map(n=>[n,fileHash(path.join(outputDir,n))])),outputs);
 }catch(error){failure??=error;}
 console.log(JSON.stringify({kind:'browser61-real-wire',mode,passed:!failure,result,childSettled:!!exitResult,exitResult,source,hashes,fixtureDll:dll,fixtureOutputs:outputs,frames,
  failure:failure?{name:failure.name,message:failure.message}:null,scope:'Actual JS bridge/controller/DOM driver and C# broker over test-process stdio; synthetic DOM/identity/attachment receipt; no browser/account/network/upload/Send.'}));
 process.exitCode=failure?1:0;
})().catch(error=>{console.error('Wire fixture runner failed after settlement: '+error.name);process.exitCode=1;});
