package app.buddy

import android.graphics.BitmapFactory
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.automirrored.filled.Undo
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.repeatOnLifecycle
import kotlinx.coroutines.delay

private val Mint = Color(0xFF8EE4C5)
private val Base = Color(0xFF111820)
private val CardColor = Color(0xFF1A242E)
private val Subtle = Color(0xFF9DB0BF)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun BuddyApp(vm: BuddyViewModel, onScan: () -> Unit, onDictate: () -> Unit, onStop: () -> Unit, onImage: () -> Unit,
             onBubble: () -> Unit, onHideBubble: () -> Unit, onAssistantSettings: () -> Unit, onKeyboardSettings: () -> Unit, onShare: () -> Unit) {
    val s by vm.state.collectAsStateWithLifecycle()
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    LaunchedEffect(lifecycle) { lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) { while (true) { delay(6000); vm.refresh() } } }
    MaterialTheme(colorScheme = darkColorScheme(primary = Mint, onPrimary = Base, background = Base, surface = Base, surfaceVariant = CardColor, onSurface = Color(0xFFEBF3F7), onSurfaceVariant = Subtle)) {
        Scaffold(topBar = { TopAppBar(title = { Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) { Image(painterResource(R.drawable.ic_buddy), "Buddy", Modifier.size(34.dp)); Text("Buddy", fontWeight = FontWeight.SemiBold) } },
            actions = { if (s.paired) { IconButton(onClick = vm::refresh, enabled = !s.busy) { Icon(Icons.Default.Refresh, "Refresh from PC") }; IconButton(onClick = vm::newChat, enabled = !s.busy) { Icon(Icons.Default.Add, "New conversation") } } }) },
            bottomBar = { if (s.paired) NavigationBar(containerColor = CardColor) { listOf("Chat", "History", "Library", "Settings").forEachIndexed { index, label -> NavigationBarItem(selected = s.tab == index, onClick = { vm.tab(index) }, enabled = !s.busy, icon = { Icon(listOf(Icons.Default.ChatBubbleOutline, Icons.Default.History, Icons.Default.Bookmarks, Icons.Default.Tune)[index], label) }, label = { Text(label) }) } } }) { padding ->
            Box(Modifier.padding(padding).consumeWindowInsets(padding).fillMaxSize()) {
                if (!s.paired) PairScreen(s, vm, onScan)
                else when (s.tab) {
                    0 -> ChatScreen(s, vm, onDictate, onStop, onImage)
                    1 -> HistoryScreen(s, vm)
                    2 -> LibraryScreen(s, vm)
                    else -> SettingsScreen(s, vm, onScan, onBubble, onHideBubble, onAssistantSettings, onKeyboardSettings, onShare)
                }
            }
        }
        s.pairInfo?.let { pair -> AlertDialog(onDismissRequest = { if (!s.busy) vm.dismissPair() }, title = { Text("Connect to this PC?") },
            text = { Column { Text(pair.connection.address); Spacer(Modifier.height(12.dp)); Text("Verify this is the PC whose code you scanned. The certificate is pinned to this device."); Spacer(Modifier.height(12.dp)); SelectionContainer { Text("Fingerprint\n${pair.connection.pin.chunked(8).joinToString(" ")}", fontSize = 12.sp) } } },
            confirmButton = { TextButton(onClick = vm::connect, enabled = !s.busy) { Text(if (s.busy) "Connecting…" else "Pair") } }, dismissButton = { TextButton(onClick = vm::dismissPair, enabled = !s.busy) { Text("Cancel") } }) }
        s.message?.let { AlertDialog(onDismissRequest = { vm.message(null) }, title = { Text("Buddy") }, text = { Text(it) }, confirmButton = { TextButton(onClick = { vm.message(null) }) { Text("OK") } }) }
        s.refined?.let { refined -> AlertDialog(onDismissRequest = vm::dismissRefine, title = { Text("Review your refined prompt") }, text = { Column(Modifier.verticalScroll(rememberScrollState())) { Text("Apply replaces your draft. Nothing is sent automatically.", color = Subtle); Spacer(Modifier.height(12.dp)); SelectionContainer { Text(refined) } } }, confirmButton = { TextButton(onClick = vm::applyRefine) { Text("Apply to draft") } }, dismissButton = { TextButton(onClick = vm::dismissRefine) { Text("Keep original") } }) }
        s.deleteId?.let { AlertDialog(onDismissRequest = { vm.requestDelete(null) }, title = { Text("Delete this conversation?") }, text = { Text("It will be removed from Buddy on your PC and all paired phones.") }, confirmButton = { TextButton(onClick = vm::delete) { Text("Delete") } }, dismissButton = { TextButton(onClick = { vm.requestDelete(null) }) { Text("Cancel") } }) }
    }
}

