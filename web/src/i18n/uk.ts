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
  TRACK_IS_A_FRAGMENT:
    'Трек покриває лише частину маршруту, тож ми перемалювали одну ділянку тренування, а решту ' +
    'лишили такою, якою її записав годинник. Перевір ділянку нижче: якщо це не вона — схоже, ' +
    'трек не від цього забігу.',

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
  TRACK_ATTACHES_NOWHERE:
    'Не вдалося прикласти трек до цього тренування: його кінці або ніде не збігаються з твоїм ' +
    'маршрутом, або між ними більше кілометрів, ніж ти пробіг за цей час. Трек може покривати ' +
    'лише частину тренування, але мусить збігтися хоч десь — перевір, чи це файли одного забігу.',
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
  TRACK_COVERAGE_MODE_INVALID:
    'Невідомий режим покриття треку. Онови сторінку й спробуй ще раз.',
} as const;

/** A code the interface can look up — the keys of {@link codeTexts}, nothing wider. */
export type DiagnosticCode = keyof typeof codeTexts;

/**
 * The units and separators a number is written with, for `format.ts`. One decision read from two
 * places — a dropzone and the result screen — rather than two literals that can diverge, which is
 * the same arrangement the design tokens are under.
 */
const units = {
  kilometres: 'км',
  perKilometre: '/км',
  decimalSeparator: ',',
} as const;

/**
 * A request that never arrived. Said about the service and never about a file, and said in one
 * voice whether it was a zone or the repair that asked.
 */
const unreachable = 'Не вдалося звернутися до сервісу. Перевір зв’язок і спробуй ще раз.';

/** The watch recorded no distance, which is not the same claim as zero metres. */
const noRecordedDistance = 'Годинник не записав дистанцію';

/**
 * What a failure reads as when the dictionary has no sentence of its own for it, and the words of
 * the attempt that follows. Said in one voice wherever a failure appears, which is why `errors.ts`
 * reads `unexpected` from here and no component writes its own version of it.
 *
 * An unregistered code is our bug and not the person's, so the sentence says what to do rather
 * than naming the code — the code travels in `data-code`, where support can read it (Story 2.7).
 */
const errors = {
  /** A code `codeTexts` does not know. The code itself never reaches the sentence. */
  unexpected: 'Щось пішло не так. Онови сторінку й спробуй ще раз, а якщо повториться — напиши нам.',
  /** The attempt offered beside a request that never arrived (EXPERIENCE.md, State Patterns). */
  retry: 'Спробувати ще',
} as const;

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
    unreachable,

    // --- The original's own quantities.
    startedAt: 'Початок',
    duration: 'Тривалість',
    recordedDistance: 'Дистанція на годиннику',
    /** `recordedDistanceMeters` is null: the device measured nothing, which is not zero. */
    noDistance: noRecordedDistance,

    // --- The track's.
    length: 'Довжина треку',
    points: 'Точок у треку',
    format: 'Формат',

    /** The units and the separator a number is written with here. */
    kilometres: units.kilometres,
    decimalSeparator: units.decimalSeparator,
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
    /** The button while the request is in flight (EXPERIENCE.md, State Patterns). */
    working: 'Вирівнюю…',
    /** The repair never reached the service. About the service, never about either file. */
    unreachable,
  },

  /**
   * The result state (Story 2.6): before → after, the warnings above the numbers, the file and
   * the way back to two empty zones. The map, the chart and the zone bar are Epic 3.
   */
  result: {
    title: 'Готово. Ось що вийшло.',
    lede: 'Час, пульс і каденс залишилися твої — змінилися маршрут і дистанція.',

    /** The two sides of every metric, named so the arrow is not the only thing saying which. */
    before: 'Було',
    after: 'Стало',

    distance: 'Дистанція',
    time: 'Час',
    pace: 'Темп',

    /** The watch recorded no distance, so the "before" column has no distance and no pace. */
    noDistance: noRecordedDistance,
    noPace: 'Невідомо',

    /** The warnings above the numbers, and the two severities spelled out beside the colour. */
    warnings: 'Попередження',
    blocking: 'Важливо',
    info: 'До уваги',

    /**
     * The fragment gate (Story 5.2): which stretch was redrawn, the confirmation it waits for, and
     * what is being withheld meanwhile. The numbers themselves come from the answer — `redrawn` is
     * the label beside them, never a sentence with a number in it (AD-14).
     */
    redrawn: 'Перемальована ділянка',
    /**
     * Said beside the stretch once it is confirmed (Story 5.3): what the repair did not touch. A
     * label like `redrawn`, with no number in it (AD-14).
     */
    keptOwn: 'Поза цією ділянкою координати, висота й дистанція лишаються такими, як їх записав годинник.',
    /** Why there is no file to take yet. Said once, under the callout, not inside the button. */
    withheld: 'Файл буде доступний, щойно підтвердиш ділянку.',
    confirmFragment: 'Так, це потрібна ділянка',
    /** The other answer: the detector was wrong and the track is the whole route after all. */
    rejectFragment: 'Ні, це цілий маршрут',
    /** The button while the repair is being asked again with the confirmation (AD-22). */
    confirming: 'Підтверджую…',
    /** The repeated request never arrived or was refused; the result on screen is untouched. */
    confirmFailed: 'Не вдалося підтвердити ділянку. Спробуй ще раз.',

    download: 'Завантажити FIT',
    startAgain: 'Почати знову',
    /** The row EXPERIENCE.md asks for beside the download: disabled, and saying why (Epic 4). */
    strava: 'Залити у Strava — незабаром',

    /**
     * The route map (Story 3.2). Everything Leaflet would have written in English is here: its
     * zoom titles, its attribution prefix and its popup close button are all replaced by these.
     */
    map: {
      /** The map's own accessible name, with the two keys a keyboard user needs. */
      label: 'Карта маршруту. Стрілки пересувають карту, плюс і мінус змінюють масштаб.',
      layers: 'Шари карти',
      repaired: 'Виправлений трек',
      original: 'Оригінал',
      start: 'Старт',
      finish: 'Фініш',
      /** The last place the position was measured; after it the track is calculated (AD-20). */
      measuredUntil: 'Далі позицію розраховано',
      zoomIn: 'Збільшити масштаб',
      zoomOut: 'Зменшити масштаб',
      /**
       * The basemap's credit, as OpenStreetMap asks for it. Markup because Leaflet writes an
       * attribution with innerHTML; the link opens apart and sends no referrer.
       */
      attribution:
        '© <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener noreferrer">' +
        'Учасники OpenStreetMap</a>',
    },
  },

  /** What a failure reads as, and the attempt beside it. Shared by the zones and the repair. */
  errors,

  /** The units `format.ts` writes a number in. */
  units,

  /** Keyed by the code the API returned (AD-18). Equal to the core's registry by test. */
  codes: codeTexts,
} as const;
