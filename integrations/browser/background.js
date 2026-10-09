import './core.js';
import './bridge.js';
const bridge=globalThis.BuddyBrowserBridge.createBridge(chrome);
chrome.runtime.onMessage.addListener((message,sender,respond)=>{
  if(sender.id!==chrome.runtime.id || sender.tab || sender.url!==chrome.runtime.getURL('popup.html') || message?.channel!=='buddy-popup-v1')return false;
  bridge.run(message.op).then(value=>respond({ok:true,value}),error=>respond({ok:false,code:BuddyBrowserCore.safeReason(error)}));return true;
});
