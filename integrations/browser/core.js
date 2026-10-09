(function (root) {
  'use strict';
  const LIMITS = Object.freeze({ frame: 65536, chunk: 32768, draftChars: 20000, draftBytes: 65536,
    historyBytes: 4194304, pairs: 256, assets: 8, assetBytes: 2000000, totalAssets: 16000000, sequence: 4096 });
  const enc = new TextEncoder();
  const fail = code => { const e = new Error(code); e.code = code; throw e; };
  const copy = value => JSON.parse(JSON.stringify(value));
  function frozen(value) { if (value && typeof value === 'object') { Object.values(value).forEach(frozen); Object.freeze(value); } return value; }
  function wire(value) { const json = JSON.stringify(value); if (enc.encode(json).length > LIMITS.frame) fail('FRAME_LIMIT'); return json; }
  function token(value) { return typeof value === 'string' && value.length > 0 && value.length <= 256 && !/[\x00-\x20\x7f]/.test(value); }
  function binding(value, readiness = false) {
    if (!value || value.providerId !== 'chatgpt' || !Number.isInteger(value.tabId) || value.tabId < 0 || value.tabId > 2147483647 ||
      !token(value.documentId) || !token(value.generation)) fail('BINDING_INVALID');
    for (const key of ['accountId', 'workspaceId', 'conversationId']) if (!token(value[key]) && !(readiness && (value[key] === '' || value[key] === null))) fail('IDENTITY_UNAVAILABLE');
    return frozen(Object.fromEntries(['providerId', 'tabId', 'documentId', 'generation', 'accountId', 'workspaceId', 'conversationId'].map(k => [k, value[k]])));
  }
  function sameBinding(a, b) { return ['providerId', 'tabId', 'documentId', 'generation', 'accountId', 'workspaceId', 'conversationId'].every(k => a[k] === b[k]); }
  async function sha256(bytes) { return Array.from(new Uint8Array(await root.crypto.subtle.digest('SHA-256', bytes)), x => x.toString(16).padStart(2, '0')).join(''); }
  const textHash = text => sha256(enc.encode(text));
  function strictText(value, maxChars = LIMITS.draftChars, maxBytes = LIMITS.draftBytes) {
    if (typeof value !== 'string' || value.length > maxChars || enc.encode(value).length > maxBytes || /[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]/u.test(value)) fail('TEXT_INVALID');
    return value;
  }
  function base64(bytes) { let s = ''; for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]); return btoa(s); }
  function fromBase64(value) {
    if (typeof value !== 'string' || value.length > 4 * Math.ceil(LIMITS.chunk / 3) || !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(value)) fail('CHUNK_INVALID');
    const bytes = Uint8Array.from(atob(value), x => x.charCodeAt(0));
    if (base64(bytes) !== value || bytes.length > LIMITS.chunk) fail('CHUNK_INVALID'); return bytes;
  }
  class ChunkAssembly {
    #bytes; #offset = 0; #index = 0; #done = false; #digest;
    constructor(size, digest, cap = LIMITS.assetBytes) {
      if (!Number.isInteger(size) || size < 0 || size > cap || !/^[a-f0-9]{64}$/.test(digest)) fail('TRANSFER_INVALID');
      this.#bytes = new Uint8Array(size); this.#digest = digest;
    }
    add(index, dataBase64) {
      if (this.#done || index !== this.#index) { this.#done = true; fail('CHUNK_ORDER'); }
      const bytes = fromBase64(dataBase64);
      if(this.#bytes.length===0){if(index!==0 || bytes.length!==0){this.#done=true;fail('CHUNK_LENGTH');}this.#index++;return;}
      if (bytes.length !== Math.min(LIMITS.chunk, this.#bytes.length - this.#offset) || !bytes.length) { this.#done = true; fail('CHUNK_LENGTH'); }
      this.#bytes.set(bytes, this.#offset); this.#offset += bytes.length; this.#index++;
    }
    async finish(check = () => {}) {
      if (this.#done || this.#offset !== this.#bytes.length || this.#index===0) fail('TRANSFER_INCOMPLETE');
      this.#done = true; check();
      const value = this.#bytes; this.#bytes = new Uint8Array();
      if (await sha256(value) !== this.#digest) { value.fill(0); fail('TRANSFER_HASH'); }
      try { check(); } catch (error) { value.fill(0); throw error; }
      return value;
    }
    clear() { this.#done = true; this.#bytes.fill(0); this.#bytes = new Uint8Array(); }
  }
  class SessionGuard {
    #binding; #clock; #last; #end; #reason = null;
    constructor(value, clock = () => performance.now(), lifetimeMs = 120000) {
      this.#binding = binding(value); this.#clock = clock; this.#last = clock();
      if (!Number.isFinite(this.#last) || !Number.isFinite(lifetimeMs) || lifetimeMs <= 0 || lifetimeMs > 120000) fail('SESSION_INVALID');
      this.#end = this.#last + lifetimeMs;
    }
    get binding() { return this.#binding; }
    invalidate(reason = 'SESSION_INVALIDATED') { this.#reason ??= reason; }
    check(current) {
      if (this.#reason) fail(this.#reason);
      const now = this.#clock();
      if (!Number.isFinite(now) || now < this.#last || now >= this.#end) { this.invalidate('SESSION_EXPIRED'); fail('SESSION_EXPIRED'); }
      this.#last = now;
      try { if (!sameBinding(this.#binding, binding(current))) fail('DESTINATION_CHANGED'); }
      catch { this.invalidate('DESTINATION_CHANGED'); fail('DESTINATION_CHANGED'); }
    }
  }
  class ReviewedStage {
    #driver; #expected; #check; #grant; #consumed = false; #receipt = null; #undoConsumed = false;
    constructor(driver, expected, check, grant) {
      if (!grant || !token(grant.reviewId) || !/^[a-f0-9]{64}$/.test(grant.originalDraftSha256) || !/^[a-f0-9]{64}$/.test(grant.draftSha256) ||
        !Array.isArray(grant.assets) || grant.assets.length>LIMITS.assets) fail('REVIEW_INVALID');
      let total=0;const ids=new Set();
      for(const a of grant.assets){
        if(!a || !token(a.contentId) || ids.has(a.contentId) || a.contentId==='draft' || typeof a.name!=='string' || !a.name || a.name.length>255 || /[\/\\\x00-\x1f]/.test(a.name) ||
          typeof a.mediaType!=='string' || !/^[a-z0-9.+-]+\/[a-z0-9.+-]+$/i.test(a.mediaType) || !Number.isInteger(a.size) || a.size<0 || a.size>LIMITS.assetBytes || !/^[a-f0-9]{64}$/.test(a.sha256))fail('REVIEW_INVALID');
        ids.add(a.contentId);total+=a.size;
      }
      if(total>LIMITS.totalAssets)fail('REVIEW_INVALID');
      this.#driver=driver;this.#expected=frozen(copy(expected));this.#check=check;this.#grant=frozen(copy(grant));
    }
    #guard() { this.#check(); this.#driver.guard(this.#expected); }
    async stage(draft, assets) {
      if (this.#consumed) fail('REVIEW_CONSUMED'); this.#consumed = true;
      let effectsMayHaveOccurred = false;const ownedAssets=[];const review=this.#grant;
      try {
        this.#guard(); strictText(draft);
        if(!Array.isArray(assets) || assets.length!==review.assets.length)fail('REVIEW_INVALID');
        // Validate actual lengths and every reviewed metadata field before any
        // allocation/copy. An asset cannot be swapped after review or hash wait.
        assets.forEach((a,i)=>{
          const wanted=review.assets[i];
          if(!a || !(a.bytes instanceof Uint8Array) || a.bytes.byteLength!==wanted.size ||
            !['contentId','name','mediaType','size','sha256'].every(k=>a[k]===wanted[k]))fail('REVIEW_INVALID');
        });
        assets.forEach((a,i)=>ownedAssets.push({...review.assets[i],bytes:a.bytes.slice()}));
        for(const asset of ownedAssets){if(await sha256(asset.bytes)!==asset.sha256)fail('ASSET_HASH_MISMATCH');this.#guard();}
        const before = this.#driver.readDraft(this.#expected);
        if (await textHash(before) !== review.originalDraftSha256 || await textHash(draft) !== review.draftSha256) fail('DRAFT_CHANGED');
        this.#guard();
        if (this.#driver.readDraft(this.#expected) !== before) fail('DRAFT_CHANGED');
        // These flags must be source-owned admitted capabilities, never inferred
        // from an attach button or the mere existence of a file input.
        if (!this.#expected.capabilities.draft || !this.#expected.capabilities.confirmedAttachments || (assets.length && !this.#expected.capabilities.originalAssets)) fail('CAPABILITY_UNAVAILABLE');
        if (this.#driver.readAttachments(this.#expected).length) fail('ATTACHMENT_STATE_CHANGED');
        effectsMayHaveOccurred = true;
        await this.#driver.stageDraft(this.#expected, before, draft, review.reviewId);
        this.#guard();
        let confirmed = [];
        if (ownedAssets.length) {
          const assigned = await this.#driver.stageAssets(this.#expected, ownedAssets, () => this.#guard());
          this.#guard(); confirmed = await this.#driver.awaitAttachments(this.#expected, ownedAssets, assigned.previousIds, () => this.#guard());
          this.#guard();
        }
        this.#guard();
        if (this.#driver.readDraft(this.#expected) !== draft) fail('DRAFT_READBACK_FAILED');
        this.#receipt = frozen({ reviewId: review.reviewId, draftSha256: review.draftSha256, confirmed,
          attachments: ownedAssets.length ? this.#driver.readAttachments(this.#expected) : [], sent: false });
        return { status: 'staged', effectsMayHaveOccurred: true, receipt: this.#receipt };
      } catch (error) {
        this.#receipt = null;
        return { status: effectsMayHaveOccurred ? 'partial-or-unknown' : 'refused', effectsMayHaveOccurred,
          reason: safeReason(error), sent: false };
      } finally {ownedAssets.forEach(a=>a.bytes.fill(0));}
    }
    async undo(reviewId) {
      if (this.#undoConsumed) fail('UNDO_CONSUMED'); this.#undoConsumed = true;
      if (!this.#receipt || this.#receipt.reviewId !== reviewId) fail('UNDO_UNAVAILABLE');
      this.#guard();
      const current = this.#driver.readDraft(this.#expected);
      if (await textHash(current) !== this.#receipt.draftSha256) fail('DRAFT_CHANGED');
      this.#guard();
      if (this.#driver.readDraft(this.#expected) !== current) fail('DRAFT_CHANGED');
      if (JSON.stringify(this.#driver.readAttachments(this.#expected)) !== JSON.stringify(this.#receipt.attachments)) fail('ATTACHMENT_STATE_CHANGED');
      const result = await this.#driver.undoDraft(this.#expected, reviewId);
      this.#guard(); return { status: 'undone', ...result, sent: false };
    }
  }
  function safeReason(error) {
    const code = error?.code;
    return REASONS.has(code) ? code : 'BROWSER_OPERATION_FAILED';
  }
  const REASONS=new Set(['FRAME_LIMIT','BINDING_INVALID','IDENTITY_UNAVAILABLE','TEXT_INVALID','CHUNK_INVALID','TRANSFER_INVALID','CHUNK_ORDER','CHUNK_LENGTH','TRANSFER_INCOMPLETE','TRANSFER_HASH','SESSION_INVALID','SESSION_EXPIRED','SESSION_INVALIDATED','DESTINATION_CHANGED','REVIEW_INVALID','REVIEW_CONSUMED','DRAFT_CHANGED','CAPABILITY_UNAVAILABLE','ATTACHMENT_STATE_CHANGED','DRAFT_READBACK_FAILED','UNDO_CONSUMED','UNDO_UNAVAILABLE','PROVIDER_ORIGIN_UNSUPPORTED','PROVIDER_UNSUPPORTED','ACCOUNT_IDENTITY_UNAVAILABLE','EDITOR_UNAVAILABLE','DRAFT_CAPABILITY_UNAVAILABLE','HISTORY_COMPLETENESS_UNAVAILABLE','HISTORY_INCOMPLETE','HISTORY_LIMIT','HISTORY_CHANGED','ATTACHMENT_CONFIRMATION_UNAVAILABLE','ATTACHMENT_INPUT_UNAVAILABLE','ASSET_INVALID','ASSET_HASH_MISMATCH','ASSET_READBACK_FAILED','ATTACHMENT_UNCONFIRMED','ATTACHMENT_TIMEOUT','ATTACHMENT_FAILED','SESSION_ALREADY_BOUND','SESSION_UNPAIRED','REQUEST_REFUSED','CONTENT_REFUSED','DOCUMENT_UNAVAILABLE','NATIVE_DISCONNECTED','NATIVE_TIMEOUT','NATIVE_UNAVAILABLE','NATIVE_REPLY_INVALID','HOST_REFUSED']);
  const api = Object.freeze({ LIMITS, wire, binding, sameBinding, sha256, textHash, strictText, base64, fromBase64,
    ChunkAssembly, SessionGuard, ReviewedStage, safeReason, frozen });
  root.BuddyBrowserCore = api;
  if (typeof module !== 'undefined') module.exports = api;
})(globalThis);
