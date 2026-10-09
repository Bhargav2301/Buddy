(async function () {
  'use strict';
  const results = [], effects = { sends: 0, enterKeys: 0, input: 0, change: 0 };
  const digest = async bytes => Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)), b => b.toString(16).padStart(2, '0')).join('');
  const eq = (a, b, message) => { if (a !== b) throw new Error(message || 'Exact comparison failed'); };
  async function refuses(action) { let refused = false; try { await action(); } catch { refused = true; } eq(refused, true, 'Expected bounded refusal'); }
  async function check(name, action) {
    try { await action(); results.push({ name, passed: true }); }
    catch (error) { results.push({ name, passed: false, error: String(error?.message || error).slice(0, 350) }); }
  }
  const dispose = f => f.root.remove();
  function fixture(tag = 'div') {
    const root = document.createElement('section'); root.className = 'fixture';
    const form = document.createElement('form'); root.append(form);
    let editor = document.createElement(tag); editor.className = 'editor';
    if (tag !== 'textarea') editor.contentEditable = 'true';
    const put = (node, value) => { if (node.tagName === 'TEXTAREA') node.value = value; else node.textContent = value; };
    const get = node => node.tagName === 'TEXTAREA' ? node.value : node.innerText;
    put(editor, 'Original owned draft.'); form.append(editor);
    const input = document.createElement('input'); input.type = 'file'; input.multiple = true; form.append(input);
    const send = document.createElement('button'); send.type = 'submit'; send.textContent = 'Synthetic Send'; form.append(send);
    const turns = document.createElement('section'); turns.className = 'turns'; root.append(turns);
    form.addEventListener('submit', event => { effects.sends++; event.preventDefault(); });
    send.addEventListener('click', () => { effects.sends++; });
    form.addEventListener('keydown', event => { if (event.key === 'Enter') effects.enterKeys++; });
    form.addEventListener('input', () => { effects.input++; });
    form.addEventListener('change', () => { effects.change++; });
    document.body.append(root);
    const state = { accountId: 'synthetic-account', workspaceId: 'synthetic-workspace', conversationId: 'synthetic-chat', receipts: [] };
    const profile = Object.freeze({ id: 'qa-native-dom-only', origin: new URL(location.href).origin,
      identityVerified: true, draftVerified: true, historyVerified: true, attachmentsVerified: true, liveValidated: false,
      editor: '.editor', files: 'input[type="file"]', turns: '.turn', body: '.body',
      readIdentity: () => ({ accountId: state.accountId, workspaceId: state.workspaceId, conversationId: state.conversationId }),
      readCoverage: () => { const all = turns.querySelectorAll('.turn'); return { complete: true, hasEarlier: false, hasLater: false, scope: 'rendered-completed-pairs',
        conversationId: state.conversationId, firstId: all[0]?.dataset.id ?? '', lastId: all[all.length - 1]?.dataset.id ?? '' }; },
      readTurn: node => ({ id: node.dataset.id, role: node.dataset.role, complete: node.dataset.complete === 'true' }),
      readAttachments: () => state.receipts.map(x => ({ ...x }))
    });
    // The production driver sees real Element/querySelector/Event/File objects.
    // Only provider-specific identity/completion/receipt readers are synthetic.
    const scope = { querySelectorAll: selector => root.querySelectorAll(selector), defaultView: window };
    const driver = BuddyBrowserDom.createDomDriver(scope, location, profile);
    const f = { root, form, input, state, driver, get: () => get(editor), put: value => put(editor, value), get editor() { return editor; },
      replaceEditor() { const next = editor.cloneNode(false); put(next, get(editor)); editor.replaceWith(next); editor = next; },
      addTurn(id, role, text, complete = true) { const turn = document.createElement('article'); turn.className = 'turn';
        Object.assign(turn.dataset, { id, role, complete: String(complete) });
        const body = document.createElement('div'); body.className = 'body'; body.textContent = text; turn.append(body); turns.append(turn); return turn; }
    };
    f.expected = driver.observe(); return f;
  }
  async function asset(name = 'owned-original.txt', text = 'Original\r\n\u03a9\ud83d\ude42\u0000bytes') {
    const bytes = new TextEncoder().encode(text);
    return { contentId: name, name, mediaType: 'text/plain', bytes, size: bytes.length, sha256: await digest(bytes) };
  }
  const receipt = (a, changes = {}) => ({ id: 'new-attachment-receipt', name: a.name, size: a.size, sha256: a.sha256,
    visible: true, state: 'complete', ...changes });

  await check('actual Chromium DOM APIs are present', async () => {
    eq(typeof BuddyBrowserDom.createDomDriver, 'function');
    eq(typeof File, 'function'); eq(typeof DataTransfer, 'function'); eq(typeof InputEvent, 'function');
    const transfer = new DataTransfer(); transfer.items.add(new File(['owned'], 'probe.txt'));
    eq(transfer.files.length, 1); eq(await transfer.files[0].text(), 'owned');
  });
  for (const tag of ['textarea', 'div']) {
    await check(`${tag}: actual input event, exact write, one-use Undo, no Send`, async () => {
      const f = fixture(tag); try {
        const events = []; f.editor.addEventListener('input', event => events.push({ type: event.inputType, data: event.data, trusted: event.isTrusted }));
        const next = 'Reviewed \u03a9 draft with exactly 4 checks.';
        eq(f.driver.stageDraft(f.expected, 'Original owned draft.', next, 'review-one').exact, true); eq(f.get(), next);
        eq(events.length, 1); eq(events[0].type, 'insertText'); eq(events[0].data, next); eq(events[0].trusted, false);
        eq(f.driver.undoDraft(f.expected, 'review-one').exact, true); eq(f.get(), 'Original owned draft.');
        await refuses(() => f.driver.undoDraft(f.expected, 'review-one'));
      } finally { dispose(f); }
    });
    await check(`${tag}: changed draft refuses overwrite`, async () => {
      const f = fixture(tag); try { f.put('Later user input'); await refuses(() => f.driver.stageDraft(f.expected, 'Original owned draft.', 'Wrong', 'r')); eq(f.get(), 'Later user input'); }
      finally { dispose(f); }
    });
    await check(`${tag}: replacement editor cannot inherit the snapshot`, async () => {
      const f = fixture(tag); try { f.replaceEditor(); await refuses(() => f.driver.stageDraft(f.expected, 'Original owned draft.', 'Wrong', 'r')); eq(f.get(), 'Original owned draft.'); }
      finally { dispose(f); }
    });
    await check(`${tag}: actual event-handler replacement rejects readback and Undo`, async () => {
      const f = fixture(tag); try {
        f.editor.addEventListener('input', () => f.replaceEditor(), { once: true });
        await refuses(() => f.driver.stageDraft(f.expected, 'Original owned draft.', 'A partial write occurred', 'r'));
        eq(f.get(), 'A partial write occurred'); await refuses(() => f.driver.undoDraft(f.expected, 'r'));
      } finally { dispose(f); }
    });
  }
  await check('actual role/body DOM preserves complete pair text, excludes completion claims for streaming', async () => {
    const f = fixture(); try {
      f.addTurn('u1', 'user', 'Do not change the 4 checks.'); const assistant = f.addTurn('a1', 'assistant', 'Keep every original constraint.');
      const got = f.driver.captureTurns(f.expected); eq(got.turns.length, 2); eq(got.turns[0].text, 'Do not change the 4 checks.'); eq(got.allConversationHistory, false);
      assistant.dataset.complete = 'false'; await refuses(() => f.driver.captureTurns(f.expected));
    } finally { dispose(f); }
  });
  await check('actual original File bytes/FileList assignment is exact but not upload confirmation', async () => {
    const f = fixture(), a = await asset(); try {
      const staged = await f.driver.stageAssets(f.expected, [a], () => {});
      eq(staged.assigned, true); eq(staged.confirmed, false); eq(f.input.files.length, 1);
      eq(f.input.files[0] instanceof File, true); eq(f.input.files[0].name, a.name);
      eq(await digest(await f.input.files[0].arrayBuffer()), a.sha256);
      await refuses(() => f.driver.confirmAssets(f.expected, [a], staged.previousIds));
      f.state.receipts = [receipt(a)]; eq(f.driver.confirmAssets(f.expected, [a], staged.previousIds)[0].sha256, a.sha256);
    } finally { dispose(f); }
  });
  await check('actual browser async digest corruption refuses before FileList mutation', async () => {
    const f = fixture(), a = await asset(); try { a.bytes[0] ^= 1; await refuses(() => f.driver.stageAssets(f.expected, [a], () => {})); eq(f.input.files.length, 0); }
    finally { dispose(f); }
  });
  await check('Stop from real file-input event leaves truthful assigned files and prevents change event', async () => {
    const f = fixture(), a = await asset(); let stopped = false, changes = 0;
    try {
      f.input.addEventListener('input', () => { stopped = true; }, { once: true }); f.input.addEventListener('change', () => { changes++; });
      await refuses(() => f.driver.stageAssets(f.expected, [a], () => { if (stopped) throw new Error('Stop'); }));
      eq(f.input.files.length, 1); eq(changes, 0);
    } finally { dispose(f); }
  });
  for (const state of ['pending', 'error', 'removed']) await check(`${state} attachment is not confirmed by real DOM file presence`, async () => {
    const f = fixture(), a = await asset(); try {
      await f.driver.stageAssets(f.expected, [a], () => {});
      f.state.receipts = state === 'removed' ? [] : [receipt(a, { state })];
      await refuses(() => f.driver.confirmAssets(f.expected, [a], []));
    } finally { dispose(f); }
  });
  await check('all actual fixture paths leave Send/submit/Enter unused', async () => { eq(effects.sends, 0); eq(effects.enterKeys, 0); });
  const report = { schema: 1, completed: true, passed: results.every(x => x.passed), cases: results.length,
    driverSha256: document.querySelector('meta[name="buddy-driver-sha256"]').content,
    scope: 'Real Chromium DOM/File/DataTransfer/Event APIs on an owned local fixture; provider identity, history completeness and upload receipts remain synthetic.',
    realAccount: false, realHistory: false, realUpload: false, extensionInstalled: false, results, effects };
  document.getElementById('result').textContent = JSON.stringify(report);
  document.documentElement.dataset.buddyCompleted = 'true';
})().catch(error => {
  document.getElementById('result').textContent = JSON.stringify({ completed: true, passed: false, error: String(error?.message || error).slice(0, 350) });
  document.documentElement.dataset.buddyCompleted = 'true';
});
