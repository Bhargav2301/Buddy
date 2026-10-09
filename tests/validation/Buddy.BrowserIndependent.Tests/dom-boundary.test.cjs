'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { File } = require('node:buffer');
const sourceRoot = process.env.BUDDY_BROWSER_SOURCE_ROOT;
if (!sourceRoot || !path.isAbsolute(sourceRoot)) throw new Error('An explicit absolute BUDDY_BROWSER_SOURCE_ROOT is required.');
const source = path.join(sourceRoot, 'dom-driver.js');
const sourceHash = crypto.createHash('sha256').update(fs.readFileSync(source)).digest('hex');
const { createDomDriver } = require(source);
const digest = bytes => crypto.createHash('sha256').update(bytes).digest('hex');

// Owned injected DOM objects, not a browser, account, DOM selector characterization,
// or provider upload. Actual production driver methods operate on these objects.
function fixture() {
  const effects = { textWrites: 0, fileWrites: 0, input: 0, change: 0, send: 0 };
  const state = { accountId: 'qa-account', workspaceId: 'qa-workspace', conversationId: 'qa-chat', attachments: [] };
  const location = { href: 'https://fixture.invalid/c/qa-chat' };
  const document = { querySelectorAll(selector) {
    if (selector === 'qa-composer') return [f.editor];
    if (selector === 'qa-turn') return f.turns;
    return [];
  } };
  class Event { constructor(type, options) { this.type = type; Object.assign(this, options); } }
  class DataTransfer { constructor() { this.files = []; this.items = { add: file => this.files.push(file) }; } }
  document.defaultView = { Event, InputEvent: Event, File, DataTransfer,
    getComputedStyle: () => ({ display: 'block', visibility: 'visible' }) };
  function node(value = '', tagName = 'DIV') {
    return { tagName, isConnected: true, hidden: false, ownerDocument: document, disabled: false, readOnly: false,
      value, getAttribute: () => null, getClientRects: () => [{}],
      get innerText() { return this.value; },
      set innerText(v) { this.value = v; },
      get textContent() { return this.value; },
      set textContent(v) { effects.textWrites++; this.value = v; },
      dispatchEvent(event) {
        if (event.type === 'input') effects.input++;
        if (event.type === 'change') effects.change++;
        this.onEvent?.(event); return true;
      },
      click() { effects.send++; throw new Error('Unexpected click'); },
      submit() { effects.send++; throw new Error('Unexpected submit'); },
      requestSubmit() { effects.send++; throw new Error('Unexpected requestSubmit'); }
    };
  }
  const input = node('', 'INPUT'); input.type = 'file'; input.multiple = true;
  let files = [];
  Object.defineProperty(input, 'files', { get: () => files, set: value => { effects.fileWrites++; files = value; } });
  const form = { querySelectorAll: selector => selector === 'qa-file' ? [input] : [] };
  const f = { state, effects, document, location, input, turns: [], node };
  f.editor = node('Original owned draft.'); f.editor.closest = () => form;
  f.newEditor = () => { const e = node('Original owned draft.'); e.closest = () => form; return e; };
  f.addTurn = (id, role, value, complete = true) => {
    const turn = node(); turn.meta = { id, role, complete }; turn.body = node(value);
    turn.querySelectorAll = selector => selector === 'qa-body' ? [turn.body] : [];
    f.turns.push(turn); return turn;
  };
  f.addTurn('u1', 'user', 'Question with \u00e9 and exact 4 checks.');
  f.addTurn('a1', 'assistant', 'Do not publish if any check fails.');
  const profile = Object.freeze({ id: 'qa-only-admitted-profile', origin: 'https://fixture.invalid',
    identityVerified: true, draftVerified: true, historyVerified: true, attachmentsVerified: true, liveValidated: false,
    editor: 'qa-composer', turns: 'qa-turn', body: 'qa-body', files: 'qa-file',
    readIdentity: () => ({ accountId: state.accountId, workspaceId: state.workspaceId, conversationId: state.conversationId }),
    readCoverage: () => ({ complete: true, hasEarlier: false, hasLater: false, scope: 'rendered-completed-pairs', conversationId: state.conversationId,
      firstId: f.turns[0]?.meta.id ?? '', lastId: f.turns.at(-1)?.meta.id ?? '' }),
    readTurn: turn => ({ ...turn.meta }),
    readAttachments: () => { const entries = state.attachments.map(x => ({ ...x })); f.onReadAttachments?.(); return entries; }
  });
  f.driver = createDomDriver(document, location, profile); f.expected = f.driver.observe();
  f.asset = (text = 'Original\r\n\u03a9\ud83d\ude42\u0000bytes') => {
    const bytes = new TextEncoder().encode(text);
    return { contentId: 'asset-one', name: 'owned.txt', mediaType: 'text/plain', size: bytes.length, sha256: digest(bytes), bytes };
  };
  f.receipt = (asset, overrides = {}) => ({ id: 'new-provider-id', name: asset.name, size: asset.size, sha256: asset.sha256,
    state: 'complete', visible: true, ...overrides });
  return f;
}

