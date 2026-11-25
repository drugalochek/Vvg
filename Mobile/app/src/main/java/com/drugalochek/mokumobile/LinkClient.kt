package com.drugalochek.mokumobile

import android.content.ContentValues
import android.content.Context
import android.net.Uri
import android.os.Environment
import android.os.ParcelFileDescriptor
import android.provider.MediaStore
import android.provider.OpenableColumns
import android.webkit.MimeTypeMap
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import org.json.JSONObject
import java.io.File
import java.io.FileInputStream
import java.io.IOException
import java.io.InputStream
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.HttpURLConnection
import java.net.InetAddress
import java.net.NetworkInterface
import java.net.SocketTimeoutException
import java.net.URL
import java.net.URLEncoder
import java.util.concurrent.ConcurrentLinkedQueue

class HttpError(val code: Int) : IOException("HTTP $code")
class Unauthorized : IOException("unauthorized")

data class Transfer(val name: String, val incoming: Boolean, val done: Long, val total: Long, val bytesPerSec: Long)

data class UiState(
    val pc: Pc? = null,
    val online: Boolean = false,
    val host: String? = null,
    val pairing: Boolean = false,
    val transfer: Transfer? = null,
    val queued: Int = 0,
    val history: List<HistoryItem> = emptyList(),
    val message: String? = null,
)

/** Общее состояние приложения: его читает интерфейс и обновляет служба передачи. */
object Link {
    private val _state = MutableStateFlow(UiState())
    val state: StateFlow<UiState> = _state
    val uiVisible = MutableStateFlow(false)

    fun update(f: (UiState) -> UiState) = _state.update(f)
    fun load(store: Store) = _state.update { it.copy(pc = store.pc, history = store.history()) }
    fun say(text: String) = _state.update { it.copy(message = text) }
}

/**
 * Файл, ожидающий отправки на ПК. Дескриптор открывается сразу при выборе файла,
 * поэтому отправка не зависит от того, сколько живёт экран «Поделиться».
 */
class Outgoing(val name: String, var size: Long, private val pfd: ParcelFileDescriptor?, private var stream: InputStream?) {
    fun open(): InputStream {
        pfd?.let { fd ->
            val s = FileInputStream(fd.fileDescriptor)
            try { s.channel.position(0) } catch (_: Exception) { }
            return s
        }
        return stream ?: throw IOException("stream already used")
    }
    val canRetry get() = pfd != null && size >= 0
    fun close() {
        try { pfd?.close() } catch (_: Exception) { }
        try { stream?.close() } catch (_: Exception) { }
        stream = null
    }
}

object Outbox {
    private val queue = ConcurrentLinkedQueue<Outgoing>()
    val isEmpty get() = queue.isEmpty()
    val size get() = queue.size
    fun add(items: List<Outgoing>) { queue.addAll(items); Link.update { it.copy(queued = queue.size) } }
    fun poll(): Outgoing? = queue.poll().also { Link.update { s -> s.copy(queued = queue.size) } }
    fun clear() { while (true) (queue.poll() ?: break).close(); Link.update { it.copy(queued = 0) } }

    /** Вызывать в фоне: открывает файл из галереи, «Файлов» или другого приложения. */
    fun open(context: Context, uri: Uri): Outgoing? {
        val cr = context.contentResolver
        var name: String? = null
        var size = -1L
        try {
            cr.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)?.use { c ->
                if (c.moveToFirst()) {
                    val ni = c.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                    val si = c.getColumnIndex(OpenableColumns.SIZE)
                    if (ni >= 0 && !c.isNull(ni)) name = c.getString(ni)
                    if (si >= 0 && !c.isNull(si)) size = c.getLong(si)
                }
            }
        } catch (_: Exception) { }
        if (name.isNullOrBlank()) name = uri.lastPathSegment?.substringAfterLast('/')?.ifBlank { null } ?: "file"
        return try {
            val pfd = cr.openFileDescriptor(uri, "r")
            if (pfd != null) {
                if (pfd.statSize >= 0) size = pfd.statSize
                if (size >= 0) return Outgoing(name!!, size, pfd, null)
                return Outgoing(name!!, -1, null, ParcelFileDescriptor.AutoCloseInputStream(pfd))
            }
            cr.openInputStream(uri)?.let { Outgoing(name!!, -1, null, it) }
        } catch (_: Exception) {
            try { cr.openInputStream(uri)?.let { Outgoing(name!!, -1, null, it) } } catch (_: Exception) { null }
        }
    }

    fun text(context: Context, text: String): Outgoing? {
        val f = File(context.cacheDir, "share").apply { mkdirs() }
            .resolve("text-" + java.text.SimpleDateFormat("yyyyMMdd-HHmmss", java.util.Locale.US).format(java.util.Date()) + ".txt")
        f.writeText(text)
        return open(context, Uri.fromFile(f))
    }
}

data class PairQr(val hosts: List<String>, val port: Int, val id: String, val name: String, val code: String)

