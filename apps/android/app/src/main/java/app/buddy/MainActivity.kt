package app.buddy

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.ImageDecoder
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.speech.RecognitionListener
import android.speech.RecognizerIntent
import android.speech.SpeechRecognizer
import android.speech.tts.TextToSpeech
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.SystemBarStyle
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.ByteArrayOutputStream
import java.util.Locale

class MainActivity : ComponentActivity(), TextToSpeech.OnInitListener {
    private val model: BuddyViewModel by viewModels()
    private var recognizer: SpeechRecognizer? = null
    private var tts: TextToSpeech? = null
    private var ttsReady = false
    private val scan = registerForActivityResult(ScanContract()) { result -> result.contents?.let(model::pairLink) }
    private val microphone = registerForActivityResult(ActivityResultContracts.RequestPermission()) { allowed -> if (allowed) dictate() else model.message("Microphone permission was declined. You can keep typing.") }
    private val notifications = registerForActivityResult(ActivityResultContracts.RequestPermission()) { startBubble() }
    private val picker = registerForActivityResult(ActivityResultContracts.PickVisualMedia()) { uri -> uri?.let(::attachImage) }
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge(statusBarStyle = SystemBarStyle.dark(android.graphics.Color.TRANSPARENT), navigationBarStyle = SystemBarStyle.dark(android.graphics.Color.rgb(17, 24, 32)))
        tts = TextToSpeech(this, this)
        model.onSpeak = { text ->
            if (ttsReady) tts?.speak(text.take(3900), TextToSpeech.QUEUE_FLUSH, null, "buddy")
            else model.message("No offline voice is installed. Add a voice in Android text-to-speech settings.")
        }
        setContent { BuddyApp(model, onScan = { scan.launch(ScanOptions().setDesiredBarcodeFormats(ScanOptions.QR_CODE).setPrompt("Scan the code in Buddy on your PC").setBeepEnabled(false).setOrientationLocked(false)) },
            onDictate = ::dictate, onStop = ::stop, onImage = { picker.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly)) },
            onBubble = ::enableBubble, onHideBubble = { stopService(Intent(this, BuddyBubbleService::class.java)) },
            onAssistantSettings = { startActivity(Intent(Settings.ACTION_VOICE_INPUT_SETTINGS)) },
            onKeyboardSettings = { startActivity(Intent(Settings.ACTION_INPUT_METHOD_SETTINGS)) }, onShare = ::shareChat)
        }
        if (savedInstanceState == null) receive(intent)
    }
    override fun onNewIntent(intent: Intent) { super.onNewIntent(intent); setIntent(intent); receive(intent) }
    private fun receive(intent: Intent) {
        if (intent.action == Intent.ACTION_VIEW) intent.data?.toString()?.let(model::pairLink)
        if (intent.action == Intent.ACTION_SEND) {
            if (intent.type?.startsWith("image/") == true) {
                val uri = if (Build.VERSION.SDK_INT >= 33) intent.getParcelableExtra(Intent.EXTRA_STREAM, Uri::class.java) else @Suppress("DEPRECATION") intent.getParcelableExtra(Intent.EXTRA_STREAM)
                uri?.let(::attachImage)
            } else intent.getStringExtra(Intent.EXTRA_TEXT)?.let(model::draft)
        }
        AssistContext.take()?.let(model::context)
    }
    private fun attachImage(uri: Uri) {
        if (uri.scheme != "content") { model.message("Choose an image from the Android picker or share sheet."); return }
        lifecycleScope.launch {
            try {
                val bytes = withContext(Dispatchers.IO) {
                    val bitmap = ImageDecoder.decodeBitmap(ImageDecoder.createSource(contentResolver, uri)) { decoder, info, _ ->
                        val scale = minOf(1f, 1280f / maxOf(info.size.width, info.size.height)); decoder.setTargetSize(maxOf(1, (info.size.width * scale).toInt()), maxOf(1, (info.size.height * scale).toInt())); decoder.allocator = ImageDecoder.ALLOCATOR_SOFTWARE
                    }
                    val out = ByteArrayOutputStream(); bitmap.compress(android.graphics.Bitmap.CompressFormat.JPEG, 80, out); bitmap.recycle(); out.toByteArray().also { require(it.size <= 2_000_000) { "Choose a smaller image." } }
                }
                model.image(bytes); model.message("Image attached. Review it before sending. Install the optional vision model on the PC if needed.")
            } catch (_: Exception) { model.message("This image could not be opened. Try another image.") }
        }
    }
    private fun dictate() {
        if (model.state.value.busy) return
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) { microphone.launch(Manifest.permission.RECORD_AUDIO); return }
        if (Build.VERSION.SDK_INT < 31 || !SpeechRecognizer.isOnDeviceRecognitionAvailable(this)) { model.message("On-device dictation is unavailable on this phone. You can type or use your keyboard’s own microphone."); return }
        tts?.stop(); recognizer?.destroy(); recognizer = SpeechRecognizer.createOnDeviceSpeechRecognizer(this)
        recognizer?.setRecognitionListener(object : RecognitionListener {
            override fun onReadyForSpeech(params: Bundle?) { model.listening(true) }
            override fun onBeginningOfSpeech() {}
            override fun onRmsChanged(rmsdB: Float) {}
            override fun onBufferReceived(buffer: ByteArray?) {}
            override fun onEndOfSpeech() { model.listening(false) }
            override fun onError(error: Int) { model.listening(false); model.message("Speech was not recognized ($error). Check your offline speech language and try again.") }
            override fun onResults(results: Bundle?) { model.listening(false); results?.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)?.firstOrNull()?.let { model.draft(it); if (model.state.value.mode == "voice") model.send() } }
            override fun onPartialResults(partialResults: Bundle?) { partialResults?.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)?.firstOrNull()?.let(model::draft) }
            override fun onEvent(eventType: Int, params: Bundle?) {}
        })
        recognizer?.startListening(Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM).putExtra(RecognizerIntent.EXTRA_PARTIAL_RESULTS, true).putExtra(RecognizerIntent.EXTRA_PREFER_OFFLINE, true).putExtra(RecognizerIntent.EXTRA_LANGUAGE, Locale.getDefault().toLanguageTag()))
        model.listening(true)
    }
    private fun stop() { model.stop(); recognizer?.cancel(); tts?.stop() }
    private fun enableBubble() {
        if (!Settings.canDrawOverlays(this)) { startActivity(Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:$packageName"))); model.message("Enable display over other apps, return here, then tap Show bubble again."); return }
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) notifications.launch(Manifest.permission.POST_NOTIFICATIONS) else startBubble()
    }
    private fun startBubble() { if (Settings.canDrawOverlays(this)) ContextCompat.startForegroundService(this, Intent(this, BuddyBubbleService::class.java)) }
    private fun shareChat() {
        val text = model.state.value.lines.joinToString("\n\n") { "${if (it.role == "user") "You" else "Buddy"}: ${it.text}" }
        if (text.isNotBlank()) startActivity(Intent.createChooser(Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT, text), "Export Buddy conversation"))
    }
    override fun onInit(status: Int) {
        if (status == TextToSpeech.SUCCESS) {
            val voice = tts?.voices?.firstOrNull { !it.isNetworkConnectionRequired && it.locale.language == Locale.getDefault().language }
                ?: tts?.voices?.firstOrNull { !it.isNetworkConnectionRequired && it.locale.language == "en" }
            if (voice != null) { tts?.voice = voice; ttsReady = true }
        }
    }
    override fun onStop() { recognizer?.cancel(); model.listening(false); super.onStop() }
    override fun onDestroy() { recognizer?.destroy(); tts?.shutdown(); model.onSpeak = null; super.onDestroy() }
}
