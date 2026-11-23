/**
 * Every string a person reads, in one place.
 *
 * AD-18 and AD-4: an exception, a `ProblemDetails` body and a warning all carry a code from
 * `DiagnosticCodes` and never Ukrainian text. This file is the other half of that bargain —
 * the only place the text exists. A component that writes a sentence of its own breaks the
 * arrangement quietly, so `uk.test.ts` holds the `codes` keys below equal to the core's
 * registry, and no component may hold a literal.
 *
 * The voice of EXPERIENCE.md: short, на «ти», and an error says what happened and what to do.
 */

/**
 * One Ukrainian text per code of `FitFix.Core.Diagnostics.DiagnosticCodes`. The keys are the
 * codes themselves, spelled as the registry spells them, because that is what the interface
 * is handed — a response says `GPS_STALL_DETECTED` and this is where it becomes a sentence.
 */
export const codeTexts = {
  // --- Warnings: the repair worked, with a caveat.
  NO_ORIGINAL_GPS:
    'У тренуванні немає жодної GPS-точки, тож прив’язати трек ні до чого. Дистанцію розподілено рівномірно за часом.',
  GPS_STALL_DETECTED:
    'GPS завис посеред тренування — саме те, що ми вміємо лагодити. Далі дистанцію взято з треку.',
  FEW_ANCHORS:
    'Збіглося надто мало точок, тож більшу частину дистанції розраховано, а не виміряно. Перевір результат на карті.',
  POSITIONS_DISCARDED:
    'Частину координат відкинуто: за ними ти рухався фізично неможливо швидко. На решту результату вони не впливали.',
  TRACK_FAR_FROM_ORIGINAL:
    'Трек починається далеко від місця, де почалося тренування. Схоже, це різні маршрути — перевір, чи не переплутав файли.',
  TRACK_LONGER_THAN_ACTIVITY:
    'Трек довший, ніж можна пройти за час цього тренування. Підтвердь, що трек правильний, — інакше ми зупинимося на правдоподібній частині.',

  // --- Errors: the core refuses the input.
  NOT_A_FIT_FILE:
    'Це не схоже на FIT-файл. Спробуй експортувати оригінал зі Strava або Garmin Connect.',
  FIT_INTEGRITY_FAILED:
    'FIT-файл пошкоджено — він не пройшов перевірку цілісності. Завантаж його з годинника чи сервісу ще раз.',
  FIT_HAS_NO_RECORDS:
    'У цьому FIT немає записів активності, лише службові дані. Потрібен файл самого тренування.',
  TRACK_TOO_FEW_POINTS:
    'У треку менше двох точок із координатами, тож маршруту в ньому немає. Візьми файл із повним маршрутом.',
  TRACK_WITHOUT_DISTINCT_POINTS:
    'Усі точки треку стоять в одному місці — маршруту з них не вийде. Схоже, трек записався на місці.',
  TRACK_FORMAT_UNKNOWN: 'Не вдалося розпізнати формат треку. Підходять FIT, GPX і TCX.',
  ACTIVITY_TOO_FEW_RECORDS:
    'У тренуванні менше двох записів, тож у нього немає тривалості. Потрібен файл довшої активності.',
  ACTIVITY_WITHOUT_ELAPSED_TIME:
    'Усі записи тренування позначені одним і тим самим часом, тож розтягувати трек ні на що. Спробуй експортувати файл ще раз.',
  ACTIVITY_TOO_MANY_LAPS:
    'У тренуванні більше кіл, ніж уміщує FIT-файл. Такий файл не вдасться зібрати.',
  SUMMARY_SERIES_MISMATCH:
    'Підсумки не збігаються з даними, за якими їх порахували. Це наша помилка, а не твого файлу — напиши нам.',
  SUMMARY_WITHOUT_RECORDS: 'Немає жодного запису, з якого можна порахувати підсумки.',
  SUMMARY_WITHOUT_LAPS:
    'У підсумку не виявилося жодного кола, а без нього Strava може не прийняти файл.',
  DISTANCE_SERIES_IMPLAUSIBLE:
    'Розрахована дистанція не дотягує до кінця треку, і фініш на ньому вийшов би стрибком. Перевір, чи це справді маршрут цього тренування.',

  // --- The shell's own codes: about the request, never about the data in it.
  FILE_TOO_LARGE:
    'Файл завеликий для сервісу. Експортуй окремо те тренування, яке треба виправити.',
  INTERNAL_ERROR: 'Щось пішло не так на нашому боці. Спробуй ще раз.',
  ROUTE_NOT_FOUND: 'Такої сторінки немає.',
  REQUEST_MALFORMED:
    'Запит дійшов не повністю — найчастіше це обірване завантаження. Спробуй ще раз.',
  METHOD_NOT_ALLOWED: 'Цей запит сервіс не приймає.',
  MEDIA_TYPE_NOT_SUPPORTED:
    'Сервіс не вміє читати запит у такому вигляді. Додай файл через форму на сторінці.',
  CLIENT_DISCONNECTED: 'З’єднання обірвалося до того, як прийшла відповідь. Спробуй ще раз.',
  INSPECT_ROLE_INVALID:
    'Незрозуміло, чим має бути цей файл — оригіналом чи треком. Обери його ще раз.',
  FILE_MISSING: 'Файл не додано. Обери файл.',
  TOO_MANY_FILES: 'В одну зону можна додати лише один файл.',
  TRACK_LENGTH_MODE_INVALID:
    'Невідомий режим довжини треку. Онови сторінку й спробуй ще раз.',
} as const;

