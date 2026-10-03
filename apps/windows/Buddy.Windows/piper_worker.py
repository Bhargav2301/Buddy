"""Private stdio-only Piper worker. Never plays audio, downloads or stores text/audio."""
import hashlib
import io
import json
import logging
import struct
import sys
import time
import wave
from pathlib import Path


def deny_network(event, args):
    if event.startswith("socket.") or event in ("subprocess.Popen", "os.system"):
        raise PermissionError("Network and subprocesses are disabled in the voice worker")


sys.addaudithook(deny_network)
logging.disable(logging.CRITICAL)
MAX_AUDIO = 12 * 1024 * 1024
ALLOWED_SPEAKERS = (95, 82, 60, 107, 90, 85)  # Model map: p226, p227, p232, p225, p228, p229.


class BoundedAudio(io.BytesIO):
    def write(self, data):
        if self.tell() + len(data) > MAX_AUDIO:
            raise ValueError("Audio limit")
        return super().write(data)


def emit(header, audio=None):
    payload = json.dumps(header, separators=(",", ":")).encode("ascii")
    sys.stdout.buffer.write(struct.pack("<I", len(payload)))
    sys.stdout.buffer.write(payload)
    if audio is not None:
        sys.stdout.buffer.write(audio.getbuffer())
    sys.stdout.buffer.flush()


try:
    # Anaconda's older app-local C++ DLLs can mask Windows' installed runtime.
    # Load the existing system copies only inside this worker; never replace files.
    import ctypes
    system_directory = ctypes.create_unicode_buffer(32768)
    if not ctypes.windll.kernel32.GetSystemDirectoryW(system_directory, len(system_directory)):
        raise OSError("Windows runtime unavailable")
    runtime_handles = [ctypes.WinDLL(str(Path(system_directory.value) / name), winmode=0x00000800)
                       for name in ("vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll")]
    import onnxruntime as ort
    worker_preset = sys.argv[2] if len(sys.argv) == 3 else ""
    if worker_preset not in ("", "f3"):
        raise ValueError("Invalid preset")
    if worker_preset == "f3":
        ort.set_seed(20261003)  # Same initial synthesis state as the approved F3 audition.
    from piper import PiperVoice, SynthesisConfig
    from piper.config import PiperConfig
    ort.disable_telemetry_events()
    root = Path(sys.argv[1]).resolve()
    model = root / "models" / "en_GB-vctk-medium.onnx"
    config = Path(str(model) + ".json")
    for path, expected in (
        (model, "4e9fc85ab9009385319fc6bae7f55577f8a2d7ee77fd9159a5500eb6531f41e6"),
        (config, "7f85e6391ed0f7f46e4abd19345929a16be931a0c9945086f96692dce2087fa8"),
    ):
        if hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise ValueError("Model integrity")
    options = ort.SessionOptions()
    options.intra_op_num_threads = 2
    options.inter_op_num_threads = 1
    options.log_severity_level = 3
    voice = PiperVoice(
        session=ort.InferenceSession(str(model), sess_options=options, providers=["CPUExecutionProvider"]),
        config=PiperConfig.from_dict(json.loads(config.read_text(encoding="utf-8"))),
        download_dir=root,
        use_tashkeel=False,
    )
    while True:
        line = sys.stdin.buffer.readline(16385)
        if not line:
            break
        if len(line) > 16384 or not line.endswith(b"\n"):
            emit({"error": "request_limit", "audioBytes": 0})
            break
        audio = None
        try:
            request = json.loads(line)
            text = request["text"]
            speaker = request["speaker"]
            rate = request["rate"]
            preset = request.get("preset", "")
            if not isinstance(text, str) or not 0 < len(text) <= 1600 or speaker not in ALLOWED_SPEAKERS or type(rate) is not int or not -3 <= rate <= 2 or preset != worker_preset or (preset == "f3" and speaker != 85):
                raise ValueError("Invalid request")
            audio = BoundedAudio()
            started = time.perf_counter()
            # F3 is fixed to the selected audition, independent of the general pace setting.
            synthesis = SynthesisConfig(speaker_id=speaker, length_scale=1.55, noise_scale=0.30, noise_w_scale=0.30, volume=0.90) if preset == "f3" else SynthesisConfig(speaker_id=speaker, length_scale=1.40 - rate * 0.15, noise_scale=0.333, noise_w_scale=0.333)
            sentence_pause = 0.20 if preset == "f3" else 0.18
            with wave.open(audio, "wb") as wav:
                wav.setframerate(22050)
                wav.setsampwidth(2)
                wav.setnchannels(1)
                first = True
                for chunk in voice.synthesize(text, syn_config=synthesis):
                    if not first:
                        wav.writeframes(b"\0" * (int(22050 * sentence_pause) * 2))
                    wav.writeframes(chunk.audio_int16_bytes)
                    first = False
            emit({"audioBytes": audio.getbuffer().nbytes, "synthesisMs": round((time.perf_counter() - started) * 1000, 2)}, audio)
        except Exception:
            emit({"error": "synthesis_failed", "audioBytes": 0})
        finally:
            if audio is not None:
                view = audio.getbuffer()
                view[:] = b"\0" * len(view)
                view.release()
                audio.close()
            line = b""
            request = None
            text = ""
except Exception:
    # Never echo exception text: it can contain private text or local paths.
    emit({"error": "worker_unavailable", "audioBytes": 0})
    sys.exit(1)
