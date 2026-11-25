package com.drugalochek.mokumobile

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.keyframes
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.KeyboardArrowUp
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.MutableState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import kotlinx.coroutines.delay
import java.text.DateFormat
import java.util.Date

private val Bg = Color(0xFF000000)
private val CardTop = Color(0xFF17171B)
private val CardBottom = Color(0xFF0B0B0D)
private val Rim = Color(0x26FFFFFF)
private val Dim = Color(0xFFA8A8B2)
private val Faint = Color(0x1FFFFFFF)
private val Accent = Color(0xFF0A84FF)
private val Green = Color(0xFF30D158)
private val Red = Color(0xFFFF6B6B)
private val Radius = 22.dp

interface Actions {
    fun scan()
    fun manual(host: String, code: String)
    fun sendFiles()
    fun sendMedia()
    fun open(item: HistoryItem)
    fun unpair()
    fun setBackground(on: Boolean)
    fun clearHistory()
    fun dismissMessage()
    fun reconnect()
}

@Composable
fun MokuTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = darkColorScheme(
            primary = Color.White, onPrimary = Color.Black, background = Bg, onBackground = Color.White,
            surface = CardTop, onSurface = Color.White, surfaceVariant = CardTop, secondary = Accent,
        ),
        content = content,
    )
}