/** Протокол Moku Link: HTTP в локальной сети (TCP 47820) и поиск ПК широковещательным UDP (47821). */
object LinkClient {
    const val PORT = 47820
    const val DISCOVERY_PORT = 47821
    private const val DISCOVER = "MOKU_LINK_DISCOVER_1"

    private fun enc(s: String) = URLEncoder.encode(s, "UTF-8").replace("+", "%20")

    private fun open(host: String, port: Int, path: String, method: String = "GET", token: String? = null,
                     connect: Int = 2500, read: Int = 15000): HttpURLConnection {
        val c = URL("http", host, port, path).openConnection() as HttpURLConnection
        c.requestMethod = method
        c.connectTimeout = connect
        c.readTimeout = read
        c.useCaches = false
        c.instanceFollowRedirects = false
        c.setRequestProperty("Accept-Encoding", "identity")
        if (token != null) c.setRequestProperty("X-Moku-Token", token)
        return c
    }

    private fun postEmpty(c: HttpURLConnection) {
        c.doOutput = true
        c.setFixedLengthStreamingMode(0)
        c.outputStream.close()
    }

    private fun json(c: HttpURLConnection): JSONObject {
        try {
            val code = c.responseCode
            if (code == 401) throw Unauthorized()
            val body = (if (code in 200..299) c.inputStream else c.errorStream)?.bufferedReader()?.use { it.readText() } ?: ""
            if (code !in 200..299) throw HttpError(code)
            return JSONObject(body.ifBlank { "{}" })
        } finally { c.disconnect() }
    }

    fun parseQr(text: String?): PairQr? {
        val u = try { Uri.parse(text?.trim() ?: return null) } catch (_: Exception) { return null }
        if (!"moku".equals(u.scheme, true) || !"pair".equals(u.host, true)) return null
        val hosts = (u.getQueryParameter("h") ?: "").split(',').map { it.trim() }.filter { it.isNotEmpty() }
        val port = u.getQueryParameter("p")?.toIntOrNull() ?: return null
        val id = u.getQueryParameter("id") ?: return null
        val code = u.getQueryParameter("c") ?: return null
        if (hosts.isEmpty() || port !in 1..65535 || code.length != 6) return null
        return PairQr(hosts, port, id, u.getQueryParameter("n") ?: "PC", code)
    }

    /** Ручной ввод: адрес ПК и шестизначный код с вкладки «Телефон». */
    fun manual(hostInput: String, code: String): PairQr {
        val raw = hostInput.trim().removePrefix("http://").trimEnd('/')
        val host = raw.substringBefore(':')
        val port = raw.substringAfter(':', "").toIntOrNull() ?: PORT
        val hello = json(open(host, port, "/api/hello"))
        if (hello.optString("app") != "Moku") throw IOException("not moku")
        return PairQr(listOf(host), port, hello.optString("id"), hello.optString("name", "PC"), code.trim())
    }

    fun pair(qr: PairQr, phoneId: String, device: String): Pc {
        var last: IOException? = null
        val hosts = qr.hosts.sortedBy { if (it.startsWith("192.168.")) 0 else 1 }
        for (h in hosts) {
            try {
                val c = open(h, qr.port, "/api/pair?code=${enc(qr.code)}&phone=${enc(phoneId)}&device=${enc(device)}", "POST")
                postEmpty(c)
                val j = json(c)
                return Pc(j.optString("id", qr.id), j.optString("name", qr.name), listOf(h) + qr.hosts.filter { it != h },
                    qr.port, j.getString("token"))
            } catch (e: HttpError) {
                if (e.code == 403) throw e
                last = e
            } catch (e: IOException) { last = e }
        }
        throw last ?: IOException("no host")
    }

    fun ping(host: String, pc: Pc): Boolean = try {
        json(open(host, pc.port, "/api/ping", token = pc.token, connect = 1500, read = 4000)); true
    } catch (e: Unauthorized) { throw e } catch (_: Exception) { false }

    /** Находит ПК: сначала по известным адресам, потом поиском в сети Wi-Fi (адрес ПК мог смениться). */
    fun resolve(store: Store, pc: Pc): String? {
        val candidates = (listOfNotNull(store.lastHost) + pc.hosts).distinct()
        for (h in candidates) if (ping(h, pc)) { store.lastHost = h; return h }
        val found = discover(pc.id) ?: return null
        if (!ping(found, pc)) return null
        store.lastHost = found
        store.pc = pc.copy(hosts = listOf(found) + pc.hosts.filter { it != found }.take(4))
        return found
    }

    private fun broadcasts(): List<String> = try {
        NetworkInterface.getNetworkInterfaces().toList()
            .filter { it.isUp && !it.isLoopback }
            .flatMap { it.interfaceAddresses }
            .mapNotNull { it.broadcast?.hostAddress }
    } catch (_: Exception) { emptyList() }

