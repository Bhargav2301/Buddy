(function (root) {
  'use strict';
  // Only source-reviewed registrations belong here. The page cannot register a
  // provider, supply selectors, or enable capabilities using DOM/query/storage.
  function select(document, location) {
    const url = new URL(location.href);
    if (url.origin === 'https://chatgpt.com') return root.BuddyChatGpt.createChatGptAdapter(document, location);
    const error = new Error('PROVIDER_UNSUPPORTED'); error.code = error.message; throw error;
  }
  const api = Object.freeze({ select, providerIds: Object.freeze(['chatgpt']) });
  root.BuddyBrowserProviders = api;
  if (typeof module !== 'undefined') module.exports = api;
})(globalThis);
