# Captura Studio

Gravador de captura de vídeo nativo para Windows — **código aberto** (MIT), com motor em **Rust**
e interface em **WinUI 3**.

Feito para placas de captura USB (UVC), com foco em placas que só expõem **NV12** no Windows:
o motor usa **Media Foundation** para abrir o dispositivo e o **FFmpeg** para codificar, sem
passar por MJPEG nem por um segundo leitor.

## Funcionalidades

- Abre qualquer placa UVC / webcam via Media Foundation (`IMFSourceReader`)
- Formatos de entrada: **Auto, NV12, MJPEG, YUY2, RGB32**
- Resoluções e FPS **lidos da própria placa** (`--caps`)
- Codificação **H.264** (baseline/main/high) ou **H.265/HEVC**
- **GPU (hardware)** ou CPU — NVENC/QSV/AMF quando disponíveis, com fallback software
- **Pipeline FFmpeg**: o motor entrega NV12 em bruto e o FFmpeg codifica com
  `h264_nvenc`/`hevc_nvenc` (GPU) ou `libx264`/`libx265` (CPU), com mux de áudio no fim
- Painel do codificador: **predefinição p1–p7**, **afinação**, **B-frames**, **multipass**,
  **controlo da frequência** (CBR, CQP, VBR, VBR com qualidade alvo, sem perdas),
  **GOP em segundos (0 = automático)** e **CQ**
- Rate control, bitrate, GOP/keyframe configuráveis também no caminho Media Foundation
- Perfis de cor: **Rec.709 / Rec.2020** e intervalo **limitado/completo**
- Áudio **AAC**, **PCM** (16/24-bit), **MP3**, **FLAC** ou **Opus** (os últimos via FFmpeg)
- Contentores: **MP4, MKV, TS**
- **Divisão automática** em segmentos (5/10/30/60 min)
- Pré-visualização em tempo real (vídeo + áudio) com **volume**, **ecrã inteiro** e controlos
  auto-ocultáveis; o preview é servido pelo próprio processo que tem a placa
- Notificações in-app (gravação guardada, erro, sem sinal), **perfis/presets** com nome,
  single-instance e template de nome (`{data}`, `{hora}`, `{dispositivo}`, `{modo}`)
- Atalhos locais **F9/F10** e globais **Ctrl+Alt+F9/F10**
- Medidor de nível de áudio, definições persistentes, tema claro/escuro, Mica/Acrílico e
  cor de destaque

## Requisitos