    fun discover(pcId: String, timeoutMs: Long = 2500): String? {
        try {
            DatagramSocket().use { s ->
                s.broadcast = true
                s.soTimeout = 500
                val msg = DISCOVER.toByteArray()
                val targets = (listOf("255.255.255.255") + broadcasts()).distinct()
                val end = System.currentTimeMillis() + timeoutMs
                var round = 0
                while (System.currentTimeMillis() < end) {
                    if (round++ % 2 == 0) for (t in targets) {
                        try { s.send(DatagramPacket(msg, msg.size, InetAddress.getByName(t), DISCOVERY_PORT)) } catch (_: Exception) { }
                    }
                    val buf = ByteArray(2048)
                    val pk = DatagramPacket(buf, buf.size)
                    try {
                        s.receive(pk)
                        val j = JSONObject(String(pk.data, 0, pk.length, Charsets.UTF_8))
                        if (j.optString("app") == "Moku" && j.optString("id") == pcId) return pk.address.hostAddress
                    } catch (_: SocketTimeoutException) { } catch (_: Exception) { }
                }
            }
        } catch (_: Exception) { }
        return null
    }

    data class InboxFile(val id: String, val name: String, val size: Long)

    fun inbox(host: String, pc: Pc, wait: Int): List<InboxFile> {
        val j = json(open(host, pc.port, "/api/inbox?wait=$wait", token = pc.token, read = (wait + 15) * 1000))
        val arr = j.optJSONArray("files") ?: return emptyList()
        return (0 until arr.length()).mapNotNull { i ->
            arr.optJSONObject(i)?.let { InboxFile(it.optString("id"), it.optString("name", "file"), it.optLong("size", -1)) }
        }.filter { it.id.isNotBlank() }
    }

    fun ack(host: String, pc: Pc, id: String) {
        val c = open(host, pc.port, "/api/ack?id=${enc(id)}", "POST", pc.token)
        postEmpty(c); json(c)
    }

    fun unpair(host: String, pc: Pc) {
        val c = open(host, pc.port, "/api/unpair", "POST", pc.token, connect = 1500, read = 4000)
        postEmpty(c); json(c)
    }

    fun upload(context: Context, host: String, pc: Pc, item: Outgoing, progress: (Long, Long) -> Unit): String {
        var tmp: File? = null
        var input = item.open()
        try {
            if (item.size < 0) {
                val t = File.createTempFile("moku", ".part", context.cacheDir)
                tmp = t
                input.use { i -> t.outputStream().use { i.copyTo(it, 1 shl 16) } }
                item.size = t.length()
                input = t.inputStream()
            }
            val total = item.size
            val c = open(host, pc.port, "/api/upload?name=${enc(item.name)}", "POST", pc.token, connect = 3000, read = 60000)
            c.doOutput = true
            c.setFixedLengthStreamingMode(total)
            c.setRequestProperty("Content-Type", "application/octet-stream")
            input.use { inp ->
                c.outputStream.use { out ->
                    val buf = ByteArray(1 shl 16)
                    var done = 0L
                    while (done < total) {
                        val n = inp.read(buf, 0, minOf(buf.size.toLong(), total - done).toInt())
                        if (n < 0) throw IOException("file changed while sending")
                        out.write(buf, 0, n)
                        done += n
                        progress(done, total)
                    }
                }
            }
            return json(c).optString("name", item.name)
        } finally {
            tmp?.delete()
        }
    }

    /** Скачивает файл с ПК в «Загрузки/Moku». Возвращает адрес файла в хранилище телефона. */
    fun download(context: Context, host: String, pc: Pc, f: InboxFile, progress: (Long, Long) -> Unit): Uri {
        val c = open(host, pc.port, "/api/file?id=${enc(f.id)}", token = pc.token, connect = 3000, read = 60000)
        try {
            val code = c.responseCode
            if (code == 401) throw Unauthorized()
            if (code !in 200..299) throw HttpError(code)
            val total = c.getHeaderField("Content-Length")?.toLongOrNull() ?: f.size
            val ext = f.name.substringAfterLast('.', "").lowercase()
            val mime = MimeTypeMap.getSingleton().getMimeTypeFromExtension(ext) ?: "application/octet-stream"
            val cr = context.contentResolver
            val values = ContentValues().apply {
                put(MediaStore.Downloads.DISPLAY_NAME, f.name)
                put(MediaStore.Downloads.MIME_TYPE, mime)
                put(MediaStore.Downloads.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/Moku")
                put(MediaStore.Downloads.IS_PENDING, 1)
            }
            val item = cr.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values) ?: throw IOException("storage unavailable")
            try {
                var done = 0L
                c.inputStream.use { inp ->
                    (cr.openOutputStream(item) ?: throw IOException("storage unavailable")).use { out ->
                        val buf = ByteArray(1 shl 16)
                        while (true) {
                            val n = inp.read(buf)
                            if (n < 0) break
                            out.write(buf, 0, n)
                            done += n
                            progress(done, total)
                        }
                    }
                }
                if (total >= 0 && done != total) throw IOException("download truncated")
                values.clear()
                values.put(MediaStore.Downloads.IS_PENDING, 0)
                cr.update(item, values, null, null)
                return item
            } catch (e: Exception) {
                try { cr.delete(item, null, null) } catch (_: Exception) { }
                throw e
            }
        } finally { c.disconnect() }
    }
}
