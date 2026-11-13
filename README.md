# EscapeBook Launcher

Компактный десктопный лаунчер Minecraft на C# / .NET 8 / Windows Forms.
Тёмный интерфейс, оффлайн-запуск, автообновление сборки с GitHub, фикс дискретной
видеокарты через реестр и телеметрия железа в Discord.

## Возможности

- Никнейм сохраняется в `%APPDATA%\.EscapeBook\user_settings.json`.
- Ползунок ОЗУ: максимум автоматически рассчитывается по объёму физической памяти
  (общий объём минус 2 ГБ резерва для системы), выделить больше нельзя.
- Отдельное окно настроек: кастомные JVM-аргументы и ручной путь к `javaw.exe`.
- Единый прогресс: конфиг → скачивание сборки → распаковка → установка Minecraft →
  телеметрия → запуск.
- Сборка клиента скачивается ZIP-архивом с прогрессом и распаковывается с
  перезаписью. `options.txt` игрока не перезаписывается, если файл уже существует.
- `javaw.exe`, которым реально запускается игра, прописывается в
  `HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences` со значением
  `GpuPreference=2;` (высокопроизводительная видеокарта).
- Телеметрия: CPU, все видеокарты, общий/свободный объём ОЗУ, выделенная играм
  память и итоговые JVM-аргументы. Отправляется POST-запросом на Discord webhook,
  если `discord_webhook_url` заполнен.
- Запуск через [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) 4.1.0 в
  оффлайн-режиме. При `ExitCode != 0` показывается тёмное окно с ошибкой и ссылкой
  на `logs/latest.log`.

## Сборка

Нужен .NET 8 SDK (Windows):

```powershell
dotnet restore
dotnet build -c Release
dotnet run
```

Готовый `.exe` появится в `bin\Release\net8.0-windows\`. Рядом с ним уже лежит
`config.json` (копируется из корня проекта автоматически).

## Настройка GitHub

1. Загрузите `config.json` в репозиторий, например в ветку `main`.
2. В `Services/ConfigService.cs` замените заглушку:

   ```csharp
   public const string CONFIG_URL =
       "https://raw.githubusercontent.com/your-account/your-repo/main/config.json";
   ```

3. Загрузите ZIP сборки клиента (например, в GitHub Releases) и укажите ссылку в
   `client_zip_url`.
4. Укажите желаемую версию ванильного Minecraft в `mc_version` и, при
   необходимости, Discord webhook в `discord_webhook_url`.

### Поля config.json

| Поле | Назначение |
| --- | --- |
| `version` | Версия сборки. Сравнивается с `%APPDATA%\.EscapeBook\.client_version`. |
| `client_zip_url` | Прямая ссылка на ZIP с файлами клиента (mods, config, resourcepacks и т.д.). |
| `mc_version` | Версия Minecraft, которую устанавливает и запускает CmlLib.Core. |
| `default_jvm_args` | JVM-аргументы по умолчанию с GitHub. Объединяются с локальными. |
| `discord_webhook_url` | Webhook для телеметрии. Пусто — телеметрия отключена. |

Если GitHub недоступен, лаунчер сначала использует кэш
`%APPDATA%\.EscapeBook\.launcher_config.json`, затем локальный `config.json`
рядом с `.exe`. Если `client_zip_url` пуст, обновление сборки пропускается, а
Minecraft всё равно устанавливается и запускается.

## Где лежат данные

```
%APPDATA%\.EscapeBook\
├── .client_version          версия установленной сборки
├── .launcher_config.json    кэш удалённого конфига
├── user_settings.json       ник, ОЗУ, JVM-аргументы, путь к Java
├── launcher.log             журнал лаунчера
├── logs\latest.log          лог Minecraft
├── versions\                версии Minecraft (CmlLib)
├── libraries\               библиотеки
├── assets\                  ресурсы
├── runtime\                 скачанный рантайм Java
├── mods\ config\ ...        файлы вашей сборки из ZIP
└── options.txt              настройки игрока (не перезаписываются)
```

ZIP может содержать файлы в корне или в одной общей папке — она автоматически
срезается при распаковке. Защита от path traversal в архиве включена.

## JVM-аргументы

`default_jvm_args` + локальные аргументы объединяются. `-Xmx` и `-Xms` из текста
игнорируются, потому что память всегда задаётся ползунком. К аргументам также
добавляются оптимизации CmlLib по умолчанию (G1GC).

## Примечания

- Путь к Java: сначала ручной из настроек, затем рантайм, установленный CmlLib,
  затем системная Java (`JAVA_HOME`, `PATH`, папки популярных вендоров).
- Телеметрия отправляется до запуска игры и никогда не блокирует запуск при
  ошибке сети.
- Лаунчер не закрывает Minecraft при закрытии окна лаунчера.
- Для работы WMI-запросов нужен обычный Windows; пакет `System.Management` уже
  подключён.