- **Windows 11** (x64)
- Uma placa de captura UVC / webcam
- [Rust](https://rustup.rs) stable (toolchain MSVC) para o motor
- **.NET 8 SDK** e **Visual Studio 2022 Build Tools** com as ferramentas de WinUI para a app
- **Windows App SDK 1.7** (vem por NuGet; a app é self-contained)
- **FFmpeg** com `h264_nvenc`/`hevc_nvenc` ou `libx264`/`libx265` — opcional para o caminho
  Media Foundation, necessário para a pipeline FFmpeg. O instalador inclui um build GPLv3
  (ver [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)).

## Build

Motor:

```powershell
cd uvc03-rec
cargo build --release --bin captura-rec
```

Interface:

```powershell
cd uwc03-winui
msbuild CapturaStudio.csproj /restore /p:Configuration=Release /p:Platform=x64 /p:NuGetAudit=false
```

O binário da app fica em `uwc03-winui\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\`.
O motor fica em `uvc03-rec\target\release\captura-rec.exe`.

## Instalação

### A partir da release

Descarrega o ZIP do instalador na [página de Releases](../../releases/latest), extrai e corre:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install-captura.ps1
```

Instala em `%LOCALAPPDATA%\CapturaStudio`, preserva `settings.json` e `profiles.json`, cria
atalhos e regista a desinstalação em HKCU. O ZIP inclui a app, o motor, o FFmpeg e os avisos
de terceiros.

### A partir do código

Depois de compilar o motor e a app:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\build-bundle.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\bundle\install-captura.ps1
```

`installer\bundle\` é gerado localmente e **não** está no repositório. O script
`installer\build-bundle.ps1` copia a app, o motor, o FFmpeg e os avisos de terceiros.

## Utilização

1. Escolhe o dispositivo de vídeo, a resolução, os FPS e o formato.
2. Define a pasta, o contentor e a codificação de áudio.
3. **GRAVAR** / **PARAR** (ou **F9/F10** com a janela focada; **Ctrl+Alt+F9/F10** em qualquer
   aplicação).
4. O botão **PERFIL** guarda/aplica presets; **Guardar…** cria um perfil com o estado atual,
   **Eliminar** remove o selecionado. Os perfis ficam em `%LOCALAPPDATA%\CapturaStudio\profiles.json`.
5. O painel **Definições → Codificação** expõe preset, tune, B-frames, multipass, rate control,
   GOP e CQ.
6. O medidor **NÍVEL** no cartão ÁUDIO usa o `LVL` emitido pelo motor.

A app usa o FFmpeg quando o encontra; `CAPTURA_PIPELINE=mf` força o caminho Media Foundation.
Em `--raw-video`, o stdout é só NV12; o áudio PCM vai para `<out>.wav` e o estado para stderr.

## Estrutura

| Pasta | Conteúdo |
|---|---|
| `uvc03-rec/` | Motor em Rust (captura, conversão, codificação, preview) |
| `uwc03-winui/` | Aplicação WinUI 3 |
| `installer/` | Instalação/desinstalação por utilizador e criação do bundle |
| `scripts/` | Smoke test do motor + FFmpeg |

## Testes

```powershell
cd uvc03-rec
cargo test --release --lib
```

O smoke test precisa da placa e do FFmpeg publicados:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\captura-smoke.ps1
```

## Linha de comandos do motor

```text
captura-rec.exe [--out ficheiro] [--container mp4|mkv|ts]
                [--w 1920 --h 1080 --fps 30] [--bitrate 12000000]
                [--codec h264|hevc] [--profile baseline|main|high]
                [--sw] [--no-audio] [--audio-kbps 192] [--pcm]
                [--color-space 709|2020] [--color-range limited|full]
                [--format auto|nv12|mjpeg|yuy2|rgb32]
                [--seconds N] [--split-min M]
                [--list] [--caps N] [--encoders]
                [--preview --port 45900]
                [--raw-video --out ficheiro.mp4 [--preview-port 45901] [--seconds N]]
```

## Notas sobre a placa

- Algumas UVC03 entregam ~49–50 fps a 1080p e ~25 fps a 4K apesar do alvo configurado; a saída
  é CFR e os frames repetidos/descartados aparecem no estado.
- A primeira imagem 4K pode demorar ~18 s; a app mostra a espera e a duração começa no primeiro
  frame válido.
- Dois leitores Media Foundation em simultâneo disputam a placa; o preview é servido pelo
  próprio processo do motor.
- Se o sinal HDMI cair, o caminho raw tenta reabrir o leitor (watchdog) e a UI mostra o estado;
  o caminho Media Foundation não tem esse watchdog.

## Modelos e agradecimentos

O projeto foi construído com direção humana, apoio de modelos de IA e revisão. O âmbito de cada
contribuição está em [CONTRIBUTORS.md](CONTRIBUTORS.md) e na secção **Sobre → Contribuições** da app:

| Empresa | Agente | Modelo | Contribuição |
|---|---|---|---|
| DeepSeek | DeepSeek Harness | DeepSeek-V4.1-Flash | Motor Rust/Media Foundation, pipeline raw + FFmpeg, WAV/sync, preview, painel do codificador, persistência e funcionalidades de produto |
| Anthropic | Antigravity (Google) | Claude Sonnet 4.6 (Thinking) | Revisão do handoff e diagnóstico do arranque WinUI; sem código |
| OpenAI | Codex | GPT-6 | Interface e resiliência UVC03 no caminho raw (fila limitada, CFR, timeouts, reaberturas) |

## Licença

MIT — © 2026 OliveTree. Feito em Portugal.

O instalador inclui um build **GPLv3** do FFmpeg; o texto da licença e as fontes estão em
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) e
[LICENSE-FFMPEG-GPLv3.txt](LICENSE-FFMPEG-GPLv3.txt).
