<#
    install-captura.ps1 - instala o Captura Studio por utilizador.

    Espera um bundle com:
      bundle\app\CapturaStudio.exe (e restantes ficheiros)
      bundle\captura-rec.exe
      bundle\ffmpeg\ffmpeg.exe (opcional mas recomendado)

    Instala em %LOCALAPPDATA%\CapturaStudio, preserva settings.json,
    cria atalhos e regista a entrada de desinstalacao em HKCU.

    Exemplo:
      powershell -NoProfile -ExecutionPolicy Bypass -File .\install-captura.ps1
      ... -Source .\bundle -Version 1.1.0
#>
[CmdletBinding()]
param(
    [string]$Source = '',
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'CapturaStudio'),
    [string]$Version = '2.0.0-beta.1',
    [switch]$NoShortcuts,
    [switch]$NoRegistry
)

$ErrorActionPreference = 'Stop'
function Fail([string]$m) { Write-Host "ERRO: $m" -ForegroundColor Red; exit 1 }

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Source) {
    $Source = if (Test-Path (Join-Path $here 'app')) { $here } else { Join-Path $here 'bundle' }
}

$appSrc = Join-Path $Source 'app'
$engineSrc = Join-Path $Source 'captura-rec.exe'
if (-not (Test-Path (Join-Path $appSrc 'CapturaStudio.exe'))) { Fail "app\CapturaStudio.exe nao encontrado em $Source" }
if (-not (Test-Path $engineSrc)) { Fail "captura-rec.exe nao encontrado em $Source" }

Write-Host "A instalar Captura Studio $Version em $InstallDir ..." -ForegroundColor Cyan

Get-Process CapturaStudio,captura-rec -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

New-Item -ItemType Directory -Force $InstallDir | Out-Null
$dstApp = Join-Path $InstallDir 'app'
$rc = robocopy $appSrc $dstApp /E /NFL /NDL /NJH /NJS /NP
if ($LASTEXITCODE -gt 7) { Fail "robocopy app falhou ($LASTEXITCODE)" }

Copy-Item $engineSrc (Join-Path $InstallDir 'captura-rec.exe') -Force

$ffSrc = Join-Path $Source 'ffmpeg'
if (Test-Path (Join-Path $ffSrc 'ffmpeg.exe')) {
    $dstFf = Join-Path $InstallDir 'ffmpeg'
    New-Item -ItemType Directory -Force $dstFf | Out-Null
    Copy-Item (Join-Path $ffSrc 'ffmpeg.exe') $dstFf -Force
    if (Test-Path (Join-Path $ffSrc 'ffprobe.exe')) {
        Copy-Item (Join-Path $ffSrc 'ffprobe.exe') $dstFf -Force
    }
    Write-Host "  FFmpeg incluido." -ForegroundColor Green
} else {
    Write-Host "  AVISO: bundle sem ffmpeg; a app usa o FFmpeg do sistema, se existir." -ForegroundColor Yellow
}

$uninstallSrc = Join-Path $here 'uninstall-captura.ps1'
if (Test-Path $uninstallSrc) {
    Copy-Item $uninstallSrc (Join-Path $InstallDir 'uninstall-captura.ps1') -Force
}

$exe = Join-Path $InstallDir 'app\CapturaStudio.exe'
if (-not $NoShortcuts) {
    $ws = New-Object -ComObject WScript.Shell
    $start = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Captura Studio.lnk'
    $s = $ws.CreateShortcut($start)
    $s.TargetPath = $exe
    $s.WorkingDirectory = (Join-Path $InstallDir 'app')
    $s.IconLocation = "$exe,0"
    $s.Description = 'Captura Studio'
    $s.Save()
    $desk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Captura Studio.lnk'
    $d = $ws.CreateShortcut($desk)
    $d.TargetPath = $exe
    $d.WorkingDirectory = (Join-Path $InstallDir 'app')
    $d.IconLocation = "$exe,0"
    $d.Save()
    Write-Host "  Atalhos criados." -ForegroundColor Green
}

if (-not $NoRegistry) {
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CapturaStudio'
    New-Item -Path $key -Force | Out-Null
    Set-ItemProperty $key 'DisplayName' 'Captura Studio'
    Set-ItemProperty $key 'DisplayVersion' $Version
    Set-ItemProperty $key 'Publisher' 'OliveTree'
    Set-ItemProperty $key 'DisplayIcon' "$exe,0"
    Set-ItemProperty $key 'InstallLocation' $InstallDir
    Set-ItemProperty $key 'NoModify' 1 -Type DWord
    Set-ItemProperty $key 'NoRepair' 1 -Type DWord
    Set-ItemProperty $key 'UninstallString' "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$InstallDir\uninstall-captura.ps1`""
    Write-Host "  Entrada de desinstalacao registada." -ForegroundColor Green
}

@{
    Version = $Version
    InstalledAt = (Get-Date).ToString('s')
    InstallDir = $InstallDir
} | ConvertTo-Json | Set-Content (Join-Path $InstallDir 'install.json') -Encoding UTF8

Write-Host "Instalado. Abre pelo atalho ou por:" -ForegroundColor Green
Write-Host "  $exe"
