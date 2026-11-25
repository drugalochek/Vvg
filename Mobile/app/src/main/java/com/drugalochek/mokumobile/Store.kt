package com.drugalochek.mokumobile

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.util.Locale
import java.util.UUID

/** Подключённый компьютер. Токен выдаёт Moku на ПК после сканирования QR-кода. */
data class Pc(val id: String, val name: String, val hosts: List<String>, val port: Int, val token: String)

data class HistoryItem(
    val name: String,
    val size: Long,
    val incoming: Boolean,
    val time: Long,
    val uri: String?,
    val ok: Boolean,
)

fun tr(ru: String, en: String): String = if (Locale.getDefault().language == "ru") ru else en

fun formatSize(bytes: Long): String {
    if (bytes < 0) return "—"
    val units = if (Locale.getDefault().language == "ru") listOf("Б", "КБ", "МБ", "ГБ") else listOf("B", "KB", "MB", "GB")
    var v = bytes.toDouble()
    var i = 0
    while (v >= 1024 && i < units.size - 1) { v /= 1024; i++ }
    return if (i == 0) "$bytes ${units[0]}" else String.format(Locale.getDefault(), if (v < 10) "%.1f %s" else "%.0f %s", v, units[i])
}

class Store(context: Context) {
    private val p = context.applicationContext.getSharedPreferences("moku", Context.MODE_PRIVATE)

    val phoneId: String
        @Synchronized get() = p.getString("phoneId", null)
            ?: UUID.randomUUID().toString().replace("-", "").also { p.edit().putString("phoneId", it).apply() }

    var pc: Pc?
        @Synchronized get() {
            val token = p.getString("token", null) ?: return null
            return Pc(
                p.getString("pcId", "") ?: "",
                p.getString("pcName", "PC") ?: "PC",
                (p.getString("hosts", "") ?: "").split(',').filter { it.isNotBlank() },
                p.getInt("port", LinkClient.PORT),
                token,
            )
        }
        @Synchronized set(v) {
            val e = p.edit()
            if (v == null) e.remove("token").remove("pcId").remove("pcName").remove("hosts").remove("lastHost")
            else e.putString("token", v.token).putString("pcId", v.id).putString("pcName", v.name)
                .putString("hosts", v.hosts.distinct().joinToString(",")).putInt("port", v.port)
            e.apply()
        }

    var lastHost: String?
        get() = p.getString("lastHost", null)
        set(v) { p.edit().putString("lastHost", v).apply() }

    /** Принимать файлы, когда приложение закрыто (уведомление «Moku на связи»). */
    var background: Boolean
        get() = p.getBoolean("background", true)
        set(v) { p.edit().putBoolean("background", v).apply() }

    @Synchronized
    fun history(): List<HistoryItem> {
        val arr = try { JSONArray(p.getString("history", "[]")) } catch (_: Exception) { JSONArray() }
        return (0 until arr.length()).mapNotNull { i ->
            val o = arr.optJSONObject(i) ?: return@mapNotNull null
            HistoryItem(
                o.optString("n"), o.optLong("s", -1), o.optBoolean("in"), o.optLong("t"),
                o.optString("u").ifBlank { null }, o.optBoolean("ok", true),
            )
        }
    }

    @Synchronized
    fun addHistory(item: HistoryItem): List<HistoryItem> {
        val list = (listOf(item) + history()).take(50)
        val arr = JSONArray()
        list.forEach {
            arr.put(JSONObject().put("n", it.name).put("s", it.size).put("in", it.incoming).put("t", it.time)
                .put("u", it.uri ?: "").put("ok", it.ok))
        }
        p.edit().putString("history", arr.toString()).apply()
        return list
    }

    @Synchronized
    fun clearHistory() { p.edit().remove("history").apply() }
}
