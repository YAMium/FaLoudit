# FaLoudit — техническое задание для Codex

Полное название: **Fallout Localization Auditor**.

## 0. Область проекта

Текущая область поддержки проекта намеренно ограничена:

- **Fallout 3**
- **Fallout: New Vegas**
- **Tale of Two Wastelands (TTW)** — главный практический сценарий

Поддержка Fallout 4, Skyrim и других игр Bethesda **сейчас не требуется**.

TTW необходимо рассматривать как сборку, работающую в экосистеме **Fallout: New Vegas**, где в одном load order присутствует контент/masters Fallout 3, Fallout New Vegas и Tale of Two Wastelands.

При этом game-specific backend не должен быть намертво смешан с поиском, индексом и CLI. Это позволит расширить поддержку позже, но **не нужно тратить время на будущие игры сейчас**.

---

# 1. Главная задача

Нужно разработать **единый локальный read-only инструмент для анализа локализации Fallout 3 / Fallout: New Vegas / TTW сборки**, в первую очередь большой TTW-сборки под Mod Organizer 2.

Инструмент должен отвечать:

> По названию предмета, объекта, персонажа, сообщения, квестовой строки, диалога или другой видимой в игре строки определить, **из какой записи и какого plugin она пришла, всю цепочку override-записей, какой plugin является winning override, какой MO2-мод физически поставляет этот файл и не был ли русский перевод позже перезаписан английским значением**.

Пользователь должен иметь возможность написать Codex:

- «Найди `Advanced Targeting System`».
- «Кто перезаписывает перевод `Тактический шлем`?»
- «Покажи всю override chain этого FormID».
- «Найди случаи, где русский перевод уже был, но поздний patch снова вернул английский».
- «Какой MO2-мод содержит winning plugin?»

Финальная цель — один цельный инструмент, которым Codex сможет пользоваться самостоятельно из терминала.

---

# 2. Главный принцип

Инструмент должен отвечать не только:

> «В каком ESP/ESM встречается эта строка?»

а на вопрос:

> **«Почему именно эту строку я вижу в игре в текущей TTW/FNV/FO3 сборке, какой физический plugin и какой record override победили, и где именно потерялся перевод?»**

---

# 3. Основные сценарии

## 3.1. Поиск источника строки

```text
faloudit find "Tactical Helmet"
```

Ожидаемый логический результат:

```text
Found: 2 candidate records

ARMO | FormID ...
EditorID: TacticalHelmet01

Override chain:
  OriginalArmor.esp        FULL = "Tactical Helmet"
  MyRussianPatch.esp       FULL = "Тактический шлем"
  TTW_Compatibility.esp    FULL = "Tactical Helmet"   <-- WINNER

Winning plugin: TTW_Compatibility.esp
MO2 mod: TTW Compatibility Collection
Status: TRANSLATION REGRESSION (RU -> EN)
```

## 3.2. Цепочка override

Для найденной записи инструмент должен:

1. определить её идентификатор;
2. определить originating/master record;
3. найти все активные overrides по фактическому load order;
4. показать их в порядке загрузки;
5. определить winning override;
6. показать изменения локализуемых строк;
7. отметить момент, когда русский перевод был снова заменён английским.

Главная диагностическая сущность — **история одной записи во всём активном load order**.

## 3.3. Массовый поиск потерянных переводов

```text
faloudit regressions
```

Искать паттерны типа:

```text
English -> Russian -> English
```

или более общий случай: более ранняя активная версия локализуемого поля выглядит переведённой на русский, но winning override снова содержит английский/непереведённый текст.

Группировать по winning plugin.

---

# 4. Безопасность — абсолютное требование

## 4.1. Сборка пользователя строго read-only

Любой путь к Fallout 3, Fallout New Vegas, TTW, MO2, `mods`, `profiles`, `overwrite`, `Data`, plugin-файлам, INI, BSA и конфигурации сборки считается **READ-ONLY SOURCE**.

Ни Codex, ни готовый инструмент не должны:

- изменять `.esm/.esp`;
- пересохранять или clean plugins;
- менять masters;
- удалять/переименовывать/перемещать файлы сборки;
- менять `plugins.txt`, `loadorder.txt`, `modlist.txt`;
- менять INI;
- менять MO2 profile;
- менять load order;
- записывать индекс/лог/временный файл в папки сборки.

## 4.2. Разрешённая запись

Писать можно только внутрь project workspace:

