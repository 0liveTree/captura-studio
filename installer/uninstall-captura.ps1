<#
    uninstall-captura.ps1 - remove o Captura Studio instalado por utilizador.

    Por omissao remove tudo (incluindo settings.json). Usa -KeepSettings para
    preservar definicoes e a pasta de destino.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'CapturaStudio'),
    [switch]$KeepSettings
)

$ErrorActionPreference = 'Continue'
Write-Host "A desinstalar Captura Studio de $InstallDir ..." -ForegroundColor Cyan

Get-Process CapturaStudio,captura-rec -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

foreach ($lnk in @(
    (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Captura Studio.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Captura Studio.lnk'))) {
    try { if (Test-Path $lnk) { Remove-Item $lnk -Force } } catch { }
}

try { Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CapturaStudio' -Recurse -Force -ErrorAction SilentlyContinue } catch { }

foreach ($p in @('app','captura-rec.exe','ffmpeg','install.json')) {
    $full = Join-Path $InstallDir $p
    try { if (Test-Path $full) { Remove-Item $full -Recurse -Force } } catch { Write-Host "  aviso: $full" -ForegroundColor Yellow }
}

if ($KeepSettings) {
    if (Test-Path (Join-Path $InstallDir 'settings.json')) {
        Write-Host "Definicoes preservadas em $InstallDir\settings.json" -ForegroundColor Green
    } else {
        Write-Host "Pasta preservada em $InstallDir (sem settings.json)." -ForegroundColor Green
    }
    Write-Host "Podes apagar a pasta manualmente quando quiseres."
} else {
    # O script esta dentro da pasta; apaga-a depois de este processo sair.
    Start-Process cmd.exe -ArgumentList '/c', "timeout /t 2 >nul & rmdir /s /q `"$InstallDir`"" -WindowStyle Hidden
    Write-Host "Removido." -ForegroundColor Green
}
