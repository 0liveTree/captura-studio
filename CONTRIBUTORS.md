# Contribuições

O Captura Studio foi construído com direção humana, apoio de modelos de IA e revisão. O âmbito
de cada contribuição está resumido aqui e na secção **Sobre → Contribuições** da aplicação.

| Empresa | Agente | Modelo | Âmbito |
|---|---|---|---|
| DeepSeek | DeepSeek Harness | DeepSeek-V4.1-Flash (4.1) | Motor Rust/Media Foundation, pipeline raw + FFmpeg (opção B), áudio WAV e sincronização, preview no processo, painel do codificador, persistência de definições e funcionalidades de produto (notificações, perfis/presets, medidor de áudio, atalhos, instalador). |
| Anthropic | Antigravity (Google) | Claude Sonnet 4.6 (Thinking) | Revisão do pacote de handoff e diagnóstico do arranque WinUI (PRI/XamlParseException); sem alterações de código. |
| OpenAI | Codex | GPT-6 | Interface (hierarquia visual, Definições, preview 16:9, agrupamento de áudio) e resiliência UVC03 no caminho raw: fila limitada, CFR, timeouts e reaberturas do leitor. |

Direção, requisitos, testes de hardware e decisões de produto: autor do projeto.

## Nota

As contribuições descrevem **âmbito e responsabilidade**, não uma contagem de linhas. O trabalho
foi iterativo e as contagens de linhas não medem autoria; a distinção que importa é entre o
trabalho de base (motor/pipeline/UI) e as rondas posteriores de interface e resiliência UVC03.
