# NexLauncher

Независимый Minecraft: Java Edition launcher на C# / Avalonia.
Не связан с Mojang или Microsoft. Текущая сборка рассчитана на Windows.

## Возможности

- «Играть», «Сборки», «Настройки», журнал запуска и понятные ошибки.
- Каталог Vanilla: релизы по умолчанию, снапшоты по желанию.
- Отдельные версии, игровые папки, миры, RAM и Java для каждой сборки.
- Установка клиента, библиотек, ресурсов и Java через CmlLib.Core;
  прогресс, отмена и восстановление файлов перед запуском.
- Microsoft → Xbox → Minecraft: штатный Windows OAuth CmlLib без собственного
  Client ID, несколько аккаунтов, выбор активного, обновление сессии и удаление.
- Quick CSS: пользовательские цвета, фоны PNG/JPEG, типографика, отступы,
  скругления и состояния controls, перезагрузка и автообновление.
- Закрытие лаунчера не завершает уже запущенный процесс игры.

Без входа доступны создание сборок и установка файлов. Для запуска полной
игры нужен Microsoft-аккаунт с доступом к Minecraft: Java Edition.

Подробности:

- [Quick CSS: полный справочник, ограничения и примеры](docs/QUICK_CSS.md).
- [Готовая тема Plum Evening](Assets/quickcss.example.css).
- [Microsoft authentication, защищённый кеш и ручные проверки](docs/MICROSOFT_AUTH.md).

## Запуск разработки

Нужны Windows, .NET 10 SDK и Microsoft Edge WebView2 Runtime для входа.
Сборка использует Windows Desktop runtime из-за штатного окна OAuth библиотеки.

    dotnet restore NexLauncher.csproj --artifacts-path .artifacts
    dotnet build NexLauncher.csproj --configuration Release --artifacts-path .artifacts
    dotnet run --project NexLauncher.csproj --configuration Release --artifacts-path .artifacts

Executable: .artifacts/bin/NexLauncher/release/NexLauncher.exe.
Параметр artifacts-path позволяет не изменять старые отслеживаемые bin/ и obj/.

CmlLib.Core 4.0.6, Auth.Microsoft 3.3.1 и Avalonia 12.1.0 сохранены.
Добавлен System.Security.Cryptography.ProtectedData 10.0.12 для DPAPI.
Прямые зависимости MSAL, нужные прежней реализации с собственным Client ID,
удалены: Windows использует встроенную OAuth-конфигурацию CmlLib.

## Quick CSS за минуту

Открой «Настройки» → «Quick CSS» → «Создать пример». Лаунчер скопирует
полную тему в папку данных, включит и применит её. Нажми «Открыть в редакторе»
и меняй переменные в начале файла. При включённом автообновлении достаточно
сохранить CSS.

Для своего файла нажми «Выбрать файл», включи Quick CSS и нажми «Применить».
«Перезагрузить» сохраняет выбранные параметры и перечитывает тему.
Чтобы вернуть стандартное оформление, выключи переключатель и нажми «Применить».

Это ограниченный API Avalonia, не браузерный CSS. Тема не исполняет код,
не управляет аккаунтами и не скачивает ресурсы. Локальные фоновые изображения
разрешены только внутри папки темы. Полный reference — в документации выше.

## Аккаунты и данные

На Windows данные находятся в %LOCALAPPDATA%\NexLauncher.

- settings.json — сборки, активная сборка, настройки Quick CSS; без токенов.
  Старое публичное поле MicrosoftClientId сохранено для совместимости конфигурации
  и не используется текущим Windows-входом.
- auth/accounts.dat — весь кеш аккаунтов и активный аккаунт, зашифрованные
  DPAPI CurrentUser. Прочитать его может текущий пользователь Windows.
- themes/ — примеры пользовательских CSS-файлов.
- instances/<guid>/game/ — отдельная игра, миры и настройки каждой сборки.
- catalogue/ — каталог версий.
- logs/launcher.log — ограниченный журнал с редактированием токенов.

В настройках можно добавить несколько аккаунтов. Выбери профиль и нажми
«Использовать»; «Выйти / удалить» удаляет выбранный аккаунт с этого устройства.
Аватар загружается с официального textures.minecraft.net; без сети показывается
буква имени. Перед каждым запуском выполняется обновление сессии, проверка
доступа к Java Edition и получение актуального Minecraft-профиля.

Повреждённые настройки и защищённый кеш не перезаписываются молча.
Удаление аккаунта из NexLauncher не завершает отдельную браузерную сессию
Microsoft/WebView2. Повторный вход показывает выбор аккаунта.

## Проверки

    dotnet run --project tests/NexLauncher.Checks --configuration Release --artifacts-path .artifacts

Console harness проверяет настройки, parser, runtime Quick CSS, DPAPI, операции
аккаунтов с тестовым backend, передачу сессии в запуск, отмену и отрисовку окна.
Настоящее окно Avalonia рендерится в Headless; снимки, включая применённую
тему, сохраняются в .artifacts/checks/. Для DPAPI-проверок нужен Windows.

Реальный каталог Mojang:

    dotnet run --project tests/NexLauncher.Checks --configuration Release --artifacts-path .artifacts -- --network

Необязательная проверка полной установки скачивает Vanilla 1.21.1 и Java
в отдельную тестовую папку, затем проверяет восстановление client JAR.
Нужны сеть, время и место; игра не запускается:

    dotnet run --project tests/NexLauncher.Checks --configuration Release --artifacts-path .artifacts -- --network --install-smoke

Реальный интерактивный вход, аккаунт без Java Edition, два Microsoft-аккаунта,
silent refresh после перезапуска и полноценный игровой запуск нужно проверить
вручную с собственными аккаунтами. Автоматические проверки их не симулируют
как успешную работу внешних сервисов.

## Дальше

Fabric/Forge, управление модами и импорт сборок. Для Linux/macOS нужен отдельный
OAuth backend с собственным зарегистрированным Client ID и хранилище секретов ОС;
границы IAccountService / IMinecraftAuthenticationBackend / IAccountVault
позволяют добавить их без переписывания UI и механизма запуска.
