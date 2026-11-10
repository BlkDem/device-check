# Device Check Agent (Windows)

Портативный сборщик данных об оборудовании для Device Check. Читает конфигурацию ПК через Windows API, WMI/CIM, SMBIOS, DXGI/D3D12 и EDID и формирует отчёт **HardwareReport v1.0** в том же формате, что и сайт ([web/lib/types.ts](../web/lib/types.ts)).

**Статус:** сборщик и JSON (этап 1), трей-приложение с локальным API, интерфейсом служит сайт Device Check. Загрузка отчёта на сервер появится вместе с бэкендом.

- Не требует установки и прав администратора (манифест `asInvoker`).
- Ничего не отправляет в сеть, не пишет на диски (кроме JSON по явной команде `--out`), не ставит драйверы и службы, телеметрии нет.
- Серийные номера, имя компьютера, Processor ID, MAC-адреса не собираются. Экспорт дополнительно вычищает такие ключи, даже если сборщик их заполнил.

## Как это работает

1. Пользователь распаковывает ZIP и запускает `DeviceCheck.Agent.exe`. Окна нет: агент прячется в область уведомлений и сразу начинает сканирование.
2. Агент открывает сайт ссылкой `<site>/#agent=<порт>&token=<токен>`. Сайт забирает отчёт у агента через `http://127.0.0.1:<порт>` и показывает его на дашборде. Режим «Windows Agent» включает 7 категорий, повторный скан тоже идёт через агента. Фрагмент `#…` не уходит ни на один сервер, а сайт сразу убирает его из адресной строки.
3. Меню трея (правая кнопка):
   - **Открыть Device Check** — то же делают левый клик и повторный запуск EXE;
   - **Сканировать заново**;
   - **Сохранить отчёт JSON…**;
   - **Копировать JSON**;
   - **Выход**.

   В шапке меню — версия, статус последнего скана и краткая сводка (CPU · RAM · GPU).
4. **Выход** закрывает порт и завершает процесс. Агент не ставит службы и автозапуск, после выхода ничего не остаётся.

Одновременно работает только один экземпляр, второй запуск лишь открывает сайт. Отчёты хранятся только в памяти процесса, на диск они попадают лишь через «Сохранить».

### Настройки (необязательно)

`DeviceCheck.Agent.json` рядом с EXE:

```json
{ "site_url": "https://devicecheck.example", "allowed_origins": ["https://staging.devicecheck.example"], "port": 47615, "open_site_on_start": true }
```

Аргументы командной строки: `--site <url>`, `--port <n>`, `--no-open`. По умолчанию сайт — `http://localhost:3000`, порт — `47615`. Если порт занят, агент берёт случайный свободный, и он попадает в ссылку.

### Локальный API

Модуль `DeviceCheck.Agent.LocalApi` (Kestrel):

| Метод | Путь | Токен | Ответ |
|---|---|---|---|
| GET | `/api/status` | нет | `status` (idle/scanning/ready), версии, `last_scan` |
| GET | `/api/version` | нет | `agent_version`, `schema_version`, `api_version` |
| POST | `/api/scan` | да | запускает скан или присоединяется к идущему → `{ id, created_at }` |
| GET | `/api/report` | да | последний HardwareReport |
| GET | `/api/report/{id}` | да | отчёт текущего запуска по id |

Защита:
- сокет слушает только `127.0.0.1`;
- проверяется заголовок `Host` (защита от DNS rebinding);
- `Origin` сверяется с белым списком, CORS только для него, включая `Access-Control-Allow-Private-Network` для Chrome;
- токен — 256 бит, свой на каждый запуск, сравнивается за постоянное время;
- набор эндпоинтов фиксирован: нет команд и доступа к файлам;
- тело запроса ограничено 1 КБ, заголовки — 16 КБ;
- ответы с `Cache-Control: no-store`.

Сайт, открытый не по ссылке из трея, токена не знает и отчёт не получит.

### Консольный сборщик

```powershell
DeviceCheck.Agent.Cli.exe                     # сводка в консоли
DeviceCheck.Agent.Cli.exe --out .             # + сохранить device-check-report-YYYY-MM-DD-HH-mm.json
DeviceCheck.Agent.Cli.exe --json --quiet      # JSON в stdout
DeviceCheck.Agent.Cli.exe --timeout 30        # таймаут на категорию, с (по умолчанию 20)
```

