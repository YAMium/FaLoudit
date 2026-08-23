# Corresponding source

The preferred form for modifying FaLoudit is the complete source tree in the
Git repository, including the solution, locked NuGet dependency graph, tests,
and `scripts/Publish-Windows.ps1`.

For an official binary release, use the Git tag whose version matches
`faloudit.exe --version`. GitHub's automatically generated source archives for
that tag are the corresponding FaLoudit source download.

Build the tagged source with the .NET SDK version selected by `global.json`:

```powershell
dotnet restore .\FaLoudit.slnx --locked-mode
dotnet test .\FaLoudit.slnx -c Release --no-restore
& '.\scripts\Publish-Windows.ps1'
```

The exact versions and upstream source locations of linked third-party
components are listed in `THIRD-PARTY-NOTICES.md` and locked in
`packages.lock.json`. No game, MO2, plugin, archive, profile, or user index data
is required to compile FaLoudit.
