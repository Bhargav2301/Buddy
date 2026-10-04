# Local Whisper recognition in Buddy 0.4.0

Buddy uses Whisper for recognition and keeps the existing Windows/Piper C/F3 speech output. In Voice settings, choose the microphone, recognition language and model. English `base.en` is the default for this PC; multilingual `small` is available for other languages. Windows speech remains an explicitly selected legacy option. Buddy never silently changes engines or languages when a model is missing.

Microphone capture starts only on an explicit voice/dictation action and is limited to 30 seconds. Audio stays in memory and anonymous local process pipes; it is neither saved nor uploaded. Finish releases capture and transcribes the recorded utterance. Stop, cancellation and shutdown discard the request and terminate Buddy's own Whisper worker. The worker has no shell, network listener, account access or action runner. Its model cache expires after 60 seconds idle. The user reviews Whisper voice requests before continuing because token probabilities are not calibrated confidence. Dictation never submits a host form.

Whisper can still mishear names, accents and background speech, and can hallucinate on non-speech. A conservative energy/variation gate rejects silence, steady hum and broadband hiss; native no-speech filtering and transcript review are additional checks, not guarantees. Retrying, choosing the correct mic/language, or editing the transcript remains necessary. Names of common supported apps seed decoding; Buddy does not rewrite the resulting transcript to pretend it heard the intended command.

## Runtime and model provenance

The MIT-licensed [Whisper.net 1.9.1 binding](https://github.com/sandrohanea/whisper.net/tree/98278acc38ae23590cdfa9859f78f089abae52a7) and `Whisper.net.Runtime` NuGet packages use CPU inference, with no CUDA/driver installation. The package repository commit is recorded in its NuGet manifest; no matching stable Git tag is assumed. Its native [whisper.cpp revision](https://github.com/ggml-org/whisper.cpp/tree/f24588a272ae8e23280d9c220536437164e6ed28) is MIT-licensed. [OpenAI Whisper code and weights are MIT-licensed](https://github.com/openai/whisper#license). Notices are under `docs/licenses/`.

Models download only after choosing **Download selected Whisper model**, or during this expressly approved local setup, from the maintainer's Hugging Face repository pinned at [5359861c739e955e79d9a303bcbc70fb988958b1](https://huggingface.co/ggerganov/whisper.cpp/tree/5359861c739e955e79d9a303bcbc70fb988958b1). Both exact byte size and SHA-256 must match before use. No access token is needed. Downloads contain weights only and cannot install code. No audio accompanies the download.

| Model | Download bytes | SHA-256 |
|---|---:|---|
| base.en | 147,964,211 | `a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002` |
| small.en Q5_1 | 190,098,681 | `bfdff4894dcb76bbf647d56263ea2a96645423f1669176f4844a1bf8e478ad30` |
| small multilingual Q5_1 | 190,085,487 | `ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb` |

Installed model storage: `%LOCALAPPDATA%\Buddy\SpeechModels\Whisper`. Preview storage is isolated under its `preview-data\SpeechModels\Whisper`. Models are outside Git and the portable app package; restore a missing model through Voice settings. The approved installed update provisions only base.en. User data deletion includes this model directory. Runtime DLLs are packaged separately via pinned NuGet dependencies.

## Measured smoke test on the connected PC

Core Ultra 9 185H, 16 GB RAM, CPU runtime. Twelve synthetic phrases total 93 reference words, spoken with the installed Microsoft David and Zira US-English voices at rates -2, 0 and +1. App names, Hyderabad/Bengaluru, pauses and a negative instruction were included. No Indian-English voice was installed; pace variation is not an Indian accent test. No microphone or user voice was recorded. Word counts lowercase and strip punctuation but count joined brands such as ChatGPT/DeepSeek literally, so formatting differences contribute errors.

| Model | Raw word errors / 93 | Median inference | Observed range including cold start | Stop at 100 ms returned after |
|---|---:|---:|---:|---:|
| base.en | 2 | 1.065 s | 0.955-2.306 s | 109 ms |
| small.en Q5_1 | 4 | 3.379 s | 3.224-4.358 s | 116 ms |
| small multilingual Q5_1 | 6 | 3.437 s | 3.299-4.174 s | 109 ms |

Base English was selected from these results, not an assumption that a larger model is always better. These are a small synthetic smoke benchmark, not a real-world accuracy estimate. Fifty-five checks cover inference, pinned models, synthetic silence/hiss/hum, worker cancellation and restart. Separate lifecycle fixtures exercise capture limits and late-result suppression. An earlier in-process native cancellation test failed its two-second bound; isolating the worker fixed the measured cancellation boundary. Physical mic accuracy and noisy-room performance remain user acceptance checks.
