package com.drugalochek.mokumobile

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.Uri
import android.os.IBinder
import android.webkit.MimeTypeMap
import androidx.core.app.NotificationCompat
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.IOException

/**
 * Связь с Moku на ПК: принимает файлы (длинный опрос /api/inbox) и отправляет очередь Outbox.
 * Работает как foreground-служба, чтобы передача не обрывалась при сворачивании приложения.
 */
class LinkService : Service() {
    companion object {
        private const val ACTION_START = "com.drugalochek.mokumobile.START"
        private const val ACTION_STOP = "com.drugalochek.mokumobile.STOP"
        private const val CH_LINK = "link"
        private const val CH_FILES = "files"
        private const val ONGOING_ID = 1

        @Volatile var running = false
            private set

        fun start(context: Context) {
            if (Store(context).pc == null) return
            try {
                context.startForegroundService(Intent(context, LinkService::class.java).setAction(ACTION_START))
            } catch (_: Exception) { }
        }
    }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private lateinit var store: Store
    private var pollJob: Job? = null
    private var sendJob: Job? = null
    private var watchJob: Job? = null
    @Volatile private var host: String? = null
    @Volatile private var busy = false
    private var lastNotify = 0L
    private var notifyId = 100

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        store = Store(this)
        val nm = getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(NotificationChannel(CH_LINK, tr("Связь с ПК", "PC connection"), NotificationManager.IMPORTANCE_MIN))
        nm.createNotificationChannel(NotificationChannel(CH_FILES, tr("Полученные файлы", "Received files"), NotificationManager.IMPORTANCE_DEFAULT))
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        goForeground(tr("Подключение к ПК…", "Connecting to PC…"))
        if (intent?.action == ACTION_STOP || store.pc == null) { stopEverything(); return START_NOT_STICKY }
        running = true
        if (pollJob?.isActive != true) pollJob = scope.launch { pollLoop() }
        if (sendJob?.isActive != true) sendJob = scope.launch { sendLoop() }
        if (watchJob?.isActive != true) watchJob = scope.launch { watchIdle() }
        return START_NOT_STICKY
    }

    override fun onTimeout(startId: Int, fgsType: Int) {
        // Android 15 ограничивает фоновую синхронизацию; при следующем открытии приложения связь возобновится.
        stopEverything()
    }

    override fun onDestroy() {
        running = false
        scope.cancel()
        Link.update { it.copy(online = false, transfer = null) }
        super.onDestroy()
    }

    private fun stopEverything() {
        running = false
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    private fun goForeground(text: String) {
        try {
            startForeground(ONGOING_ID, ongoing(text), ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
        } catch (_: Exception) { }
    }

    private fun ongoing(text: String, progress: Int = -1): Notification {
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP), PendingIntent.FLAG_IMMUTABLE)
        val stop = PendingIntent.getService(this, 1, Intent(this, LinkService::class.java).setAction(ACTION_STOP), PendingIntent.FLAG_IMMUTABLE)
        val b = NotificationCompat.Builder(this, CH_LINK)
            .setSmallIcon(R.drawable.ic_stat_moku)
            .setContentTitle("Moku Mobile")
            .setContentText(text)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setSilent(true)
            .setContentIntent(open)
            .addAction(0, tr("Остановить", "Stop"), stop)
        if (progress >= 0) b.setProgress(100, progress, false)
        return b.build()
    }

    private fun updateOngoing(text: String, progress: Int = -1, force: Boolean = false) {
        val now = System.currentTimeMillis()
        if (!force && now - lastNotify < 700) return
        lastNotify = now
        try { getSystemService(NotificationManager::class.java).notify(ONGOING_ID, ongoing(text, progress)) } catch (_: Exception) { }
    }

    private fun idleText(): String {
        val pc = store.pc
        return if (host != null && pc != null) tr("На связи с ${pc.name}", "Connected to ${pc.name}")
        else tr("Ищу компьютер в сети Wi-Fi…", "Looking for the PC on Wi-Fi…")
    }

    private fun setOnline(h: String?) {
        host = h
        Link.update { it.copy(online = h != null, host = h) }
        if (!busy) updateOngoing(idleText(), force = true)
    }

    private fun onUnauthorized() {
        store.pc = null
        Outbox.clear()
        Link.update { it.copy(pc = null, online = false, host = null, transfer = null) }
        Link.say(tr("Компьютер отключил этот телефон. Подключи заново по QR-коду.", "The PC unlinked this phone. Pair again with the QR code."))
        stopEverything()
    }

    private suspend fun ensureHost(): String? {
        host?.let { return it }
        val pc = store.pc ?: return null
        val h = LinkClient.resolve(store, pc)
        setOnline(h)
        return h
    }

    private suspend fun pollLoop() {
        var backoff = 2000L
        while (scope.isActive) {
            val pc = store.pc ?: return
            try {
                val h = ensureHost()
                if (h == null) { delay(backoff); backoff = minOf(backoff * 2, 30000); continue }
                backoff = 2000
                val files = LinkClient.inbox(h, pc, 25)
                Link.update { it.copy(online = true) }
                for (f in files) receive(h, pc, f)
            } catch (_: Unauthorized) {
                onUnauthorized(); return
            } catch (e: HttpError) {
                delay(1500)
            } catch (_: IOException) {
                setOnline(null); delay(1500)
            } catch (_: Exception) {
                delay(3000)
            }
        }
    }

    private fun receive(h: String, pc: Pc, f: LinkClient.InboxFile) {
        busy = true
        val started = System.currentTimeMillis()
        try {
            val uri = LinkClient.download(this, h, pc, f) { done, total ->
                val speed = done * 1000 / maxOf(1, System.currentTimeMillis() - started)
                Link.update { it.copy(transfer = Transfer(f.name, true, done, total, speed)) }
                updateOngoing(tr("Получаю ${f.name}", "Receiving ${f.name}"), if (total > 0) (done * 100 / total).toInt() else 0)
            }
            try { LinkClient.ack(h, pc, f.id) } catch (_: Exception) { }
            val history = store.addHistory(HistoryItem(f.name, f.size, true, System.currentTimeMillis(), uri.toString(), true))
            Link.update { it.copy(history = history, transfer = null) }
            notifyReceived(f.name, uri)
        } catch (e: HttpError) {
            if (e.code == 410 || e.code == 404) try { LinkClient.ack(h, pc, f.id) } catch (_: Exception) { }
            Link.update { it.copy(transfer = null) }
        } finally {
            busy = false
            updateOngoing(idleText(), force = true)
        }
    }

    private suspend fun sendLoop() {
        while (scope.isActive) {
            val item = Outbox.poll()
            if (item == null) { delay(400); continue }
            busy = true
            var ok = false
            var attempts = 0
            try {
                while (!ok && attempts < 3 && scope.isActive) {
                    attempts++
                    val pc = store.pc ?: break
                    val h = ensureHost()
                    if (h == null) { delay(2500); continue }
                    val started = System.currentTimeMillis()
                    try {
                        LinkClient.upload(this, h, pc, item) { done, total ->
                            val speed = done * 1000 / maxOf(1, System.currentTimeMillis() - started)
                            Link.update { it.copy(transfer = Transfer(item.name, false, done, total, speed)) }
                            updateOngoing(tr("Отправляю ${item.name}", "Sending ${item.name}"), if (total > 0) (done * 100 / total).toInt() else 0)
                        }
                        ok = true
                    } catch (e: Unauthorized) {
                        throw e
                    } catch (e: IOException) {
                        setOnline(null)
                        if (!item.canRetry) break
                        delay(1500)
                    }
                }
            } catch (_: Unauthorized) {
                item.close(); onUnauthorized(); return
            } finally {
                item.close()
                busy = false
            }
            val history = store.addHistory(HistoryItem(item.name, item.size, false, System.currentTimeMillis(), null, ok))
            Link.update { it.copy(history = history, transfer = null) }
            if (!ok) Link.say(tr("Не удалось отправить «${item.name}». Проверь, что ПК в той же сети Wi-Fi.",
                "Could not send “${item.name}”. Check that the PC is on the same Wi-Fi."))
            else if (Outbox.isEmpty) Link.say(tr("Отправлено на ПК ✓", "Sent to PC ✓"))
            updateOngoing(idleText(), force = true)
        }
    }

    /** Без фонового режима служба живёт, только пока открыт экран или идёт передача. */
    private suspend fun watchIdle() {
        while (scope.isActive) {
            delay(2000)
            if (!store.background && !Link.uiVisible.value && Outbox.isEmpty && !busy) { stopEverything(); return }
        }
    }

    private fun notifyReceived(name: String, uri: Uri) {
        val mime = MimeTypeMap.getSingleton().getMimeTypeFromExtension(name.substringAfterLast('.', "").lowercase()) ?: "*/*"
        val view = Intent(Intent.ACTION_VIEW).setDataAndType(uri, mime)
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
        val pi = PendingIntent.getActivity(this, notifyId, Intent.createChooser(view, name).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val n = NotificationCompat.Builder(this, CH_FILES)
            .setSmallIcon(R.drawable.ic_stat_moku)
            .setContentTitle(tr("Файл с ПК получен", "File received from PC"))
            .setContentText(name)
            .setContentIntent(pi)
            .setAutoCancel(true)
            .build()
        try { getSystemService(NotificationManager::class.java).notify(notifyId++, n) } catch (_: SecurityException) { }
    }
}
