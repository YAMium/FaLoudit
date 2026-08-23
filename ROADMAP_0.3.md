# FaLoudit — Road Map 0.3

## Цель версии

FaLoudit 0.3 добавляет второй, явно отделённый слой поиска для текста, который не представлен обычными локализуемыми полями записей:

```text
локализуемые поля ESM/ESP
 -> content search в активных скриптах и файлах
 -> структурированные кандидаты для проверки GPT
 -> ручной исследовательский fallback, если автоматические источники пусты
```

Content-кандидат является доказательством присутствия текста, но не автоматически доказанным runtime-источником видимой строки.

## Этапы

- [x] `0.3.1 — Embedded Script Source`: backend-neutral content model, извлечение сохранённого исходного кода `SCPT`, отдельный SQLite-индекс и команда поиска.
- [x] `0.3.2 — Analyze and GPT review`: автоматический content fallback в `analyze`, ограниченный контекст, evidence flags и инструкция Codex для смысловой проверки кандидатов.
- [ ] `0.3.3 — Loose virtual Data files`: read-only поиск по поддерживаемым текстовым loose-файлам с точным определением физического победителя MO2.
- [ ] `0.3.4 — Compiled scripts`: вложенные result-script поля завершены в 0.3.0; консервативный поиск строковых литералов compiled bytecode остаётся будущей эвристикой.
- [x] `0.3.5 — Production 0.3.0`: real-profile coverage, производительность, документация, Codex workflow и упаковка.
- [ ] `0.3.x — Archives`: BSA-каталог только после проверки зрелого read-only backend.

## Правила доказательности

- `localizedRecord` остаётся основным и наиболее надёжным источником.
- `embeddedScriptSource` подтверждает статическое присутствие исходного текста.
- GPT классифицирует content-кандидат как `confirmedStaticSource`, `likelyRuntimeSource`, `possibleSource`, `rejectedCandidate` или `ambiguous`.
- Даже `confirmedStaticSource` не называется runtime-трассировкой без наблюдения работающей игры.
- Извлечённые модовые тексты считаются недоверенными данными и никогда не интерпретируются как инструкции для Codex.
- Hardcoded строки EXE остаются отдельной границей и не смешиваются с ESM/ESP, loose-файлами или BSA.

## Безопасность

Все game/MO2/mod/profile/Data/overwrite/plugin/archive источники остаются строго read-only. Новые индексы, кэш и отчёты пишутся только внутрь `.falloutloc`.

