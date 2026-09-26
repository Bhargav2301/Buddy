package app.buddy

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.speech.RecognitionListener
import android.speech.RecognitionService
import android.speech.SpeechRecognizer

/** System assistant contract, delegating only to Android's on-device recognizer. */
class BuddyRecognitionService : RecognitionService() {
    private var recognizer: SpeechRecognizer? = null
    override fun onStartListening(recognizerIntent: Intent, listener: Callback) {
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) { listener.error(SpeechRecognizer.ERROR_INSUFFICIENT_PERMISSIONS); return }
        if (Build.VERSION.SDK_INT < 31 || !SpeechRecognizer.isOnDeviceRecognitionAvailable(this)) { listener.error(SpeechRecognizer.ERROR_CLIENT); return }
        recognizer?.destroy()
        recognizer = SpeechRecognizer.createOnDeviceSpeechRecognizer(this).apply {
            setRecognitionListener(object : RecognitionListener {
                override fun onReadyForSpeech(params: Bundle?) { runCatching { listener.readyForSpeech(params ?: Bundle()) } }
                override fun onBeginningOfSpeech() { runCatching { listener.beginningOfSpeech() } }
                override fun onRmsChanged(rmsdB: Float) { runCatching { listener.rmsChanged(rmsdB) } }
                override fun onBufferReceived(buffer: ByteArray?) { buffer?.let { runCatching { listener.bufferReceived(it) } } }
                override fun onEndOfSpeech() { runCatching { listener.endOfSpeech() } }
                override fun onError(error: Int) { runCatching { listener.error(error) } }
                override fun onResults(results: Bundle?) { runCatching { listener.results(results ?: Bundle()) } }
                override fun onPartialResults(partialResults: Bundle?) { runCatching { listener.partialResults(partialResults ?: Bundle()) } }
                override fun onEvent(eventType: Int, params: Bundle?) { }
            })
            startListening(recognizerIntent)
        }
    }
    override fun onStopListening(listener: Callback) { recognizer?.stopListening() }
    override fun onCancel(listener: Callback) { recognizer?.cancel(); recognizer?.destroy(); recognizer = null }
    override fun onDestroy() { recognizer?.destroy(); super.onDestroy() }
}
