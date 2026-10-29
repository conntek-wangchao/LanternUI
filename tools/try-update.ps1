<#
    Walks the whole update path once, against a folder of your own:

        tools\try-update.ps1                 # sandbox from release-1.0.0, update to release-1.0.1
        tools\try-update.ps1 -Fresh -Tamper  # recreate the install, and prove a corrupted package is refused

    It does exactly what a user would: check, download, then start the application normally — the
    update is applied before the window opens. Nothing here reaches the network: both releases are
    folders on disk.
#>
param(
    [string]$From = "release-1.0.0",
    [string]$To = "release-1.0.1",
    [string]$Sandbox = "sandbox",
    # Where the update comes from: a folder next to this repo, or a full URL to release.json.
    [string]$Channel = "",
    [switch]$Fresh,
    [switch]$Tamper,
    [switch]$UiTest
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
function Full([string]$path) { if ([System.IO.Path]::IsPathRooted($path)) { $path } else { Join-Path $root $path } }

$fromDir = Full $From
$toDir = Full $To
$sandboxDir = Full $Sandbox
$install = Join-Path $sandboxDir 'install'
$exe = Join-Path $install 'LanternUI.Demo.exe'
$config = Join-Path $sandboxDir 'settings.json'

foreach ($dir in $fromDir, $toDir) {
    if (-not (Test-Path (Join-Path $dir 'release.json'))) {
        throw "no release.json in $dir — make one with tools\pack.ps1 -Version <version> -Out <dir>"
    }
}

function ZipOf([string]$dir) {
    $manifest = Get-Content (Join-Path $dir 'release.json') -Raw | ConvertFrom-Json
    Join-Path $dir $manifest.package
}

function Stop-App { Get-Process -Name 'LanternUI.Demo*' -ErrorAction SilentlyContinue | Stop-Process -Force }

function Invoke-Step([string]$name, [string[]]$arguments, [int]$expected) {
    $exe = Join-Path $install 'LanternUI.Demo.exe'
    & $exe @arguments 2>&1 | Select-Object -Last 2 | ForEach-Object { "    $_" }
    $code = $LASTEXITCODE
    $mark = if ($code -eq $expected) { 'ok  ' } else { 'FAIL' }
    "$mark $name (exit $code, expected $expected)"
    return $code -eq $expected
}

Stop-App
$ok = $true

if ($Fresh -or -not (Test-Path $exe)) {
    if (Test-Path $sandboxDir) { Remove-Item $sandboxDir -Recurse -Force }
    New-Item -ItemType Directory -Path $install -Force | Out-Null
    Expand-Archive -Path (ZipOf $fromDir) -DestinationPath $install -Force
    '{}' | Set-Content $config -Encoding UTF8
    "installed $(Split-Path -Leaf $fromDir) into $install"
}

if ($UiTest) {
    # Stop here: the rest is what the window's About page does.
    $target = if ($Channel) { $Channel } else { $toDir }
    $settings = Get-Content $config -Raw | ConvertFrom-Json
    $settings | Add-Member -NotePropertyName ui_update_source -NotePropertyValue $target -Force
    $settings | ConvertTo-Json | Set-Content $config -Encoding UTF8
    ''
    'ready for the window test:'
    "  run  $exe"
    '  About page -> Update channel is already filled with ' + $target
    '  press "Check now", then "Install 1.0.1 ..." and confirm the restart'
    ''
    return
}

"installed version: " + (Get-Item $exe).VersionInfo.FileVersion
$before = (Get-Item $exe).VersionInfo.FileVersion

$ok = (Invoke-Step 'check for an update' @('--config', $config, '--update-source', $toDir, '--check-updates') 0) -and $ok

if ($Tamper) {
    $bad = Join-Path $sandboxDir 'tampered-release'
    if (Test-Path $bad) { Remove-Item $bad -Recurse -Force }
    Copy-Item $toDir $bad -Recurse
    Add-Content (Join-Path $bad (Split-Path -Leaf (ZipOf $toDir))) 'x'
    $ok = (Invoke-Step 'a corrupted package is refused' @('--config', $config, '--update-source', $bad, '--download-update') 1) -and $ok
    "    staged after the corrupted try: $(Test-Path (Join-Path $sandboxDir 'updates\pending.json'))"
}

$ok = (Invoke-Step 'download and stage' @('--config', $config, '--update-source', $toDir, '--download-update') 0) -and $ok
"    pending marker: $(Test-Path (Join-Path $sandboxDir 'updates\pending.json'))"

# A normal start: the pending update is applied before the window opens. --shot closes it again.
$shot = Join-Path $sandboxDir 'after-update.png'
Remove-Item $shot -Force -ErrorAction SilentlyContinue
& $exe --config $config --lang en --shot $shot 2>&1 | Select-Object -Last 2 | ForEach-Object { "    $_" }
$ok = ($LASTEXITCODE -eq 0) -and $ok

Stop-App
$after = (Get-Item $exe).VersionInfo.FileVersion
"version: $before -> $after"
"pending marker now: $(Test-Path (Join-Path $sandboxDir 'updates\pending.json'))"
"staged copies left: $((Get-ChildItem (Join-Path $sandboxDir 'updates') -Force -ErrorAction SilentlyContinue | Measure-Object).Count)"
"window after the update: $(Test-Path $shot)"

$log = Get-ChildItem (Join-Path $sandboxDir 'logs') -Filter '*.log' -ErrorAction SilentlyContinue | Select-Object -Last 1
if ($log) {
    'log tail:'
    Get-Content $log.FullName -Encoding UTF8 | Select-String -Pattern 'update' | Select-Object -Last 6 | ForEach-Object { "    " + $_.Line }
}

if (-not $ok) { throw 'the update path did not finish as expected' }
if ($before -eq $after) { throw "the version did not change ($before)" }
'the whole path works: checked, verified, installed, and the new version is in place'