```text
<ProjectRoot>/
  .falloutloc/
    config/
    cache/
    index/
    logs/
    reports/
    samples/
    fixtures/
```

## 4.3. Эксперименты только над копиями

Если для исследования требуется изменить plugin, проверить сериализацию, использовать потенциально пишущий xEdit script или иной инструмент, сначала файл копируется в `.falloutloc/samples/`, и работа производится только над копией.

## 4.4. Техническая защита

Желательно реализовать:

- `ReadOnlySourceGuard`;
- список configured source roots;
- запрет write destination внутри source roots;
- `SourceFileSystem` с API только чтения;
- отдельный `WorkspaceFileSystem` для записи;
- optional SHA-256 verification до/после integration tests.

---

# 5. Поддерживаемые режимы

```text
fallout3
falloutnv
ttw
```

## Fallout 3

Standalone Fallout 3 load order.

## Fallout New Vegas

Standalone New Vegas load order.

## TTW

Главный сценарий. TTW следует считать Fallout New Vegas runtime/load-order environment, в котором могут присутствовать:

- `FalloutNV.esm`;
- DLC New Vegas;
- `Fallout3.esm`;
- DLC Fallout 3;
- `TaleOfTwoWastelands.esm`;
- TTW-related masters;
- большое число модов и patch-плагинов.

**Не hardcode конкретный TTW load order.** Реальная версия TTW и порядок masters определяются из текущей сборки/profile.

Наличие `TaleOfTwoWastelands.esm` — сильный признак auto-detection режима TTW.

---

# 6. Универсальность расположения сборки

Нельзя предполагать фиксированную структуру.

MO2 может быть portable или установлен отдельно; `mods` и `profiles` могут находиться в другом каталоге; Fallout New Vegas runtime может быть отдельно; standalone Fallout 3 может существовать независимо.

Не путать:

- standalone Fallout 3 installation;
- Fallout 3 content/masters внутри TTW;
- Fallout New Vegas runtime installation.

---

# 7. Auto-discovery

Команда:

```text
faloudit discover "E:\MyTTWBuild"
```

должна read-only способом искать:

- `ModOrganizer.exe`;
- MO2 instance/base settings;
- `mods`;
- `profiles`;
- `modlist.txt`;
- `plugins.txt`;
- `loadorder.txt`, если он реально используется/существует;
- `FalloutNV.exe`;
- `Fallout3.exe`;
- `Data`;
- `FalloutNV.esm`;
- `Fallout3.esm`;
- `TaleOfTwoWastelands.esm`;
- `overwrite`;
- xEdit/FNVEdit/FO3Edit, если присутствуют.

Если найдено несколько правдоподобных instances — не угадывать молча.

---

# 8. Явная конфигурация

Все auto-discovered пути должны иметь manual override.

Логически:

```text
faloudit configure \
  --mode ttw \
  --game-root "E:\Games\Fallout New Vegas" \
  --mo2-root "D:\Tools\MO2" \
  --mods "F:\MO2Instances\TTW\mods" \
  --profiles "F:\MO2Instances\TTW\profiles" \
  --profile "Main"
```

Конфиг хранить только в `.falloutloc/config/project.json`.

---

# 9. MO2 profile и load order

Нужно исследовать реальные файлы выбранного profile:

- `modlist.txt`;
- `plugins.txt`;
- `loadorder.txt`, если есть;
- другие профильные данные MO2.

**Не переносить предположения от Fallout 4.**

Для FNV/FO3/TTW Codex должен определить authoritative source порядка активных plugins на реальной сборке. Если `plugins.txt` и `loadorder.txt` существуют одновременно, нужно понять роль каждого и проверить консистентность.

---

# 10. Два разных вида конфликтов

## File-level MO2 conflict

Какой физический файл побеждает для одного logical Data path.

Например один `SomePlugin.esp` существует в оригинальном моде и в моде-локализаторе.

## Record-level plugin conflict

После разрешения physical plugin-файлов load order определяет, какой plugin содержит winning override конкретной записи.

Эти уровни нужно показывать отдельно.

---

# 11. Универсальный MO2 file resolver

Для каждого файла хранить:

```text
Logical Data path
Physical path
Source MO2 mod
MO2 priority
Enabled/disabled
Winning physical source
File type
Size
Fingerprint/hash
```

Учитывать `Data`, MO2 `mods`, `overwrite` и другие реально участвующие sources.

