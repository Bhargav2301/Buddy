package app.buddy

import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.test.platform.app.InstrumentationRegistry
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import javax.net.ssl.SSLException

class BuddyIntegrationTest {
    @get:Rule val ui = createAndroidComposeRule<MainActivity>()
    @Test fun pairAndReceiveRealLocalAnswer() {
        val link = InstrumentationRegistry.getArguments().getString("pairLink")
            ?: throw AssertionError("Run with -e pairLink <fresh PC pairing URL>")
        val info = PairLink.parse(link)
        // A wrong fingerprint must fail before pairing credentials are transmitted.
        var rejected = false
        val wrong = BuddyApi(info.connection.copy(pin = "00".repeat(32)))
        try { runBlocking { wrong.json("/health") } } catch (_: SSLException) { rejected = true } finally { wrong.close() }
        assertTrue("Reject a PC whose TLS certificate doesn't match the QR", rejected)

        ui.onNodeWithText("Or paste the PC pairing link").performTextInput(link)
        ui.onNodeWithText("Review connection").performScrollTo().performClick()
        ui.onNodeWithText("Pair", useUnmergedTree = true).performClick()
        ui.waitUntil(30000) { ui.onAllNodesWithText("Ask Buddy anything…").fetchSemanticsNodes().isNotEmpty() }
        ui.onNodeWithText("Ask Buddy anything…").performTextInput("Reply with the single word READY.")
        ui.onNodeWithContentDescription("Send message").assertIsDisplayed().performClick()
        ui.waitUntil(180000) { ui.onAllNodesWithText("READY", substring = true).fetchSemanticsNodes().size >= 2 }
        ui.waitUntil(180000) { ui.onAllNodesWithContentDescription("Stop answer").fetchSemanticsNodes().isEmpty() }
        ui.activityRule.scenario.onActivity { activity ->
            val keyboard = activity.getSystemService(android.content.Context.INPUT_METHOD_SERVICE) as android.view.inputmethod.InputMethodManager
            keyboard.hideSoftInputFromWindow(activity.window.decorView.windowToken, 0)
        }
        ui.waitForIdle()
        ui.onNodeWithText("History").performClick()
        ui.onNodeWithText("Reply with the single word READY.").assertIsDisplayed()
    }
    @Test fun passwordFieldsAreExcluded() {
        assertTrue(PrivateFields.password(android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD))
        assertTrue(PrivateFields.password(android.text.InputType.TYPE_CLASS_NUMBER or android.text.InputType.TYPE_NUMBER_VARIATION_PASSWORD))
        assertFalse(PrivateFields.password(android.text.InputType.TYPE_CLASS_TEXT))
        assertTrue(PrivateFields.blocked("com.phonepe.app"))
        assertEquals("[redacted]", PrivateFields.redact("otp: 123456"))
    }
}