@Composable
fun MokuApp(actions: Actions, background: MutableState<Boolean>) {
    val state by Link.state.collectAsStateWithLifecycle()
    var manual by remember { mutableStateOf(false) }
    var confirmUnpair by remember { mutableStateOf(false) }

    Box(Modifier.fillMaxSize().background(Bg)) {
        Column(
            Modifier.fillMaxSize().statusBarsPadding().navigationBarsPadding().verticalScroll(rememberScrollState())
                .padding(horizontal = 18.dp, vertical = 14.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Header(state)
            val pc = state.pc
            if (pc == null) PairCard(state.pairing, actions::scan) { manual = true }
            else {
                PcCard(pc, state, actions::reconnect)
                PrimaryButton(tr("Отправить файлы на ПК", "Send files to PC"), enabled = true, onClick = actions::sendFiles)
                SecondaryButton(tr("Фото и видео", "Photos & videos"), onClick = actions::sendMedia)
                AnimatedVisibility(state.transfer != null || state.queued > 0, enter = fadeIn(), exit = fadeOut()) {
                    TransferCard(state.transfer, state.queued)
                }
                BackgroundRow(background.value) { background.value = it; actions.setBackground(it) }
                HistoryCard(state.history, actions::open, actions::clearHistory)
                TextButton(onClick = { confirmUnpair = true }, modifier = Modifier.align(Alignment.CenterHorizontally)) {
                    Text(tr("Отключить компьютер", "Unlink PC"), color = Red, fontSize = 14.sp)
                }
            }
            Spacer(Modifier.height(56.dp))
        }

        state.message?.let { msg ->
            LaunchedEffect(msg) { delay(4500); actions.dismissMessage() }
            Box(
                Modifier.align(Alignment.BottomCenter).navigationBarsPadding().padding(16.dp).fillMaxWidth()
                    .clip(RoundedCornerShape(18.dp)).background(Color(0xFF1C1C21)).border(1.dp, Rim, RoundedCornerShape(18.dp))
                    .clickable { actions.dismissMessage() }.padding(horizontal = 16.dp, vertical = 14.dp)
            ) { Text(msg, color = Color.White, fontSize = 14.sp) }
        }
    }

    if (manual) ManualDialog(onDismiss = { manual = false }) { h, c -> manual = false; actions.manual(h, c) }
    if (confirmUnpair) AlertDialog(
        onDismissRequest = { confirmUnpair = false },
        containerColor = CardTop,
        title = { Text(tr("Отключить компьютер?", "Unlink the PC?")) },
        text = { Text(tr("Чтобы снова передавать файлы, нужно будет отсканировать QR-код.", "You will need to scan the QR code again to transfer files."), color = Dim) },
        confirmButton = { TextButton(onClick = { confirmUnpair = false; actions.unpair() }) { Text(tr("Отключить", "Unlink"), color = Red) } },
        dismissButton = { TextButton(onClick = { confirmUnpair = false }) { Text(tr("Отмена", "Cancel"), color = Color.White) } },
    )
}

@Composable
private fun Header(state: UiState) {
    Row(Modifier.fillMaxWidth().padding(top = 6.dp, bottom = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        IslandLogo(Modifier.width(64.dp).height(30.dp), state.online)
        Spacer(Modifier.width(12.dp))
        Column(Modifier.weight(1f)) {
            Text("Moku", color = Color.White, fontSize = 24.sp, fontWeight = FontWeight.Bold)
            Text("Mobile", color = Dim, fontSize = 13.sp)
        }
    }
}

/** Островок Moku с моргающими глазами — как на рабочем столе. */
@Composable
fun IslandLogo(modifier: Modifier, awake: Boolean) {
    val t = rememberInfiniteTransition(label = "blink")
    val blink by t.animateFloat(
        initialValue = 1f, targetValue = 1f, label = "eyes",
        animationSpec = infiniteRepeatable(keyframes {
            durationMillis = 4200
            1f at 0; 1f at 3800; 0.1f at 3900 using LinearEasing; 1f at 4050
        }, RepeatMode.Restart),
    )
    val glow by animateColorAsState(if (awake) Green else Dim, tween(400), label = "dot")
    Canvas(modifier) {
        val r = size.height / 2
        drawRoundRect(Color.Black, cornerRadius = CornerRadius(r, r))
        drawRoundRect(Rim, cornerRadius = CornerRadius(r, r), style = androidx.compose.ui.graphics.drawscope.Stroke(1.dp.toPx()))
        val ew = size.height * 0.22f
        val eh = size.height * 0.36f * blink
        for (cx in listOf(size.width * 0.40f, size.width * 0.60f))
            drawRoundRect(Color.White, Offset(cx - ew / 2, size.height / 2 - eh / 2), Size(ew, eh), CornerRadius(ew / 2, ew / 2))
        drawCircle(glow, radius = size.height * 0.09f, center = Offset(size.width - r * 0.75f, size.height / 2))
    }
}

@Composable
private fun Card(modifier: Modifier = Modifier, content: @Composable ColumnScope.() -> Unit) {
    val shape = RoundedCornerShape(Radius)
    Column(
        modifier.fillMaxWidth().clip(shape).background(Brush.verticalGradient(listOf(CardTop, CardBottom)))
            .border(1.dp, Rim, shape).padding(16.dp),
        content = content,
    )
}

@Composable
private fun PrimaryButton(text: String, enabled: Boolean = true, busy: Boolean = false, onClick: () -> Unit) {
    val shape = RoundedCornerShape(18.dp)
    Box(
        Modifier.fillMaxWidth().height(56.dp).clip(shape).background(if (enabled) Color.White else Color(0x66FFFFFF))
            .clickable(enabled = enabled && !busy, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        if (busy) CircularProgressIndicator(Modifier.size(22.dp), color = Color.Black, strokeWidth = 2.5.dp)
        else Text(text, color = Color.Black, fontSize = 16.sp, fontWeight = FontWeight.SemiBold)
    }
}

@Composable
private fun SecondaryButton(text: String, onClick: () -> Unit) {
    val shape = RoundedCornerShape(18.dp)
    Box(
        Modifier.fillMaxWidth().height(50.dp).clip(shape).background(Faint).border(1.dp, Rim, shape).clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { Text(text, color = Color.White, fontSize = 15.sp, fontWeight = FontWeight.Medium) }
}

@Composable
private fun PairCard(pairing: Boolean, onScan: () -> Unit, onManual: () -> Unit) {
    Card {
        Text(tr("Подключи компьютер", "Connect your PC"), color = Color.White, fontSize = 20.sp, fontWeight = FontWeight.SemiBold)
        Spacer(Modifier.height(10.dp))
        Step(1, tr("На ПК в Moku: Настройки → Система → включи «Передача файлов с телефоном».", "On the PC in Moku: Settings → System → turn on “File transfer with phone”."))
        Step(2, tr("Открой вкладку с иконкой телефона — там появится QR-код.", "Open the phone-icon tab — a QR code appears there."))
        Step(3, tr("Отсканируй его. Телефон и ПК должны быть в одной сети Wi-Fi.", "Scan it. The phone and the PC must be on the same Wi-Fi."))
        Spacer(Modifier.height(14.dp))
        PrimaryButton(tr("Сканировать QR-код", "Scan QR code"), busy = pairing, onClick = onScan)
        Spacer(Modifier.height(4.dp))
        TextButton(onClick = onManual, modifier = Modifier.align(Alignment.CenterHorizontally)) {
            Text(tr("Ввести адрес и код вручную", "Enter address and code manually"), color = Dim, fontSize = 14.sp)
        }
    }
}

@Composable
private fun Step(n: Int, text: String) {
    Row(Modifier.padding(vertical = 5.dp), verticalAlignment = Alignment.Top) {
        Box(Modifier.size(24.dp).clip(CircleShape).background(Faint), contentAlignment = Alignment.Center) {
            Text("$n", color = Color.White, fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
        }
        Spacer(Modifier.width(10.dp))
        Text(text, color = Dim, fontSize = 14.sp, lineHeight = 19.sp, modifier = Modifier.padding(top = 2.dp))
    }
}

@Composable
private fun PcCard(pc: Pc, state: UiState, onRetry: () -> Unit) {
    Card {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.size(10.dp).clip(CircleShape).background(if (state.online) Green else Color(0xFF55555C)))
            Spacer(Modifier.width(10.dp))
            Column(Modifier.weight(1f)) {
                Text(pc.name, color = Color.White, fontSize = 18.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(
                    if (state.online) tr("На связи · ${state.host ?: ""}", "Connected · ${state.host ?: ""}")
                    else tr("Не найден в сети", "Not found on the network"),
                    color = Dim, fontSize = 13.sp,
                )
            }
            if (!state.online) TextButton(onClick = onRetry) { Text(tr("Найти", "Retry"), color = Accent) }
        }
        if (!state.online) {
            Spacer(Modifier.height(8.dp))
            Text(
                tr("Проверь, что Moku запущен, передача включена и оба устройства в одном Wi-Fi. Файлы встанут в очередь.",
                    "Make sure Moku is running, transfer is on and both devices share one Wi-Fi. Files will be queued."),
                color = Dim, fontSize = 12.sp, lineHeight = 16.sp,
            )
        }
    }
}

@Composable
private fun TransferCard(t: Transfer?, queued: Int) {
    Card {
        if (t == null) {
            Text(tr("В очереди: $queued", "Queued: $queued"), color = Color.White, fontSize = 15.sp)
            Spacer(Modifier.height(8.dp))
            LinearProgressIndicator(Modifier.fillMaxWidth().height(6.dp).clip(CircleShape), color = Accent, trackColor = Faint)
            return@Card
        }
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                (if (t.incoming) tr("Получаю", "Receiving") else tr("Отправляю", "Sending")) + " · " + t.name,
                color = Color.White, fontSize = 15.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f),
            )
            val pct = if (t.total > 0) (t.done * 100 / t.total).toInt() else 0
            Text("$pct%", color = Dim, fontSize = 14.sp)
        }
        Spacer(Modifier.height(10.dp))
        LinearProgressIndicator(
            progress = { if (t.total > 0) (t.done.toFloat() / t.total).coerceIn(0f, 1f) else 0f },
            modifier = Modifier.fillMaxWidth().height(6.dp).clip(CircleShape),
            color = if (t.incoming) Green else Accent, trackColor = Faint,
        )
        Spacer(Modifier.height(8.dp))
        val rest = if (queued > 0) tr(" · ещё $queued в очереди", " · $queued more queued") else ""
        Text("${formatSize(t.done)} / ${formatSize(t.total)} · ${formatSize(t.bytesPerSec)}/s$rest", color = Dim, fontSize = 12.sp)
    }
}

@Composable
private fun BackgroundRow(on: Boolean, onChange: (Boolean) -> Unit) {
    Card {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(tr("Принимать в фоне", "Receive in background"), color = Color.White, fontSize = 15.sp, fontWeight = FontWeight.Medium)
                Text(
                    tr("Файлы с ПК придут, даже когда приложение закрыто", "Files from the PC arrive even when the app is closed"),
                    color = Dim, fontSize = 12.sp, lineHeight = 16.sp,
                )
            }
            Spacer(Modifier.width(10.dp))
            Switch(
                checked = on, onCheckedChange = onChange,
                colors = SwitchDefaults.colors(checkedTrackColor = Green, checkedThumbColor = Color.White, uncheckedTrackColor = Faint, uncheckedBorderColor = Rim),
            )
        }
    }
}

