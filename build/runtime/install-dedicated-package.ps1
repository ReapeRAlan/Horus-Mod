[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ServerRoot,
    [Parameter(Mandatory = $true)][string]$PackagePath
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath($ServerRoot)
$package = [System.IO.Path]::GetFullPath($PackagePath)
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Server root not found: $root" }
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw "Package not found: $package" }
if ([System.IO.Path]::GetExtension($package) -ne '.zip') { throw 'PackagePath must be a ZIP file.' }
if (-not (Test-Path -LiteralPath (Join-Path $root 'NuclearOptionServer.exe')) -and -not (Test-Path -LiteralPath (Join-Path $root 'NuclearOptionServer.x86_64'))) {
    throw "The target is not an official Nuclear Option dedicated-server root: $root"
}

$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('horus-package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
try {
    Expand-Archive -LiteralPath $package -DestinationPath $temp -Force
    $pluginSource = Join-Path $temp 'BepInEx\plugins\Horus'
    if (-not (Test-Path -LiteralPath (Join-Path $pluginSource 'Horus.Server.dll'))) { throw 'The ZIP is not a Horus Dedicated or Full package.' }
    $pluginTarget = Join-Path $root 'BepInEx\plugins\Horus'
    New-Item -ItemType Directory -Path $pluginTarget -Force | Out-Null
    Get-ChildItem -LiteralPath $pluginSource -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $pluginTarget $_.Name) -Force }

    foreach ($relative in @('BepInEx\config\Horus.Server.cfg', 'BepInEx\config\HorusMod\dedicated_admins.txt')) {
        $source = Join-Path $temp $relative
        $target = Join-Path $root $relative
        if (-not (Test-Path -LiteralPath $target)) {
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $target
        }
    }
    Write-Host "Installed Horus dedicated assemblies in $pluginTarget"
    Write-Host 'Existing server configuration and allowlist files were preserved.'
} finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}
