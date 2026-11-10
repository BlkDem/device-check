# Деплой и релизы

## Как устроено

| Что | Когда | Workflow |
|---|---|---|
| Проверки: тесты сайта и агента, типы, статическая сборка | пуш в любую ветку, кроме `main`, и PR | `.github/workflows/ci.yml` |
| Сайт → `device-check.umolab.ru` | пуш в `main` с изменениями в `web/`, вручную (Run workflow) | `.github/workflows/deploy-web.yml` |
| Релиз агента: установщик, ZIP, GitHub Release, файлы на сайте | тег `vX.Y.Z` | `.github/workflows/release.yml` |

**Сайт** собирается статически (`nuxt generate`) и раздаётся nginx из `/var/www/fastuser/data/www/device-check.umolab.ru`. Node.js на сервере не нужен. Выгрузка идёт через rsync с `--delay-updates`: новые файлы появляются разом. Папку `downloads/` деплой сайта не трогает.

**Релиз агента** собирается на Windows-раннере:
- `agent/build/publish.ps1 -Installer`: тесты, установщик Inno Setup и ZIP-архивы;
- smoke-тест: тихая установка, запуск консольной версии, удаление;
- GitHub Release;
- выкладка в `downloads/agent/` на сайте вместе с `latest.json`. Его читает страница `/agent`; предыдущая версия удаляется.

Версия берётся из тега. Её же стоит обновить в `agent/Directory.Build.props`.

```bash
git tag v0.2.1 && git push origin v0.2.1
```

## Разовая настройка сервера

```bash
bash deploy/setup-deploy-key.sh
```

Скрипт:
- делает папку сайта `user1:fastuser` с правами 2775, как у остальных сайтов `user1`;
- добавляет в `authorized_keys` пользователя `user1` отдельный ключ для GitHub Actions, ограниченный `rrsync` этой папкой (без shell и доступа к другим путям); резервная копия `authorized_keys` сохраняется;
- записывает секреты репозитория `DEPLOY_SSH_KEY`, `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_KNOWN_HOSTS`.

Нужны SSH-доступ к `user1@fp.cloud28.umolab.ru` и `gh`, авторизованный в репозитории.

Отозвать доступ: удалить строку `github-actions device-check deploy` из `~/.ssh/authorized_keys` на сервере.

## Рекомендуется в FASTPANEL

- Включить редирект HTTP → HTTPS для `device-check.umolab.ru`. Агент принимает оба адреса, но https надёжнее.
