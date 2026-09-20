# Microsoft-вход в NexLauncher

Лаунчер открывает официальный вход Microsoft в системном браузере через MSAL.
Пароль не проходит через интерфейс NexLauncher. После входа сервис получает
Xbox/Minecraft-сессию и проверяет доступ к Java Edition и игровой профиль.
Подставные offline-сессии не используются для запуска полной игры.

## Настройка собственного приложения

1. Зарегистрируй приложение в [Microsoft Entra / Azure Portal](https://portal.azure.com/).
2. Разреши личные Microsoft-аккаунты. Библиотека использует tenant consumers.
   Подходят personal accounts или organizational directories + personal accounts.
3. В Authentication добавь платформу **Mobile and desktop applications** и
   redirect URI **http://localhost**. Используется системный браузер с loopback redirect.
4. Разреши public client flows, как описано в инструкции CmlLib.
   Client Secret не создавай: это публичное desktop-приложение.
5. Скопируй Application (client) ID в NexLauncher → Настройки →
   Подключение Microsoft и нажми «Сохранить настройки».
6. По документации CmlLib, для Minecraft API требуется допуск Client ID
   (allowlist). Одна регистрация в Entra не гарантирует доступ.
   Следуй актуальной процедуре по ссылкам ниже; без допуска возможен HTTP 403.
7. Нажми «Войти». Выбери аккаунт, у которого есть доступ к Minecraft: Java Edition
   и создан игровой профиль. Запуск потребует успешной проверки на серверах.

Документация:

- [CmlLib: Client ID и допуск Minecraft API](https://cmllib.github.io/CmlLib.Core-wiki/en/auth.microsoft/xboxauthnet.game.msal/clientid/)
- [CmlLib: вход с MSAL](https://cmllib.github.io/CmlLib.Core-wiki/en/auth.microsoft/cmllib.core.auth.microsoft/authentication-with-msal/)
- [Официальная справка Minecraft, на которую ссылается CmlLib](https://help.minecraft.net/hc/en-us/articles/16254801392141)
- [Официальная пробная версия](https://www.minecraft.net/en-us/free-trial)
- [Minecraft EULA](https://www.minecraft.net/en-us/eula)

## Хранение сессии

В текущем MVP Microsoft/Xbox/Minecraft-токены хранятся **только в памяти**.
Дисковый MSAL cache и стандартное файловое хранилище аккаунтов CmlLib не подключены.
После закрытия приложения потребуется вход снова; существующая сессия браузера
может упростить его. Перед каждым запуском токены обновляются и доступ проверяется.

settings.json содержит публичный Client ID, параметры сборок и выбранный профиль,
но не пароль и не токены. Журнал скрывает известные секреты и токены из вывода игры.
Не добавляй Client Secret в настройки, исходники или Git.

## Что доступно без входа

Создание сборок, список релизов/снапшотов, настройка RAM/Java и установка Vanilla
работают без аккаунта. Полная игра запускается после Microsoft-входа.
Официальная инструкция бесплатной пробной версии также предусматривает
Microsoft-аккаунт; отдельного обхода авторизации или прав на полную игру здесь нет.

Это описание реализации и ссылки на источники, а не подтверждение Mojang
о сертификации стороннего лаунчера. NexLauncher не связан с Mojang/Microsoft.

## Проверка вручную

Реальный интерактивный вход требует настроенного приложения; автоматические
тесты не входят в чужой аккаунт и не сохраняют токены.

- Установить Vanilla, затем войти с настроенным Client ID.
- Проверить выбор аккаунта в браузере и появление Minecraft-имени в заголовке.
- Запустить игру, проверить появление меню, логи и возврат кнопки после выхода.
- Проверить отмену входа и сообщение для аккаунта без доступа к Java Edition.
- Перезапустить лаунчер: сборки должны сохраниться, сессия — потребовать вход снова.
