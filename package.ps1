[CmdletBinding()]
param(
    [ValidateSet('All', 'GM', 'Dedicated', 'Full')]
    [string]$Package = 'All',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = '',
    [string]$NuclearOptionDir = '',
    [string]$NuclearOptionManagedDir = '',
    [string]$ServerNuclearOptionDir = '',
    [string]$ServerManagedDir = '',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $MyInvocation.MyCommand.Path))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'dist'
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
if ([string]::IsNullOrWhiteSpace($NuclearOptionDir)) {
    $NuclearOptionDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot '..'))
}
if ([string]::IsNullOrWhiteSpace($NuclearOptionManagedDir)) {
    $NuclearOptionManagedDir = Join-Path $NuclearOptionDir 'NuclearOption_Data\Managed'
}
if ([string]::IsNullOrWhiteSpace($ServerNuclearOptionDir)) {
    $ServerNuclearOptionDir = $NuclearOptionDir
}
if ([string]::IsNullOrWhiteSpace($ServerManagedDir)) {
    $ServerManagedDir = $NuclearOptionManagedDir
}

$version = '2.0.0-rc.1'
$protocolVersion = 2
$fixedTimestamp = [DateTimeOffset]::Parse('2026-08-29T00:00:00Z')
$sharedAssembly = Join-Path $repoRoot "bin\$Configuration\netstandard2.0\Horus.Shared.dll"
$clientAssembly = Join-Path $repoRoot "bin\$Configuration\net472\Horus.Client.dll"
$serverAssembly = Join-Path $repoRoot "bin\$Configuration\net472\Horus.Server.dll"

function Invoke-DotNet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code ${LASTEXITCODE}: $($Arguments -join ' ')"
    }
}

