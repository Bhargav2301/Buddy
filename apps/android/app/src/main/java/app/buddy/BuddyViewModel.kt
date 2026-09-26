package app.buddy

import android.app.Application
import android.os.Build
import android.util.Base64
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID

data class ChatLine(val role: String, val text: String)
data class ChatSummary(val id: String, val title: String)
data class SavedNote(val id: String, val title: String, val text: String)
data class BuddyUi(
    val paired: Boolean = false, val address: String = "", val tab: Int = 0, val status: String = "Pair with your PC to begin",
    val ready: Boolean = false, val model: String = "", val draft: String = "", val mode: String = "type", val readAloud: Boolean = false,
    val busy: Boolean = false, val listening: Boolean = false, val lines: List<ChatLine> = emptyList(), val chats: List<ChatSummary> = emptyList(),
    val currentId: String? = null, val pairInfo: PairInfo? = null, val refined: String? = null, val originalDraft: String? = null,
    val context: String? = null, val image: ByteArray? = null, val prompts: List<SavedNote> = emptyList(), val memories: List<SavedNote> = emptyList(),
    val message: String? = null, val deleteId: String? = null
)

class BuddyViewModel(app: Application) : AndroidViewModel(app) {
    private val vault = ConnectionVault(app)
    private var connection = vault.load()
    private var api: BuddyApi? = connection?.let(::BuddyApi)
    private val mutable = MutableStateFlow(BuddyUi(paired = connection != null, address = connection?.address.orEmpty()))
    val state = mutable.asStateFlow()
    private var active: Job? = null
    private var isRefreshing = false
    var onSpeak: ((String) -> Unit)? = null
    init { refresh() }
    fun draft(value: String) { mutable.update { it.copy(draft = value.take(20000)) } }
    fun mode(value: String) { mutable.update { it.copy(mode = value) } }
    fun readAloud(value: Boolean) { mutable.update { it.copy(readAloud = value) } }
    fun listening(value: Boolean) { mutable.update { it.copy(listening = value) } }
    fun message(value: String?) { mutable.update { it.copy(message = value) } }
    fun tab(value: Int) { mutable.update { it.copy(tab = value) }; if (value == 2) notes(); if (value == 1) refresh() }
    fun pairLink(value: String) { try { mutable.update { it.copy(pairInfo = PairLink.parse(value)) } } catch (e: Exception) { message(e.message) } }
    fun dismissPair() { mutable.update { it.copy(pairInfo = null) } }
    fun connect() {
        val pair = mutable.value.pairInfo ?: return
        if (mutable.value.busy) return
        active = viewModelScope.launch {
            mutable.update { it.copy(busy = true) }
            val candidate = BuddyApi(pair.connection)
            try {
                val response = candidate.json("/v1/pair", "POST", JSONObject().put("code", pair.code).put("name", Build.MANUFACTURER + " " + Build.MODEL))
                val linked = pair.connection.copy(token = response.getString("token")); vault.save(linked); api?.close(); connection = linked; api = BuddyApi(linked)
                mutable.value = BuddyUi(paired = true, address = linked.address, status = "Paired. Checking local AI…")
            } catch (e: Exception) { if (e !is CancellationException) message(friendly(e)) }
            finally { candidate.close(); mutable.update { it.copy(busy = false) } }
            refresh()
        }
    }
    fun disconnect() { active?.cancel(); api?.close(); api = null; connection = null; vault.clear(); mutable.value.image?.fill(0); mutable.value = BuddyUi() }
    private fun friendly(e: Exception): String = when (e) {
        is java.net.SocketTimeoutException, is java.net.ConnectException -> "PC is unreachable. Keep Buddy running and connect both devices to the same Wi-Fi. Check the PC firewall."
        is javax.net.ssl.SSLException -> "The PC certificate could not be verified. Scan a fresh code directly from your PC."
        else -> e.message ?: "Could not contact Buddy on your PC."
    }
    fun refresh() {
        val client = api ?: return
        if (isRefreshing || mutable.value.busy) return
        isRefreshing = true
        viewModelScope.launch {
            try {
                val health = client.json("/v1/status")
                val list = summaries(client.array("/v1/conversations"))
                if (client !== api || mutable.value.busy) return@launch
                mutable.update { it.copy(status = health.getString("message"), ready = health.getBoolean("ready"), model = health.getString("model"), chats = list) }
                val id = mutable.value.currentId
                if (id != null && list.any { it.id == id }) load(id, client)
                else if (id != null) mutable.update { it.copy(currentId = null, lines = emptyList()) }
            } catch (e: Exception) { if (client === api && e !is CancellationException) mutable.update { it.copy(ready = false, status = friendly(e)) } }
            finally { isRefreshing = false }
        }
    }
    private fun summaries(array: JSONArray) = (0 until array.length()).map { array.getJSONObject(it).let { j -> ChatSummary(j.getString("id"), j.getString("title")) } }
    private suspend fun load(id: String, client: BuddyApi) {
        val data = client.json("/v1/conversations/$id"); val array = data.getJSONArray("messages")
        val lines = (0 until array.length()).map { array.getJSONObject(it).let { j -> ChatLine(j.getString("role"), j.getString("text")) } }
        if (client === api && !mutable.value.busy && mutable.value.currentId == id) mutable.update { it.copy(lines = lines) }
    }
    fun select(id: String) { if (mutable.value.busy) return; clearContext(); mutable.update { it.copy(currentId = id, lines = emptyList(), tab = 0) }; viewModelScope.launch { try { api?.let { load(id, it) } } catch (e: Exception) { message(friendly(e)) } } }
    fun newChat() { if (mutable.value.busy) return; clearContext(); mutable.update { it.copy(currentId = null, lines = emptyList(), tab = 0, draft = "") } }
    fun clearContext() { mutable.value.image?.fill(0); mutable.update { it.copy(context = null, image = null) } }
    fun context(text: String) { mutable.update { it.copy(context = text.take(20000), tab = 0) } }
    fun image(bytes: ByteArray) { mutable.value.image?.fill(0); mutable.update { it.copy(image = bytes, tab = 0) } }
    fun send() {
        val client = api ?: return message("Pair Buddy with your PC first.")
        val previous = mutable.value; val draft = previous.draft.trim()
        if (draft.isEmpty() || previous.busy) return
        active = viewModelScope.launch {
            mutable.update { it.copy(busy = true, draft = "", lines = it.lines + ChatLine("user", draft) + ChatLine("assistant", ""), status = "Connecting to local AI…") }
            var success = false
            try {
                val id = previous.currentId ?: client.json("/v1/conversations", "POST", JSONObject().put("title", "New conversation").put("text", "")).getString("id")
                mutable.update { it.copy(currentId = id) }
                val body = JSONObject().put("conversationId", id).put("text", draft).put("requestId", UUID.randomUUID().toString()).put("mode", previous.mode)
                previous.context?.let { body.put("context", it) }; previous.image?.let { body.put("imageBase64", Base64.encodeToString(it, Base64.NO_WRAP)) }
                client.chat(body).collect { event ->
                    when (event.getString("type")) {
                        "status" -> mutable.update { it.copy(status = event.getString("text")) }
                        "delta" -> mutable.update { it.copy(lines = it.lines.dropLast(1) + ChatLine("assistant", it.lines.last().text + event.getString("text"))) }
                        "done" -> success = true
                    }
                }
                if (!success) error("The PC did not confirm the answer.")
                clearContext(); mutable.update { it.copy(status = "Answered locally on your PC") }
                if (previous.readAloud || previous.mode == "voice") onSpeak?.invoke(mutable.value.lines.last().text)
            } catch (e: Exception) {
                mutable.update { it.copy(draft = draft, lines = previous.lines, status = if (e is CancellationException) "Stopped. Your draft is ready to retry." else friendly(e)) }
            } finally { mutable.update { it.copy(busy = false) }; refresh() }
        }
    }
    fun stop() { active?.cancel(); listening(false) }
    fun refine() {
        val client = api ?: return message("Pair with your PC first.")
        val original = mutable.value.draft
        if (original.isBlank() || mutable.value.busy) return
        active = viewModelScope.launch {
            mutable.update { it.copy(busy = true, status = "Refining with local AI…") }
            try { val result = client.json("/v1/refine", "POST", JSONObject().put("prompt", original)); mutable.update { it.copy(refined = result.getString("refinedPrompt"), originalDraft = original) } }
            catch (e: Exception) { if (e !is CancellationException) message(friendly(e)) }
            finally { mutable.update { it.copy(busy = false) } }
        }
    }
    fun applyRefine() { mutable.update { it.copy(draft = it.refined ?: it.draft, refined = null, status = "Refined draft applied — review and send when ready") } }
    fun dismissRefine() { mutable.update { it.copy(refined = null) } }
    fun undoRefine() { mutable.update { it.copy(draft = it.originalDraft ?: it.draft, originalDraft = null) } }
    fun requestDelete(id: String?) { mutable.update { it.copy(deleteId = id) } }
    fun delete() {
        val id = mutable.value.deleteId ?: return; val client = api ?: return
        if (mutable.value.busy) return
        viewModelScope.launch { try { client.raw("/v1/conversations/$id", "DELETE"); mutable.update { it.copy(deleteId = null) }; if (mutable.value.currentId == id) newChat(); refresh() } catch (e: Exception) { message(friendly(e)) } }
    }
    fun notes() {
        val client = api ?: return
        viewModelScope.launch {
            fun parse(a: JSONArray) = (0 until a.length()).map { a.getJSONObject(it).let { j -> SavedNote(j.getString("id"), j.getString("title"), j.getString("text")) } }
            try { val prompts = parse(client.array("/v1/prompts")); val memories = parse(client.array("/v1/memories")); mutable.update { it.copy(prompts = prompts, memories = memories) } }
            catch (e: Exception) { message(friendly(e)) }
        }
    }
    fun saveNote(memory: Boolean, title: String, text: String) {
        val client = api ?: return
        viewModelScope.launch { try { client.json("/v1/" + if (memory) "memories" else "prompts", "POST", JSONObject().put("title", title).put("text", text)); notes(); message("Saved on your PC.") } catch (e: Exception) { message(friendly(e)) } }
    }
    fun deleteNote(memory: Boolean, id: String) { val client = api ?: return; viewModelScope.launch { try { client.raw("/v1/${if (memory) "memories" else "prompts"}/$id", "DELETE"); notes() } catch (e: Exception) { message(friendly(e)) } } }
    override fun onCleared() { api?.close(); mutable.value.image?.fill(0); super.onCleared() }
}
