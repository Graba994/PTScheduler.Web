-- ═══════════════════════════════════════════════════════════════════════════
-- DIAGNOSTYKA STREF CZASOWYCH — tylko odczyt, nic nie zmienia
--
-- Cel: ustalić co FAKTYCZNIE leży w bazie, zanim cokolwiek migrujemy.
-- Uruchom na bazie tenanta, np.:
--     docker exec -i pt-<slug>-db psql -U <user> -d <db> -f - < tools/tz-diagnostic.sql
-- albo skopiuj zapytania do psql/pgAdmin.
--
-- Wynik wklej z powrotem — na jego podstawie dobieramy wariant migracji.
-- ═══════════════════════════════════════════════════════════════════════════

\echo ''
\echo '════ 1. Strefa czasowa serwera i sesji ════'
-- Kluczowe: jeśli TimeZone = UTC, to odczyty aplikacji nie są przesuwane.
SELECT
    current_setting('TIMEZONE')        AS session_timezone,
    now()                              AS now_tz,
    now() AT TIME ZONE 'UTC'           AS now_utc,
    now() AT TIME ZONE 'Europe/Warsaw' AS now_warsaw,
    version()                          AS pg_version;

\echo ''
\echo '════ 2. Typy kolumn czasowych na Sessions ════'
-- Spodziewane: StartTime = timestamp with time zone
SELECT column_name, data_type, datetime_precision
FROM information_schema.columns
WHERE table_name = 'Sessions'
  AND data_type LIKE '%timestamp%'
ORDER BY column_name;

\echo ''
\echo '════ 3. NAJWAŻNIEJSZE — czy godziny sesji wyglądają sensownie? ════'
-- Jeśli konwencja "zegar ścienny zapisany jako UTC" jest prawdziwa,
-- kolumna as_utc pokaże godziny treningów tak, jak wpisał je trener
-- (czyli sensowne pory dnia: 6:00-21:00).
-- Jeśli sensowne godziny są w as_warsaw, to znaczy że zapis jest
-- prawdziwym instantem i moja hipoteza jest błędna.
SELECT
    "Id",
    "StartTime"                                AS raw_stored,
    "StartTime" AT TIME ZONE 'UTC'             AS as_utc,
    "StartTime" AT TIME ZONE 'Europe/Warsaw'   AS as_warsaw,
    "CreatedAt" AT TIME ZONE 'UTC'             AS created_utc
FROM "Sessions"
ORDER BY "StartTime" DESC
LIMIT 15;

\echo ''
\echo '════ 4. Rozkład godzin rozpoczęcia — test zdroworozsądkowy ════'
-- Treningi personalne odbywają się mniej więcej 6:00-21:00.
-- Kolumna, w której rozkład mieści się w tych godzinach, wskazuje
-- prawdziwą konwencję zapisu.
SELECT
    EXTRACT(HOUR FROM "StartTime" AT TIME ZONE 'UTC')           AS godzina_utc,
    EXTRACT(HOUR FROM "StartTime" AT TIME ZONE 'Europe/Warsaw') AS godzina_warsaw,
    COUNT(*)                                                    AS ile_sesji
FROM "Sessions"
GROUP BY 1, 2
ORDER BY 3 DESC
LIMIT 20;

\echo ''
\echo '════ 5. Sesje wokół zmiany czasu (DST) ════'
-- Zmiany czasu 2026: 29.03 (na letni) i 25.10 (na zimowy).
-- Sesje z okolic tych dat pokażą, czy offset jest stały czy zmienny.
SELECT
    "Id",
    "StartTime" AT TIME ZONE 'UTC'           AS as_utc,
    "StartTime" AT TIME ZONE 'Europe/Warsaw' AS as_warsaw,
    EXTRACT(HOUR FROM "StartTime" AT TIME ZONE 'Europe/Warsaw')
      - EXTRACT(HOUR FROM "StartTime" AT TIME ZONE 'UTC')       AS offset_godzin
FROM "Sessions"
WHERE "StartTime" BETWEEN '2026-03-25' AND '2026-04-02'
   OR "StartTime" BETWEEN '2026-10-21' AND '2026-10-29'
ORDER BY "StartTime"
LIMIT 20;

\echo ''
\echo '════ 6. Skala migracji ════'
SELECT
    (SELECT COUNT(*) FROM "Sessions")                                    AS sesji_total,
    (SELECT COUNT(*) FROM "Sessions" WHERE "StartTime" > now())          AS sesji_przyszlych,
    (SELECT COUNT(*) FROM "Sessions" WHERE "ReminderSentAt" IS NOT NULL) AS z_przypomnieniem,
    (SELECT MIN("StartTime") FROM "Sessions")                            AS najstarsza,
    (SELECT MAX("StartTime") FROM "Sessions")                            AS najnowsza;

\echo ''
\echo '════ 7. Kontrola krzyżowa: dostępność trenera (TimeOnly) ════'
-- TrainerAvailability trzyma czas jako "time without time zone" —
-- czyli czysty zegar ścienny. Te godziny MUSZĄ pokrywać się z godzinami
-- sesji z punktu 4. Jeśli się nie pokrywają, sloty i sesje są w dwóch
-- różnych układach odniesienia i to jest osobny, poważniejszy problem.
SELECT
    "StartTime" AS dostepnosc_od,
    "EndTime"   AS dostepnosc_do,
    "DayOfWeek",
    COUNT(*)    AS ile_regul
FROM "TrainerAvailabilities"
WHERE "IsActive" = true
GROUP BY 1, 2, 3
ORDER BY 1
LIMIT 15;

\echo ''
\echo '════ KONIEC — wklej wynik z powrotem do rozmowy ════'
\echo ''
