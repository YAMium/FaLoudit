[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspaceRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot '.falloutloc'))
$outputDirectory = [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot 'cache\publish\win-x64'))
$codexProjectDirectory = [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot 'cache\publish\codex-project'))
$packagesDirectory = [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot 'cache\packages'))
$reportsDirectory = [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot 'reports'))
$codexTemplateDirectory = Join-Path $workspaceRoot 'samples\codex-project'
$project = Join-Path $projectRoot 'src\FalloutLoc.Cli\FalloutLoc.Cli.csproj'
$bundledDotnet = Join-Path $workspaceRoot 'cache\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $bundledDotnet) { $bundledDotnet } else { 'dotnet' }
$licenseFile = Join-Path $projectRoot 'LICENSE'
$thirdPartyNoticesFile = Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md'
$sourceFile = Join-Path $projectRoot 'SOURCE.md'
$standardLicensesDirectory = Join-Path $projectRoot 'licenses'
$dotnetCommand = Get-Command $dotnet -ErrorAction Stop
$dotnetRoot = Split-Path -Parent $dotnetCommand.Source
$dotnetLicenseFile = Join-Path $dotnetRoot 'LICENSE.txt'
$dotnetThirdPartyNoticesFile = Join-Path $dotnetRoot 'ThirdPartyNotices.txt'

foreach ($requiredFile in @(
    $licenseFile,
    $thirdPartyNoticesFile,
    $sourceFile,
    (Join-Path $standardLicensesDirectory 'Apache-2.0.txt'),
    (Join-Path $standardLicensesDirectory 'MIT.txt'),
    (Join-Path $standardLicensesDirectory 'Reloaded.Memory-LICENSE.md'),
    $dotnetLicenseFile,
    $dotnetThirdPartyNoticesFile
)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required release license file is missing: $requiredFile"
    }
}

foreach ($destination in @($outputDirectory, $codexProjectDirectory, $packagesDirectory, $reportsDirectory)) {
    if (-not $destination.StartsWith($workspaceRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Packaging destination escaped the project workspace: $destination"
    }
}

if (Test-Path -LiteralPath $outputDirectory) {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $codexProjectDirectory) {
    Remove-Item -LiteralPath $codexProjectDirectory -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $codexProjectDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $packagesDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $reportsDirectory | Out-Null

& $dotnet publish $project -c Release -p:PublishProfile=WinX64 -p:PublishDir="$outputDirectory\" -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$releaseLicensesDirectory = Join-Path $outputDirectory 'licenses'
$releaseDotnetLicensesDirectory = Join-Path $releaseLicensesDirectory 'dotnet'
New-Item -ItemType Directory -Force -Path $releaseDotnetLicensesDirectory | Out-Null
Copy-Item -LiteralPath $licenseFile -Destination (Join-Path $outputDirectory 'LICENSE.txt')
Copy-Item -LiteralPath $thirdPartyNoticesFile -Destination $outputDirectory
Copy-Item -LiteralPath $sourceFile -Destination $outputDirectory
Copy-Item -LiteralPath (Join-Path $standardLicensesDirectory 'Apache-2.0.txt') -Destination $releaseLicensesDirectory
Copy-Item -LiteralPath (Join-Path $standardLicensesDirectory 'MIT.txt') -Destination $releaseLicensesDirectory
Copy-Item -LiteralPath (Join-Path $standardLicensesDirectory 'Reloaded.Memory-LICENSE.md') -Destination $releaseLicensesDirectory
Copy-Item -LiteralPath $dotnetLicenseFile -Destination $releaseDotnetLicensesDirectory
Copy-Item -LiteralPath $dotnetThirdPartyNoticesFile -Destination $releaseDotnetLicensesDirectory

$executable = Join-Path $outputDirectory 'faloudit.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Published executable was not created: $executable"
}

$symbols = @(Get-ChildItem -LiteralPath $outputDirectory -File -Filter '*.pdb')
if ($symbols.Count -ne 0) {
    throw "Release publish produced debug symbols: $($symbols.Name -join ', ')"
}

$version = (& $executable --version).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($version)) {
    throw 'Published executable failed its version smoke test.'
}

$archive = Join-Path $packagesDirectory "faloudit-$version-win-x64.zip"
if (Test-Path -LiteralPath $archive) {
    Remove-Item -LiteralPath $archive -Force
}

Compress-Archive -Path (Join-Path $outputDirectory '*') -DestinationPath $archive -CompressionLevel Optimal
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash

$codexToolDirectory = Join-Path $codexProjectDirectory 'tools\faloudit'
New-Item -ItemType Directory -Force -Path $codexToolDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $codexTemplateDirectory 'AGENTS.md') -Destination $codexProjectDirectory
Copy-Item -LiteralPath (Join-Path $codexTemplateDirectory 'FIRST_PROMPT.md') -Destination $codexProjectDirectory
Copy-Item -LiteralPath (Join-Path $codexTemplateDirectory 'PROJECT_CONTEXT.md') -Destination $codexProjectDirectory
Copy-Item -LiteralPath (Join-Path $codexTemplateDirectory 'README.md') -Destination $codexProjectDirectory
Copy-Item -LiteralPath (Join-Path $outputDirectory 'faloudit.exe') -Destination $codexToolDirectory
Copy-Item -LiteralPath (Join-Path $outputDirectory 'e_sqlite3.dll') -Destination $codexToolDirectory
Copy-Item -LiteralPath (Join-Path $outputDirectory 'LICENSE.txt') -Destination $codexProjectDirectory
Copy-Item -LiteralPath (Join-Path $outputDirectory 'THIRD-PARTY-NOTICES.md') -Destination $codexProjectDirectory
Copy-Item -LiteralPath (Join-Path $outputDirectory 'SOURCE.md') -Destination $codexProjectDirectory
Copy-Item -LiteralPath $releaseLicensesDirectory -Destination $codexProjectDirectory -Recurse

$codexArchive = Join-Path $packagesDirectory "faloudit-codex-project-$version.zip"
if (Test-Path -LiteralPath $codexArchive) {
    Remove-Item -LiteralPath $codexArchive -Force
}

Compress-Archive -Path (Join-Path $codexProjectDirectory '*') -DestinationPath $codexArchive -CompressionLevel Optimal
$codexArchiveHash = (Get-FileHash -LiteralPath $codexArchive -Algorithm SHA256).Hash
$manifestLines = foreach ($file in Get-ChildItem -LiteralPath $outputDirectory -File -Recurse | Sort-Object FullName) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $relativePath = [System.IO.Path]::GetRelativePath($outputDirectory, $file.FullName)
    "SHA256  $hash  $relativePath"
}
$manifestLines += "SHA256  $archiveHash  $([System.IO.Path]::GetFileName($archive))"
$manifestLines += "SHA256  $codexArchiveHash  $([System.IO.Path]::GetFileName($codexArchive))"
$manifest = ($manifestLines -join "`n") + "`n"
$manifestPath = Join-Path $reportsDirectory 'faloudit-win-x64.sha256'
[System.IO.File]::WriteAllText($manifestPath, $manifest, [System.Text.UTF8Encoding]::new($false))

Write-Host "Published: $executable"
Write-Host "Package:   $archive"
Write-Host "SHA-256:  $archiveHash"
Write-Host "Codex:     $codexArchive"
Write-Host "SHA-256:  $codexArchiveHash"
Write-Host "Manifest: $manifestPath"