Не предполагать, что plugin всегда лежит непосредственно в корне папки мода.

---

# 12. Backend анализа Bethesda plugins

## 12.1. Mutagen — кандидат, а не обязательная основа

Первоначальная идея проекта — Mutagen. Для текущего scope нельзя заранее считать его гарантированным решением.

Codex обязан отдельно доказать поддержку:

- Fallout 3;
- Fallout New Vegas;
- TTW-плагинов в FNV environment;
- нужных record types;
- FormID/master resolution;
- override semantics;
- русских строк.

## 12.2. Приоритет исследования backend

### A. Mutagen

Использовать, если актуальная версия действительно корректно покрывает FO3 + FNV/TTW.

### B. xEdit / FNVEdit / FO3Edit integration

Если Mutagen не покрывает FNV/TTW достаточно надёжно, исследовать read-only integration:

- FNVEdit/xEdit для New Vegas/TTW;
- FO3Edit/xEdit для standalone Fallout 3;
- xEdit scripting/export;
- structured JSON/CSV/text output.

Не использовать save/clean операции на originals.

### C. Другая зрелая library

Допустима после проверки корректности относительно xEdit.

### D. Собственный parser

Только как последний вариант.

---

# 13. Backend abstraction

Core должен зависеть от интерфейса примерно такого смысла:

```text
IPluginReader
ILoadOrderReader
IRecordEnumerator
IOverrideResolver
IRecordStringExtractor
```

CLI, SQLite index, language detection и MO2 resolver не должны быть намертво связаны с конкретным Mutagen/xEdit implementation.

---

# 14. xEdit как reference oracle

Для интеграционных тестов использовать xEdit как эталон:

- FNV/TTW → FNVEdit/xEdit;
- Fallout 3 → FO3Edit/xEdit.

Сравнивать:

- record identity;
- FormID;
- originating plugin;
- override chain;
- winning override;
- отображаемое строковое поле.

Если backend и xEdit расходятся — исследовать причину до дальнейшей разработки.

---

# 15. Индекс

Для большой сборки нужен persistent index, предпочтительно SQLite.

Хранить:

## Plugin

- filename;
- physical file;
- logical Data path;
- MO2 source mod;
- MO2 priority;
- plugin load order;
- enabled state;
- masters;
- timestamp/size/fingerprint.

## Record

- FormID / стабильное внутреннее представление;
- record signature/type;
- EditorID;
- originating plugin;
- plugin, содержащий override;
- winner.

## String

- record;
- field/subrecord path;
- text;
- plugin;
- normalized searchable text;
- language heuristic.

## Override history

Полная цепочка должна восстанавливаться без full rescan.

---

# 16. FormID

Инструмент должен различать:

- local/raw ID внутри plugin;
- runtime/load-order-dependent FormID;
- plugin + local ID;
- resolved identity с учётом masters/load order.

Не использовать FO4/Skyrim ESL assumptions.

Если пользователь вводит `XX012345`, нужно использовать текущий active load order для разрешения `XX`. Если однозначность невозможна — сообщить об этом.

---

# 17. Поиск

```text
faloudit find "Tactical Helmet"
faloudit edid TacticalHelmet01
faloudit form 1A012345
faloudit plugin SomePatch.esp
```

Опции поиска:

```text
--exact
--contains
--ignore-case
--regex
--plugin
--type
--limit
```

---

# 18. Trace

```text
faloudit trace <record>
```

Пример:

```text
ARMO | TacticalHelmet01

[1] OriginalArmor.esp
    FULL: "Tactical Helmet"

[2] RussianLocalization.esp
    FULL: "Тактический шлем"

[3] TTWBalancePatch.esp
    FULL: "Тактический шлем"

[4] MegaCompatibilityPatch.esp
    FULL: "Tactical Helmet"   <-- WINNER

Diagnosis:
  Translation existed before winning override.
  MegaCompatibilityPatch.esp reverted the visible name to English.

MO2 physical source:
  Mega Compatibility Patch
  F:\...\mods\Mega Compatibility Patch\MegaCompatibilityPatch.esp
```

---

# 19. Какие строки индексировать

Нужно исследовать actual FO3/FNV record model и покрыть строки, которые видит игрок:

- item names;
- armor/weapons/ammo;
- ingestibles/misc;
- activators/containers/doors/furniture;
- NPC/creature names;
- cells/worldspaces;
- quest names/objectives;
- dialogue topics/responses;
- messages;
- notes;
- terminals;
- perks/challenges;
- recipes/effects;
- descriptions;
- другие реальные user-facing string fields.