@Composable private fun PairScreen(s: BuddyUi, vm: BuddyViewModel, onScan: () -> Unit) {
    var link by remember { mutableStateOf("") }
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(24.dp), verticalArrangement = Arrangement.spacedBy(20.dp)) {
        Spacer(Modifier.height(24.dp)); Image(painterResource(R.drawable.ic_buddy), null, Modifier.size(96.dp))
        Text("Your AI.\nA little closer.", fontSize = 38.sp, lineHeight = 44.sp, fontWeight = FontWeight.Bold)
        Text("Think, write, and figure things out with Buddy. Your Windows PC does the thinking. Your phone keeps you connected.", color = Subtle, fontSize = 16.sp)
        Card(colors = CardDefaults.cardColors(containerColor = CardColor)) { Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) { Text("START ON YOUR PC", color = Mint, fontSize = 12.sp); Text("1   Open Buddy and complete PC setup."); Text("2   Put both devices on the same Wi-Fi."); Text("3   Open Pair Android phone on the PC.") } }
        Button(onClick = onScan, modifier = Modifier.fillMaxWidth().height(52.dp), enabled = !s.busy) { Icon(Icons.Default.QrCodeScanner, null); Spacer(Modifier.width(10.dp)); Text("Scan PC code") }
        OutlinedTextField(value = link, onValueChange = { link = it }, label = { Text("Or paste the PC pairing link") }, modifier = Modifier.fillMaxWidth(), maxLines = 3)
        OutlinedButton(onClick = { vm.pairLink(link) }, enabled = link.isNotBlank() && !s.busy, modifier = Modifier.fillMaxWidth()) { Text("Review connection") }
        Text("No AI API key. The PC must stay on and reachable. Buddy does not listen or capture your screen in the background.", fontSize = 12.sp, color = Subtle)
    }
}

