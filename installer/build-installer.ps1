<#
    build-installer.ps1 - gera CapturaStudio-<versao>-Setup.exe com Inno Setup 6.

    O payload e o bundle (app + motor + FFmpeg + avisos). Se installer\bundle nao
    existir, extrai CapturaStudio-<versao>.zip; se o ZIP tambem nao existir, corre
    build-bundle.ps1 (que precisa da app e do motor compilados).

    Requer Inno Setup 6:
      winget install --id JRSoftware.InnoSetup -e

    Exemplo:
      powershell -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1
      ... -Version 1.0.2
#>
[CmdletBinding()]
param(
    [string]$Version = '',
    [string]$Repo = '0liveTree/captura-studio',
    [string]$Notes = '',
    [string]$Iscc = ''
)

$ErrorActionPreference = 'Stop'
function Fail([string]$m) { Write-Host "ERRO: $m" -ForegroundColor Red; exit 1 }

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$bundle = Join-Path $here 'bundle'
$outDir = Join-Path $here 'out'

if (-not $Version) {
    $csproj = Join-Path (Split-Path -Parent $here) 'uwc03-winui\CapturaStudio.csproj'
    if (Test-Path $csproj) {
        $xml = Get-Content $csproj -Raw
        if ($xml -match '<Version>([^<]+)</Version>') { $Version = $Matches[1].Trim() }
    }
    if (-not $Version) { $Version = '1.0.2' }
}
$versionInfo = "$Version.0"
$zip = Join-Path $here "CapturaStudio-$Version.zip"

$bundleApp = Join-Path $bundle 'app\CapturaStudio.exe'
$bundleVersion = if (Test-Path (Join-Path $bundle 'VERSION.txt')) {
    (Get-Content (Join-Path $bundle 'VERSION.txt') -First 1).Trim()
} else { '' }
if ((Test-Path $bundleApp) -and $bundleVersion -ne $Version) {
    Write-Host "Bundle $bundleVersion nao corresponde a $Version; a remover ..." -ForegroundColor Yellow
    Remove-Item $bundle -Recurse -Force
}

if (-not (Test-Path $bundleApp)) {
    if (Test-Path $zip) {
        Write-Host "A extrair $zip para bundle ..." -ForegroundColor Cyan
        New-Item -ItemType Directory -Force $bundle | Out-Null
        if (Get-Command tar -ErrorAction SilentlyContinue) {
            & tar -xf $zip -C $bundle
        } else {
            Expand-Archive -Path $zip -DestinationPath $bundle -Force
        }
    } else {
        $builder = Join-Path $here 'build-bundle.ps1'
        if (-not (Test-Path $builder)) { Fail "bundle em falta e build-bundle.ps1 nao encontrado" }
        Write-Host "Bundle em falta; a correr build-bundle.ps1 -Version $Version ..." -ForegroundColor Cyan
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $builder -Version $Version
    }
}

foreach ($p in @('app\CapturaStudio.exe','captura-rec.exe','ffmpeg\ffmpeg.exe')) {
    if (-not (Test-Path (Join-Path $bundle $p))) { Fail "payload incompleto: $p" }
}

if (-not $Iscc) {
    $pf = [Environment]::GetFolderPath('ProgramFiles')
    $pf86 = [Environment]::GetFolderPath('ProgramFilesX86')
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path $pf 'Inno Setup 6\ISCC.exe'),
        (Join-Path $pf86 'Inno Setup 6\ISCC.exe')
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { $Iscc = $c; break }
    }
}
if (-not $Iscc -or -not (Test-Path $Iscc)) {
    Fail "Inno Setup 6 nao encontrado. Instala com: winget install --id JRSoftware.InnoSetup -e"
}

New-Item -ItemType Directory -Force $outDir | Out-Null
$iss = Join-Path $here 'CapturaStudio.iss'
Write-Host "A compilar o instalador $Version ..." -ForegroundColor Cyan
& $Iscc "/DMyAppVersion=$Version" "/DMyAppVersionInfo=$versionInfo" $iss
if ($LASTEXITCODE -ne 0) { Fail "ISCC falhou ($LASTEXITCODE)" }

$setup = Join-Path $outDir "CapturaStudio-$Version-Setup.exe"
if (-not (Test-Path $setup)) { Fail "setup nao encontrado: $setup" }
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
$mb = [math]::Round((Get-Item $setup).Length / 1MB, 1)

if (-not $Notes) { $Notes = "Captura Studio $Version" }
$tag = "v$Version"
$url = "https://github.com/$Repo/releases/download/$tag/CapturaStudio-$Version-Setup.exe"
$manifestPath = Join-Path $outDir 'latest.json'
[ordered]@{
    version = $Version
    url     = $url
    sha256  = $hash
    notes   = $Notes
} | ConvertTo-Json | Set-Content $manifestPath -Encoding UTF8

Write-Host ""
Write-Host "Setup:   $setup" -ForegroundColor Green
Write-Host "Tamanho: $mb MB"
Write-Host "SHA256:  $hash"
Write-Host "Manifest: $manifestPath"
Write-Host "URL:      $url"
