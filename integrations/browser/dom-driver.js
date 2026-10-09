(function (root) {
  'use strict';
  const fail = code => { const e = new Error(code); e.code = code; throw e; };
  const equal = (a, b) => JSON.stringify(a) === JSON.stringify(b);
  function visible(node) {
    if (!node || !node.isConnected || node.hidden || node.getAttribute('aria-hidden') === 'true') return false;
    const s = node.ownerDocument.defaultView.getComputedStyle(node);
    return s.display !== 'none' && s.visibility !== 'hidden' && node.getClientRects().length > 0;
  }
  async function sha256(bytes) {
    return Array.from(new Uint8Array(await root.crypto.subtle.digest('SHA-256', bytes)), x => x.toString(16).padStart(2, '0')).join('');
  }
  // Profiles are source-owned, never accepted from a message or page. The live
  // registry admits ChatGPT's observation-only profile; test profiles stay tests.
  function createDomDriver(document, location, profile, options = {}) {
    const delay = options.delay ?? (() => new Promise(resolve => setTimeout(resolve, 100)));
    const clock = options.clock ?? (() => performance.now());
    const ids = new WeakMap(); let serial = 0; let undo = null;
    const id = node => { if (!ids.has(node)) ids.set(node, 'composer-' + ++serial); return ids.get(node); };
    const nodes = (parent, selector) => Array.from(parent.querySelectorAll(selector));
    const one = (parent, selector, code, needsVisible = true) => {
      const found = nodes(parent, selector).filter(n => !needsVisible || visible(n));
      if (found.length !== 1) fail(code); return found[0];
    };
    function identity() {
      if (new URL(location.href).origin !== profile.origin) fail('PROVIDER_ORIGIN_UNSUPPORTED');
      if (!profile.identityVerified) fail('ACCOUNT_IDENTITY_UNAVAILABLE');
      const value = profile.readIdentity(document, location);
      if (!value || !['accountId', 'workspaceId', 'conversationId'].every(k => typeof value[k] === 'string' && value[k].length > 0 && value[k].length <= 256)) fail('IDENTITY_UNAVAILABLE');
      return Object.freeze({ accountId: value.accountId, workspaceId: value.workspaceId, conversationId: value.conversationId });
    }
    function composer() {
      const node = one(document, profile.editor, 'EDITOR_UNAVAILABLE');
      if (node.disabled || node.readOnly || node.getAttribute('aria-disabled') === 'true') fail('EDITOR_UNAVAILABLE');
      return node;
    }
    const text = node => node.tagName === 'TEXTAREA' ? node.value : node.innerText;
    function observe() {
      const binding = identity(), node = composer();
      return Object.freeze({ identity: binding, composerId: id(node), profileId: profile.id,
        evidence: Object.freeze({ ...profile.evidence, composerId: id(node), capabilityRevision: profile.id }),
        provenance: 'source-registered-profile', liveValidated: profile.liveValidated === true,
        capabilities: Object.freeze({ draft: profile.draftVerified === true,
          history: profile.historyVerified === true, originalAssets: profile.attachmentsVerified === true,
          confirmedAttachments: profile.attachmentsVerified === true }) });
    }
    function guard(expected) {
      const current = observe();
      if (!equal(current, expected)) fail('DESTINATION_CHANGED');
      return current;
    }
    function readDraft(expected) {
      guard(expected); if (!profile.draftVerified) fail('DRAFT_CAPABILITY_UNAVAILABLE');
      return text(composer());
    }
    function replace(expected, before, after) {
      guard(expected); if (!profile.draftVerified) fail('DRAFT_CAPABILITY_UNAVAILABLE');
      const node = composer();
      if (text(node) !== before) fail('DRAFT_CHANGED');
      if (node.tagName === 'TEXTAREA') {
        const set = Object.getOwnPropertyDescriptor(document.defaultView.HTMLTextAreaElement.prototype, 'value')?.set;
        if (!set) fail('EDITOR_UNAVAILABLE'); set.call(node, after);
      } else node.textContent = after;
      node.dispatchEvent(new document.defaultView.InputEvent('input', { bubbles: true, inputType: 'insertText', data: after }));
      guard(expected);
      if (composer() !== node || text(node) !== after) fail('DRAFT_READBACK_FAILED');
    }
    function stageDraft(expected, before, after, transactionId) {
      undo = null; // A new attempt never inherits an old reversal authority.
      replace(expected, before, after);
      undo = { expected, before, after, transactionId };
      return { exact: true, composerId: expected.composerId };
    }
    function undoDraft(expected, transactionId) {
      const saved = undo; undo = null; // One explicit attempt, including refusal.
      if (!saved || saved.transactionId !== transactionId || !equal(saved.expected, expected)) fail('UNDO_UNAVAILABLE');
      replace(expected, saved.after, saved.before);
      return { exact: true, attachmentsRemoved: false };
    }
    function captureTurns(expected) {
      guard(expected); if (!profile.historyVerified) fail('HISTORY_COMPLETENESS_UNAVAILABLE');
      const observedCoverage=profile.readCoverage(document);
      if (!observedCoverage || observedCoverage.complete !== true || observedCoverage.hasEarlier!==false || observedCoverage.hasLater!==false || observedCoverage.scope !== 'rendered-completed-pairs' || observedCoverage.conversationId !== expected.identity.conversationId) fail('HISTORY_COMPLETENESS_UNAVAILABLE');
      const coverage=Object.freeze({...observedCoverage});
      const turns = nodes(document, profile.turns);
      if (turns.length > 512 || turns.length % 2) fail('HISTORY_INCOMPLETE');
      const seen = new Set(); let size = 0;
      const values = turns.map((turn, index) => {
        if (!visible(turn)) fail('HISTORY_INCOMPLETE');
        const role = profile.readTurn(turn);
        if (!role || role.role !== (index % 2 ? 'assistant' : 'user') || role.complete !== true || typeof role.id !== 'string' || !role.id || seen.has(role.id)) fail('HISTORY_INCOMPLETE');
        seen.add(role.id);
        const body = one(turn, profile.body, 'HISTORY_INCOMPLETE');
        const content = body.innerText;
        if (typeof content !== 'string' || !content.trim()) fail('HISTORY_INCOMPLETE');
        size += new TextEncoder().encode(content).length;
        if (size > 4 * 1024 * 1024) fail('HISTORY_LIMIT');
        return Object.freeze({ id: role.id, role: role.role, text: content });
      });
      if (coverage.firstId !== (values[0]?.id ?? '') || coverage.lastId !== (values.at(-1)?.id ?? '')) fail('HISTORY_INCOMPLETE');
      guard(expected);
      if (!equal(profile.readCoverage(document), coverage)) fail('HISTORY_CHANGED');
      return Object.freeze({ scope: 'rendered-completed-pairs', allConversationHistory: false,
        coverage: Object.freeze({ ...coverage }), turns: Object.freeze(values) });
    }
    function attachmentInput(expected) {
      guard(expected); if (!profile.attachmentsVerified) fail('ATTACHMENT_CONFIRMATION_UNAVAILABLE');
      const form = composer().closest('form');
      if (!form) fail('ATTACHMENT_INPUT_UNAVAILABLE');
      const input = one(form, profile.files, 'ATTACHMENT_INPUT_UNAVAILABLE', false);
      if (!input.isConnected || input.disabled || input.type !== 'file') fail('ATTACHMENT_INPUT_UNAVAILABLE');
      return input;
    }
    function readAttachments(expected) {
      guard(expected); if (!profile.attachmentsVerified) fail('ATTACHMENT_CONFIRMATION_UNAVAILABLE');
      const entries = profile.readAttachments(document);
      if (!Array.isArray(entries) || entries.length > 8) fail('ATTACHMENT_CONFIRMATION_UNAVAILABLE');
      return entries.map(x => Object.freeze({ id: x.id, name: x.name, size: x.size, sha256: x.sha256,
        state: x.state, visible: x.visible === true }));
    }
    async function stageAssets(expected, assets, check) {
      check(); const input = attachmentInput(expected), before = readAttachments(expected);
      if (before.length || input.files.length || assets.length < 1 || assets.length > 8 || (assets.length > 1 && !input.multiple)) fail('ATTACHMENT_STATE_CHANGED');
      const files = [];
      for (const asset of assets) {
        check(); guard(expected);
        if (!(asset.bytes instanceof Uint8Array) || asset.bytes.length !== asset.size || asset.size > 2000000 ||
          typeof asset.name !== 'string' || !asset.name || asset.name.length > 255 || /[\/\\\x00-\x1f]/.test(asset.name) ||
          typeof asset.mediaType !== 'string' || !/^[a-z0-9.+-]+\/[a-z0-9.+-]+$/i.test(asset.mediaType)) fail('ASSET_INVALID');
        const ownedBytes = asset.bytes.slice(), name = asset.name, type = asset.mediaType, digest = asset.sha256;
        if (await sha256(ownedBytes) !== digest) fail('ASSET_HASH_MISMATCH');
        check(); guard(expected);
        files.push(new document.defaultView.File([ownedBytes], name, { type, lastModified: 0 }));
      }
      check(); guard(expected);
      if (attachmentInput(expected) !== input || input.files.length || readAttachments(expected).length) fail('ATTACHMENT_STATE_CHANGED');
      const transfer = new document.defaultView.DataTransfer();
      files.forEach(file => transfer.items.add(file));
      input.files = transfer.files;
      // FileList assignment is only staging. Untrusted synthetic events may be
      // ignored; the caller must await correlated explicit provider receipts.
      input.dispatchEvent(new document.defaultView.Event('input', { bubbles: true }));
      check(); guard(expected);
      input.dispatchEvent(new document.defaultView.Event('change', { bubbles: true }));
      check(); guard(expected);
      if (attachmentInput(expected) !== input || input.files.length !== files.length || files.some((file, i) => input.files[i] !== file)) fail('ASSET_READBACK_FAILED');
      return Object.freeze({ assigned: true, confirmed: false, previousIds: Object.freeze(before.map(x => x.id)) });
    }
    function confirmAssets(expected, assets, previousIds) {
      const entries = readAttachments(expected), used = new Set();
      if (entries.length !== assets.length) fail('ATTACHMENT_UNCONFIRMED');
      const confirmed = assets.map(asset => {
        const matches = entries.filter(x => typeof x.id === 'string' && x.id && !previousIds.includes(x.id) &&
          x.visible && x.state === 'complete' && x.name === asset.name && x.size === asset.size && x.sha256 === asset.sha256);
        if (matches.length !== 1 || used.has(matches[0].id)) fail('ATTACHMENT_UNCONFIRMED');
        used.add(matches[0].id); return Object.freeze({ contentId: asset.contentId, providerAttachmentId: matches[0].id, sha256: asset.sha256 });
      });
      guard(expected); return Object.freeze(confirmed);
    }
    async function awaitAttachments(expected, assets, previousIds, check) {
      const started = clock(); let last = started;
      for (let attempt = 0; attempt < 100; attempt++) {
        check(); guard(expected); const now=clock();
        if (!Number.isFinite(now) || now < last || now-started >= 10000) fail('ATTACHMENT_TIMEOUT'); last=now;
        const entries=readAttachments(expected);
        if (entries.some(x=>x.state==='error' || x.state==='removed')) fail('ATTACHMENT_FAILED');
        try { const result=confirmAssets(expected,assets,previousIds);check();return result; }
        catch(error) { if(error.code!=='ATTACHMENT_UNCONFIRMED') throw error; }
        await delay();check();guard(expected);
      }
      fail('ATTACHMENT_TIMEOUT');
    }
    return Object.freeze({ observe, guard, readDraft, stageDraft, undoDraft, captureTurns, stageAssets, readAttachments, confirmAssets, awaitAttachments });
  }
  const api = Object.freeze({ createDomDriver, sha256, visible });
  root.BuddyBrowserDom = api;
  if (typeof module !== 'undefined') module.exports = api;
})(globalThis);
