---
name: FitFix
status: final
updated: 2026-11-19
agent: UX Designer (Sally) — bmad-ux, fast path
colors:
  bg: "#0B0D10"
  surface: "#14171C"
  surface-raised: "#1B1F26"
  border: "#262B33"
  text: "#F2F4F7"
  text-muted: "#8A93A3"
  accent: "#C8F135"        # Відновлений трек, головні CTA
  accent-ink: "#0B0D10"    # текст на accent
  original: "#7C8594"      # Оригінальний (битий) GPS — пунктир
  estimated: "#F5B83D"     # Розрахована зона
  heart: "#FF5C7A"         # пульс
  danger: "#FF5C5C"
  info: "#5CC8FF"
typography:
  display:
    fontFamily: "Unbounded"
    fontWeight: 700
    letterSpacing: "-0.02em"
  body:
    fontFamily: "Manrope"
    fontWeight: 500
    fontSize: 15px
    lineHeight: 1.5
  metric:
    fontFamily: "Manrope"
    fontWeight: 800
    fontFeature: "tnum"
rounded:
  sm: 8px
  md: 14px
  lg: 22px
  pill: 999px
spacing:
  unit: 4px
  gutter: 24px
  section: 48px
components:
  button-primary:
    background: "{colors.accent}"
    color: "{colors.accent-ink}"
    rounded: "{rounded.pill}"
  dropzone:
    background: "{colors.surface}"
    border: "1.5px dashed {colors.border}"
    rounded: "{rounded.lg}"
  card:
    background: "{colors.surface}"
    border: "1px solid {colors.border}"
    rounded: "{rounded.lg}"
---

## Brand & Style

Ніч, асфальт, ліхтарі на пробіжці. Темний графітовий інтерфейс, один яскравий лаймовий акцент — колір «виправленого». Відчуття точного спортивного приладу, не «софтвера». Голос спокійно-впевнений, з легкою іронією до глюків GPS.

Anti-references: помаранчевий Strava-брендинг (ми не Strava), «дашборд з 20 віджетами», скевоморфні годинники.

## Colors

- `accent` використовується тільки для Відновленого треку, головної CTA і позитивних дельт. Не більше 10 % площі екрана.
- `original` — завжди пунктиром, щоб битий трек читався як «чернетка». Це стосується і карти, і графіка: серії **ніколи** не відрізняються лише відтінком — виправлена лінія суцільна, оригінальна пунктирна, і кожна має прямий підпис.
- `estimated` — тільки для Розрахованої зони (смуга на графіку, сегмент на карті, бейдж).
- Контраст тексту ≥ 4.5:1 на `surface`.

## Typography

- `display` (Unbounded) — заголовок, крок-лейбли, великі числа в hero-метриках.
- `body` (Manrope) — усе інше. Кирилиця підтримується обома шрифтами.
- Метрики — `tnum`, щоб числа не «стрибали».

## Layout & Spacing

- Десктоп: контейнер 1200 px, двоколонкова сітка результату (карта 7/12, метрики 5/12), графік на всю ширину.
- Мобайл (< 768 px): одна колонка, карта 320 px висотою.

## Elevation & Depth

Без тіней-«хмар». Глибина — через `surface` → `surface-raised` і тонкий `border`. Hero-картка має м'яке лаймове світіння (radial gradient 6 % opacity).

## Shapes

Великі скруглення (`lg`) для карток і drop-зон, `pill` для кнопок і бейджів.

## Components

- **Dropzone:** іконка файлу, заголовок ролі («Оригінальне тренування»), підказка форматів. Стан «заповнено» показує назву файлу, дату, тривалість, дистанцію і кнопку ×.
- **Metric delta:** «3,91 км → **6,54 км**», де нове значення має колір `accent`.
- **Zone bar:** горизонтальна смуга на всю тривалість: `accent` = Надійна, `estimated` = Розрахована.
- **Warning callout:** ліва смужка кольору `estimated`/`danger`, іконка, одне речення.

## Do's and Don'ts

- Do: показувати числа великими, а пояснення — дрібно під ними.
- Do: завжди пояснювати, що частину треку розраховано.
- Don't: використовувати `accent` для декору.
- Don't: показувати сирі JSON/технічні коди без людського тексту.
