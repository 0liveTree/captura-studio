using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CapturaStudio;

/// <summary>Traduções PT/EN. As chaves são os textos em português.</summary>
public static class Loc
{
    public static string Lang = "pt";

    public static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
    {
        // janela principal
        ["Captura Studio"] = "Captura Studio",
        ["FONTE"] = "SOURCE",
        ["VÍDEO"] = "VIDEO",
        ["ÁUDIO"] = "AUDIO",
        ["GRAVAÇÃO"] = "RECORDING",
        ["PRÉ-VISUALIZAÇÃO"] = "PREVIEW",
        ["DISPOSITIVO DE VÍDEO"] = "VIDEO DEVICE",
        ["RESOLUÇÃO"] = "RESOLUTION",
        ["FPS"] = "FPS",
        ["FORMATO"] = "FORMAT",
        ["DISPOSITIVO DE ÁUDIO"] = "AUDIO DEVICE",
        ["CODEC"] = "CODEC",
        ["PERFIL H.264"] = "H.264 PROFILE",
        ["BITRATE (Mbps)"] = "BITRATE (Mbps)",
        ["ENCODER"] = "ENCODER",
        ["RATE CONTROL"] = "RATE CONTROL",
        ["KEYFRAME (GOP)"] = "KEYFRAME (GOP)",
        ["ESPAÇO DE COR"] = "COLOR SPACE",
        ["INTERVALO"] = "RANGE",
        ["MODO"] = "MODE",
        ["CODIFICADOR DE ÁUDIO"] = "AUDIO ENCODER",
        ["PRESET"] = "PRESET",
        ["AFINAÇÃO"] = "TUNING",
        ["B-FRAMES"] = "B-FRAMES",
        ["MULTIPASS"] = "MULTIPASS",
        ["CQ (QUALIDADE)"] = "CQ (QUALITY)",
        ["CQ aplica-se aos modos de quantização constante e qualidade alvo."] =
            "CQ applies to constant quantizer and target-quality modes.",
        ["BITRATE ÁUDIO (kbps)"] = "AUDIO BITRATE (kbps)",
        ["TAXA DE AMOSTRAGEM"] = "SAMPLE RATE",
        ["CANAIS"] = "CHANNELS",
        ["PCM (BITS)"] = "PCM (BITS)",
        ["PASTA DE DESTINO"] = "DESTINATION FOLDER",
        ["CONTENTOR"] = "CONTAINER",
        ["PARAR APÓS"] = "STOP AFTER",
        ["DIVISÃO AUTO"] = "AUTO SPLIT",
        ["GRAVAR"] = "RECORD",
        ["PARAR"] = "STOP",
        ["Pronto"] = "Ready",
        ["Sem preview"] = "No preview",
        ["A ligar…"] = "Connecting…",
        ["Sem ligação ao motor (dispositivo ocupado?)"] = "No connection to the engine (device busy?)",
        ["Pré-visualizar"] = "Preview",
        ["Parar pré-visualização"] = "Stop preview",
        ["Registo"] = "Log",
        ["Ecrã inteiro"] = "Full screen",
        ["Sair do ecrã inteiro"] = "Exit full screen",
        ["Silenciar"] = "Mute",
        ["Atualizar dispositivos"] = "Refresh devices",

        // valores comuns
        ["Sem áudio"] = "No audio",
        ["Estéreo"] = "Stereo",
        ["Mono"] = "Mono",
        ["Sem divisão"] = "No split",
        ["Manual"] = "Manual",
        ["Limitado (16-235)"] = "Limited (16-235)",
        ["Completo (0-255)"] = "Full (0-255)",
        ["GPU (hardware)"] = "GPU (hardware)",
        ["CPU (software)"] = "CPU (software)",

        // definições
        ["Definições"] = "Settings",
        ["Gravação"] = "Recording",
        ["Aparência"] = "Appearance",
        ["Pré-visualização"] = "Preview",
        ["Sobre"] = "About",
        ["Informação da aplicação, recursos e créditos."] = "App details, resources and credits.",
        ["Acesso rápido"] = "Quick access",
        ["Onde e como os vídeos são guardados."] = "Where and how videos are saved.",
        ["Pasta de destino"] = "Destination folder",
        ["Qualidade, codificação e formato do vídeo."] = "Video quality, encoding and format.",
        ["Formato, qualidade e canais do áudio."] = "Audio format, quality and channels.",
        ["Procurar…"] = "Browse…",
        ["Formato"] = "Format",
        ["Contentor"] = "Container",
        ["Divisão automática"] = "Automatic split",
        ["Repor predefinições"] = "Reset defaults",
        ["Vídeo"] = "Video",
        ["Taxa de bits"] = "Bitrate",
        ["Codificador"] = "Encoder",
        ["Codificador de vídeo"] = "Video encoder",
        ["Codificador de áudio"] = "Audio encoder",
        ["Controle da frequência"] = "Rate control",
        ["Predefinição"] = "Preset",
        ["Afinação"] = "Tuning",
        ["B-frames"] = "B-frames",
        ["Multipass"] = "Multipass",
        ["Intervalo de fotogramas-chave"] = "Keyframe interval",
        ["segundos · 0 = automático"] = "seconds · 0 = automatic",
        ["CQ (qualidade)"] = "CQ (quality)",
        ["Processador"] = "Processor",
        ["Configurações do codificador"] = "Encoder settings",
        ["Aplicado pela pipeline FFmpeg (NVENC na GPU, x264/x265 na CPU)."] =
            "Applied by the FFmpeg pipeline (NVENC on GPU, x264/x265 on CPU).",
        ["Automático (segue o codec)"] = "Automatic (follows the codec)",
        ["indisponível"] = "unavailable",
        ["Taxa de bits constante"] = "Constant bitrate",
        ["Valor de quantização constante"] = "Constant quantizer",
        ["Taxa de bits variável"] = "Variable bitrate",
        ["Taxa de bits variável com qualidade do alvo"] = "Variable bitrate with target quality",
        ["Sem perdas"] = "Lossless",
        ["Qualidade"] = "Quality",
        ["Baixa latência"] = "Low latency",
        ["Ultra baixa latência"] = "Ultra low latency",
        ["Sem afinação"] = "No tuning",
        ["Desligado"] = "Off",
        ["Uma passagem"] = "Single pass",
        ["Completa"] = "Full",
        ["Opus (FFmpeg)"] = "Opus (FFmpeg)",
        ["Parar após"] = "Stop after",
        ["Taxa de amostragem"] = "Sample rate",
        ["PCM (bits)"] = "PCM (bits)",
        ["Como a app se apresenta."] = "How the app looks.",
        ["Fundo"] = "Background",
        ["Cor de destaque"] = "Accent color",
        ["Cor do Windows"] = "Windows color",
        ["Usada nos botões principais e nos realces."] = "Used on primary buttons and highlights.",
        ["Efeito translúcido por trás da janela."] = "Translucent effect behind the window.",
        ["Língua dos textos da interface."] = "Language of the interface texts.",
        ["Claro, escuro, ou o que o Windows estiver a usar."] = "Light, dark, or whatever Windows is using.",
        ["Aspeto da janela"] = "Window appearance",
        ["Escolhe o tema, o fundo, a cor de destaque e o idioma."] = "Choose the theme, backdrop, accent color and language.",
        ["Claro, escuro ou conforme o Windows."] = "Light, dark, or follow Windows.",
        ["Aplicada aos botões principais e realces."] = "Applied to primary buttons and highlights.",
        ["Efeito visual por trás da janela."] = "Visual effect behind the window.",
        ["Pré-visualização e comportamento"] = "Preview and behavior",
        ["Define o arranque e os controlos de áudio da pré-visualização."] = "Set startup behavior and preview audio controls.",
        ["Tema"] = "Theme",
        ["Seguir Windows"] = "Follow Windows",
        ["Claro"] = "Light",
        ["Escuro"] = "Dark",
        ["O que acontece ao abrir a app."] = "What happens when the app opens.",
        ["Iniciar ao abrir"] = "Start on open",
        ["Mostrar detalhes"] = "Show details",
        ["Sim"] = "Yes",
        ["Não"] = "No",
        ["Volume"] = "Volume",
        ["Idioma"] = "Language",
        ["Português"] = "Portuguese",
        ["Inglês"] = "English",
        ["Cancelar"] = "Cancel",
        ["Guardar"] = "Save",
        ["Fechar"] = "Close",
        ["PERFIL"] = "PROFILE",
        ["NÍVEL"] = "LEVEL",
        ["Personalizado"] = "Custom",
        ["Guardar…"] = "Save…",
        ["Eliminar"] = "Delete",
        ["Guardar perfil"] = "Save profile",
        ["Eliminar perfil"] = "Delete profile",
        ["Nome do perfil"] = "Profile name",
        ["Eliminar este perfil?"] = "Delete this profile?",
        ["O nome do perfil não pode estar vazio."] = "The profile name cannot be empty.",

        // sobre
        ["Captura Studio é um gravador de captura de vídeo nativo para Windows, de código aberto, escrito em Rust e WinUI 3."] =
            "Captura Studio is an open-source native video capture recorder for Windows, written in Rust and WinUI 3.",
        ["Abrir pasta de gravações"] = "Open recordings folder",
        ["Pasta de definições"] = "Settings folder",
        ["Encoders disponíveis"] = "Available encoders",
        ["Licença MIT"] = "MIT License",
        ["Obter FFmpeg"] = "Get FFmpeg",
        ["Tecnologias"] = "Technologies",
        ["MIT · Código aberto"] = "MIT · Open source",
        ["Feito em Portugal ❤"] = "Made in Portugal ❤",
        ["Contribuições"] = "Contributions",
        ["Compilação"] = "Build",
        ["Componente"] = "Component",
        ["Versão"] = "Version",
        ["Função"] = "Role",
        ["Licença MIT · código aberto — podes usar, modificar e distribuir, mantendo o aviso de copyright."] =
            "MIT License · open source — you may use, modify and distribute it, keeping the copyright notice.",
        // textos dinâmicos / cartões
        ["motor:"] = "engine:",
        ["compilado em"] = "built",
        ["não encontrado"] = "not found",
        ["Aplicação (WinUI 3)"] = "App (WinUI 3)",
        ["Interface Fluent"] = "Fluent UI",
        ["Captura e codificação via Media Foundation"] = "Capture and encoding via Media Foundation",
        ["Reprodução do áudio do preview"] = "Preview audio playback",
        ["MP3/FLAC e remux"] = "MP3/FLAC and remux",
        ["Captura, cores e codecs"] = "Capture, color and codecs",
        ["Motor + pipeline raw/FFmpeg"] = "Engine + raw/FFmpeg pipeline",
        ["Revisão · sem código"] = "Review · no code",
        ["Interface + UVC03 (raw)"] = "Interface + UVC03 (raw)",
        ["Interface e publicação"] = "Interface and publishing",
        ["A gravar"] = "Recording",
        ["A gravar…"] = "Recording…",
        ["A aguardar sinal da placa"] = "Waiting for capture card",
        ["A tentar recuperar sinal da placa"] = "Trying to recover capture card signal",
        ["Sinal da placa recuperado"] = "Capture card signal recovered",
        ["Sem sinal da placa"] = "No capture card signal",
        ["Sem sinal da placa; a gravação vai terminar"] = "No capture card signal; recording will stop",
        ["A gravação vai terminar."] = "Recording will stop.",
        ["Gravação guardada"] = "Recording saved",
        ["Erro"] = "Error",
        ["O motor captura-rec.exe não foi encontrado."] = "The captura-rec.exe engine was not found.",
        ["Não foram gravados frames."] = "No frames were recorded.",
        ["Falha ao guardar a gravação. Vê o registo."] = "Failed to save the recording. Check the log.",
        ["Nome do ficheiro"] = "File name",
        ["Usa {data}, {hora}, {dispositivo} e {modo}."] = "Use {data}, {hora}, {dispositivo} and {modo}.",
        ["A terminar"] = "Stopping",
        ["Placa"] = "Capture card",
        ["Sem frames recentes"] = "No recent frames",
        ["Métricas"] = "Metrics",
        ["fonte"] = "source",
        ["saída"] = "output",
        ["repetidos"] = "repeated",
        ["descartados"] = "dropped",
        ["recuperações"] = "recoveries",
        ["Terminado: "] = "Finished: ",
        ["Definições guardadas."] = "Settings saved.",
        ["Segmento: "] = "Segment: ",
        ["Licença"] = "License",        ["Implementação principal: motor Rust/Media Foundation e pipeline raw NV12 + FFmpeg (opção B); áudio WAV e sincronização A/V; preview em processo; painel do codificador; persistência de definições e idioma; interface WinUI 3 base; cores Rec.709/2020; resolução/FPS lidos da placa."] =
            "Main implementation: Rust/Media Foundation engine and raw NV12 + FFmpeg pipeline (option B); WAV audio and A/V sync; in-process preview; encoder panel; settings and language persistence; base WinUI 3 interface; Rec.709/2020 color; resolution/FPS read from the device.",
        ["Revisão do handoff e diagnóstico da falha de arranque WinUI relacionada com PRI/XamlParseException."] =
            "Handoff review and diagnosis of the WinUI startup failure related to PRI/XamlParseException.",
        ["Interface: cartões organizados para destino, vídeo e áudio; Definições agrupadas; página Sobre, volume, ações por separador e traduções revistas. UVC03 no pipeline raw/FFmpeg: fila limitada a 2 frames, saída CFR, contagem de frames recebidos/descartados/repetidos, espera até 40 s pelo primeiro frame e deteção de pausa após 2,5 s; até duas reaberturas do leitor, com aviso na UI e fecho da gravação se não recuperar."] =
            "Interface: organized cards for destination, video and audio; grouped Settings; About page, volume, tab-specific actions and revised translations. UVC03 in the raw/FFmpeg pipeline: 2-frame bounded queue, CFR output, counts of received/dropped/repeated frames, wait up to 40 s for the first frame and pause detection after 2.5 s; up to two reader reopens, with a UI warning and recording finalization if recovery fails.",
        ["© 2026 OliveTree · Feito em Portugal com amor e carinho ❤️"] =
            "© 2026 OliveTree · Made in Portugal with love and care ❤️",
    };

    public static string T(string pt)
    {
        if (Lang != "en") return pt;
        if (Map.TryGetValue(pt, out var en)) return en;
        // textos com placeholders (contagens, datas) começam por prefixos conhecidos
        return pt;
    }

    /// <summary>Percorre a árvore visual e traduz TextBlock/Button/ComboBoxItem/ToggleSwitch.</summary>
    public static void Apply(DependencyObject root)
    {
        if (root == null) return;
        switch (root)
        {
            case TextBlock tb:
                tb.Text = T(tb.Text);
                break;
            case Button b when b.Content is string s:
                b.Content = T(s);
                break;
            case HyperlinkButton hb when hb.Content is string hs:
                hb.Content = T(hs);
                break;
            case ComboBoxItem ci when ci.Content is string cs:
                ci.Content = T(cs);
                break;
            case ToggleSwitch ts:
                if (ts.OnContent is string oc) ts.OnContent = T(oc);
                if (ts.OffContent is string fc) ts.OffContent = T(fc);
                break;
            case Expander ex when ex.Header is string eh:
                ex.Header = T(eh);
                break;
        }

        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
            Apply(VisualTreeHelper.GetChild(root, i));
    }
}
