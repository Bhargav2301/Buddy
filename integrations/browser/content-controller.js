(function (root) {
  'use strict';
  const core = root.BuddyBrowserCore;
  function refusal(code) { const e = new Error(code); e.code = code; throw e; }
  function createController(adapter, location, generation, clock = () => performance.now()) {
    const url = location.href; let invalid = false, expected = null, session = null, stage = null;
    const transfers = new Map(); let offer = null, stagedGrant = null, capturedHistory = null;
    function invalidate() { invalid = true; session?.invalidate(); transfers.forEach(x => x.clear()); transfers.clear();
      offer=null;stagedGrant=null;capturedHistory=null;stage=null; }
    function check() {
      if (invalid || location.href !== url) { invalidate(); refusal('DESTINATION_CHANGED'); }
      if (session) { try { const current = adapter.observe(); session.check({ ...session.binding, ...current.identity }); adapter.guard(expected); }
        catch(error){invalidate();throw error;} }
    }
    function identity() { check(); const observed = adapter.observe(); return observed; }
    function bind(value) {
      if (session) refusal('SESSION_ALREADY_BOUND');
      if (value.generation !== generation) refusal('DESTINATION_CHANGED');
      expected = identity();
      if (!['accountId', 'workspaceId', 'conversationId'].every(k => value[k] === expected.identity[k])) refusal('DESTINATION_CHANGED');
      session = new core.SessionGuard(value, clock); check();
      return { evidence: expected.evidence };
    }
    function requireBound() { check(); if (!session) refusal('SESSION_UNPAIRED'); }
    function capture() {
      requireBound(); const captured = adapter.captureTurns(expected), originalDraft = adapter.readDraft(expected);
      core.strictText(originalDraft); const pairs = [];
      for (let i = 0; i < captured.turns.length; i += 2) pairs.push({ userId: captured.turns[i].id, userText: captured.turns[i].text,
        assistantId: captured.turns[i+1].id, assistantText: captured.turns[i+1].text });
      check();capturedHistory=JSON.stringify(captured);
      return { document: { pairs, originalDraft, composerId: expected.composerId }, coverage: captured.coverage };
    }
    function stageCheck() {
      requireBound();
      if(capturedHistory===null || JSON.stringify(adapter.captureTurns(expected))!==capturedHistory){invalidate();refusal('HISTORY_CHANGED');}
    }
    function prepare(value) {
      requireBound(); if (offer || stage) refusal('REVIEW_CONSUMED');
      if (!value || typeof value.reviewId!=='string' || !/^[a-f0-9]{64}$/.test(value.digest) || !/^[a-f0-9]{64}$/.test(value.originalDraftSha256) || value.composerId !== expected.composerId || !Array.isArray(value.contents) || value.contents.length > 9 ||
        value.contents.filter(c => c.contentId === 'draft' && c.role === 'draft').length !== 1 ||
        new Set(value.contents.map(c => c.contentId)).size !== value.contents.length) refusal('REVIEW_INVALID');
      const assets = value.contents.filter(c => c.contentId !== 'draft');
      if (assets.some(a=>a.role!=='original' || !/^asset-[0-7]$/.test(a.contentId)) || assets.length > core.LIMITS.assets || assets.reduce((s,a)=>s+a.byteLength,0)>core.LIMITS.totalAssets) refusal('REVIEW_INVALID');
      offer = core.frozen(JSON.parse(JSON.stringify(value)));
      for (const c of offer.contents) transfers.set(c.contentId,new core.ChunkAssembly(c.byteLength,c.sha256,c.contentId==='draft'?core.LIMITS.draftBytes:core.LIMITS.assetBytes));
      return { prepared: true };
    }
    function chunk(value) {
      requireBound(); if (!offer || value.reviewId !== offer.reviewId || !transfers.has(value.contentId)) refusal('TRANSFER_INVALID');
      transfers.get(value.contentId).add(value.index,value.dataBase64); return { accepted: true };
    }
    async function commit() {
      requireBound(); if (!offer || stage) refusal('REVIEW_CONSUMED');
      const admitted = offer; offer = null; const content = new Map();
      try {
        for (const c of admitted.contents) { const data=await transfers.get(c.contentId).finish(check); check(); content.set(c.contentId,data); }
        const draft = new TextDecoder('utf-8',{fatal:true}).decode(content.get('draft'));
        const manifest = admitted.contents.find(c=>c.contentId==='draft');
        const assets = admitted.contents.filter(c=>c.contentId!=='draft').map(c=>({contentId:c.contentId,name:c.name,mediaType:c.mimeType,size:c.byteLength,sha256:c.sha256,bytes:content.get(c.contentId)}));
        stageCheck();stage = new core.ReviewedStage(adapter,expected,stageCheck,{reviewId:admitted.reviewId,originalDraftSha256:admitted.originalDraftSha256,draftSha256:manifest.sha256,
          assets:assets.map(({bytes,...metadata})=>metadata)});
        const result=await stage.stage(draft,assets);if(result.status==='staged')stagedGrant=admitted;return result;
      } finally { transfers.forEach(x=>x.clear());transfers.clear();content.forEach(x=>x.fill(0)); }
    }
    async function undo(value) {
      requireBound();
      if (!stage || !stagedGrant || value.reviewId!==stagedGrant.reviewId || value.composerId!==expected.composerId ||
        value.currentDraftSha256!==stagedGrant.contents.find(c=>c.contentId==='draft').sha256 ||
        value.originalDraft?.sha256!==stagedGrant.originalDraftSha256 || value.verifiedOriginalSha256!==stagedGrant.originalDraftSha256) refusal('UNDO_UNAVAILABLE');
      stagedGrant=null;return await stage.undo(value.reviewId);
    }
    return Object.freeze({ generation, characterize:()=>{check();return adapter.characterize();}, identity, bind, capture, prepare, chunk, commit, undo, invalidate, check });
  }
  root.BuddyBrowserContentController = Object.freeze({ createController });
  if (typeof module !== 'undefined') module.exports = root.BuddyBrowserContentController;
})(globalThis);
