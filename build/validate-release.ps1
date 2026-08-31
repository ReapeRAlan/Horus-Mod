[CmdletBinding()]
param(
    [switch]$PublicCi,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$NuclearOptionDir = '',
    [string]$NuclearOptionManagedDir = '',
    [string]$ServerNuclearOptionDir = '',
    [string]$ServerManagedDir = '',
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = '2.0.0-rc.1'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $repoRoot 'dist' }
if ([string]::IsNullOrWhiteSpace($NuclearOptionDir)) { $NuclearOptionDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot '..')) }
if ([string]::IsNullOrWhiteSpace($NuclearOptionManagedDir)) { $NuclearOptionManagedDir = Join-Path $NuclearOptionDir 'NuclearOption_Data\Managed' }
if ([string]::IsNullOrWhiteSpace($ServerNuclearOptionDir)) { $ServerNuclearOptionDir = $NuclearOptionDir }
if ([string]::IsNullOrWhiteSpace($ServerManagedDir)) { $ServerManagedDir = $NuclearOptionManagedDir }

function Write-Step([string]$Message) { Write-Host "`n==> $Message" -ForegroundColor Cyan }
function Invoke-Checked {
    param([Parameter(Mandatory = $true)][string]$File, [Parameter(Mandatory = $true)][string[]]$Arguments)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit code ${LASTEXITCODE}: $($Arguments -join ' ')" }
}

function Get-RepositoryFiles {
    $items = @(& git -c "safe.directory=$repoRoot" ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed.' }
    return $items | Where-Object { $_ -and $_ -notmatch '^(bin|obj|dist)/' }
}

function Test-TextAndLanguage {
    Write-Step 'Validating UTF-8 and global-English visible text'
    $strictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)
    $extensions = @('.md', '.cs', '.csproj', '.props', '.ps1', '.json', '.cfg', '.txt', '.yml', '.yaml', '.sh', '.bat', '.xml')
    $spanishPattern = '(?i)\b(servidor(?:es)?|jugador(?:es)?|misi[oó]n|unidad(?:es)?|configuraci[oó]n|instalaci[oó]n|gu[ií]a|prueba(?:s)?|deshacer|rehacer|f[aá]brica(?:s)?|eliminar|borrar|atacar|permitido|denegado)\b|[¿¡]'
    $replacement = [char]0xFFFD
    $mojibake = @(
        ([char]0x00C2).ToString() + [char]0x00B0,
        ([char]0x00E2).ToString() + [char]0x20AC + [char]0x201D,
        ([char]0x00E2).ToString() + [char]0x20AC + [char]0x201C,
        ([char]0x00C3).ToString() + [char]0x00A1
    )
    foreach ($relative in Get-RepositoryFiles) {
        $leaf = [System.IO.Path]::GetFileName($relative)
        $extension = [System.IO.Path]::GetExtension($relative).ToLowerInvariant()
        if ($extension -notin $extensions -and $leaf -notin @('.editorconfig', '.gitattributes', 'LICENSE')) { continue }
        $full = Join-Path $repoRoot $relative
        try { $text = [System.IO.File]::ReadAllText($full, $strictUtf8) }
        catch { throw "Invalid UTF-8 file: $relative. $($_.Exception.Message)" }
        if ($text.Contains($replacement)) { throw "Unicode replacement character found in $relative." }
        foreach ($token in $mojibake) { if ($text.Contains($token)) { throw "Mojibake token found in $relative." } }
        $normalized = $relative.Replace('\', '/')
        $isVisible = $normalized -match '^(src|docs|build|\.github)/' -or $normalized -match '^(README|CHANGELOG|ROADMAP|SECURITY|CONTRIBUTING)\.md$' -or $extension -in @('.cfg', '.json')
        $isIntentionalTestVector = $normalized -eq 'tests/HorusLogicTests/Program.cs'
        $isLanguageValidator = $normalized -eq 'build/validate-release.ps1'
        if ($isVisible -and -not $isIntentionalTestVector -and -not $isLanguageValidator -and $text -match $spanishPattern) {
            throw "Non-English visible text found in ${relative}: $($Matches[0])"
        }
    }
}

function Test-JsonAndConfig {
    Write-Step 'Validating JSON and fail-closed configuration templates'
    foreach ($relative in Get-RepositoryFiles | Where-Object { [System.IO.Path]::GetExtension($_) -eq '.json' }) {
        try { Get-Content -LiteralPath (Join-Path $repoRoot $relative) -Raw -Encoding UTF8 | ConvertFrom-Json | Out-Null }
        catch { throw "Invalid JSON: $relative. $($_.Exception.Message)" }
    }
    $dedicated = Get-Content -LiteralPath (Join-Path $repoRoot 'docs/config/DedicatedServerConfig.example.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($dedicated.ModdedServer -ne $true) { throw 'DedicatedServerConfig.example.json must set ModdedServer=true.' }
    $serverConfig = Get-Content -LiteralPath (Join-Path $repoRoot 'docs/config/Horus.Server.cfg') -Raw -Encoding UTF8
    if ($serverConfig -notmatch '(?m)^Enabled\s*=\s*false\s*$') { throw 'Horus.Server.cfg must fail closed with Enabled=false.' }
    $allowlist = Get-Content -LiteralPath (Join-Path $repoRoot 'docs/config/HorusMod/dedicated_admins.txt') -Encoding UTF8
    if ($allowlist | Where-Object { $_ -match '^\s*\d{17}\s*$' }) { throw 'The packaged administrator allowlist must be empty.' }
}

function Test-MarkdownLinks {
    Write-Step 'Validating relative Markdown links'
    foreach ($relative in Get-RepositoryFiles | Where-Object { [System.IO.Path]::GetExtension($_) -eq '.md' }) {
        $full = Join-Path $repoRoot $relative
        $text = Get-Content -LiteralPath $full -Raw -Encoding UTF8
        foreach ($match in [regex]::Matches($text, '\]\((?!https?://|mailto:|#)([^)]+)\)')) {
            $target = $match.Groups[1].Value.Split('#')[0].Replace('%20', ' ')
            if ([string]::IsNullOrWhiteSpace($target) -or $target.StartsWith('<')) { continue }
            $resolved = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $full) $target))
            if (-not (Test-Path -LiteralPath $resolved)) { throw "Broken Markdown link in ${relative}: $target" }
        }
    }
}

