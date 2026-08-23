# Стартовый промт для Codex — FaLoudit

Прочитай `FALOUDIT_SPEC.md` целиком и затем `AGENTS.md`.

Текущий scope проекта:

- Fallout 3
- Fallout: New Vegas
- Tale of Two Wastelands (TTW), главный сценарий

Fallout 4 сейчас НЕ входит в scope.

Пока **не начинай большую реализацию**. Сначала проведи техническое исследование и верни отчёт о готовности.

## Абсолютное правило безопасности

Любая Fallout 3 / Fallout New Vegas / TTW / MO2 сборка, путь к которой я дам, является строго **READ-ONLY SOURCE**.

Никогда не:

- изменяй `.esm/.esp`;
- пересохраняй или clean plugins;
- удаляй/переименовывай/перемещай source-файлы;
- меняй `plugins.txt`, `loadorder.txt`, `modlist.txt`;
- меняй INI;
- меняй MO2 profile;
- меняй load order;
- записывай кэш/индекс/логи в сборку.

Все generated/test files создавай только внутри project workspace.

Если эксперимент требует изменения plugin — сначала скопируй его в `.falloutloc/samples/` и работай только с копией.

## 1. Сформулируй понимание проекта

Коротко опиши конечную цель и разницу между:

- MO2 file-level physical winner;
- plugin record-level winning override.

Объясни, почему для поиска потерянного перевода нужны оба уровня.

## 2. Определи фактический game mode

Если я дам корневой путь сборки, read-only способом попробуй определить:

```text
Fallout 3
Fallout New Vegas
TTW
```

Для TTW ожидается Fallout New Vegas runtime environment с Fallout 3/TTW masters в load order.

Не hardcode конкретную версию TTW.

## 3. Сам найди структуру MO2

Не проси меня сразу перечислять все пути.

Попробуй найти:

- `ModOrganizer.exe`;
- MO2 base/instance;
- `mods`;
- `profiles`;
- profiles;
- `modlist.txt`;
- `plugins.txt`;
- `loadorder.txt`, если существует;
- Fallout New Vegas runtime;
- Fallout 3 standalone installation, если релевантно;
- `Data`;
- `FalloutNV.esm`;
- `Fallout3.esm`;
- `TaleOfTwoWastelands.esm`;
- `overwrite`.

MO2 может быть portable или разделён на несколько каталогов.

Если есть несколько вариантов — покажи их и попроси выбрать только реально неоднозначный пункт.

## 4. Определи authoritative load order

Не переноси правила Fallout 4.

Для выбранного FO3/FNV/TTW profile исследуй реальные `plugins.txt`, `loadorder.txt` и MO2 profile data. Скажи, какой источник и почему будет использовать инструмент.

## 5. Исследуй backend до написания приложения

Mutagen — **кандидат, а не обязательная технология**.

Проверь актуальное состояние Mutagen отдельно для:

- Fallout 3;
- Fallout New Vegas;
- TTW/FNV plugin format;
- records;
- FormID/master resolution;
- override chain;
- winning override;
- русских строк.

Не предполагай, что поддержка Fallout 3 автоматически означает поддержку New Vegas.

Если Mutagen не подходит для FNV/TTW, исследуй:

- FNVEdit/xEdit read-only integration;
- xEdit scripting/export;
- FO3Edit для standalone Fallout 3;
- другую зрелую library.

Собственный бинарный ESP/ESM parser — только последний вариант.

В первом отчёте дай:

```text
Recommended backend:
Reason:
What was verified:
Known risks:
```

## 6. xEdit как reference oracle

Для TTW/FNV используй FNVEdit/xEdit как эталон при проверке FormID, record identity, override chain и winning override.

Для standalone FO3 — FO3Edit/xEdit.

Не запускай операции сохранения/clean на оригиналах.

## 7. Проверь Cyrillic/string encoding

До большого индексатора выясни:

- как FO3/FNV strings кодируются;
- как выбранный backend декодирует русские строки;
- не возникает ли mojibake;
- можно ли получить стабильный Unicode text для SQLite/search.

Не считай все strings UTF-8 без проверки.

## 8. Проверь MO2 physical file resolution

Докажи, что можно определить:

```text
Logical plugin: SomeMod.esp
Physical file:  ...
MO2 source mod: ...
MO2 priority:   ...
```

в том числе если одинаковый `SomeMod.esp` лежит в оригинальном моде и моде-локализаторе.

## 9. Проверь минимально полезный сценарий

До полной реализации докажи на небольшом примере цепочку:

```text
search string
 -> record
 -> FormID/EditorID
 -> all active overrides
 -> winning override
 -> physical winning plugin
 -> MO2 source mod
```

Если возможно, найди существующий пример `English -> Russian -> English`, но не меняй живую сборку ради его создания.

## 10. Опиши safety architecture

Предложи конкретно:

- read-only source abstraction;
- writable workspace abstraction;
- write guard;
- защиту от случайной записи xEdit/library;
- работу с sample copies.

## 11. Скажи, что тебе нужно от меня

В конце первого отчёта создай раздел `Что мне нужно от тебя`.

Запрашивай только то, что нельзя надёжно обнаружить самому.

В идеальном случае достаточно:

- одного пути к корню TTW/MO2 сборки;
- подтверждения profile, если найдено несколько реально возможных;
- отдельного runtime game path только если discovery его не нашёл.

## 12. Дай implementation plan

План должен привести к **одному законченному CLI-инструменту**, а не набору одноразовых scripts.

Минимально полезный milestone:

```text
find -> record -> override chain -> winner -> MO2 mod
```

После него:

```text
regressions
untranslated
reports
incremental index
polishing
```

Не начинай большую production реализацию до завершения исследования и выбора проверенного plugin backend.
