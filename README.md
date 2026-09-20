# NexLauncher

Независимый Minecraft: Java Edition launcher на C# / Avalonia. Не связан с Mojang
или Microsoft.

## Возможности MVP

- Тёмный интерфейс: «Играть», «Сборки», «Настройки», журнал ошибок.
- Актуальный каталог Vanilla, релизы по умолчанию, снапшоты по желанию.
- Создание и сохранение сборок с конкретной версией и отдельной игровой папкой.
- Установка клиента, библиотек, ресурсов и Java через CmlLib.Core из источников Mojang.
- Прогресс и отмена установки, проверка и восстановление файлов перед запуском.
- Microsoft-вход в системном браузере, проверка доступа к Java Edition.
- Настройка RAM и ручного пути Java, журнал вывода игры.
- Защита от параллельного запуска; закрытие лаунчера не вызывает принудительного завершения игры.

Без входа можно настроить сборки и установить файлы. Для запуска полной игры
нужен Microsoft-аккаунт с доступом к Java Edition и собственный Client ID
приложения, допущенный к Minecraft API. См. [настройку входа](docs/MICROSOFT_AUTH.md).
Токены хранятся только в памяти до закрытия приложения.

## Запуск разработки

Нужен .NET 10 SDK:

    dotnet restore
    dotnet run --project NexLauncher.csproj

Для сборки без изменения старых отслеживаемых bin/ и obj/:

    dotnet build NexLauncher.csproj --configuration Release --artifacts-path .artifacts

Готовый executable будет в .artifacts/bin/NexLauncher/release/.

## Данные

На Windows: %LOCALAPPDATA%\NexLauncher.

- settings.json — сборки, выбранный профиль и публичный Client ID.
- instances/<guid>/game/ — отдельная игра, миры и настройки каждой сборки.
- catalogue/ — каталог версий.
- logs/launcher.log — журнал с ограничением размера и скрытием токенов.

Повреждённые настройки не перезаписываются молча: интерфейс показывает путь
и причину ошибки. Переименуй повреждённый файл для нового старта или исправь JSON.

## Проверки

Автоматические проверки не требуют аккаунта и не запускают Minecraft.
Окно отрисовывается в Avalonia Headless, снимки попадают в .artifacts/checks/.

    dotnet run --project tests/NexLauncher.Checks --configuration Release --artifacts-path .artifacts

Проверка реального каталога Mojang:

    dotnet run --project tests/NexLauncher.Checks --configuration Release --artifacts-path .artifacts -- --network

Необязательная проверка установки: скачивает Vanilla 1.21.1 и Java в отдельную
тестовую папку, затем проверяет восстановление отсутствующего client JAR.
Требует сеть, время и место на диске; игру не запускает:

    dotnet run --project tests/NexLauncher.Checks --configuration Release --artifacts-path .artifacts -- --network --install-smoke

Проверки охватывают валидацию путей/настроек, сохранение повреждённого файла,
выбор релизов/снапшотов, необходимость авторизации, отмену, повторный запуск,
обработку выхода, редактирование настроек и отрисовку окна.
Настоящий Microsoft-вход и полноценный игровой запуск проверяются вручную
после настройки собственного Client ID.

## Дальше

- Fabric и Forge.
- Управление модами и импорт сборок.
- Защищённое хранилище сессий в keychain ОС.
- Темы и дальнейшая настройка интерфейса.