test('exact synthetic draft and one-use Undo preserve bytes and never submit', () => {
  const f = fixture(), next = 'Reviewed \u03a9 draft\r\nwith 4 checks.';
  assert.equal(f.driver.stageDraft(f.expected, 'Original owned draft.', next, 'txn-a').exact, true);
  assert.equal(f.editor.innerText, next);
  assert.equal(f.driver.undoDraft(f.expected, 'txn-a').exact, true);
  assert.equal(f.editor.innerText, 'Original owned draft.');
  assert.throws(() => f.driver.undoDraft(f.expected, 'txn-a'));
  assert.equal(f.effects.textWrites, 2); assert.equal(f.effects.send, 0);
});

for (const field of ['accountId', 'workspaceId', 'conversationId']) test(`changed ${field} refuses the old draft destination`, () => {
  const f = fixture(); f.state[field] += '-replacement';
  assert.throws(() => f.driver.stageDraft(f.expected, 'Original owned draft.', 'Wrong destination', 'txn-a'));
  assert.equal(f.effects.textWrites, 0); assert.equal(f.effects.send, 0);
});

test('same-looking replacement composer cannot inherit old authority', () => {
  const f = fixture(); f.editor = f.newEditor();
  assert.throws(() => f.driver.stageDraft(f.expected, 'Original owned draft.', 'Replacement', 'txn-a'));
  assert.equal(f.effects.textWrites, 0);
});

test('changed draft is never overwritten and later user edit defeats Undo', () => {
  const f = fixture(); f.editor.innerText = 'New user input';
  assert.throws(() => f.driver.stageDraft(f.expected, 'Original owned draft.', 'Replacement', 'txn-a'));
  assert.equal(f.effects.textWrites, 0);
  f.driver.stageDraft(f.expected, 'New user input', 'Reviewed replacement', 'txn-b');
  f.editor.innerText = 'Newer user input';
  assert.throws(() => f.driver.undoDraft(f.expected, 'txn-b'));
  assert.equal(f.editor.innerText, 'Newer user input'); assert.equal(f.effects.textWrites, 1);
});

test('provider event replacing composer is not reported as exact delivery or undoable success', () => {
  const f = fixture(), old = f.editor;
  old.onEvent = () => { f.editor = f.newEditor(); f.editor.innerText = 'Provider replacement'; };
  assert.throws(() => f.driver.stageDraft(f.expected, 'Original owned draft.', 'Partial actual write', 'txn-a'));
  assert.equal(old.innerText, 'Partial actual write', 'do not erase evidence that a write occurred');
  assert.equal(f.editor.innerText, 'Provider replacement');
  assert.throws(() => f.driver.undoDraft(f.expected, 'txn-a'));
  assert.equal(f.effects.send, 0);
});

test('complete synthetic pairs preserve exact roles/order and disclaim total history', () => {
  const f = fixture(); const got = f.driver.captureTurns(f.expected);
  assert.equal(got.allConversationHistory, false);
  assert.deepEqual(got.turns.map(x => [x.id, x.role, x.text]), [
    ['u1', 'user', 'Question with \u00e9 and exact 4 checks.'], ['a1', 'assistant', 'Do not publish if any check fails.']
  ]);
  assert.equal(f.effects.send, 0);
});