Коды выхода: `0` — успех, `1` — какой-то сборщик упал или превысил таймаут (отчёт всё равно сформирован), `2` — неверные аргументы, `3` — отчёт не прошёл валидацию, `130` — отмена (Ctrl+C).

## Сборка

Нужен .NET SDK 10.

```powershell
cd agent
dotnet build DeviceCheck.Agent.slnx
dotnet test  tests/DeviceCheck.Agent.Tests
dotnet run --project src/DeviceCheck.Agent.Cli -- --out .

# Portable ZIP (тесты → publish → zip в agent/dist)
powershell -ExecutionPolicy Bypass -File build/publish.ps1                 # win-x64
powershell -ExecutionPolicy Bypass -File build/publish.ps1 -Runtime win-arm64
```

Получаются три архива:

| Архив | Что внутри | Размер | Требования |
|---|---|---|---|
| `device-check-agent-<ver>-win-x64-self-contained.zip` | `DeviceCheck.Agent.exe` (трей) со встроенным .NET | ≈ 56 МБ | ничего |
| `device-check-agent-<ver>-win-x64-framework.zip` | `DeviceCheck.Agent.exe` + `DeviceCheck.Agent.Cli.exe` | ≈ 0,7 МБ | .NET 10 Desktop Runtime + ASP.NET Core Runtime |
| `device-check-agent-cli-<ver>-win-x64-self-contained.zip` | консольный `DeviceCheck.Agent.Cli.exe` | ≈ 31 МБ | ничего |

Trimming и Native AOT выключены: System.Text.Json работает через reflection, WMI — через COM, а WinForms trimming не поддерживает. Иконка генерируется из геометрии логотипа сайта скриптом `build/make-icon.ps1`.

## Структура

```
agent/
├── src/
│   ├── DeviceCheck.Agent.Core         модель HardwareReport и Field<T>, IHardwareCollector, оркестратор,
│   │                                  интерфейсы источников, парсеры SMBIOS/EDID/CIM-дат, справочники
│   ├── DeviceCheck.Agent.Collectors   сборщики: system, cpu, motherboard, memory, gpu, storage, display
│   ├── DeviceCheck.Agent.Windows      реализации источников: WMI (с таймаутами), SMBIOS, CPUID и топология,
│   │                                  DXGI/D3D12, QueryDisplayConfig + EDID, реестр и системные API
│   ├── DeviceCheck.Agent.Monitoring   System Health Monitor: Core / Storage / Smart / Filesystems (см. ниже)
│   ├── DeviceCheck.Agent.Export       сериализация (snake_case, UTF-8 без BOM), очистка идентификаторов, валидатор
│   ├── DeviceCheck.Agent.LocalApi     локальный HTTP API для сайта (Kestrel, только 127.0.0.1)
│   ├── DeviceCheck.Agent.UI           трей-приложение DeviceCheck.Agent.exe (WinForms NotifyIcon), настройки
│   └── DeviceCheck.Agent.Cli          консольный сборщик
├── tests/DeviceCheck.Agent.Tests      xUnit: фикстуры источников, нормализация, сборщики, JSON, контракт, API, настройки
└── build/                             publish.ps1 (portable ZIP), make-icon.ps1
```

Сборщики зависят только от интерфейсов (`IWmiClient`, `ISmbiosSource`, `IProcessorSource`, `IGraphicsSource`, `IDisplaySource`, `IPlatformSource`). Поэтому их можно тестировать на фикстурах без реального железа, а для поддержки другой ОС достаточно новых реализаций источников. Фикстуры есть только в тестах, в реальном сканировании они не используются.

Оркестратор запускает категории параллельно (по умолчанию до 3 одновременно), у каждой свой таймаут. Ошибка или зависание одного сборщика не мешает остальным: он получает статус `partial`, `failed` или `timeout`, а причина попадает в `diagnostics`. WMI-запросы тоже ограничены по времени.

## System Health Monitor — накопители (этап 1: сбор данных)

```powershell
DeviceCheck.Agent.Cli.exe storage                    # сводка в консоли
DeviceCheck.Agent.Cli.exe storage --json             # StorageHealthReport в stdout
DeviceCheck.Agent.Cli.exe storage --out .            # device-check-storage-health-YYYY-MM-DD-HH-mm.json
DeviceCheck.Agent.Cli.exe storage --quick            # только место и температура, без SMART (для частого опроса)
```

