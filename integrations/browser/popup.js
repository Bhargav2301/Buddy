'use strict';
const status=document.getElementById('status');
let pending=0;
const refresh=()=>document.querySelectorAll('button').forEach(button=>button.disabled=pending>0 && button.dataset.op!=='disconnect');
const messages={ACCOUNT_IDENTITY_UNAVAILABLE:'The active ChatGPT account and workspace cannot yet be verified. No prompt, history or files were read or changed.',
  NATIVE_UNAVAILABLE:'The approved Buddy browser bridge is not available.',HOST_REFUSED:'Buddy refused this operation. Check its review window.',
  PROVIDER_UNSUPPORTED:'This preview supports only an explicitly selected ChatGPT tab.',SESSION_UNPAIRED:'Pair this tab with Buddy first.'};
for(const button of document.querySelectorAll('button[data-op]'))button.addEventListener('click',async()=>{
  pending++;refresh();
  try{
    const reply=await chrome.runtime.sendMessage({channel:'buddy-popup-v1',op:button.dataset.op});
    if(!reply?.ok){status.textContent=messages[reply?.code]??'The operation was refused. No message was sent.';return;}
    const value=reply.value;
    if(value.observation){status.textContent='Readiness only. Verified account: unavailable. Live draft, history and attachment capabilities are not admitted.'+(value.pairingChallenge?'\nMatch this pairing code in Buddy: '+value.pairingChallenge:'');}
    else if(value.status==='partial-or-unknown')status.textContent='Staging may be partial. Review the browser draft and attachments before continuing. Nothing was sent.';
    else status.textContent=({captured:'Verified completed turns captured for Buddy review.',staged:'Reviewed draft staged. Check it and its attachments in the browser. Nothing was sent.',undone:'The reviewed text change was undone. Attachments were not removed.',disconnected:'Disconnected.','waiting-for-review':'Approve an exact draft in Buddy first.','waiting-for-undo-review':'Request text Undo in Buddy first.'})[value.status]??'Operation completed without sending a message.';
  }catch{status.textContent='The browser bridge is unavailable. No message was sent.';}
  finally{pending--;refresh();}
});
