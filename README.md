# FitFix

Виправляє тренування з битим GPS: час і сенсори з оригінального FIT + геометрія з правильного треку (FIT/GPX/TCX) → коректний FIT для Strava.

Статус: планування за BMAD (brief, PRD, UX готові; далі — архітектура). Робочий прототип алгоритму — у `prototype/`.

```bash
pip install fitparse fit-tool
python -I prototype/fix.py ORIGINAL.fit TRACK.fit   # пише out.pkl
python -I prototype/write.py                       # пише Run_4k_Chabany_fixed.fit
```

Контекст для AI-агентів — у `CLAUDE.md`.
