/* Owned, offline HTML in a fresh headless browser. Never attach to a user profile. */
'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const crypto = require('node:crypto');
const {chromium} = require('playwright');
const sourcePath = process.env.BUDDY_TEST_PROBE_SOURCE || require.resolve('../probe.js');
const source = fs.readFileSync(sourcePath, 'utf8');
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
      assert.equal(result.reply.observation.editorCandidates,expected,name);
      assert.equal(Object.keys(result.reply.observation).length,7,name+': exact schema');
      assert.equal(result.privateReads,0,name+': no content read');
      assert.equal(result.inputEvents,0,name+': no user event');
      assert.equal(result.writes,0,name+': no DOM write');
      if(name==='new chat without recognized editor')assert.deepEqual(result.reply.observation,{
        editorCandidates:0,renderedRoleNodes:0,renderedUserCount:0,renderedAssistantCount:0,
        fileInputCandidates:2,streamingIndicatorPresent:false,stableConversationRoute:false});
      results.push({name,passed:true,editorCandidates:expected});
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
