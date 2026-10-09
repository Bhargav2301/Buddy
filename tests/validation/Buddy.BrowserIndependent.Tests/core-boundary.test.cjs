'use strict';
const assert = require('node:assert/strict');
const test = require('node:test');
const path = require('node:path');
const fs = require('node:fs');
const crypto = require('node:crypto');
const sourceRoot = process.env.BUDDY_BROWSER_SOURCE_ROOT;
if (!sourceRoot || !path.isAbsolute(sourceRoot)) throw new Error('Explicit absolute source root required.');
const source = path.join(sourceRoot, 'core.js');
const digest = value => crypto.createHash('sha256').update(value).digest('hex');
const beforeHash = digest(fs.readFileSync(source));
const core = require(source);
const binding = () => ({ providerId: 'chatgpt', tabId: 61, documentId: 'doc-one', generation: 'generation-one',
  accountId: 'account-one', workspaceId: 'workspace-one', conversationId: 'chat-one' });
const codeError = code => Object.assign(new Error('SYNTHETIC PRIVATE DETAILS MUST NOT ESCAPE'), { code });
function deferred() { let resolve; const promise = new Promise(r => { resolve = r; }); return { promise, resolve }; }
function fixture(withAsset = false) {
  let value = '', stopped = false;
  const effects = { draft: 0, assets: 0, undo: 0, send: 0 };
  const expected = { identity: binding(), composerId: 'composer-one', capabilities: { draft: true, originalAssets: true, confirmedAttachments: true } };
  const bytes = new TextEncoder().encode('Exact original Ω\r\nnot OCR');
  const asset = { contentId: 'asset-0', name: 'owned.txt', mediaType: 'text/plain', size: bytes.length, sha256: digest(bytes), bytes };
  const grant = { reviewId: 'review-one', originalDraftSha256: digest(''), draftSha256: digest('Reviewed draft.'),
    assets: withAsset ? [Object.fromEntries(Object.entries(asset).filter(([k]) => k !== 'bytes'))] : [] };
  const f = { effects, expected, asset, grant, value: () => value, edit: text => { value = text; }, stop: () => { stopped = true; } };
  const driver = {
    guard: () => {}, readDraft: () => { f.onRead?.(); return value; }, readAttachments: () => [],
    stageDraft: async (_, original, next) => { assert.equal(value, original); effects.draft++; value = next; await f.stageGate?.promise; },
    stageAssets: async (_, assets, check) => { check(); effects.assets++; f.observedAsset = assets[0].bytes.slice(); return { previousIds: [] }; },
    awaitAttachments: async (_, assets) => assets.map(a => ({ contentId: a.contentId, providerAttachmentId: 'receipt-one', sha256: a.sha256 })),
    undoDraft: async () => { effects.undo++; value = ''; return { exact: true }; }
  };
  f.driver = driver; f.make = () => new core.ReviewedStage(driver, expected, () => { if (stopped) throw codeError('SESSION_INVALIDATED'); }, grant);
  return f;
}