Модуль `DeviceCheck.Agent.Monitoring`:

| Папка | Что внутри |
|---|---|
| `Core` | `StorageHealthReport` (те же `Field<T>`, `ScanInfo`, `Limitation`, diagnostics, что у HardwareReport), `IHealthMonitor<T>`, `IStorageHealthCollector`, `IStorageDeviceSource` |
| `Smart` | разбор NVMe Health Log (02h) и Identify Controller, ATA SMART (данные + пороги), каталог атрибутов, маппинг в поля |
| `Storage` | инвентаризация дисков, сборщики `temperature` / `nvme` / `ata_smart` / `reliability`, `StorageHealthMonitor` |
| `Filesystems` | тома и свободное место, привязка томов к физическим дискам |

Низкоуровневые запросы — `DeviceCheck.Agent.Windows/StorageDeviceSource.cs`. Это `IOCTL_STORAGE_QUERY_PROPERTY` на `\\.\PhysicalDriveN`: дескриптор открывается с нулевыми правами доступа, поэтому возможны только запросы, без чтения и записи данных диска.

Источники и права:

| Данные | Источник | Без админа |
|---|---|---|
| диски, модель, шина, тип, объём, статус здоровья Windows | `MSFT_PhysicalDisk`, `MSFT_Disk`, `Win32_DiskDrive` | да |
| NVMe Health Information (износ, резерв, наработка, ошибки, объёмы чтения и записи, температура и сенсоры) | `StorageDeviceProtocolSpecificProperty`, log 02h | да (проверено на Crucial P3 / stornvme) |
| пороги температуры NVMe (WCTEMP / CCTEMP) | Identify Controller | да |
| температура от драйвера (любые диски) | `StorageDeviceTemperatureProperty`, затем `StorageAdapterTemperatureProperty` | да, если драйвер поддерживает |
| SATA SMART: атрибуты, пороги, PredictFailure | `root\wmi MSStorageDriver_ATAPISmartData / FailurePredictThresholds / FailurePredictStatus` | **нет**, нужен администратор |
| счётчики надёжности Windows (износ, ошибки, max температура) | `MSFT_StorageReliabilityCounter` | **нет**, нужен администратор |
| тома, метки, ФС, свободное место, связь с дисками | `MSFT_Volume`, `MSFT_Partition`, `System.IO.DriveInfo` | да |

Правила:
- `Win32_DiskDrive` не считается источником SMART.
- У атрибутов ATA общепринятый смысл получают только те, что одинаковы у всех производителей: 4, 5, 9, 10, 12, 193, 194, 196–199. Значение помечается `detected`. Для остальных (износ SSD, объёмы записи 241/242, …) — raw, normalized и threshold без интерпретации, причина `vendor_specific`. Неизвестные id попадают в отчёт без названия.
- NVMe `Percentage Used` — это доля израсходованного ресурса, а не остаток; может быть больше 100. Объёмы считаются по спецификации: Data Units × 512 000 байт. 128-битные счётчики, не помещающиеся в 64 бита, помечаются `value_out_of_range`, а не обрезаются.
- Пороги температуры берутся только у производителя. Общие ориентировочные пороги и классификация здоровья (`health.status`) появятся на этапе 2 (Health Engine). Сейчас `status` = `unavailable` / `not_evaluated`, и отсутствие SMART никогда не превращается в плохой статус.
- Идентификатор диска — хэш его UniqueId или серийного номера. Сам серийный номер в отчёт не попадает.
- Модуль только читает данные: SMART-команды, меняющие состояние, самотесты, запись на диск, очистка и исправление ФС не используются.

Ограничения Windows:
- SATA SMART и счётчики надёжности доступны только администратору.
- USB-мосты обычно не пропускают SMART: причина `usb_bridge_no_passthrough`.
- RAID-контроллеры скрывают физические диски.
- Виртуальные диски SMART не имеют.
- Температура от драйвера поддерживается не везде: на этой машине stornvme отвечает Win32 1117, и температура берётся из NVMe-лога.

## Правила достоверности

