<#
    One command for the whole quality bar:

        build (warnings are errors) -> tests -> the localization gate

    Exit code is the number of failed steps, so CI can just run this file.
    -NoBuild skips the build (the gate script can do it instead).
#>
param(
    [switch]$NoBuild,
    [switch]$NoGate
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sln = Join-Path $root 'LanternUI.sln'

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)
if ($dotnet) { $dotnetPath = $dotnet.Source }
elseif (Test-Path 'C:\Program Files\dotnet\dotnet.exe') { $dotnetPath = 'C:\Program Files\dotnet\dotnet.exe' }
else { throw 'dotnet not found on PATH or in Program Files' }

$failed = 0

function Step([string]$name, [scriptblock]$action) {
    Write-Host ''
    Write-Host "== $name" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $name (exit $LASTEXITCODE)" -ForegroundColor Red
        $script:failed++
    }
}

Get-Process -Name 'LanternUI.Demo*' -ErrorAction SilentlyContinue | Stop-Process -Force

if (-not $NoBuild) {
    Step 'build (warnings are errors)' { & $dotnetPath build $sln -c Release -warnaserror -v q --nologo }
}

Step 'tests' { & $dotnetPath test $sln -c Release --nologo }

if (-not $NoGate) {
    Step 'localization gate' { & (Join-Path $root 'check-i18n.ps1') -NoBuild }
}

Write-Host ''
if ($failed -eq 0) {
    Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green
} else {
    Write-Host "$failed CHECK(S) FAILED" -ForegroundColor Red
}
exit $failed
