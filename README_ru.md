<p align="center">
  <img src="assets/logo.png" alt="WinYandex" width="120" height="120" />
</p>

<h1 align="center">WinYandex</h1>

<p align="center">
  <strong>Современный нативный клиент Яндекс Музыки для Windows 10/11</strong>
</p>

<p align="center">
  <a href="README.md">English</a> • <strong>Русский</strong>
</p>

<p align="center">
  <a href="https://github.com/pixiemays/WinYandex"><img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D4?logo=windows" alt="Platform" /></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet" alt=".NET 8" /></a>
  <a href="https://github.com/microsoft/WindowsAppSDK"><img src="https://img.shields.io/badge/UI-WinUI%203%20%2F%20Windows%20App%20SDK-0078D4" alt="WinUI 3" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-GPL--3.0-blue.svg" alt="License" /></a>
</p>

<p align="center">
  <img src="images/banner.png" alt="WinYandex Banner" width="900" />
</p>

---

**WinYandex** — быстрый и легковесный десктопный плеер для Яндекс Музыки, разработанный с нуля на нативном стеке Windows (WinUI 3 + Windows App SDK + C# .NET 8) взамен веб-оболочки Electron.

<p align="center">
  <img src="images/now_playing.jpg" alt="Режим «Сейчас играет» с синхронизированными текстами" width="850" />
</p>

<p align="center">
  <img src="images/wave.jpg" alt="Моя волна и фильтры настроения" width="850" />
</p>

<p align="center">
  <img src="images/playlist.jpg" alt="Просмотр плейлистов с отметками кэша и FCK" width="850" />
</p>

<p align="center">
  <img src="images/album.jpg" alt="Просмотр альбомов и организация закреплений" width="850" />
</p>

---

## Возможности

* **Fluent Design** — визуальный интерфейс в стиле Windows 11 с материалом Mica, плавными переходами и переключением тёмной и светлой тем.
* **Чёткая типографика** — экранный шрифт Inter по умолчанию с возможностью выбора фирменного Yandex Sans или системного Segoe UI Variable в настройках.
* **Дисковый аудио-кэш (LRU)** — сохранение прослушанных треков в локальный кэш (`%LocalAppData%/YMusic/Cache`) для мгновенного повторного воспроизведения (0 мс) без расхода трафика.
* **Бесшовное воспроизведение (Gapless)** — предзагрузка аудиопотока и текстов следующего трека на 80% прогресса текущей песни для устранения пауз и буферизации.
* **Синхронизированные тексты песен и подстройка тайминга** — выдвижная панель воспроизведения с построчным ведением текста в реальном времени и возможностью индивидуального сдвига тайминга (±0.5 с, сохраняется для каждого трека).
* **Игровой оверлей** — компактное всплывающее окно прямо поверх полноэкранных игр и приложений без необходимости сворачиваться по Alt+Tab.
* **Виджет для OBS** — встроенный локальный сервер (`http://localhost:21337/widget`) для вывода стильной анимированной карточки текущего трека на стрим.
* **10-полосный эквалайзер** — настройка звуковых частот от 31 Гц до 16 кГц с набором готовых пресетов (Rock, Pop, Bass Boost, Acoustic, Vocal и др.).
* **Моя волна** — персональный бесконечный поток музыки с фильтрами настроения (*Спокойное, Бодрое, Радостное, Грустное*) и занятий (*Работа, Отдых, Дорога, Спорт*).
* **FckCensor** — автоматический выбор оригинальных треков без заглушений и цензуры вместо цензурированных правообладателями версий.
* **Discord Rich Presence** — отображение названия трека, исполнителя, обложки альбома и статуса воспроизведения в профиле Discord.
* **Импорт плейлистов из Spotify** — перенос плейлистов в Яндекс Музыку.
* **Интеграция с Windows**:
  * System Media Transport Controls (SMTC) — системное окно управления и поддержка мультимедийных клавиш клавиатуры и гарнитур.
  * Taskbar Thumbnail Toolbar — управление воспроизведением прямо из миниатюры на панели задач Windows.
  * Системный трей — иконка в трее Windows с контекстным меню и опцией сворачивания при закрытии окна.
  * Toast-уведомления — всплывающие системные баннеры при переключении песен.
* **Двуязычный интерфейс** — мгновенное переключение между русским и английским языками без перезапуска приложения.

---

## Установка и запуск

### 1. Установщик Setup.exe (Рекомендуется)
Скачайте актуальный установщик `Setup-x64.exe` из раздела [Releases](https://github.com/pixiemays/WinYandex/releases/latest). Установщик настроит ярлыки в меню «Пуск» и на рабочем столе.

### 2. Портативная версия (ZIP)
Скачайте архив `win-x64-portable.zip` из раздела [Releases](https://github.com/pixiemays/WinYandex/releases/latest), распакуйте в любую папку и запустите `WinYandex.exe`. Приложение полностью автономно и не требует установки сторонних библиотек.

---

## Сборка из исходников

### Требования
* Windows 10 (1809+) или Windows 11
* [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* Visual Studio 2022 (с компонентами разработки под Windows) или VS Code с C# Dev Kit

### Сборка и запуск:
```powershell
# Клонирование репозитория
git clone https://github.com/pixiemays/WinYandex.git
cd WinYandex

# Запуск в режиме разработки
dotnet run --project src/YMusic.WinUI/YMusic.WinUI.csproj
```

### Упаковка релиза и дистрибутивов:
```powershell
powershell -ExecutionPolicy Bypass -File scripts/package_release.ps1
```
*Файлы дистрибутива сохраняются в каталог `dist/`.*

---

## Архитектура и стек

* **Интерфейс:** Windows App SDK (WinUI 3) + .NET 8
* **Архитектура:** MVVM (CommunityToolkit.Mvvm)
* **Аудио:** Windows MediaFoundation / NAudio
* **Шрифты:** Inter Variable & Yandex Sans Text
* **Интеграции:** DiscordRichPresence, Windows.Media.SystemMediaTransportControls, Microsoft.Windows.AppNotifications

---

## Отказ от ответственности (Disclaimer)

* WinYandex является неофициальным клиентом с открытым исходным кодом.
* Товарные знаки «Яндекс», «Яндекс Музыка» и связанные с ними сервисы принадлежат ООО «Яндекс».
* Проект не аффилирован с компанией Яндекс.
* Для доступа к личной медиатеке и прослушивания треков в высоком качестве требуется активная подписка Яндекс Плюс.

---

## Лицензия

Распространяется под свободной копилефт-лицензией [GNU General Public License v3.0](LICENSE). Все модификации и производные работы обязаны оставаться открытыми под той же лицензией. Проект защищён от закрытия исходного кода и проприетарной перепродажи.