- `exact` — значение прочитано из ОС, прошивки или оборудования.
- `detected` — значение выведено: по справочнику CPUID, из названия, из префикса парт-номера или вычислено (например, частота шины памяти = MT/s / 2).
- `approximate` — значение измерено или усреднено: текущая частота CPU.
- `unavailable` — `value: null` и код причины в `reason`: `not_reported`, `privacy`, `no_reliable_source`, `firmware_placeholder`…

Память:
- скорость указывается в MT/s, тактовая частота вынесена в отдельное производное поле;
- текущая (configured) скорость не смешивается с максимальной (`rated_speed_mts`);
- распаянная память не считается слотами;
- режим каналов определяется только по явным меткам каналов в SMBIOS, иначе ставится `unknown`;
- данные SMBIOS о слотах лежат отдельно, в `memory.smbios_slots`.

CPU: `MaxClockSpeed` из WMI — это номинальная (базовая) частота, а не boost. Поэтому `max_clock_mhz` и `tdp_w` честно помечены как `unavailable`.

## Что не определяется

| Параметр | Причина |
|---|---|
| `cpu.max_clock_mhz`, `cpu.tdp_w` | в Windows нет надёжного источника без драйвера |
| `motherboard.chipset` | определяется только у Intel по имени LPC/eSPI-контроллера; у AMD — `unavailable` |
| `memory.max_speed_mts`, `memory.modules[].model` | прошивка такие данные не отдаёт |
| `gpu[].architecture` | нет системного источника (в браузерном режиме берётся из WebGPU) |
| `storage[].form_factor`, сырые SMART-атрибуты | Storage API не отдаёт форм-фактор; SMART требует прав администратора, а агент показывает только `HealthStatus` |
| слоты памяти | SMBIOS может описывать несуществующие слоты, поэтому значение помечено как `detected` |
| USB / сеть / аудио / PCI | отдельные сборщики, следующий этап |

## Тесты

`dotnet test tests/DeviceCheck.Agent.Tests` — 227 тестов:
- нормализация CPU: вендор, бренд, поколение, микроархитектура;
- RAM: DDR3/DDR4/DDR5/LPDDR, MT/s ↔ МГц, слоты, распаянная память, ECC, каналы, смешанные модули, фолбэк SMBIOS → WMI;
- несколько GPU, несколько дисков и мониторов, ошибки и таймауты WMI, зависшие сборщики, отмена;
- JSON: сериализация и десериализация без потерь, валидатор, UTF-8 без BOM, удаление идентификаторов, единицы измерения в именах полей;
- контракт: тест читает `web/lib/types.ts` и проверяет, что в JSON агента есть каждый ключ сайта. Исключение — явный список полей, которые бывают только в браузере;
- переводы: у каждого кода причины, пометки и ограничения из отчёта агента есть перевод в `web/i18n/en.ts` и `ru.ts`;
- локальный API: токен, чужой Origin, DNS rebinding, preflight/PNA, лимит тела запроса, неизвестные пути, занятый порт, освобождение порта при выходе, недоступность из локальной сети, общий скан для одновременных запросов;
- настройки трея: файл, аргументы, битый файл, токен передаётся только во фрагменте ссылки;
- мониторинг накопителей:
  - разбор NVMe-лога (смещения, 128-битные счётчики, Data Units → байты, Percentage Used > 100) и Identify;
  - ATA SMART: пороги, 48-битный raw, пустые и повторяющиеся слоты, интерпретируются только стандартные атрибуты;
  - NVMe, SATA SSD, HDD, USB, виртуальный диск;
  - несколько разделов на одном диске, том на двух дисках, subst;
  - без прав администратора, отказ в доступе к NVMe, битые SMART-данные, недоступный WMI, зависший сборщик, быстрый режим;
  - расчёт свободного места, JSON без серийных номеров, стабильные id.

Сквозная проверка: настоящий API агента и сайт в headless Chrome (1366 и 375 px, RU и EN). Режим агента, 7 шагов, повторный скан — без ошибок в консоли, без непереведённых ключей, без горизонтального переполнения.

На реальном железе проверено на одной машине: AMD Ryzen 7 7840HS, DDR5 2 × 32 GiB SODIMM, Radeon 780M, NVMe, встроенная панель BOE 165 Гц, Windows 11. Intel, DDR4, дискретная GPU, две GPU и распаянная RAM пока покрыты только фикстурами.