@Composable private fun ChatScreen(s: BuddyUi, vm: BuddyViewModel, onDictate: () -> Unit, onStop: () -> Unit, onImage: () -> Unit) {
    val scroll = rememberLazyListState()
    LaunchedEffect(s.lines.size, s.lines.lastOrNull()?.text?.length) { if (s.lines.isNotEmpty()) scroll.scrollToItem(s.lines.lastIndex) }
    Column(Modifier.fillMaxSize().imePadding()) {
        Row(Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Box(Modifier.size(7.dp).background(if (s.ready) Mint else Color(0xFFEABB7A), RoundedCornerShape(4.dp)))
            Text(if (s.busy) "${s.status}" else s.status, color = Subtle, fontSize = 12.sp, maxLines = 3)
        }
        if (s.lines.isEmpty()) Column(Modifier.weight(1f).fillMaxWidth().verticalScroll(rememberScrollState()).padding(24.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
            Spacer(Modifier.height(16.dp)); Text("A little clarity.\nAny time.", fontSize = 32.sp, lineHeight = 39.sp, fontWeight = FontWeight.SemiBold)
            Text("Bring a question, a rough idea, or something you’re stuck on.", color = Subtle)
            listOf("Help me plan a focused day", "Explain something simply", "Turn my idea into a clear plan").forEach { hint ->
                Surface(onClick = { vm.draft(hint) }, shape = RoundedCornerShape(16.dp), color = CardColor, modifier = Modifier.fillMaxWidth()) { Row(Modifier.padding(18.dp), horizontalArrangement = Arrangement.SpaceBetween) { Text(hint, modifier = Modifier.weight(1f)); Icon(Icons.AutoMirrored.Filled.ArrowForward, null, tint = Mint, modifier = Modifier.size(18.dp)) } }
            }
        } else LazyColumn(state = scroll, modifier = Modifier.weight(1f).fillMaxWidth(), contentPadding = PaddingValues(18.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            items(s.lines) { line -> Surface(color = if (line.role == "user") CardColor else Base, shape = RoundedCornerShape(18.dp), modifier = Modifier.fillMaxWidth()) { Column(Modifier.padding(16.dp)) { Text(if (line.role == "user") "YOU" else "BUDDY", color = if (line.role == "user") Subtle else Mint, fontSize = 10.sp, fontWeight = FontWeight.Bold); Spacer(Modifier.height(8.dp)); SelectionContainer { Text(line.text.ifEmpty { "Thinking on your PC…" }, fontSize = 16.sp, lineHeight = 24.sp) } } } }
        }
        Column(Modifier.fillMaxWidth().background(CardColor).padding(horizontal = 16.dp, vertical = 10.dp)) {
            if (s.context != null || s.image != null) Row(verticalAlignment = Alignment.CenterVertically) {
                s.image?.let { bytes -> val bitmap = remember(bytes) { BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap() }; bitmap?.let { Image(it, "Attached image preview", Modifier.size(64.dp)) } }
                Text(if (s.image != null) "Image attached" else "Screen text attached", color = Mint, fontSize = 12.sp, modifier = Modifier.weight(1f))
                if (s.context != null) TextButton(onClick = { vm.message(s.context) }) { Text("Review") }
                IconButton(onClick = vm::clearContext, enabled = !s.busy) { Icon(Icons.Default.Close, "Remove context") }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) { listOf("type", "hybrid", "voice").forEach { value -> FilterChip(selected = s.mode == value, enabled = !s.busy && !s.listening, onClick = { vm.mode(value) }, label = { Text(value.replaceFirstChar(Char::uppercase)) }) } }
            if (s.mode == "hybrid") Text("Dictate → review → send", color = Subtle, fontSize = 11.sp)
            if (s.mode == "voice") Text("Recognized speech sends automatically", color = Subtle, fontSize = 11.sp)
            OutlinedTextField(value = s.draft, onValueChange = vm::draft, placeholder = { Text(if (s.listening) "Listening…" else "Ask Buddy anything…") }, modifier = Modifier.fillMaxWidth(), minLines = 2, maxLines = 5, enabled = !s.busy, shape = RoundedCornerShape(16.dp))
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                IconButton(onClick = onImage, enabled = !s.busy) { Icon(Icons.Default.AddPhotoAlternate, "Attach an image") }
                IconButton(onClick = if (s.listening) onStop else onDictate, enabled = !s.busy) { Icon(if (s.listening) Icons.Default.Stop else Icons.Default.MicNone, if (s.listening) "Stop listening" else "Dictate", tint = if (s.listening) Mint else Subtle) }
                TextButton(onClick = vm::refine, enabled = s.draft.isNotBlank() && !s.busy) { Text("Refine") }
                if (s.originalDraft != null) IconButton(onClick = vm::undoRefine, enabled = !s.busy) { Icon(Icons.AutoMirrored.Filled.Undo, "Undo refinement") }
                Spacer(Modifier.weight(1f))
                FilledIconButton(onClick = if (s.busy) onStop else vm::send, enabled = s.busy || s.draft.isNotBlank()) { Icon(if (s.busy) Icons.Default.Stop else Icons.AutoMirrored.Filled.Send, if (s.busy) "Stop answer" else "Send message") }
            }
        }
    }
}

@Composable private fun HistoryScreen(s: BuddyUi, vm: BuddyViewModel) {
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        item { Text("Pick up a thought", fontSize = 28.sp, fontWeight = FontWeight.SemiBold); Text("Shared with your Windows PC", color = Subtle) }
        if (s.chats.isEmpty()) item { Text("Your conversations will appear here.", Modifier.padding(top = 30.dp), color = Subtle) }
        items(s.chats, key = { it.id }) { chat -> Card(colors = CardDefaults.cardColors(containerColor = CardColor), modifier = Modifier.fillMaxWidth().clickable { vm.select(chat.id) }) { Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) { Text(chat.title, Modifier.weight(1f)); IconButton(onClick = { vm.requestDelete(chat.id) }) { Icon(Icons.Default.DeleteOutline, "Delete conversation") } } } }
    }
}