Не ограничиваться `FULL`.

EditorID индексировать отдельно и не считать переводимым текстом.

---

# 20. FO3/FNV string encoding — обязательное исследование

Это критическая часть русской локализации.

Codex должен выяснить:

- как FO3/FNV plugin strings реально кодируются;
- как выбранный backend декодирует Cyrillic;
- какие encoding/codepage assumptions применимы;
- как избежать mojibake;
- как читать русифицированные plugins без потери символов;
- как получить стабильный Unicode text для SQLite/search.

Нельзя молча считать, что все plugin strings — UTF-8.

Создать fixtures для English, Russian Cyrillic, mixed text, symbols, quotes/control characters и проверить безопасное чтение.

---

# 21. External STRINGS

Не переносить из старого Fallout 4 ТЗ обязательную поддержку `.STRINGS/.DLSTRINGS/.ILSTRINGS`.

Добавлять external localization resources только если исследование реального FO3/FNV/TTW формата покажет, что они нужны.

---

# 22. BSA

Для основной задачи поиска строк в `.esm/.esp` не нужно сразу распаковывать BSA.

BSA analysis — будущий scope для scripts/interface/voice/assets, но не первая версия.

---

# 23. Language detection

Хранить:

```text
ru / en / mixed / neutral / unknown
confidence
reason
```

Не считать любую Latin-only строку ошибкой. Учитывать числа, `10mm`, `HP`, аббревиатуры, имена собственные, технические tokens.

---

# 24. Translation regression

Высокоуверенный случай:

```text
Original: English
Earlier active override: Russian
Winner: English
```

Показывать:

- record;
- field;
- предыдущий русский plugin;
- winning English plugin;
- MO2 source каждого plugin;
- confidence;
- объяснение.

Если русский текст находится только в disabled plugin — это не активный перевод.

---

# 25. Классификация проблем

- **Record override regression** — поздний plugin вернул английский.
- **MO2 file overwrite** — переведённая копия того же plugin проиграла физический конфликт.
- **Never translated** — русской активной версии нет.
- **Disabled translation** — перевод есть только в отключённом mod/plugin.
- **Ambiguous text** — строка встречается в нескольких records.
- **Encoding problem** — повреждённое декодирование.
- **Parse incomplete** — часть plugins не разобрана.

---

# 26. Incremental indexing

```text
faloudit index
faloudit index --rebuild
faloudit index --status
```

Проверять profile, modlist, plugin list, load order, timestamps, sizes и fingerprints; перепарсивать только изменённые файлы.

---

# 27. CLI

Предлагаемые команды:

```text
faloudit discover <root>
faloudit configure ...
faloudit doctor

faloudit profiles
faloudit use-profile <name>

faloudit index
faloudit index --rebuild
faloudit index --status

faloudit find <text>
faloudit edid <editor-id>
faloudit form <id>
faloudit trace <record>

faloudit plugin <plugin>
faloudit mod <mo2-mod>

faloudit conflicts <plugin>
faloudit regressions
faloudit regressions <plugin>
faloudit untranslated

faloudit report regressions
faloudit report untranslated
```

---

# 28. `doctor`

Пример:

```text
Mode                 TTW
Workspace            OK / writable
Source roots         READ-ONLY
Runtime              Fallout New Vegas
MO2 root             OK
MO2 profile          Main
FalloutNV.esm        OK
Fallout3.esm         OK
TaleOfTwoWastelands  OK
Plugin backend       ... OK
Cyrillic decoding    OK
Index                stale
Source write guard   ACTIVE
```

---

# 29. Human + JSON output

По умолчанию — читаемый terminal output.

Для Codex:

```text
--json
```

Стабильная versioned schema:

```json
{
  "schemaVersion": 1,
  "gameMode": "ttw",
  "query": "...",
  "matches": []
}
```

---

# 30. Производительность

Целевая сборка может содержать около 1500 MO2 mods.

Нужны:

- persistent index;
- incremental rebuild;
- progress;
- транзакционная запись SQLite;
- безопасная отмена Ctrl+C;
- быстрый поиск после индексации;
- отдельное отношение к disabled mods, чтобы они не смешивались с фактической active game state.

---

# 31. Ошибки plugin

Один malformed/нестандартный plugin не должен валить весь scan.

