/* Provisional ChatGPT DOM profile. These selectors are not a public provider API.
 * No authenticated endpoint, page script, cookie, localStorage or token is read.
 * In particular a profile label cannot establish the active account/workspace.
 */
(function (root) {
  'use strict';
  const PROFILE = Object.freeze({
    id: 'chatgpt-dom-v1-unverified', origin: 'https://chatgpt.com',
    editor: '#prompt-textarea[contenteditable="true"]',
    turns: '[data-testid^="conversation-turn-"]',
    role: '[data-message-author-role]',
    body: '[data-message-author-role] .markdown, [data-message-author-role="user"] .whitespace-pre-wrap',
    streaming: '[data-is-streaming="true"], [data-testid="stop-button"]',
    files: 'form input[type="file"]'
  });
  function failure(code) { const error = new Error(code); error.code = code; return error; }
  function visible(node) {
    if (!node || !node.isConnected || node.hidden || node.getAttribute('aria-hidden') === 'true') return false;
    const style = node.ownerDocument.defaultView.getComputedStyle(node);
    return style.display !== 'none' && style.visibility !== 'hidden' && node.getClientRects().length > 0;
  }
  function createChatGptAdapter(document, location) {
    function route() {
      const url = new URL(location.href);
      if (url.origin !== PROFILE.origin || url.username || url.password) throw failure('PROVIDER_ORIGIN_UNSUPPORTED');
      const match = /^\/c\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\/?$/i.exec(url.pathname);
      return match ? match[1].toLowerCase() : null;
    }
    function all(selector) { return Array.from(document.querySelectorAll(selector)); }
    function characterize() {
      const conversation = route();
      const editors = all(PROFILE.editor).filter(visible);
      const roles = all(PROFILE.role);
      const fileInputs = all(PROFILE.files);
      return {
        providerId: 'chatgpt', profile: PROFILE.id,
        stableConversationRoute: conversation !== null,
        accountIdentity: 'unavailable', workspaceIdentity: 'unavailable',
        editorCandidates: Math.min(editors.length, 2),
        renderedRoleNodes: Math.min(roles.length, 513),
        renderedUserCount: Math.min(roles.filter(n=>n.getAttribute('data-message-author-role')==='user').length,513),
        renderedAssistantCount: Math.min(roles.filter(n=>n.getAttribute('data-message-author-role')==='assistant').length,513),
        fileInputCandidates: Math.min(fileInputs.length, 2),
        streamingIndicatorPresent: all(PROFILE.streaming).some(visible),
        capabilities: { history: false, draft: false, originalAssets: false, confirmedAttachments: false },
        reason: 'ACCOUNT_IDENTITY_UNAVAILABLE',
        liveValidated: false
      };
    }
    const driver = root.BuddyBrowserDom.createDomDriver(document, location, Object.freeze({
      ...PROFILE, identityVerified: false, draftVerified: false, historyVerified: false,
      attachmentsVerified: false, liveValidated: false,
      // No fabricated account selectors, inferred history completeness, or
      // filename-only upload receipts. These source-owned readers require a
      // separate reviewed provider characterization before admission.
      readIdentity() { throw failure('ACCOUNT_IDENTITY_UNAVAILABLE'); },
      readCoverage() { throw failure('HISTORY_COMPLETENESS_UNAVAILABLE'); },
      readTurn() { throw failure('HISTORY_COMPLETENESS_UNAVAILABLE'); },
      readAttachments() { throw failure('ATTACHMENT_CONFIRMATION_UNAVAILABLE'); }
    }));
    return Object.freeze({ id: 'chatgpt', origin: PROFILE.origin, characterize, ...driver });
  }
  const api = Object.freeze({ PROFILE, createChatGptAdapter });
  root.BuddyChatGpt = api;
  if (typeof module !== 'undefined') module.exports = api;
})(globalThis);
