# Focused staged-speech checks

Run locally from this checkout:

```powershell
dotnet run --project tests/Buddy.Staged.Tests -c Release -r win-x64 --self-contained true
```

The executable uses an injected HTTP handler, ephemeral encrypted state in a unique temporary directory, fake audio bytes and playback callbacks. It opens no audio devices and calls no actual model, web service or cloud provider. Current result: 89 checks passed. Windows compilation is separate; physical microphone/headphone and actual local-model first-audio latency remain untested.

The production option defaults false. Eligibility deliberately accepts a small neutral prompt grammar: greetings, simple jokes/poems, and specified explanations of art, language, math and nature topics. Unsupported prompts, saved memories, or retained sensitive/screen/web history use complete-answer review. This conservative gate and generated-text checks are not a semantic safety proof.

The first structured model call supplies a complete self-contained lead and every declared qualification verbatim, plus at most two optional topics. The maximum is three sentences of 500 characters each (1502 characters including spaces), with no truncation. Invalid or refused preflight falls back before any sentence is emitted. Invalid/refused/failed later generations stop without full-answer replay, completed event, or partial model-result persistence.

This is staged structured local calls, not token streaming. The real channel producer test holds a later model response until the first playback callback has begun. A fast later model can finish during slow first-sentence synthesis; first-audio latency is not guaranteed. The UI has a bounded one-item text channel, while playback holds the current buffer and at most one future render. One selected audio endpoint and the request cancellation owner span sentences and silent gaps.

The tests exercise cancellation and late source events without asserting real WASAPI/device behavior. The owned Windows source pins the endpoint once, cancels on render-default changes, rejects stale endpoint callbacks by cancellation-owner identity, and never reacquires or falls back during the utterance. Integrated/native and physical validation remain integration-owner tasks.
