<#
  Builds the Fabio Orderflow indicator + strategy against YOUR ATAS install and copies them where ATAS loads them.

  Usage (close ATAS first):
      .\install.cmd                                   ATAS in the default folder
      .\install.cmd "D:\Apps\ATAS Platform"           ATAS somewhere else
      .\install.cmd -Target net8.0-windows            force .NET 8 (ATAS 7.x); net10.0-windows = ATAS 8.x

  Needs the .NET SDK (8 for ATAS 7.x, 10 for ATAS 8.x): https://dotnet.microsoft.com/download
  Indicators go to  Documents\ATAS\Indicators   and strategies to  %APPDATA%\ATAS\Strategies
  (folders used by public ATAS indicator/strategy projects; change -IndicatorsDir / -StrategiesDir if yours differ).
#>
param(
    [string]$AtasDir = $(if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} 'ATAS Platform' } else { 'C:\Program Files (x86)\ATAS Platform' }),
    [ValidateSet('auto', 'net10.0-windows', 'net8.0-windows')]
    [string]$Target = 'auto',
    [string]$IndicatorsDir = '',
    [string]$StrategiesDir = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Fail([string]$msg) { Write-Host "`n$msg" -ForegroundColor Red; exit 1 }

if (-not $IndicatorsDir) {
    $docs = [Environment]::GetFolderPath('MyDocuments')
    if (-not $docs) { $docs = Join-Path $HOME 'Documents' }
    $IndicatorsDir = Join-Path $docs 'ATAS/Indicators'
}
if (-not $StrategiesDir -and $env:APPDATA) { $StrategiesDir = Join-Path $env:APPDATA 'ATAS/Strategies' }

# 1. ATAS install
if (-not (Test-Path (Join-Path $AtasDir 'ATAS.Indicators.dll'))) {
    Fail "ATAS.Indicators.dll not found in '$AtasDir'.`nRun again with your ATAS folder, e.g.:  .\install.cmd `"D:\Apps\ATAS Platform`""
}

# 2. which .NET the installed ATAS runs on
if ($Target -eq 'auto') {
    $Target = 'net8.0-windows'
    foreach ($f in Get-ChildItem -Path $AtasDir -Filter '*.runtimeconfig.json' -ErrorAction SilentlyContinue) {
        if ((Get-Content -Raw -Path $f.FullName) -match '"version"\s*:\s*"(1\d)\.') { $Target = "net$($Matches[1]).0-windows"; break }
    }
    if ($Target -notin @('net8.0-windows', 'net10.0-windows')) { $Target = 'net10.0-windows' }
}
$needMajor = [int]($Target -replace '^net(\d+)\..*$', '$1')
Write-Host "ATAS folder : $AtasDir"
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
try {
    New-Item -ItemType Directory -Force -Path $IndicatorsDir | Out-Null
    Copy-Item -Force (Join-Path $out 'Indicator/FabioOrderflow.Indicator.dll') $IndicatorsDir
    if ($StrategiesDir) {
        New-Item -ItemType Directory -Force -Path $StrategiesDir | Out-Null
        Copy-Item -Force (Join-Path $out 'Strategy/FabioOrderflow.Strategy.dll') $StrategiesDir
    }
} catch {
    Fail "Could not copy the DLLs ($($_.Exception.Message)). Close ATAS and run the installer again."
}

Write-Host "`nInstalled:" -ForegroundColor Green
Write-Host "  indicator -> $IndicatorsDir"
if ($StrategiesDir) { Write-Host "  strategy  -> $StrategiesDir" }
Write-Host "`nNext: start ATAS, open a 5-minute ES or NQ chart with at least 20 days loaded (60+ for the 3-month value area),"
Write-Host "then Indicators -> 'Fabio Orderflow Monitor'. The strategy ('Fabio Orderflow Strategy') starts with trading OFF."
exit 0
