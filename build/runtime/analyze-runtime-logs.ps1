[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$LogPath,
    [string[]]$RequiredPattern = @(),
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'
$fatalPatterns = @(
    '(?i)unhandled exception',
    '(?i)nullreferenceexception',
    '(?i)stackoverflowexception',
    '(?i)outofmemoryexception',
    '(?i)failed to load.*Horus',
    '(?i)rejected.*Horus.*plugin',
    '(?i)\[Error\s*:\s*Horus'
)

$resolved = @()
foreach ($path in $LogPath) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Runtime log not found: $path" }
    $resolved += (Resolve-Path -LiteralPath $path).Path
}

$findings = @()
foreach ($path in $resolved) {
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $path -Encoding UTF8) {
        $lineNumber++
        foreach ($pattern in $fatalPatterns) {
            if ($line -match $pattern) {
                $findings += [pscustomobject]@{ File = $path; Line = $lineNumber; Text = $line.Trim() }
                break
            }
        }
    }
}

$missing = @()
foreach ($pattern in $RequiredPattern) {
    $found = $false
    foreach ($path in $resolved) {
        if (Select-String -LiteralPath $path -Pattern $pattern -Quiet) { $found = $true; break }
    }
    if (-not $found) { $missing += $pattern }
}

$report = [ordered]@{
    generatedUtc = [DateTime]::UtcNow.ToString('O')
    logs = $resolved
    fatalFindingCount = $findings.Count
    fatalFindings = @($findings)
    requiredPatterns = @($RequiredPattern)
    missingRequiredPatterns = @($missing)
    result = if ($findings.Count -eq 0 -and $missing.Count -eq 0) { 'PASS' } else { 'FAIL' }
}

if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $parent = Split-Path -Parent ([System.IO.Path]::GetFullPath($ReportPath))
    if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    [System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($ReportPath), ($report | ConvertTo-Json -Depth 6) + "`n", [System.Text.UTF8Encoding]::new($false))
}

$report | ConvertTo-Json -Depth 6
if ($report.result -ne 'PASS') { throw "Runtime log validation failed: fatal=$($findings.Count), missing=$($missing.Count)." }
