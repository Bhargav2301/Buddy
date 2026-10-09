/* Owned, offline HTML in a fresh headless browser. Never attach to a user profile. */
'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const crypto = require('node:crypto');
const {chromium} = require('playwright');
const sourcePath = process.env.BUDDY_TEST_PROBE_SOURCE || require.resolve('../probe.js');
const source = fs.readFileSync(sourcePath, 'utf8');
const scope = body => `<main><div data-chatgpt-conversation-selection-target="true" data-thread-find-target="conversation">${body}</div></main>`;
const key = (role, turn=7, unit=0) => `fallback-turn-${turn}:${unit}:${role}`;
const modern = (role, turn=7, unit=0, body='owned text') => `<div data-chatgpt-search-unit-key="${key(role,turn,unit)}" data-content-search-unit-key="${key(role,turn,unit)}" data-chatgpt-search-message-ids="private-canary">${body}</div>`;
const childPair = (role, childKey=key(role), extra='') => `<div data-chatgpt-search-unit-key="${key(role)}" ${extra}><div data-content-search-unit-key="${childKey}">owned text</div></div>`;
const roles = (total,user,assistant) => ({renderedRoleNodes:total,renderedUserCount:user,renderedAssistantCount:assistant});
const cases = [
  ['new chat without recognized editor', '<form><input type="file"><input type="file"></form>', 0],
  ['legacy textarea', '<textarea id="prompt-textarea"></textarea>', 1],
  ['legacy rich editor', '<div id="prompt-textarea" contenteditable="true">owned draft</div>', 1],
  ['empty editable attribute', '<div id="prompt-textarea" contenteditable>owned draft</div>', 1],
  ['plaintext-only editable attribute', '<div id="prompt-textarea" contenteditable="plaintext-only">owned draft</div>', 1],
  ['case-insensitive HTML editable state', '<div id="prompt-textarea" contenteditable="TRUE">owned draft</div>', 1],
  ['changed editor id within a form', '<form><div id="different-editor" contenteditable>owned draft</div></form>', 1],
  ['anonymous textarea within a form', '<form><textarea>owned draft</textarea></form>', 1],
  ['accessible rich textbox outside a form', '<div role="textbox" contenteditable="plaintext-only">owned draft</div>', 1],
  ['accessible textarea outside a form', '<textarea role="textbox"></textarea>', 1],
  ['editable descendants count once', '<form><div contenteditable><div role="textbox"><p>owned draft</p></div></div></form>', 1],
  ['unrelated editable region excluded', '<div contenteditable>owned draft</div>', 0],
  ['search input excluded', '<form><input role="textbox" type="search"></form>', 0],
  ['noneditable textbox excluded', '<div role="textbox">owned draft</div>', 0],
  ['explicitly false editable state excluded', '<form><div id="prompt-textarea" contenteditable="false">owned draft</div></form>', 0],
  ['readonly textarea excluded', '<textarea id="prompt-textarea" readonly></textarea>', 0],
  ['disabled textarea excluded', '<textarea id="prompt-textarea" disabled></textarea>', 0],
  ['disabled fieldset excluded', '<fieldset disabled><textarea id="prompt-textarea"></textarea></fieldset>', 0],
  ['aria disabled ancestor excluded', '<form aria-disabled="true"><div contenteditable>owned draft</div></form>', 0],
  ['aria readonly excluded', '<div id="prompt-textarea" aria-readonly="true" contenteditable>owned draft</div>', 0],
  ['aria hidden ancestor excluded', '<form aria-hidden="true"><div contenteditable>owned draft</div></form>', 0],
  ['inert ancestor excluded', '<form inert><textarea></textarea></form>', 0],
  ['hidden ancestor excluded', '<form hidden><textarea></textarea></form>', 0],
  ['display none ancestor excluded', '<form style="display:none"><textarea></textarea></form>', 0],
  ['hidden visibility excluded', '<form style="visibility:hidden"><textarea></textarea></form>', 0],
  ['transparent editor excluded', '<form><textarea style="opacity:0"></textarea></form>', 0],
  ['ambiguous editors capped', '<form><textarea></textarea><textarea></textarea><textarea></textarea></form>', 2],
  ['hidden old editor plus new editor', '<textarea id="prompt-textarea" hidden></textarea><form><div contenteditable>owned draft</div></form>', 1]
];
cases.push(
  ['observed user child-pair and assistant self-pair', scope(childPair('user')+modern('assistant',7,1)), roles(2,1,1)],
  ['legacy roles remain supported', '<div data-message-author-role="user"></div><div data-message-author-role="assistant"></div>', roles(2,1,1)],
  ['nested same-key wrappers count once', scope(modern('user',7,0,modern('user'))), roles(1,1,0)],
  ['duplicate virtualized key counts once', scope(modern('assistant')+modern('assistant')), roles(1,0,1)],
  ['different turn keys count separately', scope(modern('user')+modern('user',8)), roles(2,2,0)],
  ['legacy marker around modern alias counts once', scope('<div data-message-author-role="user">'+modern('user')+'</div>'), roles(1,1,0)],
  ['modern wrapper around legacy alias counts once', scope(modern('assistant',7,0,'<div data-message-author-role="assistant"></div>')), roles(1,0,1)],
  ['same-node legacy and modern alias counts once', scope(childPair('user',key('user'),'data-message-author-role="user"')), roles(1,1,0)],
  ['same-node role conflict abstains', scope(childPair('user',key('user'),'data-message-author-role="assistant"')), roles(0,0,0)],
  ['nested role conflict abstains', scope(modern('user',7,0,'<div data-message-author-role="assistant"></div>')), roles(0,0,0)],
  ['nested different modern identity abstains', scope(modern('user',7,0,modern('user',8))), roles(0,0,0)],
  ['conflict taints duplicated identity', scope(modern('user')+modern('user',7,0,'<div data-message-author-role="assistant"></div>')), roles(0,0,0)],
  ['collision between alias groups abstains', scope(modern('user')+'<div data-message-author-role="user">'+modern('user')+'</div>'), roles(0,0,0)],
  ['unsupported ancestor does not mask valid unit', scope('<div data-chatgpt-search-unit-key="unknown">'+modern('user')+'</div>'), roles(1,1,0)],
  ['modern marker outside conversation excluded', modern('user'), roles(0,0,0)],
  ['modern marker in main without conversation excluded', '<main>'+modern('assistant')+'</main>', roles(0,0,0)],
  ['partial conversation root excluded', '<main><div data-thread-find-target="conversation">'+modern('user')+'</div></main>', roles(0,0,0)],
  ['conversation root outside main excluded', '<aside><div data-chatgpt-conversation-selection-target="true" data-thread-find-target="conversation">'+modern('user')+'</div></aside>', roles(0,0,0)],
  ['missing paired key excluded', scope(`<div data-chatgpt-search-unit-key="${key('user')}">owned text</div>`), roles(0,0,0)],
  ['mismatched paired key excluded', scope(childPair('user',key('assistant'))), roles(0,0,0)],
  ['unsupported key prefix excluded', scope(modern('user').replaceAll('fallback-turn-', 'unknown-turn-')), roles(0,0,0)],
  ['unsupported key role excluded', scope(modern('user').replaceAll(':user', ':tool')), roles(0,0,0)],
  ['overlong key excluded', scope(modern('user').replaceAll('fallback-turn-7:', 'fallback-turn-'+ '1'.repeat(129)+':')), roles(0,0,0)],
  ['heading and bubble alone do not establish role', scope('<h4 data-conversation-role="assistant">ChatGPT said:</h4><div data-user-message-bubble="true">owned text</div>'), roles(0,0,0)],
  ['missing virtualized turns never inferred', scope('<div data-turn-key="owned-placeholder" style="height:200px"></div>'+modern('assistant',42)), roles(1,0,1)],
  ['same key in separate scopes stays separate', scope(modern('user'))+scope(modern('user')), roles(2,2,0)],
  ['non-role legacy marker remains total only', '<div data-message-author-role="tool"></div>', roles(1,0,0)],
  ['message counts capped', scope(Array.from({length:520},(_,i)=>modern('assistant',i)).join('')), roles(513,0,513)]
);
(async () => {
  const browser = await chromium.launch({headless:true,
    ...(process.env.BUDDY_TEST_CHROME ? {executablePath:process.env.BUDDY_TEST_CHROME} : {}),
    args:['--disable-background-networking','--disable-component-update','--no-first-run']});
  try {
    const context = await browser.newContext({serviceWorkers:'block',offline:true});
    let requests = 0;
    await context.route('**/*', route => {
      requests++;
      // This is an owned intercepted fixture, not a request to the provider.
      if (route.request().url() === 'https://chatgpt.com/')
        return route.fulfill({status:200,contentType:'text/html',body:'<!doctype html><html><head><meta charset="utf-8"></head><body></body></html>'});
      return route.abort();
    });
    const page = await context.newPage();
    await page.goto('https://chatgpt.com/');
    await page.addScriptTag({content:source});
    const results = [];
    for (const [name, html, expected] of cases) {
      const result = await page.evaluate(async ({html}) => {
        document.body.innerHTML = html;
        const fixtureNodes = [...document.body.querySelectorAll('*')];
        let privateReads=0, inputEvents=0;
        for(const node of fixtureNodes) {
          const getAttribute = node.getAttribute.bind(node);
          node.getAttribute = name => {
            if (['data-chatgpt-search-message-ids','data-turn-key','aria-label','placeholder'].includes(name)) {
              privateReads++; throw Error('Private identifiers or labels must not be read');
            }
            return getAttribute(name);
          };
          for(const key of ['textContent','innerText','value','files','innerHTML'])
            Object.defineProperty(node,key,{get(){privateReads++;throw Error('Private content must not be read');}});
          for(const event of ['input','change','click','submit'])node.addEventListener(event,()=>inputEvents++);
        }
        const mutations = new MutationObserver(()=>{});
        mutations.observe(document.body,{attributes:true,childList:true,characterData:true,subtree:true});
        const reply = BuddyReadiness.inspectDocument(location.href,'owned-fixture',true);
        const writes = mutations.takeRecords().length;
        mutations.disconnect();
        return {reply,privateReads,inputEvents,writes};
      }, {html});
      assert.equal(result.reply.ok,true,name+': scan completed');
      if (typeof expected === 'number') assert.equal(result.reply.observation.editorCandidates,expected,name);
      else for (const [field,value] of Object.entries(expected)) assert.equal(result.reply.observation[field],value,name+': '+field);
      assert.equal(Object.keys(result.reply.observation).length,7,name+': exact schema');
      assert.equal(result.privateReads,0,name+': no content read');
      assert.equal(result.inputEvents,0,name+': no user event');
      assert.equal(result.writes,0,name+': no DOM write');
      assert.ok(!JSON.stringify(result.reply).includes('private-canary') && !JSON.stringify(result.reply).includes('fallback-turn'), name+': no identifiers returned');
      if(name==='new chat without recognized editor')assert.deepEqual(result.reply.observation,{
        editorCandidates:0,renderedRoleNodes:0,renderedUserCount:0,renderedAssistantCount:0,
        fileInputCandidates:2,streamingIndicatorPresent:false,stableConversationRoute:false});
      results.push({name,passed:true,expected});
    }
    // Exercise the real location parser without navigating or accessing a provider.
    for (const [name,path,expected] of [
      ['standard conversation route','/c/12345678-1234-4321-abcd-123456789abc',true],
      ['project conversation route','/g/g-p-0123456789abcdef-example-project/c/12345678-1234-4321-abcd-123456789abc',true],
      ['GPT conversation route','/g/g-Example123-example/c/12345678-1234-4321-abcd-123456789abc',true],
      ['project home is not a conversation','/g/g-p-example/project',false]
    ]) {
      const reply=await page.evaluate(path=>{
        history.replaceState(null,'',path);
        document.body.innerHTML='<article>Owned message without provider markers</article><form><textarea></textarea></form>';
        for(const node of document.body.querySelectorAll('*'))
          for(const key of ['textContent','innerText','value','files'])Object.defineProperty(node,key,{get(){throw Error('No content read');}});
        return BuddyReadiness.inspectDocument(location.href,'owned-fixture',true);
      },path);
      assert.equal(reply.ok,true,name);
      assert.equal(reply.observation.stableConversationRoute,expected,name);
      assert.equal(reply.observation.editorCandidates,1,name);
      assert.equal(reply.observation.renderedRoleNodes,0,name+': text is not a role marker');
      assert.equal(Object.keys(reply.observation).length,7,name);
      assert.ok(!JSON.stringify(reply).includes('12345678'),name+': no URL or IDs returned');
      results.push({name,passed:true,stableConversationRoute:expected});
    }
    assert.equal(requests,1,'Only the intercepted owned page was requested');
    console.log(JSON.stringify({scope:'Owned offline HTML; fresh headless context; no real provider/profile/extension',
      sourceSha256:crypto.createHash('sha256').update(source).digest('hex'),
      browser:browser.version(),cases:results.length,passed:results.length,interceptedRequests:requests,results},null,2));
    await context.close();
  } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
