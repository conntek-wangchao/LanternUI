<#
    Makes one release the updater can install:

        <Out>/release.json                  { version, beta, notes, package, sha256, size, published }
        <Out>/LanternUI-<version>.zip       the published application, files at the zip root

    The package carries its own .NET runtime by default (self-contained), so a target machine needs
    nothing installed — the price is size. Modes:

        -Mode self-contained-single   (default) one exe, runtime inside it          ~70 MB
        -Mode self-contained-folder   exe + the runtime beside it                   ~180 MB, ~200 files
        -Mode framework-dependent     what a developer already has on the machine   ~0.6 MB

    sha256 is what makes the download verification mean something, so a release without it is refused
    on the other side (UpdateService).

        tools\pack.ps1 -Version 1.2.0 -Beta 2 -Notes "fixes the tabs"
#>
param(
    [string]$Out = "release",
    [string]$Version = "1.0.0",
    [int]$Beta = 0,
    [string]$Notes = "",
    [string]$Runtime = "win-x64",
    [ValidateSet("self-contained-single", "self-contained-folder", "framework-dependent")]
    [string]$Mode = "self-contained-single",
    [switch]$IncludeSymbols
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$project = Join-Path $root 'demos\LanternUI.Demo\LanternUI.Demo.csproj'
$outDir = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $root $Out }
$appDir = Join-Path $outDir 'app'

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)
if ($dotnet) { $dotnetPath = $dotnet.Source }
elseif (Test-Path 'C:\Program Files\dotnet\dotnet.exe') { $dotnetPath = 'C:\Program Files\dotnet\dotnet.exe' }
else { throw 'dotnet not found' }

Get-Process -Name 'LanternUI.Demo*' -ErrorAction SilentlyContinue | Stop-Process -Force
if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }

$arguments = @(
    'publish', $project,
    '-c', 'Release',
    '-o', $appDir,
    "-p:Version=$Version",
    # The beta number rides on InformationalVersion: it is how a build knows it is a pre-release and
    # stays on the beta track until someone chooses.
    "-p:InformationalVersion=$(if ($Beta -gt 0) { "$Version-beta.$Beta" } else { "$Version" })",
    '--nologo', '-v', 'q'
)

switch ($Mode) {
    'self-contained-single' {
        $arguments += @('-r', $Runtime, '--self-contained', 'true',
            '-p:PublishSingleFile=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:EnableCompressionInSingleFile=true',
            # WPF loads UIAutomationTypes by name from the framework folder, which a bundle alone does
            # not answer: without this the packaged window dies in KeyboardNavigation's constructor.
            '-p:IncludeAllContentForSelfExtract=true')
    }
    'self-contained-folder' {
        $arguments += @('-r', $Runtime, '--self-contained', 'true')
    }
    'framework-dependent' {
        # Nothing extra: the package relies on the .NET 8 Desktop Runtime being installed.
    }
}

if (-not $IncludeSymbols) { $arguments += '-p:DebugType=none' }

Write-Host "publishing $Version ($Mode, $Runtime)" -ForegroundColor Cyan
& $dotnetPath @arguments
if ($LASTEXITCODE -ne 0) { throw "publish failed ($LASTEXITCODE)" }

$package = "LanternUI-$Version-$Runtime-$Mode.zip"
$zipPath = Join-Path $outDir $package
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $appDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item $zipPath).Length

$release = [ordered]@{
    version   = $Version
    beta      = $Beta
    notes     = $Notes
    package   = $package
    sha256    = $hash
    size      = $size
    published = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
}
$release | ConvertTo-Json | Set-Content -Path (Join-Path $outDir 'release.json') -Encoding UTF8

# Note: publish also refreshes bin/obj with the version it stamped, so the development build now
# reports this version too. Run a plain build (or nothing special) to get 1.0.0 back.
$files = (Get-ChildItem $appDir -Recurse -File).Count
Remove-Item $appDir -Recurse -Force

Write-Host ''
Write-Host "package: $zipPath" -ForegroundColor Green
Write-Host "mode:    $Mode ($files files inside, before compression)"
Write-Host "sha256:  $hash"
Write-Host "size:    $([math]::Round($size / 1MB, 1)) MB"
Write-Host "manifest: $(Join-Path $outDir 'release.json')"