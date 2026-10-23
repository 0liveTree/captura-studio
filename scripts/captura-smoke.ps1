<#
    captura-smoke.ps1 - smoke test do motor publicado + FFmpeg.

    Verifica:
      1. motor e FFmpeg presentes; --list e --caps respondem;
      2. raw NV12 sem audio: ficheiro alinhado ao tamanho do frame;
      3. raw com audio -> pipe FFmpeg (libx264) + WAV;
      4. mux do video com o WAV -> AAC; ffprobe confirma os streams.

    Usa cmd.exe para o pipe binario: o Windows PowerShell 5.1 descodifica
    stdout nativo como texto e corrompe o NV12.

    Exemplo:
      powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\captura-smoke.ps1
      ... -Device 1 -Seconds 5 -W 1280 -H 720
#>
[CmdletBinding()]
param(
    [string]$Exe = "$env:LOCALAPPDATA\CapturaStudio\captura-rec.exe",
    [string]$Ffmpeg = "$env:LOCALAPPDATA\CapturaStudio\ffmpeg\ffmpeg.exe",
    [string]$Ffprobe = "$env:LOCALAPPDATA\CapturaStudio\ffmpeg\ffprobe.exe",
    [int]$Device = 1,
    [int]$AudioDevice = 0,
    [int]$W = 640,
    [int]$H = 480,
    [int]$Fps = 30,
    [int]$Seconds = 3,
    [string]$OutDir = "$env:TEMP\captura-smoke"
)

$ErrorActionPreference = 'Stop'
function Fail([string]$m) { Write-Host "FAIL: $m" -ForegroundColor Red; exit 1 }
function Ok([string]$m)   { Write-Host "  OK  $m" -ForegroundColor Green }

foreach ($p in @($Exe, $Ffmpeg, $Ffprobe)) {
    if (-not (Test-Path $p)) { Fail "nao encontrado: $p" }
}
New-Item -ItemType Directory -Force $OutDir | Out-Null
Remove-Item "$OutDir\*" -Force -ErrorAction SilentlyContinue

Write-Host "== dispositivos ==" -ForegroundColor Cyan
$list = & $Exe --list 2>&1 | Out-String
if ($list -notmatch "(?m)^V\s+$Device\s") { Fail "--list nao devolveu o dispositivo de video $Device" }
$caps = & $Exe --caps $Device 2>&1 | Out-String
if (($caps -split "`n" | Where-Object { $_ -match '^M\s' }).Count -eq 0) { Fail "--caps $Device nao devolveu modos" }
Ok "dispositivo $Device e modos lidos"

Write-Host "== raw NV12 sem audio ==" -ForegroundColor Cyan
$raw = Join-Path $OutDir 'raw.nv12'
$err1 = Join-Path $OutDir 'raw.err'
$line = "`"`"$Exe`" --raw-video --out `"$OutDir\noaudio.mp4`" --device $Device --w $W --h $H --fps $Fps --seconds $Seconds --no-audio --no-stdin > `"$raw`" 2> `"$err1`"`""
& cmd.exe /c $line
if ($LASTEXITCODE -ne 0) { Fail "motor raw terminou com $LASTEXITCODE ($err1)" }
$frame = $W * $H * 3 / 2
$size = (Get-Item $raw).Length
if ($size -le 0 -or ($size % $frame) -ne 0) { Fail "NV12 desalinhado: $size bytes, frame $frame" }
$frames = $size / $frame
if ($frames -lt ($Fps * $Seconds / 2)) { Fail "poucos frames: $frames" }
Ok "$frames frames, $([math]::Round($size/1MB,1)) MB, alinhado"

Write-Host "== raw + audio -> FFmpeg ==" -ForegroundColor Cyan
$video = Join-Path $OutDir 'smoke.mp4'
$engErr = Join-Path $OutDir 'encode-engine.err'
$ffErr = Join-Path $OutDir 'encode-ff.err'
$line = "`"`"$Exe`" --raw-video --out `"$video`" --device $Device --w $W --h $H --fps $Fps --seconds $Seconds --audio-device $AudioDevice 2> `"$engErr`" | `"$Ffmpeg`" -y -hide_banner -loglevel error -f rawvideo -pix_fmt nv12 -s ${W}x${H} -r $Fps -i pipe:0 -an -c:v libx264 -preset veryfast -crf 23 `"$video`" 2> `"$ffErr`"`""
& cmd.exe /c $line
if ($LASTEXITCODE -ne 0) { Fail "encode terminou com $LASTEXITCODE ($ffErr)" }
if (-not (Test-Path $video) -or (Get-Item $video).Length -lt 10000) { Fail "video de teste vazio" }
Ok "MP4 gerado ($([math]::Round((Get-Item $video).Length/1MB,1)) MB)"

$wav = "$video.wav"
if (-not (Test-Path $wav)) { Fail "WAV nao encontrado: $wav" }
$wavLen = (Get-Item $wav).Length
if ($wavLen -le 44) { Fail "WAV vazio" }
$b = [System.IO.File]::ReadAllBytes($wav)
$riff = [BitConverter]::ToUInt32($b, 4)
$data = [BitConverter]::ToUInt32($b, 40)
if ($riff -ne ($wavLen - 8)) { Fail "cabecalho WAV: RIFF=$riff esperado $($wavLen-8)" }
if ($data -ne ($wavLen - 44)) { Fail "cabecalho WAV: data=$data esperado $($wavLen-44)" }
Ok "WAV valido ($([math]::Round($wavLen/1MB,1)) MB)"

$probe = & $Ffprobe -v error -show_entries stream=codec_name -of csv=p=0 $video 2>&1 | Out-String
if ($probe -notmatch 'h264') { Fail "ffprobe do video: $probe" }
Ok "video h264"

Write-Host "== mux video + audio ==" -ForegroundColor Cyan
$final = Join-Path $OutDir 'smoke-final.mkv'
& $Ffmpeg -y -hide_banner -loglevel error -i $video -i $wav -map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -b:a 192k $final 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $final)) { Fail "mux falhou" }
$probe = & $Ffprobe -v error -show_entries stream=codec_name -of csv=p=0 $final 2>&1 | Out-String
if ($probe -notmatch 'h264' -or $probe -notmatch 'aac') { Fail "streams do mux: $probe" }
Ok "MKV com h264 + aac"

Write-Host ""
Write-Host "PASS - artefactos em $OutDir" -ForegroundColor Green
exit 0