@Composable private fun LibraryScreen(s: BuddyUi, vm: BuddyViewModel) {
    var memory by remember { mutableStateOf(false) }; var title by remember { mutableStateOf("") }; var text by remember { mutableStateOf("") }
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        Text("Keep what helps", fontSize = 28.sp, fontWeight = FontWeight.SemiBold)
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) { FilterChip(selected = !memory, onClick = { memory = false }, label = { Text("Prompts") }); FilterChip(selected = memory, onClick = { memory = true }, label = { Text("Memory") }) }
        Text(if (memory) "Buddy remembers only the facts you explicitly save here." else "Save useful prompts and bring them into your next conversation.", color = Subtle)
        (if (memory) s.memories else s.prompts).forEach { note -> Card(colors = CardDefaults.cardColors(containerColor = CardColor)) { Column(Modifier.fillMaxWidth().padding(16.dp)) { Text(note.title, color = Mint); Text(note.text); Row { if (!memory) TextButton(onClick = { vm.draft(note.text); vm.tab(0) }) { Text("Use prompt") }; TextButton(onClick = { vm.deleteNote(memory, note.id) }) { Text("Delete") } } } } }
        OutlinedTextField(title, { title = it.take(100) }, label = { Text("Title") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(text, { text = it.take(2000) }, label = { Text(if (memory) "A fact Buddy should remember" else "Prompt text") }, modifier = Modifier.fillMaxWidth(), minLines = 3)
        Button(onClick = { vm.saveNote(memory, title, text) }, enabled = title.isNotBlank() && text.isNotBlank()) { Text("Save to PC") }
    }
}

@Composable private fun SettingsScreen(s: BuddyUi, vm: BuddyViewModel, onScan: () -> Unit, onBubble: () -> Unit, onHideBubble: () -> Unit, onAssistantSettings: () -> Unit, onKeyboardSettings: () -> Unit, onShare: () -> Unit) {
    var unlink by remember { mutableStateOf(false) }
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp), verticalArrangement = Arrangement.spacedBy(15.dp)) {
        Text("Make yourself at home", fontSize = 27.sp, fontWeight = FontWeight.SemiBold)
        Text("YOUR PC", color = Mint, fontSize = 11.sp); Text(s.address); Text("Model: ${s.model}\n${s.status}", color = Subtle)
        OutlinedButton(onClick = vm::refresh) { Text("Check connection") }
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) { Text("Read answers aloud", Modifier.weight(1f)); Switch(checked = s.readAloud, onCheckedChange = vm::readAloud) }
        Text("Voice uses installed offline speech services. Availability depends on the phone and installed language packs.", color = Subtle, fontSize = 12.sp)
        HorizontalDivider(); Text("QUICK ACCESS", color = Mint, fontSize = 11.sp)
        Button(onClick = onBubble) { Text("Show floating Buddy bubble") }; TextButton(onClick = onHideBubble) { Text("Hide bubble") }
        OutlinedButton(onClick = onAssistantSettings) { Text("Choose Buddy as default assistant") }
        OutlinedButton(onClick = onKeyboardSettings) { Text("Enable Buddy Keyboard") }
        Text("The bubble is an optional shortcut. The assistant can share screen text after you tap its review action. The basic keyboard shares selected text only when you tap Refine.", color = Subtle, fontSize = 12.sp)
        OutlinedButton(onClick = onShare) { Text("Export current conversation") }
        HorizontalDivider(); Text("PRIVACY", color = Mint, fontSize = 11.sp)
        Text("AI runs on your PC. Messages, saved prompts, and memories stay in its encrypted store. Pairing credentials are encrypted with Android Keystore. Screenshots and attached images are not saved by Buddy.", color = Subtle)
        Text("An active request may finish on the PC if Android closes unexpectedly. Use Stop to cancel before leaving.", color = Subtle, fontSize = 12.sp)
        TextButton(onClick = onScan) { Text("Scan a new PC code") }; TextButton(onClick = { unlink = true }) { Text("Forget this PC") }
        Text("Buddy ${BuildConfig.VERSION_NAME} · Local PC edition\nPhone access requires the PC to stay on and reachable.", color = Subtle, fontSize = 12.sp)
    }
    if (unlink) AlertDialog(onDismissRequest = { unlink = false }, title = { Text("Forget this PC?") }, text = { Text("Your conversations remain on the PC. Pair again to reconnect. For full revocation, remove this phone in the PC’s paired devices panel.") }, confirmButton = { TextButton(onClick = { vm.disconnect(); unlink = false }) { Text("Forget") } }, dismissButton = { TextButton(onClick = { unlink = false }) { Text("Cancel") } })
}
