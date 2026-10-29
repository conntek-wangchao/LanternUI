# The i18n gate: builds the demo, runs its offscreen self-check, prints the report and exits
# with the check's own code, so CI fails when a required language has gaps or a placeholder broke.
#
#   .\check-i18n.ps1              build, then check (every listed language is required)
#   .\check-i18n.ps1 -NoBuild     check the last build
#   .\check-i18n.ps1 -Require zh-CN,ja   require only these while others are being written
param(
    [switch]$NoBuild,
    [string]$Require = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'demos\LanternUI.Demo\LanternUI.Demo.csproj'
$exe = Join-Path $root 'demos\LanternUI.Demo\bin\Release\net8.0-windows\LanternUI.Demo.exe'

if (-not $NoBuild) {
    $dotnet = 'C:\Program Files\dotnet\dotnet.exe'
    $found = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($found) { $dotnet = $found.Source }
    & $dotnet build $project -c Release -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$config = Join-Path $env:TEMP 'lanternui\i18n.json'
$arguments = @('--config', $config, '--check-i18n')
if ($Require) { $arguments += @('--require', $Require) }

# A WinExe is not waited for by '&' and sets no $LASTEXITCODE: start it and read its code.
$process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -Wait
$code = $process.ExitCode

$report = Join-Path (Split-Path $config) 'i18n-report.txt'
if (Test-Path $report) { Get-Content $report -Encoding UTF8 }

if ($code -eq 0) { Write-Host "i18n: OK" -ForegroundColor Green }
else { Write-Host "i18n: FAILED (exit $code)" -ForegroundColor Red }
exit $code
