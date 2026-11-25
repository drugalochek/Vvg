package com.drugalochek.mokumobile

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.webkit.MimeTypeMap
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.lifecycle.lifecycleScope
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.IOException

class MainActivity : ComponentActivity() {
    private lateinit var store: Store

    private val scan = registerForActivityResult(ScanContract()) { r -> r.contents?.let { pairWith(LinkClient.parseQr(it), it) } }
    private val pickFiles = registerForActivityResult(ActivityResultContracts.OpenMultipleDocuments()) { send(it) }
    private val pickMedia = registerForActivityResult(ActivityResultContracts.PickMultipleVisualMedia(100)) { send(it) }
    private val notifications = registerForActivityResult(ActivityResultContracts.RequestPermission()) { }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        store = Store(this)
        Link.load(store)
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
            notifications.launch(Manifest.permission.POST_NOTIFICATIONS)
        handleIntent(intent)
        setContent {
            MokuTheme {
                MokuApp(object : Actions {
                    override fun scan() = startScan()
                    override fun manual(host: String, code: String) = pairManual(host, code)
                    override fun sendFiles() = pickFiles.launch(arrayOf("*/*"))
                    override fun sendMedia() = pickMedia.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageAndVideo))
                    override fun open(item: HistoryItem) = openFile(item)
                    override fun unpair() = doUnpair()
                    override fun setBackground(on: Boolean) { store.background = on; backgroundFlag.value = on }
                    override fun clearHistory() { store.clearHistory(); Link.load(store) }
                    override fun dismissMessage() = Link.update { it.copy(message = null) }
                    override fun reconnect() = LinkService.start(this@MainActivity)
                }, backgroundFlag)
            }
        }
    }

    private val backgroundFlag by lazy { androidx.compose.runtime.mutableStateOf(store.background) }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleIntent(intent)
    }

    override fun onStart() {
        super.onStart()
        Link.uiVisible.value = true
        Link.load(store)
        LinkService.start(this)
    }

    override fun onStop() {
        Link.uiVisible.value = false
        super.onStop()
    }

    /** QR-код с ПК можно отсканировать и обычной камерой телефона: ссылка moku://pair откроет приложение. */
    private fun handleIntent(intent: Intent?) {
        val data = intent?.data ?: return
        if (data.scheme == "moku") pairWith(LinkClient.parseQr(data.toString()), data.toString())
    }

    private fun startScan() {
        scan.launch(ScanOptions().apply {
            setDesiredBarcodeFormats(ScanOptions.QR_CODE)
            setPrompt(tr("Наведи камеру на QR-код на вкладке «Телефон» в Moku", "Point the camera at the QR code on Moku's Phone tab"))
            setBeepEnabled(false)
            setOrientationLocked(false)
        })
    }

    private fun deviceName(): String =
        Settings.Global.getString(contentResolver, "device_name")?.ifBlank { null } ?: "${Build.MANUFACTURER} ${Build.MODEL}".trim()

    private fun pairWith(qr: PairQr?, raw: String) {
        if (qr == null) { Link.say(tr("Это не QR-код Moku. Открой на ПК вкладку с иконкой телефона.", "This is not a Moku QR code. Open the phone tab in Moku on the PC.")); return }
        Link.update { it.copy(pairing = true) }
        lifecycleScope.launch {
            val result = withContext(Dispatchers.IO) { runCatching { LinkClient.pair(qr, store.phoneId, deviceName()) } }
            Link.update { it.copy(pairing = false) }
            result.onSuccess { pc ->
                store.pc = pc
                store.lastHost = pc.hosts.firstOrNull()
                Link.load(store)
                Link.update { it.copy(online = true, host = pc.hosts.firstOrNull()) }
                Link.say(tr("Подключено к ${pc.name} ✓", "Connected to ${pc.name} ✓"))
                LinkService.start(this@MainActivity)
            }.onFailure { e -> Link.say(pairError(e)) }
        }
    }

    private fun pairManual(host: String, code: String) {
        if (host.isBlank() || code.trim().length != 6) { Link.say(tr("Введи адрес ПК и 6 цифр кода", "Enter the PC address and the 6-digit code")); return }
        Link.update { it.copy(pairing = true) }
        lifecycleScope.launch {
            val qr = withContext(Dispatchers.IO) { runCatching { LinkClient.manual(host, code) } }
            Link.update { it.copy(pairing = false) }
            qr.onSuccess { pairWith(it, "") }.onFailure { Link.say(pairError(it)) }
        }
    }

    private fun pairError(e: Throwable): String = when {
        e is HttpError && e.code == 403 -> tr("Код устарел или неверный. Нажми «Подключить ещё» на ПК и отсканируй новый QR.", "The code expired or is wrong. Press Connect another on the PC and scan the new QR.")
        e is IOException -> tr("ПК не отвечает. Телефон и компьютер должны быть в одной сети Wi-Fi, а Windows — разрешить Moku доступ к сети.", "The PC does not answer. Both devices must be on the same Wi-Fi and Windows must allow Moku on the network.")
        else -> tr("Не удалось подключиться", "Could not connect")
    }

    private fun send(uris: List<Uri>) {
        if (uris.isEmpty()) return
        lifecycleScope.launch {
            val items = withContext(Dispatchers.IO) { uris.mapNotNull { Outbox.open(this@MainActivity, it) } }
            if (items.isEmpty()) { Link.say(tr("Не удалось открыть файл", "Could not open the file")); return@launch }
            Outbox.add(items)
            LinkService.start(this@MainActivity)
        }
    }

    private fun openFile(item: HistoryItem) {
        val uri = item.uri?.let(Uri::parse) ?: return
        val mime = MimeTypeMap.getSingleton().getMimeTypeFromExtension(item.name.substringAfterLast('.', "").lowercase()) ?: "*/*"
        try {
            startActivity(Intent.createChooser(Intent(Intent.ACTION_VIEW).setDataAndType(uri, mime)
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION), item.name))
        } catch (_: Exception) { Link.say(tr("Файл не найден — возможно, его удалили", "File not found — it may have been deleted")) }
    }

    private fun doUnpair() {
        val pc = store.pc ?: return
        val host = Link.state.value.host ?: store.lastHost
        lifecycleScope.launch {
            withContext(Dispatchers.IO) { if (host != null) try { LinkClient.unpair(host, pc) } catch (_: Exception) { } }
            store.pc = null
            Outbox.clear()
            Link.load(store)
            Link.update { it.copy(online = false, host = null, transfer = null) }
            stopService(Intent(this@MainActivity, LinkService::class.java))
            Link.say(tr("Компьютер отключён", "PC unlinked"))
        }
    }
}
