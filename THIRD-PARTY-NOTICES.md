# Third-party notices

FaLoudit is licensed under GNU GPL version 3 only. The Windows package also
contains third-party components under the licenses listed below. Package
versions are locked by the committed `packages.lock.json` files.

This file is informational and does not replace the license text of any
component. Copies of the GPL, Apache 2.0, MIT, Reloaded.Memory, and applicable
.NET distribution terms are included in the Windows archives produced by
`scripts/Publish-Windows.ps1`.

## GNU GPL version 3

The following runtime packages are GPL-3.0-only, or GPL version 3 as described
by their upstream license:

- GameFinder.Common 4.9.0
- GameFinder.RegistryUtils 4.9.0
- GameFinder.StoreHandlers.GOG 4.9.0
- GameFinder.StoreHandlers.Steam 4.9.0
- GameFinder.StoreHandlers.Xbox 4.9.0
- GameFinder.Wine 4.9.0
  - Copyright: erri120 and contributors
  - Source: https://github.com/erri120/GameFinder
- Loqui 3.7.0
  - Copyright: Noggog and contributors
  - Source: https://github.com/Noggog/Loqui/tree/3.7.0
- Mutagen.Bethesda.Core 0.54.4
- Mutagen.Bethesda.Fallout3 0.54.4
- Mutagen.Bethesda.Kernel 0.54.4
  - Copyright: Noggog and Mutagen contributors
  - Source: https://github.com/Mutagen-Modding/Mutagen
- NexusMods.Paths 0.19.1
  - Copyright: Nexus Mods and contributors
  - Source: https://github.com/Nexus-Mods/NexusMods.Paths
- Noggog.CSharpExt 4.3.0
  - Copyright: Noggog and contributors
  - Source: https://github.com/Noggog/CSharpExt/tree/4.3.0
- Reloaded.Memory 9.4.2
  - Copyright: Sewer56 and contributors
  - Source: https://github.com/Reloaded-Project/Reloaded.Memory
  - Some source files contain MIT-licensed Microsoft Community Toolkit code;
    see `licenses/Reloaded.Memory-LICENSE.md`.

License text: `LICENSE`.

## Apache License 2.0

- SQLitePCLRaw.bundle_e_sqlite3 2.1.11
- SQLitePCLRaw.core 2.1.11
- SQLitePCLRaw.lib.e_sqlite3 2.1.12
- SQLitePCLRaw.provider.e_sqlite3 2.1.11
  - Copyright 2014-2024 SourceGear, LLC
  - Source: https://github.com/ericsink/SQLitePCL.raw

License text: `licenses/Apache-2.0.txt`.

## MIT License

- DynamicData 9.4.31 — Copyright (c) Roland Pheasant 2011-2026
- FluentResults 3.15.2 — Copyright (c) Michael Altmann
- ini-parser-netstandard 2.5.3 — Ricardo Amores Hernandez and contributors
- K4os.Compression.LZ4 1.3.8 — Milosz Krajewski
- K4os.Compression.LZ4.Streams 1.3.8 — Milosz Krajewski
- K4os.Hash.xxHash 1.0.8 — Milosz Krajewski
- Microsoft.Data.Sqlite 10.0.10 — Microsoft Corporation
- Microsoft.Data.Sqlite.Core 10.0.10 — Microsoft Corporation
- Microsoft.Extensions.DependencyInjection.Abstractions 9.0.4 — Microsoft Corporation
- Microsoft.Extensions.Logging.Abstractions 9.0.4 — Microsoft Corporation
- Microsoft.NET.ILLink.Tasks 10.0.10 — Microsoft Corporation
- OneOf 3.0.271 — Harry McIntyre
- SharpZipLib 1.4.2 — SharpZipLib contributors
- StrongInject 1.4.4 — StrongInject contributors
- System.IO.Abstractions 22.1.1 — Tatham Oddie and contributors
- System.Reactive 6.1.0 — .NET Foundation and contributors
- TestableIO.System.IO.Abstractions 22.1.1 — Tatham Oddie and contributors
- TestableIO.System.IO.Abstractions.Wrappers 22.1.1 — Tatham Oddie and contributors
- Testably.Abstractions.FileSystem.Interface 10.1.0 — Testably contributors
- TransparentValueObjects.Abstractions 1.1.0 — erri120 and contributors
- ValveKeyValue 0.13.1.398 — ValveKeyValue contributors

Project and source URLs for these packages are recorded in NuGet metadata and
the locked dependency graph. License text: `licenses/MIT.txt`.

## SQLite

The adjacent `e_sqlite3.dll` contains SQLite, which is dedicated to the public
domain. See https://www.sqlite.org/copyright.html.

## Microsoft .NET

The self-contained Windows executable contains parts of the Microsoft .NET
runtime. Those parts remain under their own applicable Microsoft .NET Library
license and third-party terms; they are not relicensed under GPL. The release
archive includes the exact `LICENSE.txt` and `ThirdPartyNotices.txt` supplied
with the .NET distribution used for the build.

Source: https://github.com/dotnet/runtime

## Trademarks and game content

FaLoudit does not contain Fallout game files or Mod Organizer data. Fallout and
related names and trademarks belong to their respective owners. FaLoudit is an
unofficial community project and is not affiliated with or endorsed by Bethesda
Softworks, ZeniMax Media, Obsidian Entertainment, or the Mod Organizer 2 team.
