# Windows 0.4.1 speech-feedback correction

The 0.3.9 voice overlay could display a Windows recognition hypothesis such as Open Notepad, then receive an empty final result and show No speech heard while the tentative words remained visible. A displayed hypothesis was not an accepted transcript. The shared local recognizer now preserves accepted words, the last tentative candidate and any rejection state until a single completion; uncertain or recovered words require explicit review. Cancellation and stale sessions never recover a command.

Whisper also no longer combines an earlier accepted speech segment with the maximum no-speech score from a later silent segment. Its existing signal, segment confidence and silence gates remain active, as does mandatory voice transcript review. No thresholds were weakened. Local Piper C/F3, preferences, bounded action approvals and unsent drafts remain protected.

The focused native suite covers the actual WPF overlay using injected recognizer callbacks: hypothesis/recognized words followed by an empty result, rejected candidates, true silence, repeated completion, Stop and a new session receiving old callbacks. Synthetic Whisper inference and cancellation checks are separate from physical microphone acceptance. The reported screenshot predates the first 0.4.0 process; it is not evidence of a failed 0.4.0 Whisper session.
