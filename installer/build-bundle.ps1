<#
    build-bundle.ps1 - cria um bundle instalavel do Captura Studio.

    Junta:
      app\              (build WinUI Release)
      captura-rec.exe   (motor Rust Release)
      ffmpeg\           (ffmpeg.exe + ffprobe.exe)
      install-captura.ps1 / uninstall-captura.ps1 / LEIA-ME.txt

    Depois distribui a pasta installer\bundle (ou o ZIP com -Zip) e o
    utilizador corre install-captura.ps1.

    Exemplo:
      powershell -NoProfile -ExecutionPolicy Bypass -File .\build-bundle.ps1
      ... -SkipFfmpeg
      ... -Zip
#>
[CmdletBinding()]
param(
    [string]$OutDir = '',
    [string]$Version = '',
    [switch]$SkipFfmpeg,
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
function Fail([string]$m) { Write-Host "ERRO: $m" -ForegroundColor Red; exit 1 }

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutDir) { $OutDir = Join-Path $here 'bundle' }
$root = Split-Path -Parent $here

if (-not $Version) {
    $csproj = Join-Path $root 'uwc03-winui\CapturaStudio.csproj'
    if (Test-Path $csproj) {
        $xml = Get-Content $csproj -Raw
        if ($xml -match '<Version>([^<]+)</Version>') { $Version = $Matches[1].Trim() }
    }
    if (-not $Version) { $Version = '1.0.1' }
}
$appSrc = Join-Path $root 'uwc03-winui\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64'
$engineSrc = Join-Path $root 'uvc03-rec\target\release\captura-rec.exe'
$ffSrc = Join-Path $env:LOCALAPPDATA 'CapturaStudio\ffmpeg'

if (-not (Test-Path (Join-Path $appSrc 'CapturaStudio.exe'))) {
    Fail "app Release nao encontrada em $appSrc (compila a app primeiro)"
}
if (-not (Test-Path $engineSrc)) {
    Fail "motor Release nao encontrado em $engineSrc (cargo build --release --bin captura-rec)"
}

Write-Host "A criar bundle em $OutDir ..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force $OutDir | Out-Null

$dstApp = Join-Path $OutDir 'app'
$rc = robocopy $appSrc $dstApp /MIR /NFL /NDL /NJH /NJS /NP
if ($LASTEXITCODE -gt 7) { Fail "robocopy app falhou ($LASTEXITCODE)" }
Copy-Item $engineSrc (Join-Path $OutDir 'captura-rec.exe') -Force

if (-not $SkipFfmpeg) {
    if (-not (Test-Path (Join-Path $ffSrc 'ffmpeg.exe'))) {
        Fail "ffmpeg nao encontrado em $ffSrc (usa -SkipFfmpeg para omitir)"
    }
    $dstFf = Join-Path $OutDir 'ffmpeg'
    New-Item -ItemType Directory -Force $dstFf | Out-Null
    Copy-Item (Join-Path $ffSrc 'ffmpeg.exe') $dstFf -Force
    if (Test-Path (Join-Path $ffSrc 'ffprobe.exe')) {
        Copy-Item (Join-Path $ffSrc 'ffprobe.exe') $dstFf -Force
    }
}

foreach ($n in @('install-captura.ps1','uninstall-captura.ps1')) {
    Copy-Item (Join-Path $here $n) (Join-Path $OutDir $n) -Force
}
foreach ($n in @('THIRD-PARTY-NOTICES.md','LICENSE-FFMPEG-GPLv3.txt')) {
    $p = Join-Path $root $n
    if (Test-Path $p) { Copy-Item $p (Join-Path $OutDir $n) -Force }
}

$readme = @"
Captura Studio $Version - instalacao por utilizador

1. Clica com o botao direito em install-captura.ps1 > Executar com PowerShell
   ou, numa consola:
   powershell -NoProfile -ExecutionPolicy Bypass -File .\install-captura.ps1

2. Instala em %LOCALAPPDATA%\CapturaStudio e cria atalhos.
3. Para desinstalar:
   powershell -NoProfile -ExecutionPolicy Bypass -File "%LOCALAPPDATA%\CapturaStudio\uninstall-captura.ps1"

Requisitos: Windows 11, placa UVC. O FFmpeg vai incluido neste bundle (GPLv3); ve
THIRD-PARTY-NOTICES.md e LICENSE-FFMPEG-GPLv3.txt.
"@
$readme | Set-Content (Join-Path $OutDir 'LEIA-ME.txt') -Encoding UTF8
"$Version`r`n$(Get-Date -Format s)" | Set-Content (Join-Path $OutDir 'VERSION.txt') -Encoding UTF8

if ($Zip) {
    $zipPath = Join-Path $here "CapturaStudio-$Version.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $OutDir '*') -DestinationPath $zipPath -Force
    Write-Host "ZIP: $zipPath" -ForegroundColor Green
}

Write-Host "Bundle pronto: $OutDir" -ForegroundColor Green
Write-Host "Testa com: powershell -NoProfile -ExecutionPolicy Bypass -File `"$OutDir\install-captura.ps1`" -NoShortcuts -NoRegistry -InstallDir `"$env:TEMP\CapturaStudioTest`""
