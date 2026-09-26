package app.buddy

import android.content.Context
import android.net.Uri
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import okhttp3.HttpUrl.Companion.toHttpUrl
import org.json.JSONObject
import java.security.KeyStore
import java.security.MessageDigest
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

data class Connection(val address: String, val pin: String, val token: String = "")
data class PairInfo(val connection: Connection, val code: String)

object PairLink {
    fun parse(text: String): PairInfo {
        val uri = Uri.parse(text.trim())
        require(uri.scheme == "buddy" && uri.host == "pair") { "Paste the Buddy pairing link shown on your PC." }
        val host = uri.getQueryParameter("host") ?: error("PC address is missing.")
        require(host.matches(Regex("[A-Za-z0-9.-]+"))) { "Invalid PC address." }
        val port = uri.getQueryParameter("port")?.toIntOrNull() ?: error("Port is missing.")
        require(port in 1024..65535) { "Invalid PC port." }
        val pin = uri.getQueryParameter("pin")?.uppercase() ?: error("Certificate fingerprint is missing.")
        require(pin.matches(Regex("[0-9A-F]{64}"))) { "Invalid certificate fingerprint." }
        val code = uri.getQueryParameter("code") ?: error("Pairing code is missing.")
        require(code.matches(Regex("[0-9]{6}"))) { "Invalid pairing code." }
        val address = "https://$host:$port".toHttpUrl().toString().trimEnd('/')
        return PairInfo(Connection(address, pin), code)
    }
}

class ConnectionVault(context: Context) {
    private val prefs = context.getSharedPreferences("buddy.connection", Context.MODE_PRIVATE)
    private fun key(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (store.getKey("buddy.connection.v1", null) as? SecretKey)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").apply {
            init(KeyGenParameterSpec.Builder("buddy.connection.v1", KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build())
        }.generateKey()
    }
    fun save(connection: Connection) {
        val plain = JSONObject().put("address", connection.address).put("pin", connection.pin).put("token", connection.token).toString().toByteArray()
        val cipher = Cipher.getInstance("AES/GCM/NoPadding").apply { init(Cipher.ENCRYPT_MODE, key()) }
        val bytes = cipher.iv + cipher.doFinal(plain)
        check(prefs.edit().putString("encrypted", Base64.encodeToString(bytes, Base64.NO_WRAP)).commit()) { "Could not save pairing securely." }
        plain.fill(0)
    }
    fun load(): Connection? {
        val encoded = prefs.getString("encrypted", null) ?: return null
        return try {
            val bytes = Base64.decode(encoded, Base64.NO_WRAP)
            val cipher = Cipher.getInstance("AES/GCM/NoPadding").apply { init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, bytes.copyOfRange(0, 12))) }
            val plain = cipher.doFinal(bytes.copyOfRange(12, bytes.size))
            val json = JSONObject(String(plain)); plain.fill(0)
            Connection(json.getString("address"), json.getString("pin"), json.getString("token"))
        } catch (_: Exception) { null }
    }
    fun clear() { prefs.edit().clear().commit() }
}

fun fingerprint(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02X".format(it) }
