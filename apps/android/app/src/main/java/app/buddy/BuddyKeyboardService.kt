package app.buddy

import android.graphics.Color
import android.inputmethodservice.InputMethodService
import android.view.View
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.ExtractedTextRequest
import android.view.inputmethod.InputConnection
import android.view.inputmethod.InputMethodManager
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import kotlinx.coroutines.*
import org.json.JSONObject

/** Small in-house keyboard. No keystroke collection. Refinement is explicitly requested and reviewed. */
class BuddyKeyboardService : InputMethodService() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private var job: Job? = null
    private var api: BuddyApi? = null
    private var version = 0
    private var caps = false
    private var symbols = false
    private lateinit var root: LinearLayout
    private lateinit var notice: TextView
    private var apply: (() -> Unit)? = null
    private var undo: (() -> Unit)? = null
    override fun onCreateInputView(): View {
        root = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(4, 6, 4, 12); setBackgroundColor(Color.rgb(17, 24, 32)) }
        render(); return root
    }
    private fun render() {
        root.removeAllViews(); notice = TextView(this).apply { text = "Buddy · Select text, then Refine"; setTextColor(Color.rgb(142, 228, 197)); setPadding(16, 8, 16, 8); maxLines = 5 }; root.addView(notice)
        row(listOf("Refine", "Apply", "Undo", "🌐")) { label -> when (label) { "Refine" -> refine(); "Apply" -> apply?.invoke(); "Undo" -> undo?.invoke(); else -> getSystemService(InputMethodManager::class.java).showInputMethodPicker() } }
        val rows = if (symbols) listOf("1234567890", "@#₹%&*()-", "!?.,:;/\"") else listOf("qwertyuiop", "asdfghjkl", "zxcvbnm")
        for (letters in rows) row(letters.map { if (caps) it.uppercase() else it.toString() }) { currentInputConnection?.commitText(it, 1) }
        row(listOf("↑", if (symbols) "ABC" else "123", "space", "⌫", "↵")) { key -> when (key) {
            "↑" -> { caps = !caps; render() }; "ABC", "123" -> { symbols = !symbols; render() }; "space" -> currentInputConnection?.commitText(" ", 1)
            "⌫" -> { val ic = currentInputConnection; if (!ic?.getSelectedText(0).isNullOrEmpty()) ic?.commitText("", 1) else ic?.deleteSurroundingTextInCodePoints(1, 0) }
            "↵" -> currentInputConnection?.commitText("\n", 1)
        } }
    }
    private fun row(labels: List<String>, action: (String) -> Unit) {
        val row = LinearLayout(this)
        labels.forEach { label -> row.addView(Button(this).apply { text = label; isAllCaps = false; textSize = 15f; minWidth = 0; minimumWidth = 0; setPadding(0, 0, 0, 0); setOnClickListener { action(label) } }, LinearLayout.LayoutParams(0, (48 * resources.displayMetrics.density).toInt(), if (label == "space") 3f else 1f)) }
        root.addView(row)
    }
    override fun onStartInput(attribute: EditorInfo?, restarting: Boolean) { super.onStartInput(attribute, restarting); version++; job?.cancel(); api?.close(); apply = null; undo = null }
    override fun onFinishInput() { version++; job?.cancel(); api?.close(); apply = null; undo = null; super.onFinishInput() }
    private fun refine() {
        val info = currentInputEditorInfo ?: return; val ic = currentInputConnection ?: return
        if (PrivateFields.password(info.inputType) || PrivateFields.blocked(info.packageName.orEmpty())) { notice.text = "Refinement is disabled in this field."; return }
        val selected = ic.getSelectedText(0)?.toString().orEmpty()
        if (selected.isBlank() || selected.length > 2000) { notice.text = "Select up to 2,000 characters in your app, then tap Refine."; return }
        val connection = ConnectionVault(this).load() ?: run { notice.text = "Pair your PC in Buddy first."; return }
        if (job?.isActive == true) return
        val generation = version; val fieldId = info.fieldId; val packageName = info.packageName; apply = null; undo = null
        api = BuddyApi(connection)
        job = scope.launch {
            notice.text = "Refining the selection on your PC…"
            try {
                val revised = api!!.json("/v1/refine", "POST", JSONObject().put("prompt", selected)).getString("refinedPrompt")
                if (generation != version) return@launch
                notice.text = "Review: $revised\nTap Apply to replace the selection. Nothing will be sent."
                apply = action@{
                    if (generation != version || currentInputEditorInfo?.fieldId != fieldId || currentInputEditorInfo?.packageName != packageName || ic.getSelectedText(0)?.toString() != selected) { notice.text = "The field changed. Select text and refine again."; apply = null; return@action }
                    val start = ic.getExtractedText(ExtractedTextRequest(), 0)?.let { minOf(it.selectionStart, it.selectionEnd) + it.startOffset }
                    ic.beginBatchEdit(); ic.commitText(revised, 1); ic.endBatchEdit(); notice.text = "Applied. Review in your app before sending."; apply = null
                    val expires = android.os.SystemClock.elapsedRealtime() + 30000
                    undo = reverse@{
                        if (version != generation || android.os.SystemClock.elapsedRealtime() > expires || start == null) { notice.text = "Undo expired. Use your app’s undo."; return@reverse }
                        val extracted = ic.getExtractedText(ExtractedTextRequest(), 0) ?: return@reverse
                        val relative = start - extracted.startOffset
                        if (relative < 0 || relative + revised.length > extracted.text.length || extracted.text.subSequence(relative, relative + revised.length).toString() != revised) { notice.text = "Text changed. Use your app’s undo."; return@reverse }
                        ic.beginBatchEdit(); ic.setSelection(start, start + revised.length); ic.commitText(selected, 1); ic.endBatchEdit(); undo = null; notice.text = "Original selection restored."
                    }
                }
            } catch (e: Exception) { if (e !is CancellationException) notice.text = "Could not refine. Keep Buddy running on your PC." }
            finally { api?.close() }
        }
    }
    override fun onDestroy() { scope.cancel(); api?.close(); super.onDestroy() }
}