function Test-VersionConsistency {
    Write-Step 'Validating version consistency'
    foreach ($project in @('Horus.Shared.csproj', 'Horus.Server.csproj', 'HorusMod.csproj')) {
        [xml]$xml = Get-Content -LiteralPath (Join-Path $repoRoot $project) -Raw -Encoding UTF8
        $declared = @($xml.Project.PropertyGroup.Version | Where-Object { $_ })[0]
        if ($declared -ne $version) { throw "$project declares version '$declared' instead of '$version'." }
    }
    foreach ($relative in @('package.ps1', 'src/HorusPlugin.cs', 'src/Server/HorusServerPlugin.cs', 'README.md', 'CHANGELOG.md')) {
        if ((Get-Content -LiteralPath (Join-Path $repoRoot $relative) -Raw -Encoding UTF8) -notmatch [regex]::Escape($version)) {
            throw "$relative does not reference $version."
        }
    }
}

function Test-ScriptSyntax {
    Write-Step 'Validating PowerShell syntax and tracked artifacts'
    foreach ($relative in Get-RepositoryFiles | Where-Object { [System.IO.Path]::GetExtension($_) -eq '.ps1' }) {
        $tokens = $null; $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $repoRoot $relative), [ref]$tokens, [ref]$errors) | Out-Null
        if ($errors.Count -gt 0) { throw "PowerShell syntax error in ${relative}: $($errors[0].Message)" }
    }
    if ($env:OS -ne 'Windows_NT') {
        foreach ($relative in Get-RepositoryFiles | Where-Object { [System.IO.Path]::GetExtension($_) -eq '.sh' }) {
            Invoke-Checked 'bash' @('-n', (Join-Path $repoRoot $relative))
        }
    }
    $trackedBinaries = @(& git -c "safe.directory=$repoRoot" ls-files '*.dll' '*.zip')
    if ($trackedBinaries.Count -gt 0) { throw "Compiled or proprietary artifacts are tracked: $($trackedBinaries -join ', ')" }
}

function Test-PackageArchive {
    param([Parameter(Mandatory = $true)][string]$ZipPath)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entries = @($archive.Entries | Where-Object { $_.Name })
        if (($entries.FullName | Sort-Object -Unique).Count -ne $entries.Count) { throw "Duplicate ZIP entries in $ZipPath." }
        $allowedDlls = @('BepInEx/plugins/Horus/Horus.Shared.dll', 'BepInEx/plugins/Horus/Horus.Client.dll', 'BepInEx/plugins/Horus/Horus.Server.dll')
        foreach ($entry in $entries | Where-Object { $_.FullName.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase) }) {
            if ($entry.FullName -notin $allowedDlls) { throw "Unexpected dependency in package: $($entry.FullName)" }
        }
        foreach ($required in @('README.md', 'CHANGELOG.md', 'ROADMAP.md', 'SECURITY.md', 'docs/dedicated-server.md', 'docs/upgrade-from-v1.4.3.md', 'docs/troubleshooting.md', 'docs/releases/v2.0.0-rc.1.md', 'docs/validation/release-checklist.md', 'docs/validation/release-matrix.json', 'docs/validation/2026-08-30-windows-smoke.md', 'docs/validation/2026-08-31-linux-smoke.md', 'SHA256SUMS')) {
            if ($required -notin $entries.FullName) { throw "Missing package entry $required in $ZipPath." }
        }
        $manifestEntry = $entries | Where-Object { $_.FullName -eq 'SHA256SUMS' }
        $reader = New-Object System.IO.StreamReader($manifestEntry.Open(), [System.Text.UTF8Encoding]::new($false, $true))
        try { $lines = @($reader.ReadToEnd() -split "`r?`n" | Where-Object { $_ }) } finally { $reader.Dispose() }
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            foreach ($line in $lines) {
                if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw "Invalid embedded checksum line in ${ZipPath}: $line" }
                $expected = $Matches[1]; $name = $Matches[2]
                $entry = $entries | Where-Object { $_.FullName -eq $name }
                if ($null -eq $entry) { throw "Embedded checksum references missing entry $name." }
                $stream = $entry.Open()
                try { $actual = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() } finally { $stream.Dispose() }
                if ($actual -ne $expected) { throw "Embedded checksum mismatch for $name." }
            }
        } finally { $sha.Dispose() }
    } finally { $archive.Dispose() }
}

