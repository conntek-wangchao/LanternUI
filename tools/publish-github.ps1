# Publishes one build to a GitHub repository and points the update channel at it.
#
#   $env:GH_TOKEN = '<classic PAT with repo scope>'
#   tools\publish-github.ps1 -Owner conntek-wangchao -Repo LanternUI -Version 1.0.1 -CreateRepo -PushSource
#
# Order: who the token is, the repository, the source, the package, the release and its assets,
# then channel\release.json with the absolute asset URL.
param(
    [Parameter(Mandatory = $true)][string]$Owner,
    [Parameter(Mandatory = $true)][string]$Repo,
    [string]$Version = "1.0.0",
    [string]$Token = $env:GH_TOKEN,
    [string]$Proxy = "http://127.0.0.1:7897",
    # Uploads go to uploads.github.com, which on some networks only answers without the proxy.
    [string]$UploadProxy = "",
    [string]$Branch = "main",
    [string]$Visibility = "public",
    [string]$Notes = "",
    [switch]$CreateRepo,
    [switch]$PushSource
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$curl = 'C:\Windows\system32\curl.exe'
$api = 'https://api.github.com'

if (-not $Token) { throw 'no token: set $env:GH_TOKEN or pass -Token (classic PAT with repo scope)' }
if (-not (Test-Path $curl)) { throw "curl.exe not found at $curl" }

function Invoke-GitHub([string]$method, [string]$path, $body = $null) {
    $lines = @('-s', '-S', '--max-time', '60', '-x', $Proxy,
        '-H', "Authorization: Bearer $Token",
        '-H', 'User-Agent: lantern-publish',
        '-H', 'Accept: application/vnd.github+json',
        '-X', $method)
    $file = $null
    if ($null -ne $body) {
        # A JSON string passed on the command line loses its quotes in PowerShell; a file does not.
        $file = Join-Path ([IO.Path]::GetTempPath()) ('lantern-body-' + [guid]::NewGuid().ToString('N') + '.json')
        [IO.File]::WriteAllText($file, ($body | ConvertTo-Json -Compress), (New-Object System.Text.UTF8Encoding($false)))
        $lines += @('-H', 'Content-Type: application/json')
        $lines += @('--data-binary', "@$file")
    }
    $lines += ($api + $path)
    $raw = & $curl @lines
    if ($file -and (Test-Path $file)) { Remove-Item $file -Force -ErrorAction SilentlyContinue }
    if ($LASTEXITCODE -ne 0) { throw "curl failed: $method $path" }
    if (-not $raw) { return $null }

    # GitHub answers a refusal with a message and no object: repeat it instead of failing silently.
    if ($raw -notmatch '"id"\s*:' -and $raw -match '"message"\s*:\s*"([^"]+)"') {
        throw ("GitHub refused " + $method + " " + $path + ": " + $matches[1])
    }

    return $raw | ConvertFrom-Json
}

function Send-Asset([string]$uploadBase, [string]$file, [string]$name) {
    $lines = @('-s', '-S', '--max-time', '900')
    if ($UploadProxy) { $lines += @('-x', $UploadProxy) }
    $lines += @('-H', "Authorization: Bearer $Token",
        '-H', 'User-Agent: lantern-publish',            # keep the header list in one piece
        '-H', 'Content-Type: application/octet-stream',
        '--data-binary', "@$file",
        "$uploadBase?name=$([uri]::EscapeDataString($name))")
    $raw = & $curl @lines
    if ($LASTEXITCODE -ne 0) { return $null }
    return $raw
}

$me = Invoke-GitHub 'GET' '/user'
"token belongs to $($me.login)"
if ($me.login -ne $Owner) { Write-Warning "the token is $($me.login), not ${Owner}: repository creation will fail" }

if ($CreateRepo) {
    $private = ($Visibility -eq 'private')
    $result = Invoke-GitHub 'POST' '/user/repos' @{ name = $Repo; private = $private; auto_init = $false }
    if ($result) { "repository: $($result.full_name)" }
}

$git = 'git'
$proxyArg = @('-c', "http.proxy=$Proxy")
$basic = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("x-access-token:$Token"))
$authArg = @('-c', "http.extraheader=AUTHORIZATION: basic $basic")
$remote = "https://github.com/$Owner/$Repo.git"

Push-Location $root
try {
    if ($PushSource) {
        if (-not (Test-Path (Join-Path $root '.git'))) {
            & $git init -q
            & $git symbolic-ref HEAD "refs/heads/$Branch"
        }
        & $git add -A
        & $git @('-c', 'user.name=publish', '-c', 'user.email=publish@local') commit -q -m "release $Version"
        & $git @proxyArg @authArg push -q $remote "HEAD:refs/heads/$Branch"
        if ($LASTEXITCODE -ne 0) { throw 'push failed' }
        "source pushed to $Owner/$Repo ($Branch)"
    }

    $releaseDir = Join-Path $root "release-$Version"
    & (Join-Path $root 'tools\pack.ps1') -Version $Version -Mode self-contained-single -Out $releaseDir -Notes $Notes | Out-Host
    $manifest = Get-Content (Join-Path $releaseDir 'release.json') -Raw | ConvertFrom-Json
    $zip = Join-Path $releaseDir $manifest.package

    $tag = "v$Version"
    $release = Invoke-GitHub 'POST' "/repos/$Owner/$Repo/releases" @{
        tag_name = $tag
        name     = $Version
        body     = $(if ($Notes) { $Notes } else { "LanternUI $Version" })
    }
    $uploadBase = "https://uploads.github.com/repos/$Owner/$Repo/releases/$($release.id)/assets"

    $assetJson = Send-Asset $uploadBase $zip $manifest.package
    $asset = $assetJson | ConvertFrom-Json
    $packageUrl = $asset.browser_download_url
    if (-not $packageUrl) {
        # Re-running a publish must not fail because the asset is already on the release.
        $existing = (Invoke-GitHub 'GET' "/repos/$Owner/$Repo/releases/$($release.id)/assets" |
            Where-Object { $_.name -eq $manifest.package } | Select-Object -First 1)
        $packageUrl = if ($existing) { $existing.browser_download_url } else { $null }
    }
    if (-not $packageUrl) { throw "asset upload failed: $assetJson" }
    "asset: $packageUrl"

    Send-Asset $uploadBase (Join-Path $releaseDir 'release.json') 'release.json' | Out-Null

    $channelDir = Join-Path $root 'channel'
    New-Item -ItemType Directory -Path $channelDir -Force | Out-Null
    [ordered]@{
        version   = $manifest.version
        beta      = $manifest.beta
        notes     = $manifest.notes
        package   = $packageUrl
        sha256    = $manifest.sha256
        size      = $manifest.size
        published = $manifest.published
    } | ConvertTo-Json | Set-Content (Join-Path $channelDir 'release.json') -Encoding UTF8

    & $git add 'channel/release.json'
    & $git @('-c', 'user.name=publish', '-c', 'user.email=publish@local') commit -q -m "channel: $Version"
    & $git @proxyArg @authArg push -q $remote "HEAD:refs/heads/$Branch"
    if ($LASTEXITCODE -ne 0) { throw 'could not push channel\release.json' }

    $channelUrl = "https://raw.githubusercontent.com/$Owner/$Repo/$Branch/channel/release.json"
    ''
    "release : https://github.com/$Owner/$Repo/releases/tag/$tag"
    "channel : $channelUrl"
    ''
    'point the application at it:'
    "  LanternUI.Demo.exe --update-source $channelUrl --check-updates"
}
finally {
    Pop-Location
}