/* Isolated-world entry. Only the extension itself can call this listener. */
(function () {
  'use strict';
  if (window.top !== window) throw new Error('TOP_FRAME_REQUIRED');
  globalThis.BuddyBrowserInstalled?.close();
  const generation = crypto.randomUUID();
  const adapter = BuddyBrowserProviders.select(document,location);
  const controller = BuddyBrowserContentController.createController(adapter,location,generation);
  const allowed = new Set(['characterize','identity','bind','capture','prepare','chunk','commit','undo']);
  let busy = false;
  const listener = (message,sender,respond) => {
    if (sender.id !== chrome.runtime.id || message?.channel !== 'buddy-browser-v1') return false;
    if (message.op === 'invalidate') { controller.invalidate();respond({ok:true});return false; }
    if (!allowed.has(message.op) || busy) {respond({ok:false,code:'REQUEST_REFUSED'});return false;}
    busy=true;
    Promise.resolve().then(()=>controller[message.op](message.payload)).then(value=>respond({ok:true,value}),error=>respond({ok:false,code:BuddyBrowserCore.safeReason(error)})).finally(()=>{busy=false;});
    return true;
  };
  chrome.runtime.onMessage.addListener(listener);
  const invalidate = () => controller.invalidate();
  window.addEventListener('pagehide',invalidate);
  window.addEventListener('popstate',invalidate);
  window.addEventListener('hashchange',invalidate);
  const initialHref=location.href;
  const observer=new MutationObserver(()=>{ if(location.href!==initialHref) controller.invalidate(); });
  observer.observe(document.documentElement,{childList:true,subtree:true});
  const close=()=>{controller.invalidate();observer.disconnect();chrome.runtime.onMessage.removeListener(listener);
    window.removeEventListener('pagehide',invalidate);window.removeEventListener('popstate',invalidate);window.removeEventListener('hashchange',invalidate);};
  globalThis.BuddyBrowserInstalled=Object.freeze({close});
  return { generation, observation: controller.characterize() };
})();