@Composable
private fun HistoryCard(items: List<HistoryItem>, onOpen: (HistoryItem) -> Unit, onClear: () -> Unit) {
    Card {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(tr("Передачи", "Transfers"), color = Color.White, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
            if (items.isNotEmpty()) TextButton(onClick = onClear) { Text(tr("Очистить", "Clear"), color = Dim, fontSize = 13.sp) }
        }
        if (items.isEmpty()) {
            Spacer(Modifier.height(6.dp))
            Text(
                tr("Пока пусто. Перетащи файл на вкладку «Телефон» в Moku или нажми «Поделиться → Moku» в любом приложении.",
                    "Nothing yet. Drop a file on Moku's Phone tab or use Share → Moku in any app."),
                color = Dim, fontSize = 13.sp, lineHeight = 18.sp,
            )
        }
        val fmt = remember { DateFormat.getTimeInstance(DateFormat.SHORT) }
        items.take(30).forEach { item ->
            Row(
                Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).clickable(enabled = item.uri != null) { onOpen(item) }
                    .padding(vertical = 8.dp, horizontal = 2.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                val (icon: ImageVector, tint: Color) = when {
                    !item.ok -> Icons.Filled.Warning to Red
                    item.incoming -> Icons.Filled.KeyboardArrowDown to Green
                    else -> Icons.Filled.KeyboardArrowUp to Accent
                }
                Box(Modifier.size(34.dp).clip(CircleShape).background(tint.copy(alpha = 0.16f)), contentAlignment = Alignment.Center) {
                    Icon(icon, contentDescription = null, tint = tint, modifier = Modifier.size(20.dp))
                }
                Spacer(Modifier.width(12.dp))
                Column(Modifier.weight(1f)) {
                    Text(item.name, color = Color.White, fontSize = 14.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    val status = when {
                        !item.ok -> tr("не отправлено", "failed")
                        item.incoming -> tr("с ПК", "from PC")
                        else -> tr("на ПК", "to PC")
                    }
                    Text("${formatSize(item.size)} · ${fmt.format(Date(item.time))} · $status", color = Dim, fontSize = 12.sp)
                }
            }
        }
    }
}

@Composable
private fun ManualDialog(onDismiss: () -> Unit, onConnect: (String, String) -> Unit) {
    var host by remember { mutableStateOf("") }
    var code by remember { mutableStateOf("") }
    val colors = OutlinedTextFieldDefaults.colors(
        focusedBorderColor = Color.White, unfocusedBorderColor = Rim, focusedLabelColor = Color.White, unfocusedLabelColor = Dim, cursorColor = Color.White,
    )
    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = CardTop,
        title = { Text(tr("Подключение вручную", "Manual connection")) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text(tr("Адрес и код показаны на вкладке «Телефон» под QR-кодом.", "The address and code are shown under the QR code on the Phone tab."), color = Dim, fontSize = 13.sp)
                OutlinedTextField(host, { host = it.trim() }, label = { Text(tr("Адрес ПК, например 192.168.1.5", "PC address, e.g. 192.168.1.5")) },
                    singleLine = true, colors = colors, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri))
                OutlinedTextField(code, { v -> code = v.filter { it.isDigit() }.take(6) }, label = { Text(tr("Код из 6 цифр", "6-digit code")) },
                    singleLine = true, colors = colors, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword))
            }
        },
        confirmButton = { TextButton(onClick = { onConnect(host, code) }) { Text(tr("Подключить", "Connect"), color = Color.White) } },
        dismissButton = { TextButton(onClick = onDismiss) { Text(tr("Отмена", "Cancel"), color = Dim) } },
    )
}