for (const mutation of ['streaming', 'half', 'duplicate', 'wrong-role']) test(`history ${mutation} cannot be called a completed pair`, () => {
  const f = fixture();
  if (mutation === 'streaming') f.turns[1].meta.complete = false;
  if (mutation === 'half') f.turns.pop();
  if (mutation === 'duplicate') f.turns[1].meta.id = f.turns[0].meta.id;
  if (mutation === 'wrong-role') f.turns[1].meta.role = 'user';
  assert.throws(() => f.driver.captureTurns(f.expected));
  assert.equal(f.effects.textWrites, 0); assert.equal(f.effects.send, 0);
});

test('original bytes become the synthetic File unchanged; staging alone is unconfirmed', async () => {
  const f = fixture(), a = f.asset();
  const staged = await f.driver.stageAssets(f.expected, [a], () => {});
  assert.equal(staged.assigned, true); assert.equal(staged.confirmed, false);
  assert.deepEqual(Buffer.from(await f.input.files[0].arrayBuffer()), Buffer.from(a.bytes));
  assert.equal(f.input.files[0].name, a.name);
  assert.throws(() => f.driver.confirmAssets(f.expected, [a], staged.previousIds));
  f.state.attachments = [f.receipt(a)];
  const confirmed = f.driver.confirmAssets(f.expected, [a], staged.previousIds);
  assert.equal(confirmed[0].sha256, a.sha256); assert.equal(f.effects.send, 0);
});

test('wrong original hash refuses before FileList assignment or input events', async () => {
  const f = fixture(), a = f.asset(); a.bytes[0] ^= 1;
  await assert.rejects(() => f.driver.stageAssets(f.expected, [a], () => {}));
  assert.equal(f.effects.fileWrites, 0); assert.equal(f.effects.input, 0); assert.equal(f.effects.change, 0);
});

test('asset identity is rechecked after the asynchronous hash', async () => {
  const f = fixture(), a = f.asset(); let calls = 0;
  await assert.rejects(() => f.driver.stageAssets(f.expected, [a], () => { if (++calls === 3) f.state.accountId = 'different-account'; }));
  assert.equal(f.effects.fileWrites, 0); assert.equal(f.effects.send, 0);
});

test('Stop at the asset input event prevents change event while retaining truthful partial effect', async () => {
  const f = fixture(), a = f.asset(); let cancelled = false;
  f.input.onEvent = () => { cancelled = true; };
  await assert.rejects(() => f.driver.stageAssets(f.expected, [a], () => { if (cancelled) throw new Error('STOP'); }));
  assert.equal(f.effects.fileWrites, 1); assert.equal(f.effects.input, 1); assert.equal(f.effects.change, 0);
  assert.equal(f.input.files.length, 1); assert.equal(f.effects.send, 0);
});

for (const state of ['pending', 'error', 'removed', 'wrong-hash', 'old-id']) test(`attachment ${state} does not confirm the requested original`, () => {
  const f = fixture(), a = f.asset();
  f.state.attachments = state === 'removed' ? [] : [f.receipt(a, state === 'wrong-hash' ? { sha256: '0'.repeat(64) }
    : state === 'old-id' ? { id: 'previous-id' } : { state })];
  assert.throws(() => f.driver.confirmAssets(f.expected, [a], ['previous-id']));
  assert.equal(f.effects.fileWrites, 0); assert.equal(f.effects.send, 0);
});

test('pre-existing same-name attachment is preserved and never counted as a new upload', async () => {
  const f = fixture(), a = f.asset(); f.state.attachments = [f.receipt(a, { id: 'unrelated-existing' })];
  await assert.rejects(() => f.driver.stageAssets(f.expected, [a], () => {}));
  assert.equal(f.effects.fileWrites, 0); assert.equal(f.state.attachments[0].id, 'unrelated-existing');
});

test('identity loss while reading receipt prevents final confirmation', () => {
  const f = fixture(), a = f.asset(); f.state.attachments = [f.receipt(a)];
  f.onReadAttachments = () => { f.state.conversationId = 'new-chat'; };
  assert.throws(() => f.driver.confirmAssets(f.expected, [a], []));
  assert.equal(f.effects.send, 0);
});

test('exact external DOM source did not change during the independent run', () => {
  assert.equal(digest(fs.readFileSync(source)), sourceHash);
  console.log(JSON.stringify({ kind: 'source-receipt', path: source, sha256: sourceHash,
    scope: 'injected DOM primitives only; no browser, account, upload or Send' }));
});