function Copy-PackageFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required package input is missing: $Source"
    }
    $parent = Split-Path -Parent $Destination
    if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Write-PackageHashes {
    param([Parameter(Mandatory = $true)][string]$StageDirectory)
    $lines = foreach ($file in Get-ChildItem -LiteralPath $StageDirectory -Recurse -File | Sort-Object FullName) {
        $relative = $file.FullName.Substring($StageDirectory.Length).TrimStart('\').Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relative"
    }
    $hashPath = Join-Path $StageDirectory 'SHA256SUMS'
    [System.IO.File]::WriteAllLines($hashPath, $lines, [System.Text.UTF8Encoding]::new($false))
}

function New-DeterministicZip {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $DestinationPath) { Remove-Item -LiteralPath $DestinationPath -Force }
    $archive = [System.IO.Compression.ZipFile]::Open($DestinationPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $SourceDirectory -Recurse -File | Sort-Object FullName) {
            $relative = $file.FullName.Substring($SourceDirectory.Length).TrimStart('\').Replace('\', '/')
            $entry = $archive.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $fixedTimestamp
            $entry.ExternalAttributes = 0
            $input = [System.IO.File]::OpenRead($file.FullName)
            $output = $entry.Open()
            try { $input.CopyTo($output) }
            finally { $output.Dispose(); $input.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}

function New-HorusPackage {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$Assemblies,
        [Parameter(Mandatory = $true)][bool]$IncludeServerConfig
    )
    $stage = Join-Path $OutputDirectory ('.stage-' + $Name)
    $stageFull = [System.IO.Path]::GetFullPath($stage)
    if ($stageFull -eq $OutputDirectory -or $stageFull -eq $repoRoot) {
        throw "Unsafe staging path: $stageFull"
    }
    if (Test-Path -LiteralPath $stageFull) { Remove-Item -LiteralPath $stageFull -Recurse -Force }
    New-Item -ItemType Directory -Path $stageFull -Force | Out-Null

    $pluginDirectory = Join-Path $stageFull 'BepInEx\plugins\Horus'
    foreach ($assembly in $Assemblies) {
        Copy-PackageFile $assembly (Join-Path $pluginDirectory (Split-Path -Leaf $assembly))
    }
    foreach ($document in @('README.md', 'CHANGELOG.md', 'ROADMAP.md', 'SECURITY.md')) {
        Copy-PackageFile (Join-Path $repoRoot $document) (Join-Path $stageFull $document)
    }
    foreach ($document in @(
        'docs\dedicated-server.md',
        'docs\upgrade-from-v1.4.3.md',
        'docs\troubleshooting.md',
        'docs\releases\v2.0.0-rc.1.md',
        'docs\validation\release-checklist.md',
        'docs\validation\release-matrix.json',
        'docs\validation\2026-08-30-windows-smoke.md',
        'docs\validation\2026-08-31-linux-smoke.md',
        'docs\validation\2026-08-31-authentication-audit.md'
    )) {
        Copy-PackageFile (Join-Path $repoRoot $document) (Join-Path $stageFull $document)
    }

    if ($IncludeServerConfig) {
        Copy-PackageFile (Join-Path $repoRoot 'docs\config\Horus.Server.cfg') (Join-Path $stageFull 'BepInEx\config\Horus.Server.cfg')
        Copy-PackageFile (Join-Path $repoRoot 'docs\config\HorusMod\dedicated_admins.txt') (Join-Path $stageFull 'BepInEx\config\HorusMod\dedicated_admins.txt')
        Copy-PackageFile (Join-Path $repoRoot 'docs\config\DedicatedServerConfig.example.json') (Join-Path $stageFull 'DedicatedServerConfig.horus.example.json')
    }

    Write-PackageHashes $stageFull
    foreach ($file in Get-ChildItem -LiteralPath $stageFull -Recurse -File) { $file.LastWriteTimeUtc = $fixedTimestamp.UtcDateTime }
    $zipPath = Join-Path $OutputDirectory ("$Name-v$version.zip")
    New-DeterministicZip $stageFull $zipPath
    Remove-Item -LiteralPath $stageFull -Recurse -Force
    Get-Item -LiteralPath $zipPath
}

function Write-ReleaseSidecars {
    param([Parameter(Mandatory = $true)][System.IO.FileInfo[]]$Artifacts)
    $sourceCommit = (& git -c "safe.directory=$repoRoot" rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceCommit)) { $sourceCommit = 'unknown' }
    $dirty = @(& git -c "safe.directory=$repoRoot" status --porcelain --untracked-files=no 2>$null).Count -gt 0
    $artifactRecords = @($Artifacts | Sort-Object Name | ForEach-Object {
        [ordered]@{
            file = $_.Name
            size = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
    $manifest = [ordered]@{
        schemaVersion = 1
        version = $version
        protocolVersion = $protocolVersion
        sourceCommit = $sourceCommit.Trim()
        sourceTreeDirty = $dirty
        reproducibleTimestampUtc = $fixedTimestamp.ToString('O')
        validationMatrix = 'docs/validation/release-matrix.json'
        artifacts = $artifactRecords
    }
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    $manifestPath = Join-Path $OutputDirectory 'release-manifest.json'
    [System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6) + "`n", $utf8)
    $checksumLines = @($Artifacts | Sort-Object Name | ForEach-Object {
        "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"
    })
    $checksumLines += "$((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant())  release-manifest.json"
    [System.IO.File]::WriteAllLines((Join-Path $OutputDirectory 'SHA256SUMS.txt'), $checksumLines, $utf8)
}

if (-not $SkipBuild) {
    Invoke-DotNet @('build', (Join-Path $repoRoot 'Horus.Shared.csproj'), '-c', $Configuration, "-p:NuclearOptionDir=$NuclearOptionDir", "-p:NuclearOptionManagedDir=$NuclearOptionManagedDir")
    Invoke-DotNet @('build', (Join-Path $repoRoot 'Horus.Server.csproj'), '-c', $Configuration, "-p:NuclearOptionDir=$ServerNuclearOptionDir", "-p:NuclearOptionManagedDir=$ServerManagedDir")
    Invoke-DotNet @('build', (Join-Path $repoRoot 'HorusMod.csproj'), '-c', $Configuration, "-p:NuclearOptionDir=$NuclearOptionDir", "-p:NuclearOptionManagedDir=$NuclearOptionManagedDir")
    Invoke-DotNet @('run', '--project', (Join-Path $repoRoot 'tests\HorusLogicTests\HorusLogicTests.csproj'), '-c', $Configuration)
}

foreach ($assembly in @($sharedAssembly, $clientAssembly, $serverAssembly)) {
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw "Build output is missing: $assembly" }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$created = @()
if ($Package -in @('All', 'GM')) {
    $created += New-HorusPackage 'Horus-GM' @($sharedAssembly, $clientAssembly) $false
}
if ($Package -in @('All', 'Dedicated')) {
    $created += New-HorusPackage 'Horus-Dedicated' @($sharedAssembly, $serverAssembly) $true
}
if ($Package -in @('All', 'Full')) {
    $created += New-HorusPackage 'Horus-Full' @($sharedAssembly, $clientAssembly, $serverAssembly) $true
}

Write-ReleaseSidecars @($created)
$created | Select-Object FullName, Length, @{Name = 'SHA256'; Expression = { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } }
