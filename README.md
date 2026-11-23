# FitFix

Виправляє тренування з битим GPS: час і сенсори з оригінального FIT + геометрія з правильного треку (FIT/GPX/TCX) → коректний FIT для Strava.

Навіщо: в умовах війни GPS в Україні часто працює погано, активності виходять зі зламаною дистанцією й темпом — і зникає мотивація тренуватись. FitFix повертає активності правдивий вигляд.

Статус: планування за BMAD (brief, PRD, UX готові). Ядро, CLI, веб-API і сторінка працюють — сервіс піднімається одною командою. Деталі — у [CLAUDE.md](CLAUDE.md) і [спайку](docs/spikes/spike-dotnet-fit-sdk-2026-11-19.md).

## Запуск

Усе одною командою: один образ віддає зібраний фронтенд і `/api/*` з одного origin.

```bash
docker compose up        # далі http://localhost:8080
```

Розробка — два процеси, Vite проксює `/api` на Kestrel:

```bash
dotnet run --project src/FitFix.Api                    # Kestrel на :5000
npm --prefix web install && npm --prefix web run dev   # сторінка на http://localhost:5173
```

Тести:

```bash
dotnet test
npm --prefix web run test
```

CLI:

```bash
dotnet run --project src/FitFix.Cli -- repair ОРИГІНАЛ.fit ТРЕК.gpx -o РЕЗУЛЬТАТ.fit
```

Приклад виводу:

```
Вид спорту:        Running
Дистанція:         3,90 км → 6,50 км
Загальний час:     36:00
Темп:              9:13/км → 5:32/км
Надійна зона:      до 21:35 (60 % активності), якорів: 1129
GPS завис на:      21:36
Попередження:      GPS_STALL_DETECTED
```

## Структура

| Шлях | Що це |
|---|---|
| `src/FitFix.Core` | Ядро: геометрія, вирівнювання, читання/запис FIT, читачі GPX і TCX |
| `src/FitFix.Cli` | CLI `fitfix repair` |
| `src/FitFix.Api` | Minimal API: `/api/inspect`, `/api/repair`, контракт відмов, зібраний фронт із `wwwroot` |
| `web/` | Фронтенд: React 19 + Vite + Tailwind 4, словник `src/i18n/uk.ts` |
| `Dockerfile`, `compose.yaml` | Один образ: фронт збирається в Node, API публікується в SDK, працює на `aspnet:10.0-alpine` без root і з read-only ФС |
| `tests/FitFix.Core.Tests` | xunit-тести + генератор синтетичної пробіжки з битим GPS |
| `prototype/` | Початковий Python-прототип алгоритму (довідково) |
| `docs/` | BMAD-артефакти: brief, PRD, UX, спайки |

Реальні файли з GPS-даними — лише в `tests/fixtures/private/` (не потрапляють у репозиторій).
