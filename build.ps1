<#
.SYNOPSIS
  Build, verify and publish HammerOS into dist/HammerOS.

.DESCRIPTION
  Runs every verification layer in order — headless Avalonia checks, real
  PowerShell/CMD sessions, native window embedding, the WebView2 browser and
  media engine, and the rendered previews — then publishes the self-contained
  single-file host and proves the published binary runs. A release never skips
  them.

  TiledChecks and WebChecks open real windows on this desktop for a few seconds
  and TiledChecks starts its own throwaway fixture applications. They never
  touch windows they did not create.

.EXAMPLE
  .\build.ps1
  .\build.ps1 -SkipTests      # local iteration only; never for a release
#>
param(
    # Windows only: ConPTY, native window embedding and WebView2 have no
    # equivalent on the other runtimes, so there is nothing to cross-publish.
    [ValidateSet('win-x64')]
    [string] $Rid = 'win-x64',
    [string] $Config = 'Release',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$distApp = Join-Path $PSScriptRoot 'dist/HammerOS'
$preview = Join-Path $PSScriptRoot 'dist/preview'
$exeName = 'HammerOS.exe'

function Write-Step([string] $text) { Write-Host "`n$text" -ForegroundColor Cyan }

function Invoke-Checked([string] $what, [scriptblock] $action) {
    & $action
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" }
}

Write-Step '[1/6] Verifying versions agree...'
. (Join-Path $PSScriptRoot 'version.ps1')
$versions = Show-Report $PSScriptRoot $null
if (-not $versions.AllOk) { throw 'Versions disagree; run version.ps1 before building.' }
$version = $versions.Target

Write-Step "[2/6] Building ($Config)..."
Invoke-Checked 'dotnet build' { dotnet build HammerOS.csproj -c $Config --nologo }

if ($SkipTests) {
    Write-Host "`n[3/6] Skipping checks and previews (-SkipTests)" -ForegroundColor Yellow
} else {
    Write-Step '[3/6] Running checks and rendering previews...'
    Invoke-Checked 'tools/Checks'      { dotnet run --project tools/Checks -c $Config }
    Invoke-Checked 'tools/HostChecks'  { dotnet run --project tools/HostChecks -c $Config }
    Invoke-Checked 'tools/TiledChecks' { dotnet run --project tools/TiledChecks -c $Config }
    Invoke-Checked 'tools/WebChecks'   { dotnet run --project tools/WebChecks -c $Config }
    Invoke-Checked 'tools/Preview'     { dotnet run --project tools/Preview -c $Config -- $preview }
    Invoke-Checked 'tools/Preview --host'   { dotnet run --project tools/Preview -c $Config -- "$preview-host" --host }
    Invoke-Checked 'tools/Preview --chrome' { dotnet run --project tools/Preview -c $Config -- (Join-Path $PSScriptRoot 'dist/chrome') --chrome }
}

Write-Step '[4/6] Closing any running copy of this build...'
# Publishing over a running exe fails on Windows. Scope this to our own output;
# an installed copy elsewhere is left alone.
Get-Process -Name HammerOS -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($distApp, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object {
        Write-Host "  stopping pid $($_.Id)" -ForegroundColor DarkGray
        Stop-Process -Id $_.Id -Force
    }
Start-Sleep -Milliseconds 400

Write-Step "[5/6] Publishing self-contained $Rid..."
if (Test-Path -LiteralPath $distApp) { Remove-Item -LiteralPath $distApp -Recurse -Force }
Invoke-Checked 'dotnet publish' {
    dotnet publish HammerOS.csproj -c $Config -r $Rid --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        --nologo -o $distApp
}

# dist/HammerOS is the complete install image: forge.toml ships it with a single
# [[dirs]] entry, so anything the install needs has to be in here. The licences
# deliberately travel with the payload rather than as [[files]] entries — Forge
# stages the license step's `file` as bundle-only first and then dedups
# [[files]] by source path, which silently drops a root LICENSE.txt.
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE.txt') -Destination (Join-Path $distApp 'LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'vendor/README.md') -Destination (Join-Path $distApp 'THIRD-PARTY.md') -Force

Write-Step '[6/6] Proving the published binary actually runs...'
$exe = Join-Path $distApp $exeName
if (-not (Test-Path -LiteralPath $exe)) { throw "Publish produced no executable at $exe" }

# --selftest starts the Avalonia platform and seeds the virtual workspace without
# opening a window. Both resolve differently inside a single-file bundle than
# under dotnet run, which is the whole point of running it against the artifact.
$log = Join-Path $env:TEMP 'hammeros-build-selftest.txt'
$proc = Start-Process -FilePath $exe -ArgumentList '--selftest' -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput $log
$output = (Get-Content -LiteralPath $log -Raw).Trim()
if ($proc.ExitCode -ne 0) { throw "Self-test failed (exit $($proc.ExitCode)): $output" }

$versionLog = Join-Path $env:TEMP 'hammeros-build-version.txt'
$proc = Start-Process -FilePath $exe -ArgumentList '--version' -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput $versionLog
$reported = (Get-Content -LiteralPath $versionLog -Raw).Trim()
if ($proc.ExitCode -ne 0) { throw "--version failed (exit $($proc.ExitCode))" }
if ($reported -notmatch [regex]::Escape($version)) {
    throw "Published binary reports '$reported' but the release version is $version."
}

$item = Get-Item -LiteralPath $exe
Write-Host ''
Write-Host "Done. Output: $($item.FullName)" -ForegroundColor Green
Write-Host ("  size:      {0} MB" -f [math]::Round($item.Length / 1MB, 1))
Write-Host "  version:   $version"
Write-Host "  self-test: $output"
Write-Host "  SHA256:    $((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash)"
