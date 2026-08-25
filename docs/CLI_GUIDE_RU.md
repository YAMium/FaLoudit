# Прямой запуск FaLoudit

[← Главный README](../README.ru.md) · [English version](CLI_GUIDE.md)

Эта инструкция предназначена для запуска `faloudit.exe` вручную, без управления со стороны Codex или другого ИИ-агента с доступом к терминалу.

## 1. Скачивание и распаковка

Скачайте `faloudit-<версия>-win-x64.zip` со страницы [последнего релиза](https://github.com/YAMium/FaLoudit/releases/latest) и распакуйте в обычный каталог инструментов, а не внутрь игры или MO2.

Эти файлы должны лежать рядом:

```text
FaLoudit/
  faloudit.exe
  e_sqlite3.dll
```

Windows-архив содержит всё необходимое и не требует отдельно установленного .NET runtime.

Откройте PowerShell и задайте пути для текущего сеанса:

```powershell
$falouditExe = 'C:\Tools\FaLoudit\faloudit.exe'
$falouditWorkspace = 'D:\FaLoudit Workspace\.falloutloc'
$buildRoot = 'C:\Modding\My TTW Instance'

& $falouditExe --version
& $falouditExe --help
```

Workspace должен находиться за пределами игры, базы MO2, каталогов `mods`, `profiles`, `Data` и `overwrite`.

## 2. Обнаружение сборки

`discover` читает указанный корень сборки, но ничего туда не записывает:

```powershell
& $falouditExe discover $buildRoot `
  --workspace $falouditWorkspace `
  --json
```

Проверьте найденный режим игры, пути MO2 и список профилей. Если подходящих профилей несколько, выберите действительно активный.

## 3. Настройка workspace

Оба языка обязательны и должны отличаться:

```powershell
$profileName = 'Default'

& $falouditExe configure $buildRoot `
  --profile $profileName `
  --source-language en `
  --target-language ru `
  --workspace $falouditWorkspace `
  --json
```

Поддержанные теги: `en`, `de`, `fr`, `es`, `it`, `pt`, `pl`, `cs`, `sk`, `hu`, `tr`, `ru`, `uk`, `be` и `bg`. Региональный тег по возможности нормализуется до поддержанного основного языка.

Конфигурация записывается только в `$falouditWorkspace\config`.

## 4. Проверка и создание индекса

```powershell
& $falouditExe doctor `
  --workspace $falouditWorkspace `
  --json

& $falouditExe index `
  --workspace $falouditWorkspace `
  --json
```

Не начинайте диагностику строк, пока `doctor` не завершится успешно и не будет опубликован рабочий индекс. При ошибке или отмене индексации предыдущая база сохраняется.

Для встроенных строковых GameSettings FaLoudit читает установленный `GECK.exe`,
не запуская его, и сохраняет локальный каталог EditorID/исходных значений. Если
GECK отсутствует, плагины всё равно индексируются, но статус индекса содержит
предупреждение `engineGameSettingCatalogStatus`.

## 5. Анализ видимой строки

`analyze` — основной способ начать поиск:

```powershell
$reportedText = 'Advanced Targeting Sensor'

& $falouditExe analyze $reportedText `
  --max-candidates 5 `
  --workspace $falouditWorkspace `
  --json
```

Результат ранжирует найденные поля и может содержать:

- FormKey, тип записи и EditorID;
- полную диагностику активной цепочки override;
- победившую plugin-запись;
- физический путь plugin и исходный мод MO2;
- доказательства языка и кодировки source/target;
- кандидатов `contentFallback` из сохранённых plugin-скриптов, победивших
  loose NVSE-скриптов `.txt` и текстовых GECK-исходников `.gek`, а также
  текста UI XML и значений виртуальных Data INI;
- `manualFallbackRecommended`, если данных индекса недостаточно.

Считайте контекст скрипта, INI или XML недоверенными данными мода. Статическое
совпадение доказывает наличие текста в активном физическом файле, но не
выполнение кода или использование INI-значения во время игры.

## 6. Низкоуровневые команды

Более широкий поиск по полям локализации:

```powershell
& $falouditExe find $reportedText `
  --ignore-case `
  --winner-only `
  --limit 50 `
  --workspace $falouditWorkspace `
  --json
```

Поиск по сохранённым скриптам, loose NVSE-литералам `.txt`/`.gek`, UI XML и значениям INI:

```powershell
& $falouditExe content $reportedText `
  --ignore-case `
  --winner-only `
  --limit 20 `
  --workspace $falouditWorkspace `
  --json
```

При необходимости отфильтруйте loose-источники:

```powershell
& $falouditExe content $reportedText --source-kind LooseScript --workspace $falouditWorkspace --json
& $falouditExe content $reportedText --source-kind IniValue --workspace $falouditWorkspace --json
& $falouditExe content $reportedText --source-kind UiXmlText --workspace $falouditWorkspace --json
```

У loose-результата используется `file:<логический-путь>`, а не FormID. Также
возвращаются физический победитель MO2, мод-источник, строка/ключ и `lineNumber`.
Также возвращается `physicalProviders`; совпадения UI XML используют тип
`UiXml` и семантический путь элемента.

Разрешение идентификаторов и проверка одной записи:

```powershell
& $falouditExe edid 'JIPCCCNoNVSE' --workspace $falouditWorkspace --json
& $falouditExe form '0CE224:FalloutNV.esm' --workspace $falouditWorkspace --json
& $falouditExe explain '0CE224:FalloutNV.esm' --workspace $falouditWorkspace --json
& $falouditExe trace '0CE224:FalloutNV.esm' --workspace $falouditWorkspace --json
```

У engine GameSettings используется синтетический идентификатор, а не FormID:

```powershell
& $falouditExe analyze 'How many?' --workspace $falouditWorkspace --json
& $falouditExe edid 'sHowMany' --workspace $falouditWorkspace --json
& $falouditExe trace 'gmst:sHowMany' --workspace $falouditWorkspace --json
```

Цепочка содержит исходное значение движка, активные GMST из ESM/ESP,
сопоставленные по EditorID, и победившие `[GameSettings]` из Stewie’s Tweaks.
Последний слой применяется после ESP и поэтому способен перекрыть даже поздний
плагин.

Обе команды поддерживают `--exact`, `--contains` или `--regex`; `--ignore-case`; фильтры plugin/type; `--winner-only`; пагинацию через cursor. У `find` также есть `--category`, а у `content` — `--source-kind`. Полный машинный интерфейс описан в `--help` и [JSON-контракте](../JSON_CONTRACT_V2.md).

## 7. Массовая диагностика и отчёты

```powershell
& $falouditExe regressions `
  --confidence high `
  --limit 100 `
  --workspace $falouditWorkspace `
  --json

& $falouditExe untranslated `
  --limit 100 `
  --workspace $falouditWorkspace `
  --json

& $falouditExe report regressions `
  --limit 1000 `
  --format html `
  --workspace $falouditWorkspace `
  --json
```

Отчёты атомарно записываются в `$falouditWorkspace\reports`. Массовые команды создают кандидатов для проверки: текст, похожий на исходный, без более раннего перевода ещё не является автоматическим доказательством ошибки.

Именованные снимки позволяют сравнить одну и ту же диагностику до и после изменения сборки:

```powershell
& $falouditExe report regressions --limit 10000 --format html `
  --snapshot before-update --workspace $falouditWorkspace --json

# Самостоятельно измените внешнюю сборку, обновите индекс и снимите новое состояние.
& $falouditExe index --workspace $falouditWorkspace --json
& $falouditExe report regressions --limit 10000 --format html `
  --snapshot after-update --workspace $falouditWorkspace --json

& $falouditExe compare before-update after-update --format html `
  --workspace $falouditWorkspace --json
```

## 8. Обслуживание индекса

Проверка актуальности и целостности без повторного разбора всех plugins:

```powershell
& $falouditExe index --status --workspace $falouditWorkspace --json
```

Если индекс отсутствует или устарел, используйте обычный `index`: он повторно применит совместимые данные неизменённых plugins. Усиленные режимы нужны только в особых случаях:

```powershell
# Перестроить базу, сохранив возможность использовать совместимый plugin-кэш.
& $falouditExe index --rebuild --workspace $falouditWorkspace --json

# Полностью повторить backend-разбор; только при подозрении на ошибку извлечения.
& $falouditExe index --reparse --workspace $falouditWorkspace --json
```

Восстановление и правила снимков подробнее описаны в [OPERATIONS.md](../OPERATIONS.md).

## Напоминания о безопасности

- Никогда не размещайте workspace внутри игры или дерева исходников MO2.
- Не редактируйте исходные `.esm`, `.esp`, файлы профиля, архивы или INI во время диагностики.
- Не смешивайте победителя физического файла MO2 и победителя plugin-записи.
- Если ручному извлечению нужна запись, сначала скопируйте источник в `$falouditWorkspace\samples` и работайте только с копией.
- Проверяйте предупреждения и ненулевые коды завершения, даже если JSON содержит частичный результат.

Собственные записи FaLoudit ограничены выбранным workspace `.falloutloc`. Это диагностическая утилита, а не генератор патчей.