Статусы:

```text
Parsed
PartiallyParsed
Failed
```

Если цепочка неполна, явно предупреждать пользователя.

---

# 32. Архитектура

Рекомендуемый layout:

```text
src/
  FalloutLoc.Cli/
  FalloutLoc.Core/
  FalloutLoc.Mo2/
  FalloutLoc.Index/
  FalloutLoc.Analysis/
  FalloutLoc.Backends/

tests/
  FalloutLoc.Core.Tests/
  FalloutLoc.Mo2.Tests/
  FalloutLoc.Backend.Tests/
  FalloutLoc.IntegrationTests/

fixtures/
  synthetic/
  sanitized/

.falloutloc/
  config/
  cache/
  index/
  logs/
  reports/
  samples/

AGENTS.md
README.md
```

Основные компоненты:

```text
InstallationDiscovery
ProjectConfiguration
ReadOnlySourceGuard
SourceFileSystem
WorkspaceFileSystem
Mo2ProfileReader
Mo2FileResolver
LoadOrderResolver
IPluginBackend
PluginIndexer
OverrideAnalyzer
LocalizationAnalyzer
LanguageDetector
SearchService
ReportService
```

---

# 33. Тесты

## Unit

- MO2 parsing;
- path discovery;
- file priority;
- load order;
- language detection;
- normalization;
- encoding helpers;
- search ranking.

## Synthetic fixtures

Создать/получить маленькие безопасные test plugins:

```text
A: English
B: Russian override
C: English override
```

Ожидание:

```text
winner = C
regression = true
```

## Integration на реальной сборке

Только read-only. Начать с 2–5 известных plugins, затем небольшой части load order, затем full index. Результаты сравнивать с FNVEdit/FO3Edit.

---

# 34. Этапы разработки

## Phase 0 — feasibility и исследование

До большого production code:

1. прочитать ТЗ;
2. определить FO3/FNV/TTW mode;
3. исследовать MO2 layout;
4. определить authoritative load order;
5. проверить Cyrillic decoding;
6. проверить актуальную поддержку Mutagen Fallout 3;
7. отдельно проверить поддержку Mutagen Fallout New Vegas;
8. проверить xEdit/FNVEdit/FO3Edit automation;
9. выбрать backend;
10. доказать override-chain на небольшом примере;
11. сообщить, что ещё нужно от пользователя.

## Phase 1 — safety + config + discovery

- project skeleton;
- source guard;
- workspace;
- `discover`;
- `configure`;
- `doctor`.

## Phase 2 — MO2 resolver

- profiles;
- enabled mods;
- priorities;
- logical -> physical;
- active physical plugin winners;
- runtime game detection.

## Phase 3 — plugin backend

- records;
- FormID;
- EditorID;
- masters;
- text fields;
- override semantics.

## Phase 4 — index

- SQLite;
- incremental indexing;
- active load order.

## Phase 5 — search + trace

Минимально полезный milestone:

```text
find
 -> record
 -> override chain
 -> winning plugin
 -> physical plugin
 -> MO2 source mod
```

## Phase 6 — localization diagnostics

- Cyrillic/English heuristic;
- regressions;
- untranslated;
- encoding diagnostics.

## Phase 7 — polishing

- JSON schema;
- reports;
- error handling;
- performance;
- README;
- packaging;
- один удобный executable/launcher.

---

# 35. Что НЕ делать сейчас

Не требуется:

- Fallout 4;
- Skyrim;
- ESL-specific logic;
- автоматический перевод;
- редактирование plugins;
- генерация patch;
- load-order sorting;
- cleaning masters;
- замена xTranslator;
- BSA repacking;
- изменение MO2 profile.

---

# 36. Definition of Done

Пользователь пишет Codex:

> «В игре остался `Advanced Targeting Sensor`. Найди почему».

Codex запускает готовый инструмент и получает:

1. matching records;
2. record type;
3. EditorID;
4. FormID;
5. originating plugin;
6. full active override chain;
7. winning override;
8. physical path winning plugin;
9. MO2 source mod;
10. предыдущую русскую версию, если она была;
11. RU -> EN regression status;
12. предупреждение об encoding/parse ambiguity при необходимости.

И всё это без изменения единого файла исходной сборки.

---

# 37. Что Codex должен сообщить перед реализацией

После чтения ТЗ Codex сначала должен ответить:

## Что определено автоматически

