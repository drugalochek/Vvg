# Moku — Dynamic Moku для Windows

## Запуск из исходников
    cd Island
    dotnet run -c Release

## Как получить Moku.exe
1. Поставь .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
2. Запусти build.bat (или команду ниже в папке проекта)
       dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
3. Готовый файл: publish\Moku.exe (маленький, ~1–3 МБ).
   На других ПК нужен .NET 8 Desktop Runtime.
   Чтобы работал везде без установки .NET, замени --self-contained false на true (файл ~70 МБ).

## Управление
- Клик по острову или Alt+Space — раскрыть / свернуть; Esc или клик мимо — свернуть
- Alt+Space — раскрыть / свернуть
- Иконка Moku в системном трее: двойной клик — открыть, ПКМ — открыть / свернуть / выйти

## Скриншоты (плитка «Область»)
- Выдели область мышью → внизу появится панель: Перо, Стрелка, Рамка, Маркер, цвета, толщина
- Enter или Ctrl+C — копировать в буфер, Ctrl+S — сохранить PNG, Ctrl+Z — отменить, Esc — закрыть
- «Экран» — снимок всего экрана сразу в буфер обмена

## Настройки (вкладка «Настройки»)
Положение (слева / по центру / справа), отступ сверху, размер острова, цвет для музыки и событий,
автозапуск. Хранятся в %APPDATA%\Moku\settings.json

## Жидкий остров
- Пока играет музыка, нижний край острова превращается в «слаймовую» волну цвета акцента (в свёрнутом и в раскрытом виде).
- Слева в свёрнутом острове — глазки: следят за курсором, моргают, под музыку покачиваются, а если мышь долго стоит — засыпают.
- Справа — время и значок погоды с температурой (Open-Meteo, место определяется по IP, обновление раз в 15 минут; если сеть недоступна, приложение повторяет попытку и показывает последнее сохранённое значение).

## Производительность и стабильность
- Убрана принудительная полная сборка мусора и очистка working set из UI-потока.
- Движение волны и слежение глаз синхронизированы с кадрами WPF-композиции и используют реальный delta time.
- Clipboard обрабатывается с debounce и повторными попытками, если его временно удерживает другое приложение.
- Погода повторяет запросы при ошибках сети; последнее успешное значение хранится в `%APPDATA%\Moku\weather-cache.json`.
- Иконка системного трея освобождается при штатном завершении приложения.


## Установщик и публикация релиза

### Собрать установщик Windows x64
1. Установи **.NET 8 SDK**. Inno Setup не требуется: установщик Moku — отдельное WPF-приложение.
2. Открой папку `Island` и запусти `build-release.bat`.
3. Получишь два файла:
   - `Island\\publish\\Moku.exe` — автономное приложение (не требует установленного .NET Runtime).
   - `Island\\release\\Moku-Setup-2.5.6-x64.exe` — полноценный установщик с мастером установки, ярлыками, автозапуском и деинсталляцией через «Программы и компоненты».

### Опубликовать на GitHub Releases
Чтобы опубликовать релиз, собери файлы командой выше и приложи `Island\\publish\\Moku.exe` и `Island\\release\\Moku-Setup-2.5.6-x64.exe` к GitHub Release. Перед публичным релизом рекомендуется подписать EXE/установщик Authenticode-сертификатом, чтобы Windows SmartScreen меньше предупреждал пользователей.


## English UI and system tray

The application interface and screenshot editor use English labels. The installer defaults to English and also offers Russian. Moku stays running in the Windows notification area when its window is closed; double-click the tray icon to reopen it, or right-click for Open, Hide, and Exit. The tray icon is extracted from the icon embedded in Moku.exe, so it also works with single-file publishing.


## Interface language (English / Русский)

Open **Settings → Language** and choose **English** or **Русский**. The choice is saved in `%APPDATA%\Moku\settings.json` and is also available from the Moku tray icon context menu (**Language / Язык**). The screenshot annotation toolbar follows the same language setting. The installer itself offers English and Russian setup languages.


## License

Moku is proprietary software. All rights are reserved by the copyright holder. See the [LICENSE](LICENSE) file for the full terms. No permission to copy, modify, redistribute, sublicense, or create derivative works is granted except where the copyright holder gives prior written permission. Third-party components remain subject to their respective licenses.