test('chunk assembly preserves exact original across full chunk boundary', async () => {
  const bytes = Uint8Array.from({ length: core.LIMITS.chunk + 11 }, (_, i) => i % 251);
  const transfer = new core.ChunkAssembly(bytes.length, digest(bytes));
  transfer.add(0, core.base64(bytes.slice(0, core.LIMITS.chunk))); transfer.add(1, core.base64(bytes.slice(core.LIMITS.chunk)));
  assert.deepEqual(await transfer.finish(), bytes); await assert.rejects(() => transfer.finish());
});
test('empty original consumes exactly one empty chunk before finish', async () => {
  const transfer = new core.ChunkAssembly(0, digest(''));
  await assert.rejects(() => transfer.finish()); transfer.add(0, ''); assert.equal((await transfer.finish()).length, 0);
  assert.throws(() => transfer.add(0, ''));
});
test('out-of-order and duplicated chunks cannot be repaired into replay success', async () => {
  for (const replay of [false, true]) {
    const transfer = new core.ChunkAssembly(3, digest('abc'));
    if (replay) transfer.add(0, core.base64(new TextEncoder().encode('abc')));
    assert.throws(() => transfer.add(replay ? 0 : 1, 'YWJj'));
    await assert.rejects(() => transfer.finish());
  }
});
test('final digest mismatch and declared byte length refuse', async () => {
  const transfer = new core.ChunkAssembly(3, digest('abc')); transfer.add(0, 'YWJk'); await assert.rejects(() => transfer.finish());
  assert.throws(() => new core.ChunkAssembly(core.LIMITS.assetBytes + 1, digest('')));
  assert.throws(() => new core.ChunkAssembly(-1, digest('')));
});
test('base64 must be canonical and bounded', () => {
  for (const value of ['YQ==\n', 'YQ', 'YR==', '====', 'A'.repeat(50000)]) assert.throws(() => core.fromBase64(value));
});
test('post-hash cancellation prevents released bytes', async () => {
  const transfer = new core.ChunkAssembly(3, digest('abc')); transfer.add(0, 'YWJj'); let checks = 0;
  await assert.rejects(() => transfer.finish(() => { if (++checks === 2) throw codeError('SESSION_INVALIDATED'); }));
  assert.equal(checks, 2); await assert.rejects(() => transfer.finish());
});
test('text bounds and malformed UTF16 refuse but real emoji is preserved', () => {
  assert.equal(core.strictText('Ω🙂\\u0041'), 'Ω🙂\\u0041');
  for (const value of ['\ud800', '\udc00', 'x'.repeat(20001)]) assert.throws(() => core.strictText(value));
  assert.throws(() => core.strictText('€', 10, 2));
});
test('unknown error codes never echo private uppercase data', () => {
  assert.equal(core.safeReason(codeError('SYNTHETIC_PRIVATE_TOKEN_ABC')), 'BROWSER_OPERATION_FAILED');
  assert.equal(core.safeReason(codeError('DRAFT_CHANGED')), 'DRAFT_CHANGED');
});
test('tab identity rejects unsafe numbers and missing identities', () => {
  for (const tabId of [Number.MAX_SAFE_INTEGER, NaN, -1, 0.5]) assert.throws(() => core.binding({ ...binding(), tabId }));
  assert.throws(() => core.binding({ ...binding(), accountId: '' }));
});
for (const key of ['tabId', 'documentId', 'generation', 'accountId', 'workspaceId', 'conversationId']) test(`session ${key} replacement permanently invalidates`, () => {
  const original = binding(), guard = new core.SessionGuard(original, () => 1000);
  assert.throws(() => guard.check({ ...original, [key]: key === 'tabId' ? 62 : 'replacement' }));
  assert.throws(() => guard.check(original));
});
test('malformed current identity permanently invalidates rather than recovering', () => {
  const original = binding(), guard = new core.SessionGuard(original, () => 1000);
  assert.throws(() => guard.check({ ...original, accountId: '' })); assert.throws(() => guard.check(original));
});
test('expiry and monotonic reversal never reset authority', () => {
  for (const end of [121000, 999]) {
    let now = 1000; const guard = new core.SessionGuard(binding(), () => now);
    now = end; assert.throws(() => guard.check(binding())); now = 1000; assert.throws(() => guard.check(binding()));
  }
});
test('review grant is immutable; exact draft and originals stage once without Send', async () => {
  const f = fixture(true), stage = f.make(); f.grant.reviewId = 'forged-later-review';
  const result = await stage.stage('Reviewed draft.', [f.asset]);
  assert.equal(result.status, 'staged'); assert.equal(result.receipt.reviewId, 'review-one');
  assert.deepEqual(f.observedAsset, f.asset.bytes); assert.equal(f.effects.send, 0);
  await assert.rejects(() => stage.stage('Reviewed draft.', [f.asset])); assert.equal(f.effects.draft, 1);
});
for (const field of ['contentId', 'name', 'mediaType', 'size', 'sha256']) test(`reviewed asset ${field} cannot be substituted`, async () => {
  const f = fixture(true), stage = f.make(); const changed = { ...f.asset, [field]: field === 'size' ? f.asset.size + 1 : 'forged' };
  const outcome = await stage.stage('Reviewed draft.', [changed]); assert.equal(outcome.status, 'refused');
  assert.equal(f.effects.draft, 0); assert.equal(f.effects.assets, 0);
});
test('forged small length cannot trigger copying a larger original', async () => {
  const f = fixture(true), stage = f.make(); let copied = false;
  const tooBig = new Uint8Array(f.asset.size + 1); tooBig.slice = () => { copied = true; throw new Error('Should not copy'); };
  const outcome = await stage.stage('Reviewed draft.', [{ ...f.asset, bytes: tooBig }]);
  assert.equal(outcome.status, 'refused'); assert.equal(copied, false); assert.equal(f.effects.draft, 0);
});
test('Stop before dispatch and changed draft both refuse without writes', async () => {
  for (const stopped of [true, false]) { const f = fixture(), stage = f.make(); if (stopped) f.stop(); else f.edit('Later user text');
    assert.equal((await stage.stage('Reviewed draft.', [])).status, 'refused'); assert.equal(f.effects.draft, 0); }
});
test('concurrent commit claims one owner; Stop during pending write reports uncertainty', async () => {
  const f = fixture(); f.stageGate = deferred(); const stage = f.make(), running = stage.stage('Reviewed draft.', []);
  await assert.rejects(() => stage.stage('Reviewed draft.', []));
  while (!f.effects.draft) await new Promise(resolve => setImmediate(resolve));
  f.stop(); f.stageGate.resolve(); const result = await running;
  assert.equal(result.status, 'partial-or-unknown'); assert.equal(result.effectsMayHaveOccurred, true);
  assert.equal(f.effects.draft, 1); assert.equal(f.effects.send, 0); await assert.rejects(() => stage.undo('review-one'));
});
test('post-stage user edit prevents Undo without overwriting the new draft', async () => {
  const f = fixture(), stage = f.make(); assert.equal((await stage.stage('Reviewed draft.', [])).status, 'staged');
  f.edit('Newer user input'); await assert.rejects(() => stage.undo('review-one')); assert.equal(f.value(), 'Newer user input'); assert.equal(f.effects.undo, 0);
});
test('external core source remained unchanged during the independent run', () => {
  assert.equal(digest(fs.readFileSync(source)), beforeHash);
  console.log(JSON.stringify({ kind: 'source-receipt', path: source, sha256: beforeHash, scope: 'pure injected memory only; no browser/network' }));
});
