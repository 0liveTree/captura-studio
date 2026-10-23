# Third-party notices / Avisos de terceiros

O Captura Studio é distribuído sob a licença MIT (ver [LICENSE](LICENSE)). O instalador inclui
componentes de terceiros com licenças próprias. O repositório **não** contém binários do FFmpeg;
o ZIP do instalador publicado na release contém `ffmpeg.exe` e `ffprobe.exe`.

## FFmpeg 9.0.2 — build `essentials_build-www.gyan.dev`

- Copyright (c) 2000–2026 os autores do FFmpeg.
- Este build foi configurado com `--enable-gpl --enable-version3` e é distribuído sob a
  **GNU General Public License v3** (GPLv3). O texto completo está em
  [LICENSE-FFMPEG-GPLv3.txt](LICENSE-FFMPEG-GPLv3.txt).
- Configuração relevante: `--enable-gpl --enable-version3 --enable-libx264 --enable-libx265
  --enable-nvenc --enable-libopus --enable-libmp3lame ...` (a lista completa aparece em
  `ffmpeg -version`).
- Fontes e informação do build:
  - https://ffmpeg.org/
  - https://ffmpeg.org/legal.html
  - https://www.gyan.dev/ffmpeg/builds/
  - https://ffmpeg.org/download.html
- O código-fonte do FFmpeg e as instruções de build estão disponíveis nos links acima. O ZIP do
  instalador inclui este aviso e o texto da GPLv3; se precisares do código-fonte exato
  correspondente ao binário, usa a versão 9.0.2 e a configuração indicada.
- O Captura Studio não altera o FFmpeg; chama os executáveis por linha de comandos.

## NAudio 2.2.1

- Biblioteca de áudio usada pela app para o preview.
- Licença: **Microsoft Public License (MS-PL)**.
- https://github.com/naudio/NAudio
- https://github.com/naudio/NAudio/blob/master/license.txt

## Microsoft Windows App SDK / .NET / Windows SDK BuildTools

- **Windows App SDK 1.7** e **.NET 8**: licença MIT da Microsoft.
- **Microsoft.Windows.SDK.BuildTools**: licença da Microsoft.
- https://github.com/microsoft/WindowsAppSDK
- https://dotnet.microsoft.com/
- https://learn.microsoft.com/windows/apps/windows-app-sdk/

## Rust e crates

O motor usa o crate `windows` e, opcionalmente, `eframe`; as restantes dependências estão em
`uvc03-rec/Cargo.lock`. As licenças são maioritariamente MIT/Apache-2.0. Para gerar a lista
completa:

```powershell
cargo install cargo-license
cargo license --manifest-path uvc03-rec/Cargo.toml
```

## Nota

Este ficheiro é um aviso de atribuição, não aconselhamento jurídico. Antes de distribuir o
instalador com FFmpeg, confirma o cumprimento da GPLv3 (texto da licença, fonte correspondente
e oferta de código-fonte) para o build que estiveres a distribuir.
