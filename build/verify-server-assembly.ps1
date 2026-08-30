[CmdletBinding()]
param([string]$ServerAssembly = '')

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($ServerAssembly)) {
    $ServerAssembly = Join-Path $repoRoot 'bin\Release\net472\Horus.Server.dll'
}
$ServerAssembly = [System.IO.Path]::GetFullPath($ServerAssembly)
if (-not (Test-Path -LiteralPath $ServerAssembly -PathType Leaf)) {
    throw "Server assembly not found: $ServerAssembly"
}

$banned = @(
    'Rewired_Core',
    'UnityEngine.IMGUIModule',
    'UnityEngine.InputLegacyModule',
    'UnityEngine.TextRenderingModule',
    'UnityEngine.UI',
    'UnityEngine.UIModule'
)
$assembly = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($ServerAssembly)
$references = @($assembly.GetReferencedAssemblies() | ForEach-Object Name | Sort-Object -Unique)
$violations = @($references | Where-Object { $_ -in $banned })
if ($violations.Count -gt 0) {
    throw "Horus.Server.dll contains forbidden graphical/input references: $($violations -join ', ')"
}

[pscustomobject]@{
    Assembly = $ServerAssembly
    Result = 'PASS'
    References = $references -join ', '
}
