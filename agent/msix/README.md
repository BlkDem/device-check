# Device Check Agent в Microsoft Store

Пакеты из Store подписывает Microsoft, поэтому SmartScreen их не предупреждает. Обновления Store доставляет сам.

Пакет: `device-check-agent-<ver>-win-x64.msix`. Это полнодоверенное настольное приложение (`runFullTrust`): трей, WMI, IOCTL накопителей и локальный дашборд на `127.0.0.1` работают так же, как в установщике. Собирается в CI на каждом релизе. Артефакт `store-package` можно скачать на странице запуска workflow «Release agent». В публичные загрузки пакет не попадает: без подписи Store его нельзя установить.

Локальная сборка: `powershell -File agent/build/publish.ps1 -Msix -SkipTests`. Нужен Windows SDK (`makeappx`).

## Первая публикация (делает владелец аккаунта)

1. **Аккаунт разработчика** в [Partner Center](https://partner.microsoft.com/dashboard): для физлиц бесплатный, для компаний — разовый взнос. Проверьте, что регистрация доступна для вашей страны и способа оплаты.
2. **Apps and games → New product → MSIX or PWA app**, зарезервировать имя `Device Check Agent`.
3. **Product management → Product identity.** Скопировать три значения в переменные репозитория:
   ```bash
   gh variable set MSIX_IDENTITY_NAME --body "12345Publisher.DeviceCheckAgent"   # Package/Identity/Name
   gh variable set MSIX_PUBLISHER --body "CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX"  # Package/Identity/Publisher
   gh variable set MSIX_PUBLISHER_DISPLAY_NAME --body "Device Check"            # Package/Properties/PublisherDisplayName
   ```
   Без них пакет собирается с заглушками, и Store его не примет.
4. **Выпустить релиз** (`git tag vX.Y.Z && git push origin vX.Y.Z`) и скачать артефакт `store-package`.
5. **Submission:**
   - **Packages** — загрузить `.msix`.
   - **Properties** — категория *Utilities & tools*, политика конфиденциальности `https://device-check.umolab.ru/privacy`, сайт `https://device-check.umolab.ru`.
   - **Restricted capabilities** — обоснование для `runFullTrust`: *Desktop app: reads hardware information (WMI, SMBIOS, storage IOCTL) read-only and shows it in a local dashboard served on 127.0.0.1. No drivers, no services, no data sent to servers.*
   - **Age ratings** — анкета IARC.
   - **Store listings (ru, en)** — описание, скриншоты (дашборд и раздел «Здоровье накопителей»), значок 300×300.
6. После сертификации указать ссылку на приложение:
   ```bash
   gh variable set STORE_URL --body "https://apps.microsoft.com/detail/<product-id>"
   ```
   При следующем деплое на странице `/agent` появится кнопка «Microsoft Store» как рекомендуемый вариант. Установщик и ZIP останутся для тех, у кого Store недоступен.

## Следующие версии

Каждый релиз собирает новый `.msix` с версией `X.Y.Z.0`. Его загружают в новую submission. Автоматизировать это можно через Microsoft Store Submission API (`msstore` CLI) после первой ручной публикации.

## Чем Store-версия отличается
- Нет автозапуска при входе. В пакетах он делается через StartupTask, а для этого агенту нужно определять запуск по задаче, чтобы не открывать сайт. Это запланировано.
- Файл `DeviceCheck.Agent.json` рядом с EXE не поддерживается: папка пакета только для чтения. Используются настройки по умолчанию.
