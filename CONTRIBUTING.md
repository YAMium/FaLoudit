# Contributing

FaLoudit welcomes focused bug fixes, tests, documentation improvements, and
read-only diagnostic features for Fallout 3, Fallout: New Vegas, and TTW.

Before submitting a change:

```powershell
dotnet restore .\FaLoudit.slnx --locked-mode
dotnet test .\FaLoudit.slnx -c Release --no-restore
```

Do not commit game files, plugins, archives, MO2 profiles, user indexes, local
reports, or data extracted from a private mod setup. Use synthetic fixtures
under `.falloutloc/fixtures` and keep every external game/MO2 source read-only.

By contributing, you agree that your contribution is licensed under
GPL-3.0-only, the same license as FaLoudit.
