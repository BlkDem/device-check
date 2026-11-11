# Заявка в SignPath Foundation (бесплатная подпись кода для open source)

Форма: https://signpath.org/apply. Ниже готовые ответы на английском, их можно копировать в форму.
Условия программы: https://signpath.org/terms.

Перед отправкой проверьте:
- [x] лицензия OSI (`LICENSE`, MIT);
- [x] сборка только в CI из исходников (`.github/workflows/release.yml`);
- [x] страница политики подписи ([CODE_SIGNING.md](../CODE_SIGNING.md)) с ролями и политикой приватности;
- [x] опубликованные релизы (GitHub Releases, https://device-check.umolab.ru/agent);
- [ ] включённая двухфакторная аутентификация на GitHub у всех, кто может пушить в `main` (требование программы).

---

**Project name:** Device Check Agent

**Repository:** https://github.com/BlkDem/device-check

**Homepage:** https://device-check.umolab.ru (download page: https://device-check.umolab.ru/agent)

**License:** MIT

**Short description:**
Device Check is an open-source hardware diagnostics project: a website that shows what the browser can tell about a PC,
and a small Windows agent for the full picture — CPU, memory modules, motherboard/BIOS, GPUs, monitors and drive
health (NVMe health log, SATA SMART) — shown in a local dashboard on 127.0.0.1. Read-only, no administrator rights,
no drivers or services, no telemetry; reports never leave the computer.

**What will be signed:**
- `device-check-agent-<version>-setup.exe` — Inno Setup per-user installer;
- `DeviceCheck.Agent.exe` (tray app) and `DeviceCheck.Agent.Cli.exe` (console), .NET 10, inside the installer and the portable ZIP files.

**Build process:**
Releases are built by GitHub Actions (`.github/workflows/release.yml`) on a `windows-latest` runner from a version
tag on `main`:
1. `dotnet test` (≈250 unit/integration tests);
2. `agent/build/publish.ps1`: `nuxt generate` for the embedded dashboard, `dotnet publish` (self-contained), Inno Setup;
3. an installer smoke test (silent install, run, silent uninstall);
4. GitHub Release + checksums.

We would add the SignPath GitHub Action between steps 2 and 3, signing the EXE files before packaging and the installer afterwards.

**Why signing is needed:**
Windows SmartScreen warns every user of the unsigned installer. The project has no company behind it, and commercial
certificates and Microsoft Store/Trusted Signing are not available to the maintainer in their country.

**Maintainers / roles:** @BlkDem — committer, reviewer and release approver (see CODE_SIGNING.md).

**Privacy:** the program does not transfer any information to other networked systems unless specifically requested by the user.
