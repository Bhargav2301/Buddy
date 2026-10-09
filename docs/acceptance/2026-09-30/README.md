# Local model fixture results — 30 September 2026

These reports use synthetic prompts and images with disposable encrypted state. Their UTC timestamps fall on 29 September; the local test date was 30 September in Asia/Calcutta. They contain no captured user screen or conversation.

- `real-local-refine.json`: Gemma 3 4B with all-minilm 22M intent checks. Quick retained the exact original at 0.772 similarity; Guided and Council produced accepted rewrites at 0.970 and 0.978. The cancellation check dispatched no inference. The fixture persisted no conversation.
- `real-local-vision.json`: a bordered synthetic button was confirmed, and a matching phrase in document text was rejected before model inference because it lacked independent control-boundary evidence. Earlier probing exposed a false positive from Gemma alone; these two regression cases pass after adding the boundary requirement and constraining the returned reference.

These narrow fixtures do not establish 85% grounding accuracy, creative-app support, real microphone quality, or native stop latency. Flat/unlabeled controls can still abstain. The 150-case evaluation and real-app acceptance remain pending in `../../MVP-Implementation-Status.md`.

Reproduce with a local Ollama instance and the listed installed models:

```powershell
dotnet run --project tests/Buddy.Mvp.Tests -c Release -r win-x64 --self-contained true -- --real-local dist/real-local-refine.json
dotnet run --project tests/Buddy.Windows.IntegrationTests -c Release -- --real-vision dist/real-local-vision.json
```

Model results can vary. New runs should retain their timestamps and actual rejected/accepted outcomes.