Push-Location $repoRoot
try {
    Test-TextAndLanguage
    Test-JsonAndConfig
    Test-MarkdownLinks
    Test-VersionConsistency
    Test-ScriptSyntax

    Write-Step 'Building portable contracts and running logic/security tests'
    Invoke-Checked 'dotnet' @('restore', (Join-Path $repoRoot 'tests/HorusLogicTests/HorusLogicTests.csproj'), '--nologo')
    Invoke-Checked 'dotnet' @('build', (Join-Path $repoRoot 'Horus.Shared.csproj'), '-c', $Configuration, '--nologo', '-warnaserror')
    Invoke-Checked 'dotnet' @('run', '--project', (Join-Path $repoRoot 'tests/HorusLogicTests/HorusLogicTests.csproj'), '-c', $Configuration, '--no-restore')

    if (-not $PublicCi) {
        Write-Step 'Building game-dependent client and server assemblies'
        Invoke-Checked 'dotnet' @('build', (Join-Path $repoRoot 'Horus.Server.csproj'), '-c', $Configuration, '--nologo', '-warnaserror', "-p:NuclearOptionDir=$ServerNuclearOptionDir", "-p:NuclearOptionManagedDir=$ServerManagedDir")
        Invoke-Checked 'dotnet' @('build', (Join-Path $repoRoot 'HorusMod.csproj'), '-c', $Configuration, '--nologo', '-warnaserror', "-p:NuclearOptionDir=$NuclearOptionDir", "-p:NuclearOptionManagedDir=$NuclearOptionManagedDir")
        & (Join-Path $repoRoot 'build/verify-server-assembly.ps1') -ServerAssembly (Join-Path $repoRoot "bin/$Configuration/net472/Horus.Server.dll")

        Write-Step 'Building and comparing deterministic release packages'
        $secondOutput = Join-Path $repoRoot 'obj/release-validation/second-build'
        if (Test-Path -LiteralPath $secondOutput) { Remove-Item -LiteralPath $secondOutput -Recurse -Force }
        & (Join-Path $repoRoot 'package.ps1') -Package All -Configuration $Configuration -OutputDirectory $OutputDirectory -NuclearOptionDir $NuclearOptionDir -NuclearOptionManagedDir $NuclearOptionManagedDir -ServerNuclearOptionDir $ServerNuclearOptionDir -ServerManagedDir $ServerManagedDir -SkipBuild
        & (Join-Path $repoRoot 'package.ps1') -Package All -Configuration $Configuration -OutputDirectory $secondOutput -NuclearOptionDir $NuclearOptionDir -NuclearOptionManagedDir $NuclearOptionManagedDir -ServerNuclearOptionDir $ServerNuclearOptionDir -ServerManagedDir $ServerManagedDir -SkipBuild
        foreach ($name in @("Horus-GM-v$version.zip", "Horus-Dedicated-v$version.zip", "Horus-Full-v$version.zip", 'release-manifest.json', 'SHA256SUMS.txt')) {
            $first = Join-Path $OutputDirectory $name; $second = Join-Path $secondOutput $name
            if (-not (Test-Path -LiteralPath $first) -or -not (Test-Path -LiteralPath $second)) { throw "Missing reproducibility artifact: $name" }
            if ((Get-FileHash -LiteralPath $first -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $second -Algorithm SHA256).Hash) { throw "Non-deterministic artifact: $name" }
        }
        foreach ($zip in Get-ChildItem -LiteralPath $OutputDirectory -Filter "Horus-*-v$version.zip") { Test-PackageArchive $zip.FullName }
    }

    Write-Step 'Checking repository whitespace'
    Invoke-Checked 'git' @('-c', "safe.directory=$repoRoot", 'diff', '--check')
    Write-Host "`nHORUS RELEASE VALIDATION: PASS" -ForegroundColor Green
} finally {
    Pop-Location
}
