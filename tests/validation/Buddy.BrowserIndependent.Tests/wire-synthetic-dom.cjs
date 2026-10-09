'use strict';

const assert = require('node:assert/strict');
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
  const location = { href: 'https://chatgpt.com/c/qa-conversation' };
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
  const profile = Object.freeze({ id: 'qa-v1', evidence: { accountSignal:'qa-account-signal',workspaceSignal:'qa-workspace-signal',conversationSignal:'qa-chat-signal',historySignal:'qa-complete' }, origin: 'https://chatgpt.com',
    identityVerified: true, draftVerified: true, historyVerified: true, attachmentsVerified: true, liveValidated: false,
    editor: 'qa-composer', turns: 'qa-turn', body: 'qa-body', files: 'qa-file',
    readIdentity: () => ({ accountId: state.accountId, workspaceId: state.workspaceId, conversationId: state.conversationId }),
    readCoverage: () => ({ complete: true, hasEarlier: false, hasLater: false, scope: 'rendered-completed-pairs', signal:'qa-complete', conversationId: state.conversationId,
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


module.exports={fixture,sourceHash};
