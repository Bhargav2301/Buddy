'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');
const path = require('node:path');
const fs = require('node:fs');
const crypto = require('node:crypto');

const sourceRoot = process.env.BUDDY_BROWSER_SOURCE_ROOT;
if (!sourceRoot || !path.isAbsolute(sourceRoot)) throw new Error('An explicit absolute BUDDY_BROWSER_SOURCE_ROOT is required.');
const files = ['dom-driver.js', 'providers/chatgpt.js', 'providers/registry.js'];
const inputs = files.map(file => {
  const full = path.join(sourceRoot, file);
  return { path: full, sha256: crypto.createHash('sha256').update(fs.readFileSync(full)).digest('hex') };
});
require(path.join(sourceRoot, files[0]));
const provider = require(path.join(sourceRoot, files[1]));
const registry = require(path.join(sourceRoot, files[2]));

function fixture() {
  const effects = { input: 0, writes: 0, send: 0, reads: 0, storage: 0 };
  let draft = 'An owned synthetic draft.';
  const location = { href: 'https://chatgpt.com/c/11111111-2222-4333-8444-555555555555' };
  const document = {
    defaultView: {
      getComputedStyle: () => ({ display: 'block', visibility: 'visible' }),
      InputEvent: class { constructor(type, options) { this.type = type; Object.assign(this, options); } }
    },
    querySelectorAll(selector) {
      if (selector === provider.PROFILE.editor) return [editor];
      return [];
    }
  };
  const editor = {
    isConnected: true, hidden: false, ownerDocument: document,
    getAttribute: () => null, getClientRects: () => [{}],
    get innerText() { effects.reads++; return draft; },
    get textContent() { effects.reads++; return draft; },
    set textContent(value) { effects.writes++; draft = value; },
    dispatchEvent(event) { if (event.type === 'input') effects.input++; return true; },
    click() { effects.send++; throw new Error('Send must not be invoked.'); }
  };
  document.cookie = 'SYNTHETIC_NOT_AN_ACCOUNT_ID';
  document.documentElement = { dataset: { accountId: 'forged-account', workspaceId: 'forged-workspace', verified: 'true' } };
  return { effects, document, location, editor, adapter: registry.select(document, location), draft: () => draft };
}

test('declared readiness does not claim live history, draft, assets or attachment confirmation', () => {
  const f = fixture();
  const status = f.adapter.characterize();
  assert.equal(status.liveValidated, false);
  for (const key of ['history', 'draft', 'originalAssets', 'confirmedAttachments']) assert.equal(status.capabilities[key], false);
  assert.equal(f.effects.reads, 0, 'readiness must not read draft content');
  assert.equal(f.effects.writes, 0);
  assert.equal(f.effects.send, 0);
});

test('forged DOM account/workspace/verified metadata cannot enable observation', () => {
  const f = fixture();
  assert.throws(() => f.adapter.observe());
  assert.equal(f.effects.reads, 0);
  assert.equal(f.effects.writes, 0);
});

for (const method of ['readDraft', 'stageDraft', 'captureTurns', 'stageAssets', 'readAttachments']) {
  test(`unsupported production ${method} refuses at its own entrypoint`, async () => {
    const f = fixture();
    let refusal;
    const args = method === 'stageDraft' ? [{}, 'An owned synthetic draft.', 'REPLACEMENT MUST NOT BE WRITTEN', 'review-one']
      : method === 'stageAssets' ? [{}, [], () => {}] : [{}];
    try { await f.adapter[method](...args); }
    catch (error) { refusal = error; }
    assert.ok(refusal instanceof Error, `${method} must not bypass unavailable account identity`);
    assert.equal(refusal.code, 'ACCOUNT_IDENTITY_UNAVAILABLE', 'refusal must establish the identity gate, not an unrelated bad-arguments error');
    assert.equal(f.effects.reads, 0, 'unsupported production operation must not read draft content');
    assert.equal(f.effects.writes, 0, 'unsupported production operation must not mutate');
    assert.equal(f.effects.input, 0);
    assert.equal(f.effects.send, 0);
    assert.equal(f.draft(), 'An owned synthetic draft.');
  });
}

test('a captured adapter cannot write after cross-origin navigation', async () => {
  const f = fixture();
  f.location.href = 'https://unrelated.example/c/11111111-2222-4333-8444-555555555555';
  let refused = false;
  try { await f.adapter.stageDraft({}, 'An owned synthetic draft.', 'WRONG ORIGIN', 'review-two'); }
  catch { refused = true; }
  assert.equal(refused, true);
  assert.equal(f.effects.writes, 0);
  assert.equal(f.effects.input, 0);
  assert.equal(f.effects.send, 0);
});

test('provider registry refuses unsupported origins and lookalikes despite matching editor', () => {
  for (const href of [
    'http://chatgpt.com/c/11111111-2222-4333-8444-555555555555',
    'https://chatgpt.com.unrelated.example/',
    'https://unrelated.example/?provider=chatgpt',
    'https://127.0.0.1/',
    'file:///synthetic/chatgpt.html'
  ]) {
    const f = fixture(); f.location.href = href;
    assert.throws(() => registry.select(f.document, f.location));
    assert.equal(f.effects.reads, 0); assert.equal(f.effects.writes, 0); assert.equal(f.effects.send, 0);
  }
});

test('credential-bearing URLs are not treated as supported provider evidence', () => {
  const f = fixture();
  f.location.href = 'https://synthetic-user:synthetic-secret@chatgpt.com/c/11111111-2222-4333-8444-555555555555';
  assert.throws(() => f.adapter.characterize());
  assert.equal(f.effects.reads, 0); assert.equal(f.effects.writes, 0);
});

test('external source inputs remain unchanged for this run', () => {
  for (const input of inputs) assert.equal(crypto.createHash('sha256').update(fs.readFileSync(input.path)).digest('hex'), input.sha256);
  console.log(JSON.stringify({ kind: 'source-receipt', scope: 'headless synthetic DOM only; no browser/network/account', files: inputs }));
});
