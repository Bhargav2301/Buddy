'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const core = require('../core.js');
const { fixture } = require('./dom-fixture.cjs');
const binding = () => ({ providerId: 'chatgpt', tabId: 1, documentId: 'document-one', generation: 'generation-one', accountId: 'account-one', workspaceId: 'workspace-one', conversationId: 'chat-one' });
const refused = (fn, code) => assert.throws(fn, e => e.code === code);
test('binding snapshot, every identity, expiry, backward time and invalidation fail closed', () => {
  let now = 0; const original = binding(), guard = new core.SessionGuard(original, () => now);
  original.accountId = 'changed'; assert.equal(guard.binding.accountId, 'account-one');
  for (const key of Object.keys(binding())) { const g = new core.SessionGuard(binding(), () => now); const changed = binding(); changed[key] = key === 'tabId' ? 2 : 'other'; assert.throws(() => g.check(changed)); }
  now = 1; guard.check(binding()); now = 0; refused(() => guard.check(binding()), 'SESSION_EXPIRED');
  const g = new core.SessionGuard(binding(), () => now); now = 120000; refused(() => g.check(binding()), 'SESSION_EXPIRED');
});
test('bounded frames count encoded bytes; Unicode/literal escapes preserved', () => {
  assert.equal(JSON.parse(core.wire({ text: 'Zoë 李明 🌙 \\u674E' })).text, 'Zoë 李明 🌙 \\u674E');
  refused(() => core.wire({ text: '🌙'.repeat(17000) }), 'FRAME_LIMIT');
  refused(() => core.strictText('\ud800'), 'TEXT_INVALID');
});
test('chunk transfer requires exact order, lengths/hash and one use', async () => {
  const bytes = new Uint8Array(32770).fill(42), digest = await core.sha256(bytes);
  const assembly = new core.ChunkAssembly(bytes.length, digest);
  assembly.add(0, core.base64(bytes.slice(0,32768))); assembly.add(1, core.base64(bytes.slice(32768)));
  assert.deepEqual(await assembly.finish(), bytes); await assert.rejects(assembly.finish());
  for (const index of [1, -1]) { const a = new core.ChunkAssembly(bytes.length, digest); refused(() => a.add(index, core.base64(bytes.slice(0,32768))), 'CHUNK_ORDER'); }
  const short = new core.ChunkAssembly(bytes.length,digest); refused(() => short.add(0,core.base64(bytes.slice(0,10))), 'CHUNK_LENGTH');
  const wrong = new core.ChunkAssembly(1,'0'.repeat(64)); wrong.add(0,'Kg=='); await assert.rejects(wrong.finish(), e=>e.code==='TRANSFER_HASH');
  refused(()=>core.fromBase64('Kh=='),'CHUNK_INVALID');
});
test('same DOM driver captures whole user/assistant pairs and rejects partial/foreign/duplicate/reordered coverage', () => {
  const f=fixture(), expected=f.driver.observe();
  const capture=f.driver.captureTurns(expected); assert.equal(capture.turns.length,2); assert.equal(capture.allConversationHistory,false);
  f.assistant.attrs.complete='false'; refused(()=>f.driver.captureTurns(expected),'HISTORY_INCOMPLETE');
  f.assistant.attrs.complete='true'; f.assistant.attrs.id='u1'; refused(()=>f.driver.captureTurns(expected),'HISTORY_INCOMPLETE');
  f.assistant.attrs.id='a1'; f.coverage.attrs.last='other'; refused(()=>f.driver.captureTurns(expected),'HISTORY_INCOMPLETE');
  f.coverage.attrs.last='a1'; f.identity.attrs.conversation='different'; refused(()=>f.driver.captureTurns(expected),'DESTINATION_CHANGED');
});
test('same DOM driver stages exact draft and supports one-use text-only Undo without Send', async () => {
  const f=fixture(), expected=f.driver.observe(), draft='Reviewed exact draft 🌙';
  const review={reviewId:'review-one',originalDraftSha256:await core.textHash(f.editor.innerText),draftSha256:await core.textHash(draft),assets:[]};
  const stage=new core.ReviewedStage(f.driver,expected,()=>{},review);
  const result=await stage.stage(draft,[]); assert.equal(result.status,'staged'); assert.equal(f.editor.innerText,draft); assert.equal(result.receipt.sent,false);
  assert.equal((await stage.undo('review-one')).status,'undone'); assert.equal(f.editor.innerText,'Original draft');
  await assert.rejects(stage.undo('review-one')); await assert.rejects(stage.stage(draft,[]));
  assert.deepEqual(f.editor.events,['input','input']); assert.deepEqual(f.input.events,[]);
});
test('composer replacement and reentrant interruption cannot report clean no-effect success',async()=>{
  const f=fixture(),expected=f.driver.observe(); let stopped=false;
  f.editor.handlers.input=()=>{stopped=true;};
  const draft='Reviewed';const grant={reviewId:'review-two',originalDraftSha256:await core.textHash(f.editor.innerText),draftSha256:await core.textHash(draft),assets:[]};
  const stage=new core.ReviewedStage(f.driver,expected,()=>{if(stopped){const e=new Error();e.code='SESSION_INVALIDATED';throw e;}},grant);
  const result=await stage.stage(draft,[]);
  assert.equal(result.status,'partial-or-unknown');assert.equal(result.effectsMayHaveOccurred,true);assert.equal(result.sent,false);
  const other=fixture(),obs=other.driver.observe(); other.editor.remove(); other.add('div',{id:'prompt-textarea',contenteditable:'true'},'Original draft',other.form);
  refused(()=>other.driver.stageDraft(obs,'Original draft','new','id'),'DESTINATION_CHANGED');
});
test('original bytes use real File plus DataTransfer setter; pending filename is not confirmation',async()=>{
  const f=fixture(),expected=f.driver.observe(),bytes=new Uint8Array([1,2,3]);
  const asset={contentId:'asset-one',name:'image.png',mediaType:'image/png',size:3,sha256:await core.sha256(bytes),bytes};
  const assigned=await f.driver.stageAssets(expected,[asset],()=>{});assert.equal(assigned.confirmed,false);
  assert.deepEqual(new Uint8Array(await f.input.files[0].arrayBuffer()),bytes);assert.deepEqual(f.input.events,['input','change']);
  f.receipts.push({id:'remote-one',name:asset.name,size:3,sha256:asset.sha256,state:'pending',visible:true});
  refused(()=>f.driver.confirmAssets(expected,[asset],[]),'ATTACHMENT_UNCONFIRMED');
  f.receipts[0].state='complete';assert.equal(f.driver.confirmAssets(expected,[asset],[])[0].providerAttachmentId,'remote-one');
  refused(()=>f.driver.confirmAssets(expected,[asset],['remote-one']),'ATTACHMENT_UNCONFIRMED');
  f.receipts[0].sha256='0'.repeat(64);refused(()=>f.driver.confirmAssets(expected,[asset],[]),'ATTACHMENT_UNCONFIRMED');
});
test('changed draft refuses Undo and unchanged attachment set is required',async()=>{
  const f=fixture(),expected=f.driver.observe(),draft='Reviewed';
  const stage=new core.ReviewedStage(f.driver,expected,()=>{},{reviewId:'review-one',originalDraftSha256:await core.textHash(f.editor.innerText),draftSha256:await core.textHash(draft),assets:[]});
  await stage.stage(draft,[]);
  f.editor.textContent='User changed this';await assert.rejects(stage.undo('review-one'),e=>e.code==='DRAFT_CHANGED');assert.equal(f.editor.innerText,'User changed this');
});
test('review assets are immutable; actual size and manifest checked before clone or DOM effects',async()=>{
  const f=fixture(),expected=f.driver.observe(),draft='Reviewed',bytes=new Uint8Array([1,2,3]);
  const asset={contentId:'asset-0',name:'original.png',mediaType:'image/png',size:3,sha256:await core.sha256(bytes)};
  const grant={reviewId:'asset-review',originalDraftSha256:await core.textHash(f.editor.innerText),draftSha256:await core.textHash(draft),assets:[asset]};
  const stage=new core.ReviewedStage(f.driver,expected,()=>{},grant);asset.name='changed.png';
  const result=await stage.stage(draft,[{...asset,bytes}]);assert.equal(result.status,'refused');assert.equal(f.editor.writes,0);assert.equal(f.input.writes,0);
  const malformed={...asset,size:1};let copied=false;const large=new Uint8Array(20);large.slice=()=>{copied=true;return large;};
  const second=new core.ReviewedStage(f.driver,expected,()=>{},{...grant,assets:[malformed]});
  assert.equal((await second.stage(draft,[{...malformed,bytes:large}])).status,'refused');assert.equal(copied,false);
  assert.equal(core.safeReason({code:'PRIVATE_ACCOUNT_SECRET'}),'BROWSER_OPERATION_FAILED');
});
test('invalid current identity permanently invalidates a session even if later restored',()=>{
  const guard=new core.SessionGuard(binding(),()=>0),missing=binding();delete missing.accountId;
  refused(()=>guard.check(missing),'DESTINATION_CHANGED');refused(()=>guard.check(binding()),'DESTINATION_CHANGED');
});
test('empty transfer needs exactly one explicit empty frame; repeated empty frame poisons it',async()=>{
  const digest=await core.sha256(new Uint8Array()),a=new core.ChunkAssembly(0,digest);
  await assert.rejects(a.finish(),e=>e.code==='TRANSFER_INCOMPLETE');a.add(0,'');assert.equal((await a.finish()).length,0);
  const b=new core.ChunkAssembly(0,digest);b.add(0,'');refused(()=>b.add(1,''),'CHUNK_LENGTH');await assert.rejects(b.finish());
});
test('adding an attachment after a text-only stage blocks text Undo',async()=>{
  const f=fixture(),expected=f.driver.observe(),draft='Reviewed';
  const stage=new core.ReviewedStage(f.driver,expected,()=>{},{reviewId:'review-one',originalDraftSha256:await core.textHash(f.editor.innerText),draftSha256:await core.textHash(draft),assets:[]});
  assert.equal((await stage.stage(draft,[])).status,'staged');
  f.receipts.push({id:'later',name:'extra.png',size:1,sha256:'a'.repeat(64),state:'complete',visible:true});
  await assert.rejects(stage.undo('review-one'),e=>e.code==='ATTACHMENT_STATE_CHANGED');assert.equal(f.editor.innerText,draft);
});
