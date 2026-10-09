'use strict';
const status = document.getElementById('status');
const check = document.getElementById('check');
const disconnect = document.getElementById('disconnect');
let generation = 0, busy = false, probe;
try { probe = BuddyReadiness.createProbe(chrome, window); }
catch { status.textContent = BuddyReadiness.diagnostic('R31'); check.disabled = true; }
check.addEventListener('click', async () => {
  if (!probe || busy) return;
  const current = ++generation;
  busy = true; check.disabled = true;
  status.textContent = 'Checking this selected tab. Keep the popup open.';
  try {
    const reply = await probe.run();
    if (current === generation) status.textContent = reply.ok ? BuddyReadiness.describe(reply.observation) : BuddyReadiness.diagnostic(reply.diagnostic);
  } catch { if (current === generation) status.textContent = BuddyReadiness.diagnostic('R00'); }
  finally { busy = false; check.disabled = false; }
});
disconnect.addEventListener('click', () => {
  ++generation;
  const stopped = probe?.stop();
  status.textContent = stopped?.readDispatched
    ? 'Disconnected. An already-dispatched structural read may finish; its result is discarded. No further work or message will be sent.'
    : 'Disconnected. No further check will be dispatched. No message was sent.';
});
window.addEventListener('pagehide', () => { ++generation; probe?.dispose(); });
