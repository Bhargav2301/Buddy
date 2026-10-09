(function (root) {
  'use strict';
  const VERSION = '0.1.6';
  const counts = Object.freeze({editorCandidates: 2, renderedRoleNodes: 513,
    renderedUserCount: 513, renderedAssistantCount: 513, fileInputCandidates: 2});
  const flags = ['streamingIndicatorPresent', 'stableConversationRoute'];
  const messages = Object.freeze({
    R00: 'An unexpected readiness boundary failed.',
    R01: 'Chrome could not identify the popup browser window.',
    R02: 'The Buddy popup is no longer focused and visible.',
    R03: 'Chrome could not identify the selected tab.',
    R04: 'Chrome did not return one matching selected tab.',
    R06: 'Select a tab on https://chatgpt.com before opening the probe.',
    R07: 'Chrome could not establish the selected top document.',
    R08: 'Chrome did not return exactly one document result.',
    R09: 'The response was not from the top frame.',
    R10: 'The original document could not be verified.',
    R11: 'The document response did not match this check.',
    R15: 'The popup window or selected tab changed during the check.',
    R18: 'Chrome could not complete the structural document check.',
    R21: 'The destination changed before the result was accepted.',
    R23: 'The document refused the check or returned an invalid observation.',
    R24: 'A previous check is still settling. No new check was started.',
    R29: 'This check was cancelled. Its result was discarded.',
    R30: 'This check expired. No follow-on work will be dispatched.',
    R31: 'Open this probe using its Chrome toolbar button.',
    R32: 'The page exceeded the bounded structural scan. No partial result was accepted.'
  });
  function diagnostic(code) {
    const key = Object.hasOwn(messages, code) ? code : 'R00';
    return `Readiness diagnostic ${key} (probe ${VERSION})\n${messages[key]}\nReport this code before another check; no message was sent.`;
  }
  function observation(value) {
    if (!value || typeof value !== 'object' || Array.isArray(value) ||
        Object.keys(value).length !== Object.keys(counts).length + flags.length) return null;
    const safe = {};
    for (const [key, cap] of Object.entries(counts)) {
      if (!Object.hasOwn(value, key) || !Number.isSafeInteger(value[key]) || value[key] < 0 || value[key] > cap) return null;
      safe[key] = value[key];
    }
    for (const key of flags) {
      if (!Object.hasOwn(value, key) || typeof value[key] !== 'boolean') return null;
      safe[key] = value[key];
    }
    return Object.freeze(safe);
  }
  function describe(value) {
    const safe = observation(value);
    if (!safe) return diagnostic('R23');
    const count = key => String(safe[key]) + (safe[key] === counts[key] ? '+' : '');
    return [`Readiness observation (probe ${VERSION}; capped DOM counts)`,
      'Visible composer candidates: ' + count('editorCandidates'),
      'Role elements: ' + count('renderedRoleNodes'),
      'User-role elements: ' + count('renderedUserCount'),
      'Assistant-role elements: ' + count('renderedAssistantCount'),
      'File input elements: ' + count('fileInputCandidates'),
      'Streaming indicator present: ' + (safe.streamingIndicatorPresent ? 'yes' : 'no'),
      'Conversation-shaped URL: ' + (safe.stableConversationRoute ? 'yes' : 'no'),
      safe.editorCandidates === 0 ? 'No supported visible editor was detected; this does not prove the message box is absent.' :
        safe.editorCandidates === 1 ? 'One provisional editor candidate was detected; draft access is not verified.' :
          'Multiple editor candidates were detected; the composer is ambiguous.',
      '+ means at least that many. Counts cover loaded DOM only; virtualized turns may be absent.',
      ...(safe.renderedRoleNodes === 0 ? ['No supported message-role markers were detected; visible messages may still be present.'] : []),
      'File inputs count controls, not attachments. Recognized URL shapes: /c/UUID and /g/g-.../c/UUID.',
      'Live account, history, draft and attachment capabilities remain unavailable.',
      'No chat text, draft, account details or file contents were read. Nothing was sent.'].join('\n');
  }

  // Serialized by executeScript. No closure, persistent listener, page write or network API.
  function inspectDocument(expectedHref, nonce, readStructure) {
    if (window.top !== window || location.href !== expectedHref || document.visibilityState !== 'visible')
      return {ok: false};
    const url = new URL(location.href);
    if (url.origin !== 'https://chatgpt.com' || url.username || url.password) return {ok: false};
    if (!readStructure) return {ok: true, nonce};
    const result = {editorCandidates: 0, renderedRoleNodes: 0, renderedUserCount: 0,
      renderedAssistantCount: 0, fileInputCandidates: 0, streamingIndicatorPresent: false,
      stableConversationRoute: /^\/(?:g\/g-[a-z0-9]+(?:-[a-z0-9]+)*\/)?c\/[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\/?$/i.test(url.pathname)};
    function visible(node) {
      if (!node.isConnected || node.closest('[hidden], [inert], [aria-hidden="true"]')) return false;
      const style = getComputedStyle(node);
      return style.display !== 'none' && style.visibility !== 'hidden' &&
        style.visibility !== 'collapse' && style.opacity !== '0' &&
        Array.from(node.getClientRects()).some(rect => rect.width > 0 && rect.height > 0);
    }
    function composerCandidate(node) {
      // isContentEditable handles empty/plaintext-only/inherited HTML states.
      // Count editing hosts, not their editable descendants. No labels or values.
      const textarea = node.localName === 'textarea';
      const editingHost = node.isContentEditable === true && node.parentElement?.isContentEditable !== true;
      if (!textarea && !editingHost) return false;
      if (!node.matches('#prompt-textarea') && !node.closest('form') && node.getAttribute('role') !== 'textbox') return false;
      if (node.matches(':disabled') || node.readOnly === true ||
          node.closest('[aria-disabled="true"], [aria-readonly="true"]')) return false;
      return visible(node);
    }
    // Live-observed search-unit wrappers replace the legacy role attribute on
    // the current page. Keys stay request-local; only capped counts leave here.
    const markerNodes = new Map(), groups = new Set(), unitsByScope = new Map();
    const markerSelector = '[data-message-author-role], [data-chatgpt-search-unit-key]';
    function roleMarker(node) {
      const legacy = node.hasAttribute('data-message-author-role') ? node.getAttribute('data-message-author-role') : null;
      const key = node.getAttribute('data-chatgpt-search-unit-key');
      const match = typeof key === 'string' && key.length <= 128 ? /^fallback-turn-\d+:\d+:(user|assistant)$/.exec(key) : null;
      const scope = match && node.closest('main [data-chatgpt-conversation-selection-target="true"][data-thread-find-target="conversation"]');
      const paired = scope && (node.getAttribute('data-content-search-unit-key') === key ||
        node.firstElementChild?.getAttribute('data-content-search-unit-key') === key);
      if (paired) return {role: match[1], key, scope, conflict: legacy !== null && legacy !== match[1]};
      return legacy === null ? null : {role: legacy, key: null, scope: null, conflict: false};
    }
    function addMarker(node, marker) {
      let ancestor = node.parentElement?.closest(markerSelector);
      while (ancestor && !markerNodes.has(ancestor)) {
        if (performance.now() - start > 100) return false;
        ancestor = ancestor.parentElement?.closest(markerSelector);
      }
      let sameKey;
      if (marker.key) {
        if (!unitsByScope.has(marker.scope)) unitsByScope.set(marker.scope, new Map());
        sameKey = unitsByScope.get(marker.scope).get(marker.key);
      }
      const group = markerNodes.get(ancestor) ?? sameKey ?? {role: marker.role, key: null, scope: null, conflict: false};
      if (marker.conflict || group.role !== marker.role) group.conflict = true;
      // Overlapping units with inconsistent identity cannot establish a count.
      if (marker.key && group.key && (marker.key !== group.key || marker.scope !== group.scope)) group.conflict = true;
      if (marker.key && !group.key) { group.key = marker.key; group.scope = marker.scope; }
      if (sameKey && sameKey !== group) { sameKey.conflict = true; group.conflict = true; }
      if (marker.key) unitsByScope.get(marker.scope).set(marker.key, group);
      markerNodes.set(node, group); groups.add(group);
      return true;
    }
    const start = performance.now();
    const walker = document.createTreeWalker(document.documentElement, NodeFilter.SHOW_ELEMENT);
    let node = walker.currentNode, visited = 0;
    while (node) {
      if (++visited > 8000 || performance.now() - start > 100) return {ok: false, reason: 'limit'};
      if (result.editorCandidates < 2 && composerCandidate(node)) result.editorCandidates++;
      if (node.hasAttribute('data-message-author-role') || node.hasAttribute('data-chatgpt-search-unit-key')) {
        const marker = roleMarker(node);
        if (marker && !addMarker(node, marker)) return {ok: false, reason: 'limit'};
      }
      if (result.fileInputCandidates < 2 && node.matches('form input[type="file"]')) result.fileInputCandidates++;
      if (!result.streamingIndicatorPresent && node.matches('[data-is-streaming="true"], [data-testid="stop-button"]') && visible(node)) result.streamingIndicatorPresent = true;
      node = walker.nextNode();
    }
    for (const group of groups) {
      if (performance.now() - start > 100) return {ok: false, reason: 'limit'};
      if (group.conflict) continue;
      result.renderedRoleNodes = Math.min(513, result.renderedRoleNodes + 1);
      if (group.role === 'user') result.renderedUserCount = Math.min(513, result.renderedUserCount + 1);
      if (group.role === 'assistant') result.renderedAssistantCount = Math.min(513, result.renderedAssistantCount + 1);
    }
    if (location.href !== expectedHref || document.visibilityState !== 'visible') return {ok: false};
    return {ok: true, nonce, observation: result};
  }

  function createProbe(chrome, popup, options = {}) {
    const doc = popup.document, expectedPopup = chrome.runtime.getURL('popup.html');
    const now = options.now ?? (() => performance.now());
    const schedule = options.schedule ?? setTimeout, unschedule = options.unschedule ?? clearTimeout;
    let active = null, pending = false, disposed = false;
    const subscriptions = [];
    const fail = code => { throw Object.assign(new Error('Readiness check refused'), {diagnostic: code}); };
    function owner() {
      let views;
      try { views = chrome.extension.getViews({type: 'popup'}); } catch { fail('R31'); }
      if (disposed || popup.document !== doc || popup.location.href !== expectedPopup ||
          !Array.isArray(views) || views.filter(view => view === popup).length !== 1) fail('R31');
      if (doc.visibilityState !== 'visible' || doc.hasFocus() !== true) fail('R02');
    }
    function guard(attempt) {
      if (active !== attempt || attempt.cancelled) fail(attempt.reason ?? 'R29');
      if (now() >= attempt.deadline) { stop('R30'); fail('R30'); }
      owner();
    }
    function stop(reason = 'R29') {
      const attempt = active;
      if (attempt) { attempt.cancelled = true; attempt.reason = reason; unschedule(attempt.timer); attempt.end(); }
      active = null;
      return {status: 'disconnected', readDispatched: attempt?.readDispatched === true};
    }
    async function call(attempt, code, action) {
      guard(attempt);
      let value;
      try { value = await Promise.race([action(), attempt.ended.then(() => fail(attempt.reason ?? 'R29'))]); } catch {
        guard(attempt); fail(code);
      }
      guard(attempt); // A completed await never regains cancelled authority.
      return value;
    }
    function normalWindow(value) {
      return value && Number.isSafeInteger(value.id) && value.id >= 0 && value.type === 'normal' && typeof value.focused === 'boolean' && ['normal', 'maximized', 'fullscreen'].includes(value.state);
    }
    function supported(tab) {
      try { const url = new URL(tab.url); return url.origin === 'https://chatgpt.com' && !url.username && !url.password; }
      catch { return false; }
    }
    function sameTab(tab, attempt) {
      return tab && tab.id === attempt.tabId && tab.windowId === attempt.windowId &&
        tab.active === true && tab.url === attempt.url && !tab.pendingUrl && tab.status === 'complete';
    }
    async function scope(attempt, code) {
      const current = await call(attempt, code, () => chrome.windows.getCurrent({populate: false, windowTypes: ['normal']}));
      const last = await call(attempt, code, () => chrome.windows.getLastFocused({populate: false, windowTypes: ['normal']}));
      if (!normalWindow(current) || !normalWindow(last) || current.id !== attempt.windowId || last.id !== attempt.windowId) fail(code);
      const tabs = await call(attempt, code, () => chrome.tabs.query({active: true, windowId: attempt.windowId}));
      if (!Array.isArray(tabs) || tabs.length !== 1 || !sameTab(tabs[0], attempt)) fail(code);
    }
    function result(rows, attempt, documentId) {
      if (!Array.isArray(rows) || rows.length !== 1) fail('R08');
      const row = rows[0];
      if (row.frameId !== 0) fail('R09');
      if (typeof row.documentId !== 'string' || !row.documentId || (documentId && row.documentId !== documentId)) fail('R10');
      if (row.result?.ok !== true) fail(row.result?.reason === 'limit' ? 'R32' : 'R23');
      if (row.result.nonce !== attempt.nonce) fail('R11');
      return row;
    }
    async function run() {
      if (pending) return {ok: false, diagnostic: 'R24'};
      // Own the operation before any API await, including the initial window lookup.
      pending = true;
      const attempt = {deadline: now() + 10000, nonce: crypto.randomUUID(), readDispatched: false};
      attempt.ended = new Promise(resolve => { attempt.end = resolve; });
      active = attempt;
      attempt.timer = schedule(() => { if (active === attempt) stop('R30'); }, 10000);
      try {
        const current = await call(attempt, 'R01', () => chrome.windows.getCurrent({populate: false, windowTypes: ['normal']}));
        if (!normalWindow(current)) fail('R01');
        attempt.windowId = current.id;
        const last = await call(attempt, 'R01', () => chrome.windows.getLastFocused({populate: false, windowTypes: ['normal']}));
        if (!normalWindow(last) || last.id !== attempt.windowId) fail('R15');
        const tabs = await call(attempt, 'R03', () => chrome.tabs.query({active: true, windowId: attempt.windowId}));
        if (!Array.isArray(tabs) || tabs.length !== 1 || !Number.isSafeInteger(tabs[0].id) || tabs[0].id < 0 ||
            tabs[0].windowId !== attempt.windowId || tabs[0].active !== true || tabs[0].pendingUrl || tabs[0].status !== 'complete') fail('R04');
        if (!supported(tabs[0])) fail('R06');
        attempt.tabId = tabs[0].id; attempt.url = tabs[0].url;
        await scope(attempt, 'R15');
        const bound = result(await call(attempt, 'R07', () => chrome.scripting.executeScript({
          target: {tabId: attempt.tabId, frameIds: [0]}, world: 'ISOLATED',
          func: inspectDocument, args: [attempt.url, attempt.nonce, false]})), attempt);
        await scope(attempt, 'R15');
        const measured = result(await call(attempt, 'R18', () => {
          attempt.readDispatched = true;
          return chrome.scripting.executeScript({target: {tabId: attempt.tabId, documentIds: [bound.documentId]},
            world: 'ISOLATED', func: inspectDocument, args: [attempt.url, attempt.nonce, true]});
        }), attempt, bound.documentId);
        await scope(attempt, 'R21');
        const safe = observation(measured.result.observation);
        if (!safe) fail('R23');
        return {ok: true, observation: safe};
      } catch (error) {
        return {ok: false, diagnostic: Object.hasOwn(messages, error?.diagnostic) ? error.diagnostic : 'R00'};
      } finally {
        unschedule(attempt.timer);
        if (active === attempt) active = null;
        pending = false;
      }
    }
    function domEvent(target, name, fn) {
      target.addEventListener(name, fn); subscriptions.push(() => target.removeEventListener(name, fn));
    }
    function browserEvent(event, fn) {
      event.addListener(fn); subscriptions.push(() => event.removeListener(fn));
    }
    domEvent(popup, 'blur', () => stop());
    domEvent(popup, 'pagehide', () => { disposed = true; stop(); });
    domEvent(doc, 'visibilitychange', () => { if (doc.visibilityState !== 'visible') stop(); });
    browserEvent(chrome.tabs.onActivated, info => { if (active && (active.windowId === undefined || info.windowId === active.windowId)) stop(); });
    browserEvent(chrome.tabs.onRemoved, id => { if (active?.tabId === id) stop(); });
    browserEvent(chrome.tabs.onUpdated, (id, change) => { if (active && (active.tabId === undefined || active.tabId === id) && (change.status === 'loading' || Object.hasOwn(change, 'url'))) stop(); });
    browserEvent(chrome.windows.onRemoved, id => { if (active?.windowId === id) stop(); });
    browserEvent(chrome.windows.onFocusChanged, id => {
      if (active && ((id >= 0 && active.windowId !== undefined && id !== active.windowId) || doc.hasFocus() !== true)) stop();
    });
    return Object.freeze({run, stop, dispose() { disposed = true; stop(); for (const unsubscribe of subscriptions) unsubscribe(); }});
  }
  const api = Object.freeze({VERSION, createProbe, inspectDocument, observation, diagnostic, describe});
  root.BuddyReadiness = api;
  if (typeof module !== 'undefined') module.exports = api;
})(globalThis);
