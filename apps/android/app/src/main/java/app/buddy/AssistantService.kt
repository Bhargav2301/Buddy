package app.buddy

import android.app.assist.AssistStructure
import android.content.Context
import android.content.Intent
import android.graphics.Color
import android.os.Bundle
import android.service.voice.VoiceInteractionService
import android.service.voice.VoiceInteractionSession
import android.service.voice.VoiceInteractionSessionService
import android.text.InputType
import android.view.View
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView

object AssistContext {
    private var text: String? = null
    @Synchronized fun put(value: String) { text = value }
    @Synchronized fun take(): String? = text.also { text = null }
}
object PrivateFields {
    fun password(inputType: Int): Boolean {
        val type = inputType and InputType.TYPE_MASK_CLASS
        val variation = inputType and InputType.TYPE_MASK_VARIATION
        return type == InputType.TYPE_CLASS_TEXT && variation in listOf(InputType.TYPE_TEXT_VARIATION_PASSWORD, InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD, InputType.TYPE_TEXT_VARIATION_WEB_PASSWORD) || type == InputType.TYPE_CLASS_NUMBER && variation == InputType.TYPE_NUMBER_VARIATION_PASSWORD
    }
    fun blocked(packageName: String): Boolean = listOf("bank", "phonepe", "paytm", "net.one97", "nbu.paisa", "password", "bitwarden", "keepass", "authenticator", "lastpass").any { packageName.lowercase().contains(it) }
    fun redact(text: String): String = text.replace(Regex("(?i)\\b(?:\\d[ -]?){13,19}\\b|\\b[A-Z]{5}[0-9]{4}[A-Z]\\b|(?:password|otp|secret|api[_ -]?key)\\s*[:=]\\s*\\S+"), "[redacted]")
}
class BuddyVoiceService : VoiceInteractionService()
class BuddySessionService : VoiceInteractionSessionService() { override fun onNewSession(args: Bundle?): VoiceInteractionSession = BuddySession(this) }
class BuddySession(context: Context) : VoiceInteractionSession(context) {
    private var structure: AssistStructure? = null
    private var hint: TextView? = null
    override fun onHandleAssist(state: AssistState) { structure = state.assistStructure }
    override fun onCreateContentView(): View {
        return LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL; setPadding(32, 28, 32, 28); setBackgroundColor(Color.rgb(17, 24, 32))
            hint = TextView(context).apply { text = "Buddy\nHow can I help? Screen text is shared only if you choose it below."; textSize = 18f; setTextColor(Color.WHITE) }; addView(hint)
            addView(Button(context).apply { text = "Open Buddy"; setOnClickListener { open() } })
            addView(Button(context).apply { text = "Review screen text in Buddy"; setOnClickListener { read() } })
        }
    }
    private fun read() {
        val source = structure ?: run { hint?.text = "This app did not provide screen text. Open Buddy and type your question."; return }
        val packageName = source.activityComponent?.packageName.orEmpty()
        if (PrivateFields.blocked(packageName)) { hint?.text = "Buddy blocks screen context from sensitive apps."; return }
        val result = StringBuilder("App: $packageName\n")
        var visited = 0
        fun visit(node: AssistStructure.ViewNode, depth: Int) {
            if (visited++ >= 400 || depth > 18 || result.length > 18000 || node.isAssistBlocked || PrivateFields.password(node.inputType)) return
            node.text?.takeIf { it.isNotBlank() }?.let { result.appendLine(it) }
            node.contentDescription?.takeIf { it.isNotBlank() }?.let { result.appendLine(it) }
            for (i in 0 until node.childCount) visit(node.getChildAt(i), depth + 1)
        }
        for (i in 0 until source.windowNodeCount) visit(source.getWindowNodeAt(i).rootViewNode, 0)
        AssistContext.put(PrivateFields.redact(result.toString()).take(20000)); open()
    }
    private fun open() { startAssistantActivity(Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP)); hide() }
    override fun onHide() { structure = null; super.onHide() }
}
