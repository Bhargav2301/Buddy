package app.buddy

import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.channels.trySendBlocking
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.suspendCancellableCoroutine
import okhttp3.*
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.security.SecureRandom
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.concurrent.TimeUnit
import javax.net.ssl.SSLContext
import javax.net.ssl.X509TrustManager
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

class BuddyApi(private val connection: Connection) {
    companion object {
        private val cleanup = java.util.concurrent.Executors.newSingleThreadExecutor { task -> Thread(task, "BuddyConnectionCleanup").apply { isDaemon = true } }
    }
    init { require(connection.address.startsWith("https://")); require(connection.pin.matches(Regex("[0-9A-Fa-f]{64}"))) }
    private val trust = object : X509TrustManager {
        override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
        override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) = throw CertificateException("Client certificates are not accepted")
        override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {
            if (chain.isEmpty() || !fingerprint(chain[0].encoded).equals(connection.pin, true)) throw CertificateException("PC certificate changed. Pair again from the PC.")
            chain[0].checkValidity()
        }
    }
    private val tls = SSLContext.getInstance("TLS").apply { init(null, arrayOf(trust), SecureRandom()) }
    private val client = OkHttpClient.Builder().sslSocketFactory(tls.socketFactory, trust)
        .hostnameVerifier { _, session -> runCatching { fingerprint(session.peerCertificates[0].encoded).equals(connection.pin, true) }.getOrDefault(false) }
        .connectTimeout(8, TimeUnit.SECONDS).readTimeout(5, TimeUnit.MINUTES).callTimeout(6, TimeUnit.MINUTES)
        .followRedirects(false).followSslRedirects(false).build()
    private fun request(path: String, method: String, body: JSONObject? = null): Request {
        val builder = Request.Builder().url(connection.address + path).header("Authorization", "Bearer " + connection.token)
        return builder.method(method, if (method == "POST") (body ?: JSONObject()).toString().toRequestBody("application/json".toMediaType()) else null).build()
    }
    private fun error(code: Int, text: String): IOException {
        val message = runCatching { JSONObject(text).getJSONObject("error").getString("message") }.getOrNull()
        return IOException(message ?: when (code) { 401 -> "Pairing was revoked. Pair with your PC again."; 429 -> "Too many requests. Wait a minute and try again."; else -> "PC returned an error ($code)." })
    }
    suspend fun raw(path: String, method: String = "GET", body: JSONObject? = null): String = suspendCancellableCoroutine { continuation ->
        val call = client.newCall(request(path, method, body))
        continuation.invokeOnCancellation { cleanup.execute { call.cancel() } }
        call.enqueue(object : Callback {
            override fun onFailure(call: Call, e: IOException) { if (continuation.isActive) continuation.resumeWithException(e) }
            override fun onResponse(call: Call, response: Response) {
                try { response.use { val text = it.body?.string().orEmpty(); if (!it.isSuccessful) throw error(it.code, text); if (continuation.isActive) continuation.resume(text) } }
                catch (e: Exception) { if (continuation.isActive) continuation.resumeWithException(e) }
            }
        })
    }
    suspend fun json(path: String, method: String = "GET", body: JSONObject? = null) = JSONObject(raw(path, method, body))
    suspend fun array(path: String) = JSONArray(raw(path))
    fun chat(body: JSONObject): Flow<JSONObject> = callbackFlow {
        val call = client.newCall(request("/v1/chat", "POST", body))
        call.enqueue(object : Callback {
            override fun onFailure(call: Call, e: IOException) { close(e) }
            override fun onResponse(call: Call, response: Response) {
                try {
                    response.use {
                        if (!it.isSuccessful) throw error(it.code, it.body?.string().orEmpty())
                        var complete = false
                        val source = it.body?.source() ?: throw IOException("PC returned an empty answer.")
                        while (true) {
                            val line = source.readUtf8Line() ?: break
                            if (line.isBlank()) continue
                            val event = JSONObject(line)
                            if (event.optString("type") == "error") throw IOException(event.optString("text", "AI request failed."))
                            if (trySendBlocking(event).isFailure) break
                            if (event.optString("type") == "done") { complete = true; break }
                        }
                        if (!complete) throw IOException("The answer was interrupted. Your draft is available to retry.")
                    }
                    close()
                } catch (e: Exception) { close(e) }
            }
        })
        awaitClose { cleanup.execute { call.cancel() } }
    }
    fun close() { cleanup.execute { client.dispatcher.cancelAll(); client.connectionPool.evictAll(); client.dispatcher.executorService.shutdown() } }
}