- game mode;
- MO2 root/base;
- mods;
- profiles;
- runtime FNV/FO3 root;
- Data;
- active profile candidate;
- load-order files;
- наличие TTW masters;
- .NET;
- xEdit/FNVEdit/FO3Edit;
- feasible backend candidates.

## Что требует решения

Запрашивать только реально неразрешимые автоматически пункты.

## Backend recommendation

```text
Chosen plugin backend: ...
Why: ...
Verified on: ...
Known risks: ...
```

Не начинать большой implementation, пока не доказано корректное чтение FNV/TTW plugin и override chain.

---

# 38. Источники для проверки

Во время реализации сверяться с актуальными источниками:

- Mutagen: https://mutagen-modding.github.io/Mutagen/
- Mutagen GitHub: https://github.com/Mutagen-Modding/Mutagen
- xEdit: https://github.com/TES5Edit/TES5Edit
- Mod Organizer 2: https://github.com/ModOrganizer2/modorganizer
- Tale of Two Wastelands: https://taleoftwowastelands.com/

Старые TTW guides могут относиться к предыдущим версиям. Реальная текущая сборка пользователя и актуальные инструменты имеют приоритет над устаревшими примерами.

---

# 39. Multilingual extension (version 0.4)

The original English/Russian examples remain the primary TTW validation case,
but the production model is configured explicitly with `sourceLanguage` and
`targetLanguage` before indexing. Diagnostics use relative `Source` / `Target`
roles and target-to-source regression terminology.

Supported 0.4 profiles cover the European single-byte Fallout localization
families Windows-1250, Windows-1251, Windows-1252, and Windows-1254. Shared
Latin-script detection must remain conservative. An exact return to an earlier
source value after an intervening different value is useful regression evidence;
plain Latin text without override history is only a review candidate.

Configuration schema 1 requires explicit migration. SQLite schema 4 is rebuilt
atomically as schema 5 and is never mutated inside a source directory. See
`docs/MULTILINGUAL_DESIGN_0.4.md` for the compatibility design.

---

# 40. Engine string GameSettings (version 0.4.1)

String GameSettings initialized by `Fallout3.exe` or `FalloutNV.exe` are a
separate diagnostic identity class. They do not have an engine-default FormID
and must be represented as `gmst:<EditorID>`, never as an invented plugin
FormKey.

The default EditorID/value catalog is extracted read-only from the user's local
FO3/FNV `GECK.exe`. FaLoudit must not bundle Bethesda strings, load the editor,
or inspect runtime process memory. An unavailable or unrecognized GECK produces
an actionable incomplete-catalog warning while normal plugin indexing remains
usable.

The effective value trace is resolved by EditorID in this order:

1. validated engine default;
2. active plugin `GameSettingString.Data` assignments in load order;
3. MO2-winning Stewie Tweaks `[GameSettings]` INI assignments applied after
   plugins, including the dedicated `NVSE/Plugins/Tweaks/Gamesettings` tree.

INI bytes use the same reversible configured-code-page recovery policy as
plugin strings. Engine catalog and post-plugin INI inputs participate in the
index freshness fingerprint. SQLite schema 5 is rebuilt atomically as schema 6.

---

# 41. MO2-winning loose content (version 0.4.2)

The fallback content index includes read-only evidence from active virtual Data
files outside ESM/ESP:

1. quoted literals in MO2-winning `NVSE/Plugins/Scripts/**/*.txt`;
2. quoted literals in MO2-winning `NVSE/user_defined_functions/**/*.txt`;
3. values from MO2-winning virtual Data `*.ini` files.

Loose content uses `file:<logical Data path>` identities and must never be
presented as a plugin FormID. Store the physical winner, source MO2 mod,
effective priority, semantic line or INI section/key, line number, configured
encoding evidence, and bounded source context. Preserve the complete physical
provider chain through the general MO2 file resolver.

INI comments, MO2 `meta.ini`, `.mohidden` trees, and Windows `desktop.ini` are
not runtime content and are excluded. Apply a bounded file-size and binary-file
guard. Never execute a script or load an INI through a game/plugin runtime.

Every result is untrusted static evidence requiring semantic GPT review. A
matching literal or value does not prove that the code path executes or that a
consumer uses that key. Compiled-only script bytecode and BSA content remain a
manual read-only fallback. Loose metadata participates in freshness detection;
SQLite schema 6 is rebuilt atomically as schema 7.
