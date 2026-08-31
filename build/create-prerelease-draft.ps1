[CmdletBinding()]
param(
    [string]$Repository = 'ReapeRAlan/Horus-Mod',
    [string]$Tag = 'v2.0.0-rc.1',
    [string]$StableTag = 'v1.4.3',
    [switch]$CreateDraft
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = $Tag.TrimStart('v')
$notesPath = Join-Path $repoRoot "docs\releases\$Tag.md"
$dist = Join-Path $repoRoot 'dist'
$manifestPath = Join-Path $dist 'release-manifest.json'
$checksumsPath = Join-Path $dist 'SHA256SUMS.txt'
$matrixPath = Join-Path $repoRoot 'docs\validation\release-matrix.json'
$assetNames = @(
    "Horus-GM-$Tag.zip",
    "Horus-Dedicated-$Tag.zip",
    "Horus-Full-$Tag.zip",
    'SHA256SUMS.txt',
    'release-manifest.json'
)

function Invoke-Git([string[]]$Arguments) {
    $output = @(& git -c "safe.directory=$repoRoot" @Arguments 2>$null)
    if ($LASTEXITCODE -ne 0) { throw "git failed: $($Arguments -join ' ')" }
    return ($output -join "`n").Trim()
}

function Invoke-Gh([string[]]$Arguments) {
    $output = @(& gh @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "gh failed: $($Arguments -join ' ')`n$($output -join "`n")" }
    return ($output -join "`n").Trim()
}

Push-Location $repoRoot
try {
    if ($Tag -ne 'v2.0.0-rc.1') { throw 'This release-candidate script is pinned to v2.0.0-rc.1.' }
    if ((Invoke-Git @('branch', '--show-current')) -ne 'main') { throw 'Draft creation is allowed only from main after the release PR is merged.' }
    if (Invoke-Git @('status', '--porcelain', '--untracked-files=all')) { throw 'The release source tree must be clean.' }

    $head = Invoke-Git @('rev-parse', 'HEAD')
    if ((Invoke-Git @('rev-parse', 'origin/main')) -ne $head) { throw 'HEAD must exactly match origin/main.' }
    if ((Invoke-Git @('cat-file', '-t', "refs/tags/$Tag")) -ne 'tag') { throw "$Tag must exist locally as an annotated tag." }
    if ((Invoke-Git @('rev-list', '-n', '1', $Tag)) -ne $head) { throw "$Tag must point to the exact main HEAD." }
    if ((Invoke-Git @('rev-parse', "$StableTag^{commit}")) -eq $head) { throw "$StableTag must remain on its historical stable commit." }

    $remoteTag = Invoke-Git @('ls-remote', '--tags', 'origin', "refs/tags/$Tag", "refs/tags/$Tag^{}")
    if ($remoteTag -notmatch "(?m)^$([regex]::Escape($head))\s+refs/tags/$([regex]::Escape($Tag))\^\{\}$") {
        throw "$Tag must be pushed as an annotated tag whose peeled commit is the exact main HEAD."
    }

    foreach ($required in @($notesPath, $manifestPath, $checksumsPath, $matrixPath)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required release file is missing: $required" }
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.version -ne $version -or $manifest.sourceCommit -ne $head -or $manifest.sourceTreeDirty -ne $false) {
        throw 'release-manifest.json must be clean and bound to the exact tagged commit.'
    }
    $archiveNames = @($assetNames | Where-Object { $_.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase) })
    if (@($manifest.artifacts).Count -ne $archiveNames.Count) { throw 'release-manifest.json has an unexpected artifact count.' }
    foreach ($name in $archiveNames) {
        $record = @($manifest.artifacts | Where-Object { $_.file -eq $name })
        $path = Join-Path $dist $name
        if ($record.Count -ne 1 -or [long]$record[0].size -ne (Get-Item -LiteralPath $path).Length -or
            $record[0].sha256 -ne (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()) {
            throw "release-manifest.json does not match $name."
        }
    }

    $matrix = Get-Content -LiteralPath $matrixPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($requiredCheck in @('automated-portable', 'windows-headless-soak', 'linux-headless-soak', 'release-assets')) {
        $check = @($matrix.checks | Where-Object { $_.id -eq $requiredCheck })
        if ($check.Count -ne 1 -or $check[0].status -ne 'PASS') { throw "Release-matrix check is not PASS: $requiredCheck" }
    }
    foreach ($pendingCheck in @('windows-full-runtime', 'linux-full-runtime')) {
        $check = @($matrix.checks | Where-Object { $_.id -eq $pendingCheck })
        if ($check.Count -ne 1 -or $check[0].status -ne 'PENDING') { throw "Test-RC connected-runtime check must be explicitly PENDING: $pendingCheck" }
    }
    $notes = Get-Content -LiteralPath $notesPath -Raw -Encoding UTF8
    foreach ($warning in @('TEST RELEASE', 'EXPERIMENTAL PRERELEASE', 'NOT PRODUCTION-CERTIFIED', '## Pending')) {
        if (-not $notes.Contains($warning)) { throw "Release notes are missing the mandatory test warning: $warning" }
    }
    $manualPath = Join-Path $repoRoot 'docs\user-manual.md'
    $manual = Get-Content -LiteralPath $manualPath -Raw -Encoding UTF8
    foreach ($warning in @('TEST RELEASE', 'EXPERIMENTAL PRERELEASE', 'NOT PRODUCTION-CERTIFIED', 'Still pending')) {
        if (-not $manual.Contains($warning)) { throw "User manual is missing the mandatory test warning: $warning" }
    }

    $checksumRecords = @{}
    foreach ($line in Get-Content -LiteralPath $checksumsPath -Encoding UTF8) {
        if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw "Invalid SHA256SUMS line: $line" }
        $checksumRecords[$Matches[2]] = $Matches[1]
    }
    foreach ($name in $assetNames) {
        $path = Join-Path $dist $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Release asset is missing: $name" }
        if ($name -eq 'SHA256SUMS.txt') { continue }
        if (-not $checksumRecords.ContainsKey($name)) { throw "SHA256SUMS.txt does not contain $name." }
        $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $checksumRecords[$name]) { throw "Local asset checksum mismatch: $name" }
    }

    Invoke-Gh @('auth', 'status', '--hostname', 'github.com') | Out-Null
    $stable = Invoke-Gh @('release', 'view', $StableTag, '--repo', $Repository, '--json', 'isDraft,isPrerelease,tagName') | ConvertFrom-Json
    if ($stable.tagName -ne $StableTag -or $stable.isDraft -or $stable.isPrerelease) { throw "$StableTag is no longer the published stable release." }
    $latestTag = Invoke-Gh @('api', "repos/$Repository/releases/latest", '--jq', '.tag_name')
    if ($latestTag -ne $StableTag) { throw "$StableTag must remain GitHub Latest before creating the RC draft." }

    & gh release view $Tag --repo $Repository *> $null
    if ($LASTEXITCODE -eq 0) { throw "A release already exists for $Tag; this script will not replace or update it." }
    if (-not $CreateDraft) {
        Write-Host 'PRERELEASE DRAFT PREFLIGHT: PASS'
        Write-Host 'No GitHub release was created. Re-run with -CreateDraft only after explicit publication approval.'
        return
    }

    $assetPaths = @($assetNames | ForEach-Object { Join-Path $dist $_ })
    Invoke-Gh (@('release', 'create', $Tag) + $assetPaths + @('--repo', $Repository, '--verify-tag', '--draft', '--prerelease', '--latest=false', '--fail-on-no-commits', '--title', "TEST RELEASE - Horus $Tag", '--notes-file', $notesPath)) | Write-Host

    $release = Invoke-Gh @('release', 'view', $Tag, '--repo', $Repository, '--json', 'tagName,isDraft,isPrerelease,isImmutable,assets') | ConvertFrom-Json
    if ($release.tagName -ne $Tag -or -not $release.isDraft -or -not $release.isPrerelease -or $release.isImmutable) {
        throw 'The created release does not have the expected draft/prerelease state.'
    }
    foreach ($name in $assetNames) {
        $remote = @($release.assets | Where-Object { $_.name -eq $name })
        if ($remote.Count -ne 1) { throw "Draft release asset is missing or duplicated: $name" }
        $localPath = Join-Path $dist $name
        $localDigest = 'sha256:' + (Get-FileHash -LiteralPath $localPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($remote[0].digest -ne $localDigest -or [long]$remote[0].size -ne (Get-Item -LiteralPath $localPath).Length) {
            throw "Remote draft asset verification failed: $name"
        }
    }
    if ((Invoke-Gh @('api', "repos/$Repository/releases/latest", '--jq', '.tag_name')) -ne $StableTag) {
        throw "$StableTag did not remain GitHub Latest after draft creation."
    }
    Write-Host 'PRERELEASE DRAFT CREATION: PASS'
    Write-Host 'The draft remains unpublished and must be reviewed manually before any publish action.'
} finally {
    Pop-Location
}
