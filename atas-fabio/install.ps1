<#
  Windows installer: builds the Fabio Orderflow indicator + strategy against YOUR ATAS install and copies them
  to the folders ATAS loads extensions from (docs.atas.net -> Project setup).

  Usage:
      .\install.cmd                                      finds classic ATAS or ATAS X in the default folders
      .\install.cmd "D:\Apps\ATAS Platform"              ATAS installed somewhere else
      .\install.cmd -Platform x                          force ATAS X (C:\Program Files\ATAS X)
      .\install.cmd -Target net8.0-windows               an older ATAS that still runs on .NET 8

  Needs the .NET 10 SDK (or 8 for an old ATAS): https://dotnet.microsoft.com/download
  Classic ATAS: %APPDATA%\ATAS\Indicators and %APPDATA%\ATAS\Strategies
  ATAS X:       %APPDATA%\ATAS X\Indicators and %APPDATA%\ATAS X\Strategies
  ATAS reloads changed indicator DLLs while running (classic ATAS: press the reload button in the status bar).
  On macOS use install-mac.sh instead.
#>
param(
    [string]$AtasDir = '',
    [ValidateSet('auto', 'classic', 'x')]
    [string]$Platform = 'auto',
    [ValidateSet('auto', 'net10.0-windows', 'net8.0-windows')]
    [string]$Target = 'auto',
    [string]$IndicatorsDir = '',
    [string]$StrategiesDir = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
function Fail([string]$msg) { Write-Host "`n$msg" -ForegroundColor Red; exit 1 }

$pf86 = ${env:ProgramFiles(x86)}; if (-not $pf86) { $pf86 = 'C:\Program Files (x86)' }
$pf = $env:ProgramFiles; if (-not $pf) { $pf = 'C:\Program Files' }
$classicDir = "$pf86\ATAS Platform"
$xDir = "$pf\ATAS X"

# 1. which ATAS
if (-not $AtasDir) {
    if ($Platform -eq 'x') { $AtasDir = $xDir }
    elseif ($Platform -eq 'classic') { $AtasDir = $classicDir }
    elseif (Test-Path (Join-Path $classicDir 'ATAS.Indicators.dll')) { $AtasDir = $classicDir }
    else { $AtasDir = $xDir }
}
if (-not (Test-Path (Join-Path $AtasDir 'ATAS.Indicators.dll'))) {
    Fail "ATAS.Indicators.dll not found in '$AtasDir'.`nRun again with your ATAS folder, e.g.:  .\install.cmd `"D:\Apps\ATAS Platform`""
}
if ($Platform -eq 'auto') { $Platform = if (Test-Path (Join-Path $AtasDir 'OFT.PlatformX.runtimeconfig.json')) { 'x' } else { 'classic' } }

# 2. which .NET the installed ATAS runs on (tfm in OFT.Platform(X).runtimeconfig.json)
if ($Target -eq 'auto') {
    $Target = 'net10.0-windows'
    $cfgs = @(Get-ChildItem -Path $AtasDir -Filter 'OFT.Platform*.runtimeconfig.json' -ErrorAction SilentlyContinue)
    if ($cfgs.Count -eq 0) { $cfgs = @(Get-ChildItem -Path $AtasDir -Filter '*.runtimeconfig.json' -ErrorAction SilentlyContinue) }
    foreach ($f in $cfgs) {
        $txt = Get-Content -Raw -Path $f.FullName
        if ($txt -match '"tfm"\s*:\s*"net(\d+)\.' -or $txt -match '"version"\s*:\s*"(\d+)\.') {
            if ([int]$Matches[1] -lt 10) { $Target = 'net8.0-windows' }
            break
        }
    }
}
$needMajor = [int]($Target -replace '^net(\d+)\..*$', '$1')
Write-Host "ATAS        : $AtasDir ($Platform)"
Write-Host "Target      : $Target"

# 3. .NET SDK
$sdkLines = @()
try { $sdkLines = @(& dotnet --list-sdks 2>$null) } catch { }
$majors = @($sdkLines | ForEach-Object { if ($_ -match '^(\d+)\.') { [int]$Matches[1] } })
if ($majors.Count -eq 0) { Fail ".NET SDK not found. Install the .NET $needMajor SDK: https://dotnet.microsoft.com/download" }
if (($majors | Measure-Object -Maximum).Maximum -lt $needMajor) { Fail "This ATAS needs the .NET $needMajor SDK (found: $($majors -join ', ')). Install it: https://dotnet.microsoft.com/download" }

# 4. build
$out = Join-Path $root 'out'
foreach ($part in 'Indicator', 'Strategy') {
    $proj = Join-Path $root "src/Atas/$part/FabioOrderflow.$part.csproj"
    Write-Host "`nBuilding $part ..."
    & dotnet build $proj -c Release "-p:ATAS_BASE=$AtasDir" "-p:AtasTarget=$Target" -o (Join-Path $out $part) --nologo -v q
    if ($LASTEXITCODE -ne 0) { Fail "Build of the $part failed. Send the error lines above so the code can be adjusted to your ATAS version." }
}

# 5. install
$appRoot = if ($Platform -eq 'x') { 'ATAS X' } else { 'ATAS' }
$appData = $env:APPDATA; if (-not $appData) { $appData = Join-Path $HOME 'AppData/Roaming' }
if (-not $IndicatorsDir) { $IndicatorsDir = Join-Path $appData "$appRoot/Indicators" }
if (-not $StrategiesDir) { $StrategiesDir = Join-Path $appData "$appRoot/Strategies" }
try {
    New-Item -ItemType Directory -Force -Path $IndicatorsDir, $StrategiesDir | Out-Null
    Copy-Item -Force (Join-Path $out 'Indicator/FabioOrderflow.Indicator.dll') $IndicatorsDir
    Copy-Item -Force (Join-Path $out 'Strategy/FabioOrderflow.Strategy.dll') $StrategiesDir
} catch {
    Fail "Could not copy the DLLs ($($_.Exception.Message)). Close ATAS (or the chart using the strategy) and run the installer again."
}

Write-Host "`nInstalled:" -ForegroundColor Green
Write-Host "  indicator -> $IndicatorsDir"
Write-Host "  strategy  -> $StrategiesDir"
Write-Host "`nNext: open a 5-minute ES or NQ chart with at least 20 days loaded (60+ for the 3-month value area),"
Write-Host "then Indicators -> 'Fabio Orderflow Monitor'. The strategy 'Fabio Orderflow Strategy' starts with trading OFF."
if ($Platform -eq 'classic') { Write-Host "If ATAS was running: press the reload button in the status bar (indicators) / the blinking button in the strategies window." }
exit 0