/** A code the interface can look up — the keys of {@link codeTexts}, nothing wider. */
export type DiagnosticCode = keyof typeof codeTexts;

export const uk = {
  hero: {
    title: 'Пробіжка з битим GPS? Полагодимо.',
    lede:
      'Час, пульс і каденс залишаємо твої, а маршрут і дистанцію беремо з правильного треку. На виході — FIT, який Strava приймає як є.',
  },

  dropzones: {
    /** The file whose time and sensors the repair keeps. */
    original: {
      title: 'Оригінальне тренування',
      hint: 'FIT з годинника або експорт зі Strava',
      role: 'Звідси — час, пульс, каденс',
      prompt: 'Перетягни файл або натисни, щоб обрати',
      pick: 'Обрати оригінальне тренування: FIT з годинника або експорт зі Strava',
    },
    /** The file whose geometry the repair borrows. */
    track: {
      title: 'Еталонний трек',
      hint: 'FIT, GPX або TCX з правильним маршрутом',
      role: 'Звідси — маршрут і дистанція',
      prompt: 'Перетягни файл або натисни, щоб обрати',
      pick: 'Обрати еталонний трек: FIT, GPX або TCX з правильним маршрутом',
    },
    /** Shown once a file is in the zone, beside what the service understood about it. */
    clear: 'Прибрати файл',
  },

  /**
   * What a zone says about the file it was given (Story 2.5). Shared by both zones on purpose:
   * these label the service's answer, and a label that existed twice would let the two zones
   * name one quantity differently. The role-specific words stay in `dropzones` above.
   */
  inspection: {
    /** While `/api/inspect` is being asked. */
    reading: 'Читаємо файл…',
    /** The bottom line of a zone that already holds a file — and the way out of a refusal. */
    another: 'Обрати інший файл',
    /** The request never arrived. About the service, never about the file (see `codes`). */
    unreachable: 'Не вдалося звернутися до сервісу. Перевір зв’язок і спробуй ще раз.',

    // --- The original's own quantities.
    startedAt: 'Початок',
    duration: 'Тривалість',
    recordedDistance: 'Дистанція на годиннику',
    /** `recordedDistanceMeters` is null: the device measured nothing, which is not zero. */
    noDistance: 'Годинник не записав дистанцію',

    // --- The track's.
    length: 'Довжина треку',
    points: 'Точок у треку',
    format: 'Формат',

    /** The units and the separator a number is written with here. */
    kilometres: 'км',
    decimalSeparator: ',',
  },

  howItWorks: {
    title: 'Як це працює',
    steps: [
      {
        title: 'Даєш два файли',
        body: 'Тренування з битим GPS і трек, на якому маршрут записаний правильно.',
      },
      {
        title: 'Зводимо їх за часом',
        body: 'Кожну живу GPS-точку оригіналу накладаємо на трек, а проміжки між ними розраховуємо.',
      },
      {
        title: 'Забираєш готовий FIT',
        body: 'Заливаєш у Strava — дистанція й темп нарешті правдиві.',
      },
    ],
  },

  repair: {
    action: 'Виправити трек',
    /** Why the button is still disabled — said once, under it, not inside it. */
    waitingForFiles: 'Додай обидва файли, щоб продовжити.',
  },

  /** Keyed by the code the API returned (AD-18). Equal to the core's registry by test. */
  codes: codeTexts,
} as const;
