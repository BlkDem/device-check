# winget

Пакет `BlkDem.DeviceCheckAgent`: `winget install BlkDem.DeviceCheckAgent`. Ставится тот же установщик Inno Setup, что и на сайте, для текущего пользователя.
Файл, скачанный winget, не получает пометку «из интернета», поэтому SmartScreen не показывает предупреждение.

Манифесты лежат в `manifests/b/BlkDem/DeviceCheckAgent/<версия>/`, так же, как в `microsoft/winget-pkgs`.

## Новая версия

1. Скопировать папку предыдущей версии и заменить в трёх файлах версию, `InstallerUrl`, `InstallerSha256` (из `SHA256SUMS.txt` релиза), `ReleaseDate` и `ReleaseNotesUrl`.
2. Проверить: `winget validate --manifest deploy/winget/manifests/b/BlkDem/DeviceCheckAgent/<версия>`.
3. Отправить PR в `microsoft/winget-pkgs`:
   ```bash
   bash deploy/winget/submit.sh <версия>
   ```
   При первом PR бот попросит принять CLA: ответьте в PR `@microsoft-github-policy-service agree`.
4. Проверку бот проводит автоматически, обычно от нескольких часов до пары дней. После слияния PR включите команду установки на сайте:
   ```bash
   gh variable set WINGET_ID --body "BlkDem.DeviceCheckAgent"
   ```
   Кнопка появится на странице `/agent` при следующем деплое сайта.
