package com.drugalochek.mokumobile

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.core.content.IntentCompat
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** «Поделиться → Moku»: файлы из галереи, «Файлов», Telegram и т. д. сразу уходят на ПК. */
class ShareActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val store = Store(this)
        val pc = store.pc
        if (pc == null) {
            Toast.makeText(this, tr("Сначала подключи компьютер в Moku Mobile", "Pair your PC in Moku Mobile first"), Toast.LENGTH_LONG).show()
            startActivity(Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
            finish(); return
        }
        val i = intent
        val uris: List<Uri> = when (i?.action) {
            Intent.ACTION_SEND -> listOfNotNull(IntentCompat.getParcelableExtra(i, Intent.EXTRA_STREAM, Uri::class.java))
            Intent.ACTION_SEND_MULTIPLE -> IntentCompat.getParcelableArrayListExtra(i, Intent.EXTRA_STREAM, Uri::class.java) ?: emptyList()
            else -> emptyList()
        }.ifEmpty {
            val clip = i?.clipData
            if (clip == null) emptyList() else (0 until clip.itemCount).mapNotNull { clip.getItemAt(it).uri }
        }
        val text = i?.getStringExtra(Intent.EXTRA_TEXT)
        lifecycleScope.launch {
            val items = withContext(Dispatchers.IO) {
                if (uris.isNotEmpty()) uris.mapNotNull { Outbox.open(this@ShareActivity, it) }
                else if (!text.isNullOrBlank()) listOfNotNull(Outbox.text(this@ShareActivity, text))
                else emptyList()
            }
            if (items.isEmpty()) {
                Toast.makeText(this@ShareActivity, tr("Не удалось открыть файл", "Could not open the file"), Toast.LENGTH_SHORT).show()
            } else {
                Outbox.add(items)
                LinkService.start(this@ShareActivity)
                val what = if (items.size == 1) items[0].name else tr("${items.size} файлов", "${items.size} files")
                Toast.makeText(this@ShareActivity, tr("Отправляю на ${pc.name}: $what", "Sending to ${pc.name}: $what"), Toast.LENGTH_SHORT).show()
            }
            finish()
        }
    }
}
