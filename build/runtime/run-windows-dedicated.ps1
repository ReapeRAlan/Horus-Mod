[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ServerRoot,
    [Parameter(Mandatory = $true)][string]$ConfigPath,
    [int]$DurationMinutes = 2,
    [string]$EvidenceRoot = '',
    [int]$ReadyTimeoutSeconds = 300,
    [long]$MaxLogBytes = 16777216
)

$ErrorActionPreference = 'Stop'
if ($DurationMinutes -lt 1) { throw 'DurationMinutes must be at least 1.' }
if ($ReadyTimeoutSeconds -lt 10) { throw 'ReadyTimeoutSeconds must be at least 10.' }
if ($MaxLogBytes -lt 1048576) { throw 'MaxLogBytes must be at least 1 MiB.' }
$root = [System.IO.Path]::GetFullPath($ServerRoot)
$config = [System.IO.Path]::GetFullPath($ConfigPath)
$exe = Join-Path $root 'NuclearOptionServer.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Windows server executable not found: $exe" }
if (-not (Test-Path -LiteralPath $config -PathType Leaf)) { throw "Dedicated configuration not found: $config" }
$parsed = Get-Content -LiteralPath $config -Raw -Encoding UTF8 | ConvertFrom-Json
if ($parsed.ModdedServer -ne $true) { throw 'The runtime configuration must set ModdedServer=true.' }
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) { $EvidenceRoot = Join-Path $root 'runtime-evidence\windows' }
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$evidence = Join-Path ([System.IO.Path]::GetFullPath($EvidenceRoot)) $stamp
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$unityLog = Join-Path $evidence 'server.log'
$metrics = Join-Path $evidence 'metrics.csv'
$configCopy = Join-Path $evidence 'DedicatedServerConfig.sanitized.json'
$sanitized = $parsed
$sanitized.Password = ''
[System.IO.File]::WriteAllText($configCopy, ($sanitized | ConvertTo-Json -Depth 8) + "`n", [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($metrics, "utc,workingSetBytes,privateBytes,cpuSeconds`n", [System.Text.UTF8Encoding]::new($false))

function Quote-ProcessArgument([string]$Value) { return '"' + $Value.Replace('"', '\"') + '"' }
$arguments = @('-batchmode', '-nographics', '-logFile', (Quote-ProcessArgument $unityLog), '-DedicatedServer', (Quote-ProcessArgument $config))
$startedUtc = [DateTime]::UtcNow
$readyObserved = $false
$runtimeFailure = ''
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddMinutes($DurationMinutes)
    $readyDeadline = [DateTime]::UtcNow.AddSeconds($ReadyTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 10
        $process.Refresh()
        if ($process.HasExited) {
            $runtimeFailure = "Dedicated server exited early with code $($process.ExitCode)."
            break
        }
        $row = '{0},{1},{2},{3}' -f [DateTime]::UtcNow.ToString('O'), $process.WorkingSet64, $process.PrivateMemorySize64, $process.TotalProcessorTime.TotalSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
        Add-Content -LiteralPath $metrics -Value $row -Encoding UTF8

        if (Test-Path -LiteralPath $unityLog) {
            $logBytes = (Get-Item -LiteralPath $unityLog).Length
            if ($logBytes -gt $MaxLogBytes) {
                $runtimeFailure = "Unity log exceeded the $MaxLogBytes byte safety limit (observed $logBytes bytes)."
                break
            }
            if (-not $readyObserved -and (Select-String -LiteralPath $unityLog -SimpleMatch 'Waiting for Players before loading next map' -Quiet)) {
                $readyObserved = $true
            }
        }
        if (-not $readyObserved -and [DateTime]::UtcNow -ge $readyDeadline) {
            $runtimeFailure = "Dedicated server did not become ready within $ReadyTimeoutSeconds seconds."
            break
        }
    }
} finally {
    $process.Refresh()
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}

$bepInLog = Join-Path $root 'BepInEx\LogOutput.log'
$logs = @($unityLog)
if (Test-Path -LiteralPath $bepInLog) {
    $copiedBepInLog = Join-Path $evidence 'BepInEx.LogOutput.log'
    Copy-Item -LiteralPath $bepInLog -Destination $copiedBepInLog -Force
    $logs += $copiedBepInLog
}
Get-FileHash -Algorithm SHA256 $exe, $config, (Join-Path $root 'BepInEx\plugins\Horus\Horus.Server.dll'), (Join-Path $root 'BepInEx\plugins\Horus\Horus.Shared.dll') | Select-Object Path, Hash | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'hashes.json') -Encoding UTF8
$status = [ordered]@{
    startedUtc = $startedUtc.ToString('O')
    completedUtc = [DateTime]::UtcNow.ToString('O')
    requestedDurationMinutes = $DurationMinutes
    readyTimeoutSeconds = $ReadyTimeoutSeconds
    maxLogBytes = $MaxLogBytes
    readyObserved = $readyObserved
    runtimeFailure = $runtimeFailure
}
[System.IO.File]::WriteAllText((Join-Path $evidence 'runtime-status.json'), ($status | ConvertTo-Json -Depth 4) + "`n", [System.Text.UTF8Encoding]::new($false))
$analysisFailure = ''
try {
    & (Join-Path $PSScriptRoot 'analyze-runtime-logs.ps1') -LogPath $logs -RequiredPattern @('Horus Dedicated Server', 'Waiting for Players before loading next map') -ReportPath (Join-Path $evidence 'analysis.json')
} catch {
    $analysisFailure = $_.Exception.Message
}
if (-not [string]::IsNullOrWhiteSpace($runtimeFailure)) { throw $runtimeFailure }
if (-not [string]::IsNullOrWhiteSpace($analysisFailure)) { throw $analysisFailure }
Write-Host "Windows runtime evidence: $evidence"
