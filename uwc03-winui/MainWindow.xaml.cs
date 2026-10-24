using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.InteropServices;
using NAudio.Wave;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Net.Sockets;
using System.Net.Http;
using System.Threading;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media;

namespace CapturaStudio;

// ---------------------------------------------------------------------------
// Modelos de dados
// ---------------------------------------------------------------------------

public sealed class DeviceItem
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public override string ToString() => Name;
}

public sealed class ModeItem
{
    public uint W { get; set; }
    public uint H { get; set; }
    public uint Fps { get; set; }
    public string Fmt { get; set; } = "";
    public override string ToString() => $"{W}x{H} @ {Fps} fps ({Fmt})";
}

// ---------------------------------------------------------------------------
// Settings persistentes  (%LOCALAPPDATA%\CapturaStudio\settings.json)
// ---------------------------------------------------------------------------

public sealed class WinUISettings
{
    public string VideoDevice  { get; set; } = "";
    public string AudioDevice  { get; set; } = "";
    public int    Codec        { get; set; } = 0;   // 0=H.264  1=HEVC
    public int    Profile      { get; set; } = 2;   // 0=base 1=main 2=high
    public int    Bitrate      { get; set; } = 2;   // índice em Bps[]
    public int    Hw           { get; set; } = 0;   // 0=GPU 1=CPU
    public int    Rate         { get; set; } = 0;   // 0=CBR 1=VBR
    public int    Gop          { get; set; } = 1;   // índice em Gops[]
    public int    AudioMode    { get; set; } = 1;   // 0=none 1=AAC 2=PCM
    public int    Abr          { get; set; } = 1;   // índice em Abr[]
    public int    Container    { get; set; } = 0;   // 0=mp4 1=mkv 2=ts
    public int    Duration     { get; set; } = 0;   // índice em Durs[]
    public int    Split        { get; set; } = 0;   // índice em Splits[]
    public int    Res          { get; set; } = 0;
    public int    Fps          { get; set; } = 2;
    public int    Format       { get; set; } = 0;
    public int    AudioRate    { get; set; } = 1;
    public int    AudioChannels{ get; set; } = 0;
    public int    AudioBits    { get; set; } = 0;
    public int    ColorSpace   { get; set; } = 0;
    public int    ColorRange   { get; set; } = 0;
    public int    VideoEncoder { get; set; } = 0;   // 0=auto 1=nvenc h264 2=nvenc hevc 3=x264 4=x265
    public int    RateMode     { get; set; } = -1;  // 0=CBR 1=CQP 2=VBR 3=VBR alvo 4=sem perdas (-1 migra de Rate)
    public double GopSeconds   { get; set; } = -1;  // -1 migra de Gop
    public int    Preset       { get; set; } = 4;   // 0..6
    public int    Tune         { get; set; } = 0;   // 0=qualidade 1=baixa lat. 2=ultra baixa 3=sem afinação
    public int    Bframes      { get; set; } = 3;   // 0..4
    public int    Multipass    { get; set; } = 0;   // 0=desligado 1=uma passagem 2=completa
    public int    Cq           { get; set; } = 23;  // valor 18..32
    public int    Backdrop     { get; set; } = 0;
    public int    Accent       { get; set; } = 0;
    public int    Theme        { get; set; } = 0;
    public bool   AutoPreview  { get; set; } = false;
    public double PreviewVolume { get; set; } = 0.8;
    public bool   ShowInfo     { get; set; } = true;
    public string FileTemplate { get; set; } = "Captura_{data}_{hora}";
    public string ResText { get; set; } = "";
    public string FpsText { get; set; } = "";
    public string FormatText { get; set; } = "";
    public string Language { get; set; } = "pt";
    public string Folder       { get; set; } = "";
    public string UpdateUrl    { get; set; } = UpdateDefaults.ManifestUrl;
    public bool   UpdateAuto   { get; set; } = true;
}

public static class UpdateDefaults
{
    public const string ManifestUrl =
        "https://github.com/0liveTree/captura-studio/releases/latest/download/latest.json";
}

public sealed class UpdateManifest
{
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Notes { get; set; } = "";
}

// ---------------------------------------------------------------------------
// Perfis/presets de captura  (%LOCALAPPDATA%\CapturaStudio\profiles.json)
// ---------------------------------------------------------------------------

public sealed class CaptureProfile
{
    public string Name { get; set; } = "";
    public string ResText { get; set; } = "";
    public string FpsText { get; set; } = "";
    public string FormatText { get; set; } = "";
    public int Res { get; set; }
    public int Fps { get; set; }
    public int Format { get; set; }
    public int Codec { get; set; }
    public int Profile { get; set; } = 2;
    public int Bitrate { get; set; } = 2;
    public int Hw { get; set; }
    public int RateMode { get; set; }
    public double GopSeconds { get; set; } = 2;
    public int Preset { get; set; } = 4;
    public int Tune { get; set; }
    public int Bframes { get; set; } = 3;
    public int Multipass { get; set; }
    public int Cq { get; set; } = 23;
    public int VideoEncoder { get; set; }
    public int AudioMode { get; set; } = 1;
    public int Abr { get; set; } = 1;
    public int AudioRate { get; set; } = 1;
    public int AudioChannels { get; set; }
    public int AudioBits { get; set; }
    public int ColorSpace { get; set; }
    public int ColorRange { get; set; }
    public int Container { get; set; }
    public int Duration { get; set; }
    public int Split { get; set; }
    public string FileTemplate { get; set; } = "Captura_{data}_{hora}";
}

// ---------------------------------------------------------------------------
// MainWindow
// ---------------------------------------------------------------------------

public sealed partial class MainWindow : Window
{
    // --- tabelas de opções estáticas ---
    private static readonly string[] Exts    = { "mp4", "mkv", "ts" };
    private static readonly (uint W, uint H)[] ResList =
    {
        (1920, 1080), (2560, 1440), (1280, 720), (3840, 2160), (640, 480)
    };
    private static readonly uint[]   FpsList = { 24, 25, 30, 50, 60 };
    private List<(uint W, uint H, uint Fps)>? _modes;
    private List<(uint W, uint H)>? _capRes;
    private List<uint>? _capFps;
    private List<string>? _capFmts;
    private int _modesToken;
    private bool _devicesLoading;
    private bool _settingsApplied;
    private static readonly uint[]   Bps     = { 4, 8, 12, 20, 30, 50 };
    private static readonly uint[]   Abr     = { 96, 128, 160, 192, 256, 320 };
    private static readonly uint[]   Durs    = { 0, 5, 10, 30, 60 };
    private static readonly uint[]   Gops    = { 1, 2, 4, 8 };
    private static readonly uint[]   Splits  = { 0, 5, 10, 30, 60 };
    private static readonly string[] Profiles = { "baseline", "main", "high" };
    private static readonly string[] X264Presets =
        { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow" };
    private static readonly int[]    CqValues = { 18, 20, 22, 23, 25, 28, 32 };
    private static readonly int[]    Bframes = { 0, 1, 2, 3, 4 };

    // --- estado de gravação ---
    private Process?  _proc;
    private DateTime  _start;
    private DateTime  _waitStart;
    private DateTime  _lastFpsUpdate = DateTime.MinValue;
    private bool      _captureReady;
    private bool      _stopRequested;
    private double    _captureSourceFps = -1;
    private double    _captureOutputFps = -1;
    private int       _captureTargetFps = 30;
    private ulong    _captureDroppedFrames;
    private ulong    _captureRepeatedFrames;
    private ulong    _captureRecoveries;
    private string   _captureHealthText = "";
    private string    _out = "";
    private string?   _ffmpegPath;
    private string?   _ffmpegVersion;
    private string?   _pendingAudioCodec;
    private DateTime  _lastRefresh = DateTime.MinValue;
    private string    _savedResText = "";
    private string    _savedFpsText = "";
    private string    _savedFmtText = "";
    private readonly StringBuilder _log    = new();
    private readonly StringBuilder _stdout = new();
    private readonly object _stdoutLock = new();

    // --- pipeline raw + FFmpeg (opção B) ---
    private Process?  _ffProc;
    private Task?     _pipeTask;
    private string?   _rawVideo;
    private string?   _rawWav;
    private bool      _rawMode;
    private bool      _finalizing;
    private bool      _closingAfterRec;
    private int       _rawAudioMode;
    private int       _syncV = -1;
    private int       _syncA = -1;
    private int       _videoEncoder;
    private bool      _loadingSettings;
    private readonly StringBuilder _ffLog = new();
    private readonly object _ffLogLock = new();
    private bool?     _hasNvencH264;
    private bool?     _hasNvencHevc;
    private bool?     _hasX264;
    private bool?     _hasX265;

    // --- perfis/presets ---
    private List<CaptureProfile> _profilePresets = new();
    private bool _loadingProfilePresets;

    // --- atualizações ---
    private string _updateUrl = UpdateDefaults.ManifestUrl;
    private bool _updateAuto = true;
    private bool _updateChecking;
    private bool _startupUpdateChecked;
    private Action? _notifyAction;
    private static string AppVersion =>
        typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.1";

    // --- atalhos globais (Ctrl+Alt+F9/F10) ---
    private IntPtr _hotkeyHwnd;
    private IntPtr _oldWndProc;
    private WndProcDelegate? _wndProc;
    private const int GWLP_WNDPROC = -4;
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_REC = 1;
    private const int HOTKEY_STOP = 2;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
    private const uint VK_F9 = 0x78, VK_F10 = 0x79;
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // --- timer de UI ---
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _notifyTimer = new() { Interval = TimeSpan.FromSeconds(8) };

    private static string ExePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CapturaStudio", "captura-rec.exe");

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CapturaStudio", "settings.json");

    private static string ProfilesPath => Path.Combine(
        Path.GetDirectoryName(SettingsPath)!, "profiles.json");

    // ---------- fullscreen Win32 ----------
    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000L, WS_THICKFRAME = 0x00040000L, WS_SYSMENU = 0x00080000L,
                       WS_MINIMIZEBOX = 0x00020000L, WS_MAXIMIZEBOX = 0x00010000L, WS_BORDER = 0x00800000L;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_DEFAULT = 0;
    private static readonly int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    private static readonly int DWMWA_COLOR_DEFAULT = unchecked((int)0xFFFFFFFF);
    private const uint SWP_FRAMECHANGED = 0x0020, SWP_SHOWWINDOW = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    private IntPtr _savedStyle;
    private RECT _savedRect;
    // ---------- ecra inteiro ----------
    private bool _fullscreen;
    private int _backdrop = 0;
    private int _accent = 0;
    private int _theme = 0;
    private bool _autoPreview = false;
    private double _previewVolume = 0.8;
    private bool _showInfo = true;
    private string _fileTemplate = "Captura_{data}_{hora}";
    private readonly Microsoft.UI.Xaml.DispatcherTimer _fsTimer = new();
    private readonly Microsoft.UI.Xaml.DispatcherTimer _cursorTimer = new();
    private POINT _lastCursor;


    // ---------- aparencia ----------
    private void ApplyAppearance()
    {
        try
        {
            SystemBackdrop = _backdrop switch
            {
                1 => new MicaBackdrop { Kind = MicaKind.BaseAlt },
                2 => new DesktopAcrylicBackdrop(),
                3 => null,
                _ => new MicaBackdrop()
            };
        }
        catch (Exception ex) { Log("backdrop: " + ex.Message); }
        ApplyAccent(_accent);
        try
        {
            RootGrid.RequestedTheme = _theme switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }
        catch { }
    }

    private void ApplyAccent(int idx)
    {
        if (idx <= 0) return;
        var c = idx switch
        {
            1 => Windows.UI.Color.FromArgb(255, 0, 120, 215),
            2 => Windows.UI.Color.FromArgb(255, 16, 124, 16),
            3 => Windows.UI.Color.FromArgb(255, 202, 80, 16),
            4 => Windows.UI.Color.FromArgb(255, 136, 23, 152),
            5 => Windows.UI.Color.FromArgb(255, 196, 43, 28),
            _ => Windows.UI.Color.FromArgb(255, 255, 185, 0)
        };
        var res = Application.Current.Resources;
        res["AccentFillColorDefaultBrush"] = new SolidColorBrush(c);
        res["AccentFillColorSecondaryBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(230, c.R, c.G, c.B));
        res["AccentFillColorTertiaryBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(204, c.R, c.G, c.B));
    }
    private void BtnFull_Click(object sender, RoutedEventArgs e) { Log($"BtnFull_Click (atual={_fullscreen})"); SetFullscreen(!_fullscreen); }


    private void StartAudioOutput(int rate = 48000, int channels = 2)
    {
        try
        {
            _audioProvider = new BufferedWaveProvider(new WaveFormat(rate, 16, channels))
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(2)
            };
            try
            {
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_audioProvider);
                _waveOut.Volume = (float)Math.Clamp(VolSlider.Value / 100.0, 0.0, 1.0);
                _waveOut.Play();
                Log("Preview: audio (WaveOut) devices=" + NAudio.Wave.WaveOut.DeviceCount);
            }
            catch (Exception ex)
            {
                Log("WaveOut EX: " + ex.GetType().Name + " hr=0x" + ex.HResult.ToString("X8") + " " + ex.Message);
                var dso = new DirectSoundOut();
                dso.Init(_audioProvider);
                dso.Play();
                _dsOut = dso;
                Log("Preview: audio (DirectSound)");
            }
        }
        catch (Exception ex) { Log("audio output EX: " + ex.Message); }
    }
    private void ShowFsControls()
    {
        if (!_fullscreen) return;
        PreviewControls.Visibility = Visibility.Visible;
        FsBar.Visibility = Visibility.Visible;
        _fsTimer.Stop();
        _fsTimer.Start();
    }

    private void SetFullscreen(bool on)
    {
        Log($"SetFullscreen({on})");
        _fullscreen = on;
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (on)
            {
                GetWindowRect(hwnd, out _savedRect);
                _savedStyle = GetWindowLongPtr(hwnd, GWL_STYLE);
                long style = (long)_savedStyle & ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_BORDER);
                SetWindowLongPtr(hwnd, GWL_STYLE, (IntPtr)style);
                int corner = DWMWCP_DONOTROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
                int border = DWMWA_COLOR_NONE;
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, 4);
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                IntPtr mon = MonitorFromWindow(hwnd, 2 /*NEAREST*/);
                GetMonitorInfo(mon, ref mi);
                int mx = mi.rcMonitor.Left, my = mi.rcMonitor.Top;
                int sw = mi.rcMonitor.Right - mi.rcMonitor.Left;
                int sh = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
                SetWindowPos(hwnd, new IntPtr(-1), mx, my, sw, sh, SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            }
            else
            {
                int corner0 = DWMWCP_DEFAULT;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner0, 4);
                int border0 = DWMWA_COLOR_DEFAULT;
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border0, 4);
                SetWindowLongPtr(hwnd, GWL_STYLE, _savedStyle);
                SetWindowPos(hwnd, new IntPtr(-2), _savedRect.Left, _savedRect.Top,
                             _savedRect.Right - _savedRect.Left, _savedRect.Bottom - _savedRect.Top,
                             SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            }
        }
        catch (Exception ex) { Log("fullscreen: " + ex.Message); }

        FsOverlay.Visibility        = on ? Visibility.Visible : Visibility.Collapsed;
        RootScroll.Visibility       = on ? Visibility.Collapsed : Visibility.Visible;
        NotifyBar.Visibility        = on ? Visibility.Collapsed : Visibility.Visible;
        HeaderRow.Visibility        = on ? Visibility.Collapsed : Visibility.Visible;
        DevicesCard.Visibility      = on ? Visibility.Collapsed : Visibility.Visible;
        LeftPane.Visibility         = on ? Visibility.Collapsed : Visibility.Visible;
        ColLeft.Width  = on ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ColRight.Width = on ? new GridLength(1, GridUnitType.Star) : new GridLength(1.3, GridUnitType.Star);
        PreviewTitle.Visibility     = on ? Visibility.Collapsed : Visibility.Visible;
        FsRecRow.Visibility         = on ? Visibility.Visible : Visibility.Collapsed;
        RootScroll.Margin           = on ? new Thickness(0) : new Thickness(0, 36, 0, 0);
        PreviewCard.BorderThickness = on ? new Thickness(0) : new Thickness(1);
        PreviewCard.Padding         = on ? new Thickness(0) : new Thickness(16);
        PreviewControls.Margin      = on ? new Thickness(12, -64, 12, 0) : new Thickness(0);
        PreviewControls.Background  = on ? new SolidColorBrush(Windows.UI.Color.FromArgb(170, 0, 0, 0)) : null;
        try
        {
            ToolTipService.SetToolTip(BtnFull, on ? "Sair do ecrã inteiro" : "Ecrã inteiro");
            if (FullIcon != null) FullIcon.Glyph = on ? "\uE73F" : "\uE740";
        }
        catch { }
        PreviewImage.Stretch = _fullscreen ? Stretch.UniformToFill : Stretch.Uniform;

        if (on) { PreviewControls.Visibility = Visibility.Visible; _fsTimer.Stop(); _fsTimer.Start(); }
        else { _fsTimer.Stop(); _cursorTimer.Stop(); PreviewControls.Visibility = Visibility.Visible; }

        double scale = RootScroll.XamlRoot?.RasterizationScale ?? 1.0;
        PreviewBorder.Height = on ? Math.Max(320, AppWindow.Size.Height / scale) : 620;
        DispatcherQueue.TryEnqueue(() =>
        {
            double s2 = RootScroll.XamlRoot?.RasterizationScale ?? 1.0;
            PreviewBorder.Height = on ? Math.Max(320, AppWindow.Size.Height / s2) : 620;
        });
    }
    private void UpdateTitleBarInsets()
    {
        try
        {
            // 160 DIPs cobrem minimizar/maximizar/fechar (138) + folga.
            double right = Math.Max(160, AppWindow.TitleBar.RightInset);
            HeaderRow.Margin = new Thickness(0, 0, right, 0);
            NotifyBar.Margin = new Thickness(24, 12, 24 + right, 0);
        }
        catch { }
    }

    public MainWindow()
    {
        // O idioma tem de estar definido ANTES de InitializeComponent/SaveSettings:
        // repor os combos dispara SaveSettings, que grava Language.
        LoadLanguageEarly();
        InitializeComponent();
        LoadProfilePresets();
        try
        {
            SystemBackdrop = new MicaBackdrop();
            ExtendsContentIntoTitleBar = true;
            var tb = AppWindow.TitleBar;
            tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonForegroundColor = Microsoft.UI.Colors.White;
            tb.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(60, 255, 255, 255);
            tb.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(90, 255, 255, 255);
        }
        catch { }

        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "videotape.ico")); }
        catch { }
        try { AboutIcon.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "videotape.png"))); }
        catch { }
        UpdateTitleBarInsets();
        AppWindow.Changed += (_, _) => UpdateTitleBarInsets();

            try
            {
                var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
                var wa = area.WorkArea;
                if (AppWindow.Size.Width > wa.Width || AppWindow.Size.Height > wa.Height)
                {
                    int w2 = Math.Min(1400, wa.Width - 80);
                    int h2 = Math.Min(900, wa.Height - 80);
                    AppWindow.Resize(new Windows.Graphics.SizeInt32(w2, h2));
                    AppWindow.Move(new Windows.Graphics.PointInt32(
                        wa.X + (wa.Width - w2) / 2, wa.Y + (wa.Height - h2) / 2));
                }
            }
            catch { }        var up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        FolderBox.Text = Path.Combine(up, "Videos", "Captura Studio");

        _timer.Tick += (_, _) => Tick();
        _notifyTimer.Tick += (_, _) => { _notifyTimer.Stop(); NotifyBar.Visibility = Visibility.Collapsed; _notifyAction = null; };

        // Em WinUI 3, o evento Closed é disparado antes do processo sair,
        // mas o handler tem de ser síncrono para garantir que SaveSettings() corre.
        // Usamos AppWindow.Closing (cancelável e síncrono) em vez de Closed.
        AppWindow.Closing += (_, args) =>
        {
            SaveSettings();
            if (_proc != null)
            {
                // Fechar a meio de uma gravação (raw ou MF): cancelar o fecho, parar
                // o motor e só fechar quando ele terminar/finalizar o ficheiro.
                args.Cancel = true;
                _closingAfterRec = true;
                StopRec();
                return;
            }
            StopRec();
            StopPreview();
        };
        Closed += (_, _) => UnregisterGlobalHotkeys();
        RootGrid.Loaded += (_, _) => InitGlobalHotkeys();
        RootGrid.Loaded += (_, _) => UpdateTitleBarInsets();
        UpdateUrlBox.LostFocus += (_, _) => SaveUpdateSettings();
        UpdateAutoSwitch.Toggled += (_, _) => SaveUpdateSettings();
        RootGrid.Loaded += async (_, _) =>
        {
            if (_startupUpdateChecked) return;
            _startupUpdateChecked = true;
            if (!_updateAuto) return;
            await Task.Delay(3000);
            await CheckForUpdatesAsync(silent: true);
        };

        VideoBox.SelectionChanged += (_, _) => { RestartPreviewIfActive(); _ = LoadModesAsync(); };
        AudioBox.SelectionChanged += (_, _) => RestartPreviewIfActive();
        ResBox.SelectionChanged += (_, _) => { UpdateFpsForRes(); RestartPreviewIfActive(); };
        FpsBox.SelectionChanged += (_, _) => RestartPreviewIfActive();
        FormatBox.SelectionChanged += (_, _) => RestartPreviewIfActive();
        AudioModeBox.SelectionChanged += (_, _) => UpdateAudioFields();
        CodecBox.SelectionChanged += (_, _) => UpdateVideoFields();
        HwBox.SelectionChanged += (_, _) => UpdateVideoFields();
        ColorSpaceBox.SelectionChanged += (_, _) => RestartPreviewIfActive();
        ColorRangeBox.SelectionChanged += (_, _) => RestartPreviewIfActive();
        PreviewAudioSwitch.Toggled += (_, _) => RestartPreviewIfActive();
        foreach (var cb in new ComboBox[] { CodecBox, ProfileBox, BitrateBox, HwBox, RateBox,
                                         AudioModeBox, AbrBox, ContBox, DurBox, SplitBox,
                                         AudioRateBox, AudioChannelsBox, AudioBitsBox, ColorSpaceBox, ColorRangeBox,
                                         PresetBox, TuneBox, BframesBox, MultipassBox, CqBox })
            cb.SelectionChanged += (_, _) => SaveSettings();
        GopBox.ValueChanged += (_, _) => SaveSettings();
        RateBox.SelectionChanged += (_, _) => UpdateVideoFields();
        RateBox2.SelectionChanged += (_, _) => UpdateEncoderFields2();
        EncoderBox2.SelectionChanged += (_, _) => UpdateEncoderFields2();
        QualityHint.Text = Loc.T("CQ: 0 = máxima qualidade, 51 = mínima; 18–32 é a faixa prática. Aplica-se a CQP/qualidade alvo.");
        FolderBox.TextChanged += (_, _) => SaveSettings();
        _cursorTimer.Interval = TimeSpan.FromMilliseconds(200);
        _cursorTimer.Tick += (_, _) =>
        {
            if (!_fullscreen) return;
            POINT p;
            if (GetCursorPos(out p))
            {
                if (Math.Abs(p.X - _lastCursor.X) > 2 || Math.Abs(p.Y - _lastCursor.Y) > 2)
                {
                    _lastCursor = p;
                    ShowFsControls();
                }
            }
        };
        _fsTimer.Interval = TimeSpan.FromMilliseconds(2500);
        _fsTimer.Tick += (_, _) => { if (_fullscreen) { PreviewControls.Visibility = Visibility.Collapsed; FsBar.Visibility = Visibility.Collapsed; } _fsTimer.Stop(); };
        RootScroll.PointerMoved += (_, _) => ShowFsControls();
        RootScroll.PointerPressed += (_, _) => ShowFsControls();
        FsOverlay.PointerMoved += (_, _) => ShowFsControls();
        FsOverlay.PointerPressed += (_, _) => ShowFsControls();
        FsOverlay.PointerEntered += (_, _) => ShowFsControls();
        if (Content is UIElement rootEl)
        {
        }
        // Carregar dispositivos de forma assíncrona (UI não bloca)
        Activated += (_, e) =>
        {
            try
            {
                if (e.WindowActivationState == WindowActivationState.Deactivated) return;
                if (_proc != null || _pvProc != null) return;
                if ((DateTime.Now - _lastRefresh).TotalSeconds < 5) return;
                _lastRefresh = DateTime.Now;
                _ = LoadDevicesAsync();
            }
            catch { }
        };
        RootGrid.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.F11) { SetFullscreen(!_fullscreen); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Escape && _fullscreen) { SetFullscreen(false); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.F9) { if (_proc == null) BtnRec_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.F10) { if (_proc != null) StopRec(); e.Handled = true; }
        };
        Log("Idioma inicial: " + Loc.Lang);
        RootGrid.Loaded += (_, _) =>
        {
            try { Loc.Apply(RootGrid); Log("Idioma: " + Loc.Lang); }
            catch (Exception ex) { Log("loc: " + ex.Message); }
        };
        DetectFfmpeg();
        DetectHardwareEncoder();
        _ = DetectFfmpegEncoders();
        _ = LoadDevicesAsync();
    }

    // -----------------------------------------------------------------------
    // Enumeração de dispositivos (--list e --caps N)
    // -----------------------------------------------------------------------

    private async Task LoadDevicesAsync()
    {
        if (_devicesLoading) return;
        _devicesLoading = true;
        _lastRefresh = DateTime.Now;
        try
        {
            if (!File.Exists(ExePath))
            {
                Log("captura-rec.exe não encontrado em: " + ExePath);
                Log("Executa scripts\\deploy.ps1 para instalar o motor.");
                return;
            }

            string keepVideo = (VideoBox.SelectedItem as DeviceItem)?.Name ?? "";
            string keepAudio = (AudioBox.SelectedItem as DeviceItem)?.Name ?? "";
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var sv = System.Text.Json.JsonSerializer.Deserialize<WinUISettings>(File.ReadAllText(SettingsPath));
                    if (sv != null)
                    {
                        if (string.IsNullOrEmpty(keepVideo)) keepVideo = sv.VideoDevice;
                        if (string.IsNullOrEmpty(keepAudio)) keepAudio = sv.AudioDevice;
                    }
                }
            }
            catch { }

            string output = await RunEngineAsync("--list");

            var videos = new List<DeviceItem>();
            var audios = new List<DeviceItem>();

            foreach (var raw in output.Split('\n'))
            {
                var t = raw.Trim().Split('\t');
                if (t.Length < 3) continue;
                if (t[0] == "V" && int.TryParse(t[1], out var vi))
                    videos.Add(new DeviceItem { Index = vi, Name = t[2] });
                else if (t[0] == "A" && int.TryParse(t[1], out var ai))
                    audios.Add(new DeviceItem { Index = ai, Name = t[2] });
            }

            // Actualizar ComboBoxes no thread da UI
            VideoBox.Items.Clear();
            foreach (var d in videos) VideoBox.Items.Add(d);
            SelectByName(VideoBox, keepVideo);

            AudioBox.Items.Clear();
            foreach (var d in audios) AudioBox.Items.Add(d);
            SelectByName(AudioBox, keepAudio);

            Log($"Dispositivos: {videos.Count} vídeo, {audios.Count} áudio.");

            // Primeiro os modos reais da placa, depois as definições (Res/Fps
            // índices só fazem sentido contra a lista de caps).
            await LoadModesAsync();
            // As definições só se aplicam no arranque; um refresh de dispositivos
            // (ao reactivar a janela) não pode repor resolução/fps/encoder.
            if (!_settingsApplied)
            {
                ApplySettings();
                _settingsApplied = true;
                _ = RecoverOrphanTempsAsync();
            }
            UpdateAudioFields();
            UpdateVideoFields();
        }
        finally { _devicesLoading = false; }
    }


    // Corre o motor com os args fornecidos e devolve stdout (async, sem bloquear UI)
    private static Task<string> RunEngineAsync(params string[] args)
    {
        return Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo(ExePath)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    CreateNoWindow = true
                };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                if (p == null) return "";
                var text = p.StandardOutput.ReadToEnd();
                var err = p.StandardError.ReadToEnd();
                p.WaitForExit(8000);
                try
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "winui-engine.txt"),
                        DateTime.Now.ToString("HH:mm:ss") + " args=" + string.Join(' ', args) +
                        " exit=" + p.ExitCode + " outLen=" + text.Length +
                        " err=" + err.Trim() + Environment.NewLine);
                }
                catch { }
                return text;
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "winui-engine.txt"),
                        DateTime.Now.ToString("HH:mm:ss") + " EX args=" + string.Join(' ', args) +
                        " " + ex.Message + Environment.NewLine);
                }
                catch { }
                return "";
            }
        });
    }

    private static void SelectByName(ComboBox box, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { if (box.Items.Count > 0) box.SelectedIndex = 0; return; }
        foreach (var item in box.Items)
            if (item is DeviceItem d && d.Name == name) { box.SelectedItem = item; return; }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    // -----------------------------------------------------------------------
    // Preview (video TCP 45901 + audio TCP 45902)
    // -----------------------------------------------------------------------
    private Process? _pvProc;
    private CancellationTokenSource? _pvCts;
    private bool _recPreview;
    private TcpClient? _pvVideo;
    private TcpClient? _pvAudio;
    private WriteableBitmap? _pvBmp;
    private WaveOutEvent? _waveOut;
    private NAudio.Wave.DirectSoundOut? _dsOut;
    private bool _audioLogged;
    private BufferedWaveProvider? _audioProvider;

    private void BtnPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_pvProc != null) { StopPreview(); return; }
        if (!File.Exists(ExePath)) { Log("motor nao encontrado"); return; }

        var pvres = GetSelRes();
        var pvfps = GetSelFps();
        var pvfmt = GetSelFormat();
        var psi = new ProcessStartInfo(ExePath) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "--preview", "--port", "45901",
                                  "--w", pvres.W.ToString(), "--h", pvres.H.ToString(),
                                  "--fps", pvfps.ToString() })
            psi.ArgumentList.Add(a);
        if (pvfmt != "auto")
        {
            psi.ArgumentList.Add("--format");
            psi.ArgumentList.Add(pvfmt);
        }
        var pvcs = ColorSpaceBox.SelectedIndex == 1 ? "2020" : "709";
        var pvcr = ColorRangeBox.SelectedIndex == 1 ? "full" : "limited";
        psi.ArgumentList.Add("--color-space"); psi.ArgumentList.Add(pvcs);
        psi.ArgumentList.Add("--color-range"); psi.ArgumentList.Add(pvcr);
        if (!PreviewAudioSwitch.IsOn) psi.ArgumentList.Add("--no-audio");
        if (VideoBox.SelectedItem is DeviceItem vd)
        {
            psi.ArgumentList.Add("--device");
            psi.ArgumentList.Add(vd.Index.ToString());
        }
        if (AudioBox.SelectedItem is DeviceItem ad)
        {
            psi.ArgumentList.Add("--audio-device");
            psi.ArgumentList.Add(ad.Index.ToString());
        }
        _pvProc = Process.Start(psi);
        _pvCts = new CancellationTokenSource();
        var ct = _pvCts.Token;
        if (PreviewBtnText != null) PreviewBtnText.Text = Loc.T("Parar pré-visualização"); else BtnPreview.Content = Loc.T("Parar pré-visualização");
        ShowPreviewLoading("A ligar...");
        _ = Task.Run(() => PreviewVideoLoop(ct));
        if (PreviewAudioSwitch.IsOn)
        {
            StartAudioOutput();
            _ = Task.Run(() => PreviewAudioLoop(ct));
        }
    }



    private void PreviewBorder_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        PreviewImage.Stretch = PreviewImage.Stretch == Stretch.Uniform
            ? Stretch.UniformToFill
            : Stretch.Uniform;
    }

    private void PreviewBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fullscreen || e.NewSize.Width <= 0) return;
        PreviewBorder.Height = GetPreviewHeight(e.NewSize.Width);
    }

    private double GetPreviewHeight(double width = 0)
    {
        if (width <= 0) width = PreviewBorder.ActualWidth;
        if (width <= 0) return 480;
        return Math.Clamp(width * 9.0 / 16.0, 320, 520);
    }
    private void UpdateInfo()
    {
        try
        {
            InfoDevice.Text = (VideoBox.SelectedItem as DeviceItem)?.Name ?? "—";
            var r = GetSelRes();
            var f = GetSelFps();
            InfoMode.Text = $"{r.W}x{r.H} @ {f} fps · {((ComboBoxItem)FormatBox.SelectedItem).Content}";
            InfoAudio.Text = (AudioBox.SelectedItem as DeviceItem)?.Name ?? "—";
        }
        catch { }
    }


    // ---------- modos reais da placa ----------
    private void PopulateFallbackModes()
    {
        if (ResBox.Items.Count > 0 && FpsBox.Items.Count > 0) return;
        _capRes = ResList.Select(r => (r.W, r.H)).ToList();
        ResBox.Items.Clear();
        foreach (var r in _capRes)
            ResBox.Items.Add(new ComboBoxItem { Content = $"{r.W}x{r.H}", Tag = $"{r.W}x{r.H}" });
        ResBox.SelectedIndex = 0;
        _capFps = FpsList.ToList();
        FpsBox.Items.Clear();
        foreach (var fp in _capFps)
            FpsBox.Items.Add(new ComboBoxItem { Content = fp.ToString(), Tag = fp.ToString() });
        FpsBox.SelectedIndex = 2;
    }
    private async Task LoadModesAsync()
    {
        int token = ++_modesToken;
        _modes = null; _capRes = null; _capFps = null; _capFmts = new List<string>();
        PopulateFallbackModes();
        if (!File.Exists(ExePath)) return;
        if (VideoBox.SelectedItem is not DeviceItem vd) return;

        string output = await RunEngineAsync("--caps", vd.Index.ToString());
        // Várias chamadas podem sobrepor-se (mudanças de dispositivo/definições):
        // só a mais recente aplica o resultado.
        if (token != _modesToken) return;
        var modes = new List<(uint W, uint H, uint Fps)>();
        foreach (var raw in output.Split('\n'))
        {
            var t = raw.Trim().Split('\t');
            if (t.Length < 5 || t[0] != "M") continue;
            if (!uint.TryParse(t[1], out var w) || !uint.TryParse(t[2], out var h) ||
                !uint.TryParse(t[3], out var fp)) continue;
            var fmtName = t.Length > 4 ? t[4].Trim().ToLowerInvariant() : "";
            if (fmtName.Length > 0 && _capFmts != null && !_capFmts.Contains(fmtName)) _capFmts.Add(fmtName);
            modes.Add((w, h, fp));
        }
        if (modes.Count == 0)
        {
            Log("Modos: a placa não reportou modos; a manter a lista genérica.");
            return;
        }
        _modes = modes;
        _capRes = modes.Select(m => (m.W, m.H)).Distinct()
                       .OrderByDescending(r => (long)r.W * r.H).ToList();
        var cur = GetSelRes();
        ResBox.Items.Clear();
        foreach (var r in _capRes)
            ResBox.Items.Add(new ComboBoxItem { Content = $"{r.W}x{r.H}", Tag = $"{r.W}x{r.H}" });
        int idx = _capRes.IndexOf(cur);
        ResBox.SelectedIndex = idx >= 0 ? idx : 0;
        if (!string.IsNullOrEmpty(_savedResText))
        {
            for (int i = 0; i < ResBox.Items.Count; i++)
                if ((ResBox.Items[i] as ComboBoxItem)?.Content?.ToString() == _savedResText) { ResBox.SelectedIndex = i; break; }
            _savedResText = "";
        }
        UpdateFpsForRes();
        RebuildFormatList();
        if (!string.IsNullOrEmpty(_savedFpsText))
        {
            for (int i = 0; i < FpsBox.Items.Count; i++)
                if ((FpsBox.Items[i] as ComboBoxItem)?.Content?.ToString() == _savedFpsText) { FpsBox.SelectedIndex = i; break; }
            _savedFpsText = "";
        }
        Log($"Modos de '{vd.Name}': {_capRes.Count} resoluções.");
    }

    private void UpdateFpsForRes()
    {
        var res = GetSelRes();
        var devFps = _modes?.Where(m => m.W == res.W && m.H == res.H)
                             .Select(m => m.Fps).Distinct().ToList() ?? new List<uint>();
        var fpsList = devFps.Union(FpsList).OrderBy(x => x).ToList();
        if (fpsList.Count == 0) return;
        _capFps = fpsList;
        var cur = GetSelFps();
        FpsBox.Items.Clear();
        foreach (var fp in fpsList)
            FpsBox.Items.Add(new ComboBoxItem { Content = fp.ToString(), Tag = fp.ToString() });
        int idx = fpsList.IndexOf(cur);
        FpsBox.SelectedIndex = idx >= 0 ? idx : 0;
    }


    private void RebuildFormatList()
    {
        if (FormatBox == null) return;
        var cur = (FormatBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        if (!string.IsNullOrEmpty(_savedFmtText)) { cur = _savedFmtText; _savedFmtText = ""; }
        var items = new List<string> { "Auto" };
        if (_capFmts != null)
            foreach (var f in _capFmts) items.Add(f.ToUpperInvariant());
        if (items.Count == 1)
            items.AddRange(new[] { "NV12", "MJPEG", "YUY2", "RGB32" });
        FormatBox.Items.Clear();
        foreach (var it in items)
            FormatBox.Items.Add(new ComboBoxItem { Content = it });
        int idx = items.FindIndex(s => string.Equals(s, cur, StringComparison.OrdinalIgnoreCase));
        FormatBox.SelectedIndex = idx >= 0 ? idx : 0;
        Log("Formatos: " + string.Join(", ", items));
    }

    private string GetSelFormat()
    {
        if (FormatBox?.SelectedIndex <= 0) return "auto";
        var txt = (FormatBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "auto";
        return txt.ToLowerInvariant();
    }
    private (uint W, uint H) GetSelRes()
    {
        if (_capRes != null && ResBox.SelectedIndex >= 0 && ResBox.SelectedIndex < _capRes.Count)
            return _capRes[ResBox.SelectedIndex];
        var r = ResList[Math.Max(0, ResBox.SelectedIndex)];
        return (r.W, r.H);
    }

    private uint GetSelFps()
    {
        if (_capFps != null && FpsBox.SelectedIndex >= 0 && FpsBox.SelectedIndex < _capFps.Count)
            return _capFps[FpsBox.SelectedIndex];
        return FpsList[Math.Max(0, FpsBox.SelectedIndex)];
    }
    private void RestartPreviewIfActive()
    {
        UpdateInfo();
        if (_pvProc == null) return;
        StopPreview();
        BtnPreview_Click(this, new RoutedEventArgs());
    }    private async Task PreviewVideoLoop(CancellationToken ct)
    {
        TcpClient? tcp = null;
        for (int i = 0; i < 150 && !ct.IsCancellationRequested; i++)
        {
            try { tcp = new TcpClient(); await tcp.ConnectAsync("127.0.0.1", 45901); break; }
            catch { await Task.Delay(200); tcp?.Dispose(); tcp = null; }
        }
        if (tcp == null)
        {
            DispatcherQueue.TryEnqueue(() => ShowPreviewMessage("Sem ligação ao motor (dispositivo ocupado?)"));
            return;
        }
        _pvVideo = tcp;
        var s = tcp.GetStream();
        var hdr = new byte[12];
        int frames = 0;
        while (!ct.IsCancellationRequested)
        {
            if (!await ReadExactAsync(s, hdr, 12, ct)) break;
            int w = BitConverter.ToInt32(hdr, 0);
            int h = BitConverter.ToInt32(hdr, 4);
            int len = BitConverter.ToInt32(hdr, 8);
            if (w <= 0 || h <= 0 || len != w * h * 4) break;
            var buf = new byte[len];
            if (!await ReadExactAsync(s, buf, len, ct)) break;
            frames++;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_pvBmp == null || _pvBmp.PixelWidth != w || _pvBmp.PixelHeight != h)
                    _pvBmp = new WriteableBitmap(w, h);
                using (var st = _pvBmp.PixelBuffer.AsStream())
                    st.Write(buf, 0, buf.Length);
                _pvBmp.Invalidate();
                PreviewImage.Source = _pvBmp;
                FsImage.Source = _pvBmp;
                HidePreviewLoading();
                });
        }
    }


    // ---------- volume do preview ----------
    private bool _volSync;

    private void VolSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        => ApplyVolume((float)(e.NewValue / 100.0), VolSlider);

    private void FsVolSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        => ApplyVolume((float)(e.NewValue / 100.0), FsVolSlider);


    private double _lastVolume = 0.8;

    private void BtnMute_Click(object sender, RoutedEventArgs e) => ToggleMute();

    private void FsMute_Click(object sender, RoutedEventArgs e) => ToggleMute();

    private void ToggleMute()
    {
        try
        {
            double v = VolSlider.Value / 100.0;
            if (v > 0.001) { _lastVolume = v; ApplyVolume(0f, VolSlider); }
            else { ApplyVolume((float)_lastVolume, VolSlider); }
            UpdateMuteIcons();
        }
        catch { }
    }

    private void UpdateMuteIcons()
    {
        string glyph = VolSlider.Value > 0.5 ? "\uE767" : "\uE74F";
        if (MuteIcon != null) MuteIcon.Glyph = glyph;
        if (FsMuteIcon != null) FsMuteIcon.Glyph = glyph;
    }
    private void ApplyVolume(float v, Slider src)
    {
        if (_volSync) return;
        _volSync = true;
        try
        {
            if (_waveOut != null) _waveOut.Volume = Math.Clamp(v, 0f, 1f);
            if (src != VolSlider && VolSlider != null && Math.Abs(VolSlider.Value - v * 100) > 0.5) VolSlider.Value = v * 100;
            if (src != FsVolSlider && FsVolSlider != null && Math.Abs(FsVolSlider.Value - v * 100) > 0.5) FsVolSlider.Value = v * 100;
            var pct = (int)Math.Round(Math.Clamp(v, 0, 1) * 100) + " %";
            if (VolText != null) VolText.Text = pct;
            if (FsVolText != null) FsVolText.Text = pct;
            UpdateMuteIcons();
        }
        finally { _volSync = false; }
    }
    private async Task PreviewAudioLoop(CancellationToken ct)
    {
        TcpClient? tcp = null;
        for (int i = 0; i < 150 && !ct.IsCancellationRequested; i++)
        {
            try { tcp = new TcpClient(); await tcp.ConnectAsync("127.0.0.1", 45902); break; }
            catch { await Task.Delay(200); tcp?.Dispose(); tcp = null; }
        }
        if (tcp == null) { DispatcherQueue.TryEnqueue(() => Log("Preview: sem ligacao ao audio (porta 45902)")); return; }
        _pvAudio = tcp;
        DispatcherQueue.TryEnqueue(() => Log("Preview: audio ligado"));

        var s = tcp.GetStream();
        var buf = new byte[8192];
        while (!ct.IsCancellationRequested)
        {
            int n;
            try { n = await s.ReadAsync(buf.AsMemory(0, buf.Length), ct); }
            catch { break; }
            if (n <= 0) break;
            if (_audioProvider != null) _audioProvider.AddSamples(buf, 0, n);
            int peak = 0;
            for (int i = 0; i + 1 < n; i += 2)
            {
                short sample = (short)(buf[i] | (buf[i + 1] << 8));
                int abs = sample == short.MinValue ? 32768 : Math.Abs((int)sample);
                if (abs > peak) peak = abs;
            }
            float pdb = peak > 0 ? (float)(20 * Math.Log10(peak / 32768.0)) : -60f;
            DispatcherQueue.TryEnqueue(() => UpdateLevel(pdb));
            if (!_audioLogged && n > 0) { _audioLogged = true; DispatcherQueue.TryEnqueue(() => Log("Preview: a receber audio (" + n + " bytes)")); }
        }
    }

    private static async Task<bool> ReadExactAsync(NetworkStream s, byte[] buf, int len, CancellationToken ct)
    {
        int off = 0;
        while (off < len)
        {
            int n = await s.ReadAsync(buf.AsMemory(off, len - off), ct);
            if (n <= 0) return false;
            off += n;
        }
        return true;
    }

    private void ShowPreviewLoading(string text)
    {
        PreviewLoadingPanel.Visibility = Visibility.Visible;
        PreviewHint.Visibility = Visibility.Visible;
        PreviewHint.Text = text;
        PreviewSpinner.IsActive = true;
    }

    private void ShowPreviewMessage(string text)
    {
        PreviewLoadingPanel.Visibility = Visibility.Visible;
        PreviewHint.Visibility = Visibility.Visible;
        PreviewHint.Text = text;
        PreviewSpinner.IsActive = false;
    }

    private void HidePreviewLoading()
    {
        PreviewSpinner.IsActive = false;
        PreviewLoadingPanel.Visibility = Visibility.Collapsed;
    }

    private void StopPreview()
    {
        try { _pvCts?.Cancel(); } catch { }
        try { _waveOut?.Stop(); _waveOut?.Dispose(); _waveOut = null; } catch { }
        try { _pvVideo?.Close(); _pvVideo = null; } catch { }
        try { _pvAudio?.Close(); _pvAudio = null; } catch { }
        try { if (_pvProc != null && !_pvProc.HasExited) _pvProc.Kill(); } catch { }
        _pvProc?.Dispose();
        _pvProc = null;
        _pvBmp = null;
        if (PreviewBtnText != null) PreviewBtnText.Text = Loc.T("Pré-visualizar"); else BtnPreview.Content = Loc.T("Pré-visualizar");
        PreviewImage.Source = null;
        ShowPreviewMessage("Sem preview");
    }
    // -----------------------------------------------------------------------
    // Handlers de eventos da UI
    // -----------------------------------------------------------------------


    private void BtnSettings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private void ShowSettings()
    {
        // Aproveita a altura da janela: o cartão Vídeo tem agora as opções do
        // codificador e, a 640 px, ficavam abaixo da dobra.
        try
        {
            double h = RootGrid.ActualHeight;
            if (h > 400) SettingsPanel.Height = Math.Clamp(h - 80, 560, 860);
        }
        catch { }
        FolderBox2.Text = FolderBox.Text;
        FileTemplateBox.Text = _fileTemplate;
        UpdateUrlBox.Text = _updateUrl;
        UpdateAutoSwitch.IsOn = _updateAuto;
        UpdateCurrentText.Text = Loc.T("Versão instalada:") + " " + AppVersion;
        UpdateStatusText.Text = "";
        FormatBox2.SelectedIndex = Math.Max(0, FormatBox.SelectedIndex);
        ContBox2.SelectedIndex = Math.Max(0, ContBox.SelectedIndex);
        SplitBox2.SelectedIndex = Math.Max(0, SplitBox.SelectedIndex);
        BackdropBox.SelectedIndex = Math.Max(0, _backdrop);
        AccentBox.SelectedIndex = Math.Max(0, _accent);
        ThemeBox.SelectedIndex = Math.Max(0, _theme);
        AutoPrevSwitch.IsOn = _autoPreview;
        ShowInfoSwitch.IsOn = _showInfo;
        VolSlider2.Value = Math.Clamp(_previewVolume, 0, 1) * 100;
        if (LanguageBox2 != null) LanguageBox2.SelectedIndex = Loc.Lang == "en" ? 1 : 0;
        CodecBox2.SelectedIndex = Math.Max(0, CodecBox.SelectedIndex);
        BitrateBox2.SelectedIndex = Math.Max(0, BitrateBox.SelectedIndex);
        HwBox2.SelectedIndex = Math.Max(0, HwBox.SelectedIndex);
        DurBox2.SelectedIndex = Math.Max(0, DurBox.SelectedIndex);
        AudioModeBox2.SelectedIndex = Math.Max(0, AudioModeBox.SelectedIndex);
        AbrBox2.SelectedIndex = Math.Max(0, AbrBox.SelectedIndex);
        AudioRateBox2.SelectedIndex = Math.Max(0, AudioRateBox.SelectedIndex);
        AudioChannelsBox2.SelectedIndex = Math.Max(0, AudioChannelsBox.SelectedIndex);
        AudioBitsBox2.SelectedIndex = Math.Max(0, AudioBitsBox.SelectedIndex);
        BuildEncoderList();
        EncoderBox2.SelectedIndex = Math.Clamp(_videoEncoder, 0, EncoderBox2.Items.Count - 1);
        RateBox2.SelectedIndex = Math.Max(0, RateBox.SelectedIndex);
        GopBox2.Value = Math.Clamp(GopBox.Value, 0, 30);
        PresetBox2.SelectedIndex = Math.Max(0, PresetBox.SelectedIndex);
        TuneBox2.SelectedIndex = Math.Max(0, TuneBox.SelectedIndex);
        BframesBox2.SelectedIndex = Math.Max(0, BframesBox.SelectedIndex);
        MultipassBox2.SelectedIndex = Math.Max(0, MultipassBox.SelectedIndex);
        CqBox2.SelectedIndex = Math.Max(0, CqBox.SelectedIndex);
        UpdateEncoderFields2();

        var asm = typeof(MainWindow).Assembly;
        string ver = asm.GetName().Version?.ToString(3) ?? "1.0.0";
        string build = File.GetLastWriteTime(asm.Location).ToString("dd/MM/yyyy");

        AboutText.Text = Loc.T("Captura Studio é um gravador de captura de vídeo nativo para Windows, de código aberto, escrito em Rust e WinUI 3.");
        VersionText.Text = Loc.T("Versão") + " " + ver + "  ·  " + Loc.T("Compilação") + " " + build;
        LicenseText.Text = Loc.T("Licença MIT · código aberto — podes usar, modificar e distribuir, mantendo o aviso de copyright.");
        CreditsText.Text = Loc.T("© 2026 OliveTree · Feito em Portugal com amor e carinho ❤️");

        BuildTechCards(TechList);
        BuildContributionCards(ModelsList);

        SettingsNav.SelectedIndex = 0;
        SettingsEditActions.Visibility = Visibility.Visible;
        SettingsAboutActions.Visibility = Visibility.Collapsed;
        SettingsOverlay.Visibility = Visibility.Visible;
    }

    private void BuildTable(StackPanel target, string[] headers, string[][] rows, double[] widths)
    {
        var secondary = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        var primary = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        var divider = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
        target.Children.Clear();

        Grid MakeGrid(string[] cells, bool header)
        {
            var g = new Grid { ColumnSpacing = 14, Padding = new Thickness(0, 7, 0, 7) };
            for (int i = 0; i < widths.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[i], GridUnitType.Star) });
            for (int i = 0; i < cells.Length && i < widths.Length; i++)
            {
                var tb = new TextBlock
                {
                    Text = cells[i],
                    FontSize = header ? 11 : 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = header ? secondary : primary
                };
                if (header) tb.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                Grid.SetColumn(tb, i);
                g.Children.Add(tb);
            }
            return g;
        }

        target.Children.Add(MakeGrid(headers, true));
        target.Children.Add(new Border { Height = 1, Background = divider });
        foreach (var r in rows)
        {
            target.Children.Add(MakeGrid(r, false));
            target.Children.Add(new Border { Height = 1, Background = divider, Opacity = 0.5 });
        }
    }

    private Border MakeInfoCard(string title, string meta, string? subtitle, string body)
    {
        var primary = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        var secondary = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        var fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"];
        var stroke = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = primary
        });
        var m = new TextBlock
        {
            Text = meta,
            FontSize = 11,
            Foreground = secondary,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        Grid.SetColumn(m, 1);
        head.Children.Add(m);

        var sp = new StackPanel { Spacing = 4 };
        sp.Children.Add(head);
        if (!string.IsNullOrEmpty(subtitle))
            sp.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = primary, TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(new TextBlock { Text = body, FontSize = 12, Foreground = secondary, TextWrapping = TextWrapping.Wrap });

        return new Border
        {
            Background = fill,
            BorderBrush = stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Child = sp
        };
    }

    private void BuildTechCards(Grid target)
    {
        var asm = typeof(MainWindow).Assembly;
        string ver = asm.GetName().Version?.ToString(3) ?? "1.0.0";
        string engine = "—";
        try
        {
            engine = (System.Diagnostics.FileVersionInfo.GetVersionInfo(ExePath).FileVersion ?? "").Trim();
            if (engine.Length == 0)
                engine = Loc.T("compilado em") + " " + File.GetLastWriteTime(ExePath).ToString("dd/MM/yyyy HH:mm");
        }
        catch { }
        var rows = new (string Name, string Ver, string Role)[]
        {
            ("Captura Studio", ver, "Aplicação (WinUI 3)"),
            ("Windows App SDK", "1.7.260224002", "Interface Fluent"),
            (".NET", "8.0", "Runtime"),
            ("captura-rec (Rust)", engine, "Captura e codificação via Media Foundation"),
            ("NAudio", "2.2.1", "Reprodução do áudio do preview"),
            ("FFmpeg", _ffmpegVersion ?? Loc.T("não encontrado"), "MP3/FLAC e remux"),
            ("Media Foundation", "Windows", "Captura, cores e codecs")
        };
        target.Children.Clear();
        target.ColumnDefinitions.Clear();
        target.RowDefinitions.Clear();
        target.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        target.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < (rows.Length + 1) / 2; i++)
            target.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < rows.Length; i++)
        {
            var card = MakeInfoCard(rows[i].Name, rows[i].Ver, null, Loc.T(rows[i].Role));
            card.Padding = new Thickness(12, 9, 12, 9);
            Grid.SetRow(card, i / 2);
            Grid.SetColumn(card, i % 2);
            target.Children.Add(card);
        }
    }

    private void BuildContributionCards(StackPanel target)
    {
        var entries = new (string Provider, string Scope, string Identity, string Details)[]
        {
            ("DeepSeek", "Motor + pipeline raw/FFmpeg", "DeepSeek Harness · DeepSeek-V4.1-Flash · 4.1",
                "Implementação principal: motor Rust/Media Foundation e pipeline raw NV12 + FFmpeg (opção B); áudio WAV e sincronização A/V; preview em processo; painel do codificador; persistência de definições e idioma; interface WinUI 3 base; cores Rec.709/2020; resolução/FPS lidos da placa."),
            ("Anthropic", "Revisão · sem código", "Antigravity (Google) · Claude Sonnet 4.6 (Thinking) · 4.6",
                "Revisão do handoff e diagnóstico da falha de arranque WinUI relacionada com PRI/XamlParseException."),
            ("OpenAI", "Interface + UVC03 (raw)", "Codex · GPT-6",
                "Interface: cartões organizados para destino, vídeo e áudio; Definições agrupadas; página Sobre, volume, ações por separador e traduções revistas. UVC03 no pipeline raw/FFmpeg: fila limitada a 2 frames, saída CFR, contagem de frames recebidos/descartados/repetidos, espera até 40 s pelo primeiro frame e deteção de pausa após 2,5 s; até duas reaberturas do leitor, com aviso na UI e fecho da gravação se não recuperar.")
        };
        target.Children.Clear();
        foreach (var e in entries)
            target.Children.Add(MakeInfoCard(e.Provider, Loc.T(e.Scope), e.Identity, Loc.T(e.Details)));
    }
    private void SettingsNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsOverlay == null || SecGravacao == null) return;
        int i = Math.Max(0, SettingsNav.SelectedIndex);
        SecGravacao.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
        SecAparencia.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed;
        SecSobre.Visibility = i == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (SettingsEditActions != null)
            SettingsEditActions.Visibility = i == 2 ? Visibility.Collapsed : Visibility.Visible;
        if (SettingsAboutActions != null)
            SettingsAboutActions.Visibility = i == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SettingsVolume_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (SettingsVolumeValue != null)
            SettingsVolumeValue.Text = $"{Math.Round(e.NewValue):0} %";
    }

    private void SettingsCancel_Click(object sender, RoutedEventArgs e)
        => SettingsOverlay.Visibility = Visibility.Collapsed;

    private void SettingsSave_Click(object sender, RoutedEventArgs e)
    {
        FolderBox.Text = FolderBox2.Text;
        _fileTemplate = string.IsNullOrWhiteSpace(FileTemplateBox.Text)
            ? "Captura_{data}_{hora}" : FileTemplateBox.Text.Trim();
        _updateUrl = UpdateUrlBox.Text.Trim();
        _updateAuto = UpdateAutoSwitch.IsOn;
        FormatBox.SelectedIndex = Math.Max(0, FormatBox2.SelectedIndex);
        ContBox.SelectedIndex = Math.Max(0, ContBox2.SelectedIndex);
        SplitBox.SelectedIndex = Math.Max(0, SplitBox2.SelectedIndex);
        _backdrop = Math.Max(0, BackdropBox.SelectedIndex);
        _accent = Math.Max(0, AccentBox.SelectedIndex);
        _theme = Math.Max(0, ThemeBox.SelectedIndex);
        _autoPreview = AutoPrevSwitch.IsOn;
        _showInfo = ShowInfoSwitch.IsOn;
        CodecBox.SelectedIndex = Math.Max(0, CodecBox2.SelectedIndex);
        BitrateBox.SelectedIndex = Math.Max(0, BitrateBox2.SelectedIndex);
        HwBox.SelectedIndex = Math.Max(0, HwBox2.SelectedIndex);
        DurBox.SelectedIndex = Math.Max(0, DurBox2.SelectedIndex);
        AudioModeBox.SelectedIndex = Math.Max(0, AudioModeBox2.SelectedIndex);
        AbrBox.SelectedIndex = Math.Max(0, AbrBox2.SelectedIndex);
        AudioRateBox.SelectedIndex = Math.Max(0, AudioRateBox2.SelectedIndex);
        AudioChannelsBox.SelectedIndex = Math.Max(0, AudioChannelsBox2.SelectedIndex);
        AudioBitsBox.SelectedIndex = Math.Max(0, AudioBitsBox2.SelectedIndex);
        RateBox.SelectedIndex = Math.Max(0, RateBox2.SelectedIndex);
        GopBox.Value = Math.Clamp(GopBox2.Value, 0, 30);
        PresetBox.SelectedIndex = Math.Max(0, PresetBox2.SelectedIndex);
        TuneBox.SelectedIndex = Math.Max(0, TuneBox2.SelectedIndex);
        BframesBox.SelectedIndex = Math.Max(0, BframesBox2.SelectedIndex);
        MultipassBox.SelectedIndex = Math.Max(0, MultipassBox2.SelectedIndex);
        CqBox.SelectedIndex = Math.Max(0, CqBox2.SelectedIndex);
        _videoEncoder = Math.Clamp(EncoderBox2.SelectedIndex, 0, 4);
        UpdateAudioFields();
        UpdateVideoFields();
        if (LanguageBox2 != null)
        {
            Loc.Lang = LanguageBox2.SelectedIndex == 1 ? "en" : "pt";
            try { Loc.Apply(RootGrid); } catch { }
        }
        _previewVolume = Math.Clamp(VolSlider2.Value / 100.0, 0, 1);
        ApplyAppearance();
        try { VolSlider.Value = Math.Clamp(_previewVolume, 0, 1); FsVolSlider.Value = VolSlider.Value; } catch { }
        try { InfoDevice.Visibility = _showInfo ? Visibility.Visible : Visibility.Collapsed; } catch { }
        SaveSettings();
        SettingsOverlay.Visibility = Visibility.Collapsed;
        Log("Definições guardadas.");
    }

    private async void BrowseMainFolder_Click(object sender, RoutedEventArgs e)
    {
        await PickFolderAsync(FolderBox);
    }

    private async void SettingsBrowse_Click(object sender, RoutedEventArgs e)
    {
        await PickFolderAsync(FolderBox2);
    }

    private async Task PickFolderAsync(TextBox target)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add("*");
            var f = await picker.PickSingleFolderAsync();
            if (f != null) target.Text = f.Path;
        }
        catch (Exception ex) { Log("pasta: " + ex.Message); }
    }

    private void SettingsReset_Click(object sender, RoutedEventArgs e)
    {
        FolderBox2.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", "Captura Studio");
        FileTemplateBox.Text = "Captura_{data}_{hora}";
        UpdateUrlBox.Text = UpdateDefaults.ManifestUrl;
        UpdateAutoSwitch.IsOn = true;
        FormatBox2.SelectedIndex = 0;
        ContBox2.SelectedIndex = 0;
        SplitBox2.SelectedIndex = 0;
        BackdropBox.SelectedIndex = 0;
        AccentBox.SelectedIndex = 0;
        ThemeBox.SelectedIndex = 0;
        AutoPrevSwitch.IsOn = false;
        ShowInfoSwitch.IsOn = true;
        VolSlider2.Value = 80;
        CodecBox2.SelectedIndex = 0;
        BitrateBox2.SelectedIndex = 2;
        HwBox2.SelectedIndex = 0;
        DurBox2.SelectedIndex = 0;
        AudioModeBox2.SelectedIndex = 1;
        AbrBox2.SelectedIndex = 3;
        AudioRateBox2.SelectedIndex = 1;
        AudioChannelsBox2.SelectedIndex = 0;
        AudioBitsBox2.SelectedIndex = 0;
        EncoderBox2.SelectedIndex = 0;
        RateBox2.SelectedIndex = 0;
        GopBox2.Value = 2;
        PresetBox2.SelectedIndex = 4;
        TuneBox2.SelectedIndex = 0;
        BframesBox2.SelectedIndex = 3;
        MultipassBox2.SelectedIndex = 0;
        CqBox2.SelectedIndex = 3;
        UpdateEncoderFields2();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Directory.Exists(FolderBox.Text)) Directory.CreateDirectory(FolderBox.Text);
            Process.Start("explorer.exe", "\"" + FolderBox.Text + "\"");
        }
        catch (Exception ex) { Log("abrir pasta: " + ex.Message); }
    }

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(SettingsPath) + "\""); }
        catch (Exception ex) { Log("abrir definições: " + ex.Message); }
    }

    private async void OpenEncoders_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var psi = new ProcessStartInfo(ExePath, "--encoders")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var pr = Process.Start(psi);
            if (pr == null) return;
            string text = await pr.StandardOutput.ReadToEndAsync();
            var sp = new StackPanel { Spacing = 4, MinWidth = 520 };
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim().Split('\t');
                if (t.Length < 4 || t[0] != "E") continue;
                sp.Children.Add(new TextBlock { Text = $"{t[1]}  ·  {t[2]}  ·  {t[3]}", FontSize = 12, TextWrapping = TextWrapping.Wrap });
            }
            var dlg = new ContentDialog { Title = "Encoders disponíveis", Content = sp, CloseButtonText = "Fechar", XamlRoot = Content.XamlRoot };
            await dlg.ShowAsync();
        }
        catch (Exception ex) { Log("encoders: " + ex.Message); }
    }

    private void OpenFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://www.ffmpeg.org/download.html") { UseShellExecute = true }); }
        catch (Exception ex) { Log("ffmpeg.org: " + ex.Message); }
    }

    private void OpenLicense_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var here = AppContext.BaseDirectory;
            var cands = new[] { Path.Combine(here, "LICENSE"), Path.Combine(here, "..", "..", "..", "..", "LICENSE") };
            foreach (var p in cands)
            {
                var full = Path.GetFullPath(p);
                if (File.Exists(full)) { Process.Start("notepad.exe", "\"" + full + "\""); return; }
            }
            Log("LICENSE não encontrado junto da app.");
        }
        catch (Exception ex) { Log("licença: " + ex.Message); }
    }

    private void CodecBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateVideoFields();

    private void AudioModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAudioFields();

    private async void DetectHardwareEncoder()
    {
        try
        {
            string text = await RunEngineAsync("--encoders");
            string name = "GPU (hardware)";
            foreach (var line in text.Split('\n'))
            {
                var t = line.Trim().Split('\t');
                if (t.Length < 4 || t[0] != "E" || t[1] != "H.264" || t[3] != "HW") continue;
                if (t[2].Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) { name = "GPU · NVENC"; break; }
                if (t[2].Contains("DX12", StringComparison.OrdinalIgnoreCase)) name = "GPU · AVC DX12";
            }
            DispatcherQueue.TryEnqueue(() =>
            {
                if (HwBox?.Items.Count > 0 && HwBox.Items[0] is ComboBoxItem it) it.Content = name;
            });
        }
        catch { }
    }
    private void DetectFfmpeg()
    {
        var cands = new List<string>();
        cands.Add(Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe"));
        cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CapturaStudio", "ffmpeg", "ffmpeg.exe"));
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try { cands.Add(Path.Combine(dir.Trim(), "ffmpeg.exe")); } catch { }
        }
        cands.AddRange(new[]
        {
            @"C:\ffmpeg\bin\ffmpeg.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Kdenlive\bin\ffmpeg.exe"),
            @"C:\Program Files\MediaHuman\Audio Converter\ffmpeg.exe",
            @"C:\Program Files\KeepStreams\KeepStreams\ffmpeg.exe"
        });
        _ffmpegPath = cands.FirstOrDefault(File.Exists);
        if (_ffmpegPath != null)
        {
            try
            {
                var psi = new ProcessStartInfo(_ffmpegPath, "-hide_banner -version")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
                };
                using var p = Process.Start(psi);
                string line = p?.StandardOutput.ReadLine() ?? "";
                p?.WaitForExit();
                var m = System.Text.RegularExpressions.Regex.Match(line, @"version\s+(\S+)");
                _ffmpegVersion = m.Success ? m.Groups[1].Value : line.Trim();
            }
            catch { }
        }
        Log(_ffmpegPath != null
            ? $"FFmpeg {_ffmpegVersion ?? "?"}: {_ffmpegPath}"
            : "FFmpeg não encontrado (MP3/FLAC indisponíveis).");
    }

    private void TranscodeAudio(string file, string codec)
    {
        try
        {
            if (_ffmpegPath == null) return;
            var tmp = Path.Combine(Path.GetDirectoryName(file) ?? "",
                Path.GetFileNameWithoutExtension(file) + ".tmp" + Path.GetExtension(file));
            int kbps = (int)Abr[Math.Max(0, AbrBox.SelectedIndex)];
            var codecArgs = codec switch
            {
                "flac" => "-c:a flac",
                "opus" => $"-c:a libopus -b:a {kbps}k",
                _ => $"-c:a libmp3lame -b:a {kbps}k"
            };
            var args = $"-y -hide_banner -i \"{file}\" -map 0 -c:v copy {codecArgs} \"{tmp}\"";
            var psi = new ProcessStartInfo(_ffmpegPath, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true
            };
            using var p = Process.Start(psi);
            if (p == null) return;
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode == 0 && File.Exists(tmp))
            {
                File.Delete(file);
                File.Move(tmp, file);
                DispatcherQueue.TryEnqueue(() => Log($"Áudio convertido para {codec.ToUpperInvariant()}: {file}"));
            }
            else
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                var last = err.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
                DispatcherQueue.TryEnqueue(() => Log("FFmpeg falhou: " + last.Trim()));
            }
        }
        catch (Exception ex) { DispatcherQueue.TryEnqueue(() => Log("FFmpeg: " + ex.Message)); }
    }

    private void UpdateAudioFields()
    {
        if (AudioModeBox == null || AbrBox == null || AudioBitsBox == null ||
            AudioRateBox == null || AudioChannelsBox == null) return;
        int m = AudioModeBox.SelectedIndex; // 0 sem áudio, 1 AAC, 2 PCM, 3 MP3, 4 FLAC, 5 Opus
        bool any = m != 0;
        try
        {
            AbrBox.IsEnabled = m == 1 || m == 3 || m == 5;
            AudioBitsBox.IsEnabled = m == 2 || m == 3 || m == 4 || m == 5;
            AudioRateBox.IsEnabled = any;
            AudioChannelsBox.IsEnabled = any;
            AbrBox.Opacity = AbrBox.IsEnabled ? 1.0 : 0.45;
            AudioBitsBox.Opacity = AudioBitsBox.IsEnabled ? 1.0 : 0.45;
        }
        catch { }
    }

    private void UpdateVideoFields()
    {
        if (CodecBox == null || ProfileBox == null) return;
        bool h264 = CodecBox.SelectedIndex == 0;
        try
        {
            ProfileBox.IsEnabled = h264;
            ProfileBox.Opacity = h264 ? 1.0 : 0.45;
        }
        catch { }
        UpdateEncoderFields();
        UpdateEncoderFields2();
    }

    // -----------------------------------------------------------------------
    // Painel do codificador (opções passadas ao FFmpeg / MF)
    // -----------------------------------------------------------------------

    private (string Enc, bool Nvenc, bool Hevc) ResolveEncoder(bool logFallback = false)
    {
        int choice = Math.Clamp(_videoEncoder, 0, 4);
        bool wantHevc = CodecBox.SelectedIndex == 1;
        bool gpu = HwBox.SelectedIndex == 0;
        switch (choice)
        {
            case 1 when _hasNvencH264 != false: return ("h264_nvenc", true, false);
            case 2 when _hasNvencHevc != false: return ("hevc_nvenc", true, true);
            case 3 when _hasX264 != false: return ("libx264", false, false);
            case 4 when _hasX265 != false: return ("libx265", false, true);
            case 0: break;
            default:
                if (logFallback) Log("Codificador escolhido indisponível; a usar o Automático.");
                break;
        }
        bool nvenc = gpu && (wantHevc ? _hasNvencHevc != false : _hasNvencH264 != false);
        if (nvenc) return (wantHevc ? "hevc_nvenc" : "h264_nvenc", true, wantHevc);
        return (wantHevc ? "libx265" : "libx264", false, wantHevc);
    }

    private void UpdateEncoderFields()
    {
        if (RateBox == null || BitrateBox == null || CqBox == null || TuneBox == null ||
            MultipassBox == null || QualityHint == null) return;
        int rate = Math.Max(0, RateBox.SelectedIndex);
        bool lossless = rate == 4;
        bool cq = rate == 1 || rate == 3;
        bool br = rate == 0 || rate == 2 || rate == 3;
        BitrateBox.IsEnabled = br;
        BitrateBox.Opacity = br ? 1.0 : 0.45;
        CqBox.IsEnabled = cq;
        CqBox.Opacity = cq ? 1.0 : 0.45;
        TuneBox.IsEnabled = !lossless;
        TuneBox.Opacity = lossless ? 0.45 : 1.0;
        bool nvenc = ResolveEncoder().Nvenc;
        MultipassBox.IsEnabled = nvenc && !lossless;
        MultipassBox.Opacity = MultipassBox.IsEnabled ? 1.0 : 0.45;
        QualityHint.Opacity = cq ? 1.0 : 0.55;
    }

    private void UpdateEncoderFields2()
    {
        if (RateBox2 == null || BitrateBox2 == null || CqBox2 == null || TuneBox2 == null ||
            MultipassBox2 == null) return;
        int rate = Math.Max(0, RateBox2.SelectedIndex);
        bool lossless = rate == 4;
        bool cq = rate == 1 || rate == 3;
        bool br = rate == 0 || rate == 2 || rate == 3;
        BitrateBox2.IsEnabled = br;
        BitrateBox2.Opacity = br ? 1.0 : 0.45;
        CqBox2.IsEnabled = cq;
        CqBox2.Opacity = cq ? 1.0 : 0.45;
        TuneBox2.IsEnabled = !lossless;
        TuneBox2.Opacity = lossless ? 0.45 : 1.0;
        int choice = EncoderBox2?.SelectedIndex ?? 0;
        bool nvenc = choice == 1 || choice == 2 ||
                     (choice == 0 && ResolveEncoder().Nvenc);
        MultipassBox2.IsEnabled = nvenc && !lossless;
        MultipassBox2.Opacity = MultipassBox2.IsEnabled ? 1.0 : 0.45;
    }

    private void BuildEncoderList()
    {
        if (EncoderBox2 == null) return;
        int keep = Math.Clamp(_videoEncoder, 0, 4);
        EncoderBox2.Items.Clear();
        EncoderBox2.Items.Add(new ComboBoxItem { Content = Loc.T("Automático (segue o codec)") });
        void Add(string label, bool? available)
        {
            var it = new ComboBoxItem
            {
                Content = Loc.T(label) + (available == false ? " — " + Loc.T("indisponível") : ""),
                IsEnabled = available != false
            };
            EncoderBox2.Items.Add(it);
        }
        Add("NVIDIA NVENC H.264", _hasNvencH264);
        Add("NVIDIA NVENC HEVC", _hasNvencHevc);
        Add("x264 (CPU)", _hasX264);
        Add("x265 (CPU)", _hasX265);
        EncoderBox2.SelectedIndex = keep;
    }

    private async Task DetectFfmpegEncoders()
    {
        if (_ffmpegPath == null)
        {
            _hasNvencH264 = _hasNvencHevc = _hasX264 = _hasX265 = false;
            return;
        }
        try
        {
            var psi = new ProcessStartInfo(_ffmpegPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-encoders");
            using var p = Process.Start(psi);
            if (p == null) return;
            string text = await p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            _hasNvencH264 = text.Contains("h264_nvenc");
            _hasNvencHevc = text.Contains("hevc_nvenc");
            _hasX264 = text.Contains("libx264");
            _hasX265 = text.Contains("libx265");
            DispatcherQueue.TryEnqueue(() =>
            {
                BuildEncoderList();
                UpdateEncoderFields();
                UpdateEncoderFields2();
            });
            Log($"FFmpeg encoders: NVENC H.264={_hasNvencH264}, HEVC={_hasNvencHevc}, x264={_hasX264}, x265={_hasX265}");
        }
        catch (Exception ex) { Log("encoders FFmpeg: " + ex.Message); }
    }
    private void BtnRec_Click(object sender, RoutedEventArgs e)
    {
        if (_proc != null) return;
        bool wasPreview = _pvProc != null;
        if (wasPreview) StopPreview();

        if (!File.Exists(ExePath))
        {
            Log("ERRO: captura-rec.exe não encontrado em " + ExePath);
            ShowNotice(InfoBarSeverity.Error, Loc.T("Erro"),
                       Loc.T("O motor captura-rec.exe não foi encontrado."));
            return;
        }

        var folder = FolderBox.Text.Trim();
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex)
        {
            Log("ERRO pasta: " + ex.Message);
            ShowNotice(InfoBarSeverity.Error, Loc.T("Erro"), ex.Message);
            return;
        }

        int au = AudioModeBox.SelectedIndex;  // 0=none 1=AAC 2=PCM 3=MP3 4=FLAC 5=Opus
        bool pcmCapture = au == 2 || au == 3 || au == 4 || au == 5;
        var ext = pcmCapture ? "mkv" : Exts[Math.Max(0, ContBox.SelectedIndex)];

        // O FPS das combos pode não existir na placa (a lista junta valores genéricos).
        var mode = ResolveCaptureMode(GetSelRes().W, GetSelRes().H, GetSelFps());
        _out = Path.Combine(folder,
            BuildOutputName(_fileTemplate, mode.W, mode.H, mode.Fps) + "." + ext);
        int splitMin = (int)Splits[Math.Max(0, SplitBox.SelectedIndex)];
        // CAPTURA_PIPELINE=mf força o caminho Media Foundation (diagnóstico/fallback).
        bool forceMf = string.Equals(Environment.GetEnvironmentVariable("CAPTURA_PIPELINE"),
                                     "mf", StringComparison.OrdinalIgnoreCase);
        bool useFf = _ffmpegPath != null && splitMin == 0 && !forceMf;
        if (forceMf) Log("Pipeline: Media Foundation (CAPTURA_PIPELINE=mf).");

        _captureReady = false;
        _stopRequested = false;
        _waitStart = DateTime.Now;
        _start = _waitStart;
        _lastFpsUpdate = DateTime.MinValue;
        _captureSourceFps = -1;
        _captureOutputFps = -1;
        _captureTargetFps = (int)mode.Fps;
        _captureDroppedFrames = 0;
        _captureRepeatedFrames = 0;
        _captureRecoveries = 0;
        _captureHealthText = "";
        UpdateLevel(-60);
        try { _notifyTimer.Stop(); NotifyBar.Visibility = Visibility.Collapsed; } catch { }

        if (useFf)
            StartFfmpegRecording(wasPreview, au, mode.W, mode.H, mode.Fps, folder);
        else
            StartMfRecording(wasPreview, au, pcmCapture, mode.W, mode.H, mode.Fps, ext);

        _timer.Start();
        BtnRec.IsEnabled  = false;
        BtnStop.IsEnabled = true;
        StatusText.Text   = Loc.T("A aguardar sinal da placa");
        Log("A aguardar sinal da placa: " + _out);
        BeginRecordingUi(wasPreview, au);
    }

    private (uint W, uint H, uint Fps) ResolveCaptureMode(uint w, uint h, uint fps)
    {
        if (_modes == null || _modes.Count == 0) return (w, h, fps);
        if (_modes.Any(m => m.W == w && m.H == h && m.Fps == fps)) return (w, h, fps);
        var best = _modes.OrderBy(m => Math.Abs((long)m.W * m.H - (long)w * h)).First();
        var cands = _modes.Where(m => m.W == best.W && m.H == best.H).ToList();
        var chosen = cands.OrderBy(m => Math.Abs((long)m.Fps - fps)).First();
        Log($"Modo ajustado: {w}x{h}@{fps} → {chosen.W}x{chosen.H}@{chosen.Fps}");
        return (chosen.W, chosen.H, chosen.Fps);
    }

    private string BuildOutputName(string template, uint w, uint h, uint fps)
    {
        string name = string.IsNullOrWhiteSpace(template) ? "Captura_{data}_{hora}" : template;
        string device = (VideoBox.SelectedItem as DeviceItem)?.Name ?? "placa";
        name = name
            .Replace("{data}", DateTime.Now.ToString("yyyyMMdd"))
            .Replace("{hora}", DateTime.Now.ToString("HHmmss"))
            .Replace("{dispositivo}", device)
            .Replace("{modo}", $"{w}x{h}@{fps}");
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.', ' ');
        return name.Length > 0 ? name : "Captura";
    }

    private void ShowNotice(InfoBarSeverity severity, string title, string message, bool autoClose = true,
                            string? actionText = null, Action? action = null)
    {
        try
        {
            var color = severity switch
            {
                InfoBarSeverity.Success => Windows.UI.Color.FromArgb(255, 46, 125, 50),
                InfoBarSeverity.Warning => Windows.UI.Color.FromArgb(255, 178, 106, 0),
                InfoBarSeverity.Error   => Windows.UI.Color.FromArgb(255, 196, 43, 28),
                _                       => Windows.UI.Color.FromArgb(255, 0, 120, 212)
            };
            NotifyIcon.Glyph = severity switch
            {
                InfoBarSeverity.Success => "\uE73E",
                InfoBarSeverity.Warning => "\uE7BA",
                InfoBarSeverity.Error   => "\uEA39",
                _                       => "\uE946"
            };
            var brush = new SolidColorBrush(color);
            NotifyIcon.Foreground = brush;
            NotifyBar.BorderBrush = brush;
            NotifyTitle.Text = title;
            NotifyMessage.Text = message;
            NotifyMessage.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
            _notifyAction = action;
            NotifyAction.Content = actionText ?? "";
            NotifyAction.Visibility = action != null && !string.IsNullOrWhiteSpace(actionText)
                ? Visibility.Visible : Visibility.Collapsed;
            NotifyBar.Visibility = Visibility.Visible;
            _notifyTimer.Stop();
            _notifyTimer.Interval = severity == InfoBarSeverity.Error
                ? TimeSpan.FromSeconds(15) : TimeSpan.FromSeconds(8);
            if (autoClose) _notifyTimer.Start();
        }
        catch (Exception ex) { Log("notify: " + ex.Message); }
    }

    private void NotifyClose_Click(object sender, RoutedEventArgs e)
    {
        _notifyTimer.Stop();
        _notifyAction = null;
        NotifyBar.Visibility = Visibility.Collapsed;
    }

    private void NotifyAction_Click(object sender, RoutedEventArgs e)
    {
        var action = _notifyAction;
        _notifyAction = null;
        _notifyTimer.Stop();
        NotifyBar.Visibility = Visibility.Collapsed;
        try { action?.Invoke(); } catch (Exception ex) { Log("notify action: " + ex.Message); }
    }

    // -----------------------------------------------------------------------
    // Atualizações (GitHub Releases + latest.json)
    // -----------------------------------------------------------------------

    private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        _updateUrl = UpdateUrlBox.Text.Trim();
        _updateAuto = UpdateAutoSwitch.IsOn;
        SaveUpdateSettings();
        await CheckForUpdatesAsync(silent: false);
    }

    private void SaveUpdateSettings()
    {
        if (_loadingSettings || UpdateUrlBox == null || UpdateAutoSwitch == null) return;
        _updateUrl = UpdateUrlBox.Text.Trim();
        _updateAuto = UpdateAutoSwitch.IsOn;
        SaveSettings();
    }

    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (_updateChecking) return;
        _updateChecking = true;
        if (!silent) UpdateStatusText.Text = Loc.T("A verificar…");
        try
        {
            string url = (_updateUrl ?? "").Trim();
            if (string.IsNullOrEmpty(url))
            {
                if (!silent) UpdateStatusText.Text = Loc.T("Configura a URL do manifesto.");
                return;
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CapturaStudio/" + AppVersion);
            string json = await http.GetStringAsync(url);
            json = json.TrimStart('\uFEFF');
            var m = System.Text.Json.JsonSerializer.Deserialize<UpdateManifest>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (m == null || string.IsNullOrWhiteSpace(m.Version))
            {
                if (!silent) UpdateStatusText.Text = Loc.T("Resposta de atualização inválida.");
                return;
            }
            if (!Version.TryParse(m.Version, out var latest) ||
                !Version.TryParse(AppVersion, out var current))
            {
                if (!silent) UpdateStatusText.Text = Loc.T("Versão inválida no manifesto.");
                return;
            }
            if (latest <= current)
            {
                if (!silent)
                {
                    UpdateStatusText.Text = Loc.T("Estás na versão mais recente.");
                    ShowNotice(InfoBarSeverity.Success, Loc.T("Atualizações"),
                               Loc.T("Estás na versão mais recente."));
                }
                return;
            }

            if (!silent) UpdateStatusText.Text = Loc.T("Nova versão disponível:") + " " + m.Version;
            ShowUpdateAvailable(m, silent);
        }
        catch (HttpRequestException hex) when (hex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Log("updates: 404 " + _updateUrl);
            if (!silent)
            {
                UpdateStatusText.Text = Loc.T("Ainda não há release publicada (ou o repositório é privado).");
                ShowNotice(InfoBarSeverity.Warning, Loc.T("Atualizações"), UpdateStatusText.Text);
            }
        }
        catch (TaskCanceledException)
        {
            Log("updates: timeout");
            if (!silent)
            {
                UpdateStatusText.Text = Loc.T("Tempo esgotado ao verificar atualizações.");
                ShowNotice(InfoBarSeverity.Warning, Loc.T("Atualizações"), UpdateStatusText.Text);
            }
        }
        catch (HttpRequestException ex)
        {
            Log("updates: " + ex.Message);
            if (!silent)
            {
                UpdateStatusText.Text = Loc.T("Sem ligação ao GitHub.");
                ShowNotice(InfoBarSeverity.Warning, Loc.T("Atualizações"), UpdateStatusText.Text);
            }
        }
        catch (Exception ex)
        {
            Log("updates: " + ex.Message);
            if (!silent)
            {
                UpdateStatusText.Text = Loc.T("Não foi possível verificar atualizações.");
                ShowNotice(InfoBarSeverity.Warning, Loc.T("Atualizações"), UpdateStatusText.Text);
            }
        }
        finally { _updateChecking = false; }
    }

    private void ShowUpdateAvailable(UpdateManifest m, bool silent)
    {
        if (silent)
        {
            ShowNotice(InfoBarSeverity.Informational, Loc.T("Nova versão disponível"),
                       $"{m.Version} · {Loc.T("Vê Definições → Sobre para atualizar.")}",
                       autoClose: false, actionText: Loc.T("Ver"),
                       action: () => ShowUpdateDialog(m));
            return;
        }
        ShowUpdateDialog(m);
    }

    private async void ShowUpdateDialog(UpdateManifest m)
    {
        try
        {
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = $"{Loc.T("Versão instalada:")} {AppVersion}   ·   {Loc.T("Nova versão:")} {m.Version}",
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrWhiteSpace(m.Notes))
                panel.Children.Add(new TextBlock { Text = m.Notes, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrWhiteSpace(m.Sha256))
                panel.Children.Add(new TextBlock
                {
                    Text = "SHA-256: " + m.Sha256,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 120, 120))
                });
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("A app vai fechar durante a instalação."),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 120, 120))
            });

            var dlg = new ContentDialog
            {
                Title = Loc.T("Atualização disponível"),
                Content = new ScrollViewer { Content = panel, MaxHeight = 360 },
                PrimaryButtonText = Loc.T("Transferir"),
                CloseButtonText = Loc.T("Agora não"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            await OpenUpdateUrlAsync(m.Url);
        }
        catch (Exception ex) { Log("update dialog: " + ex.Message); }
    }

    private async Task OpenUpdateUrlAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ShowNotice(InfoBarSeverity.Error, Loc.T("Erro"), Loc.T("URL de atualização inválida."));
            return;
        }
        try { await Windows.System.Launcher.LaunchUriAsync(uri); }
        catch (Exception ex) { ShowNotice(InfoBarSeverity.Error, Loc.T("Erro"), ex.Message); }
    }

    // -----------------------------------------------------------------------
    // Perfis/presets
    // -----------------------------------------------------------------------

    private void LoadProfilePresets()
    {
        _profilePresets = new List<CaptureProfile>();
        try
        {
            if (File.Exists(ProfilesPath))
                _profilePresets = System.Text.Json.JsonSerializer
                    .Deserialize<List<CaptureProfile>>(File.ReadAllText(ProfilesPath)) ?? new();
        }
        catch (Exception ex)
        {
            Log("perfis: " + ex.Message);
            _profilePresets = new List<CaptureProfile>();
        }

        _loadingProfilePresets = true;
        try
        {
            ProfilePresetBox.Items.Clear();
            ProfilePresetBox.Items.Add(new ComboBoxItem { Content = Loc.T("Personalizado") });
            foreach (var p in _profilePresets)
                ProfilePresetBox.Items.Add(new ComboBoxItem { Content = p.Name });
            ProfilePresetBox.SelectedIndex = 0;
        }
        finally { _loadingProfilePresets = false; }
    }

    private void SaveProfilePresets()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilesPath)!);
            File.WriteAllText(ProfilesPath, System.Text.Json.JsonSerializer.Serialize(
                _profilePresets, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Log("perfis: " + ex.Message); }
    }

    private static string SelectedComboText(ComboBox cb)
    {
        return (cb.SelectedItem as ComboBoxItem)?.Content?.ToString()
            ?? cb.SelectedItem?.ToString() ?? "";
    }

    private static int FindComboIndex(ComboBox cb, string text)
    {
        if (string.IsNullOrEmpty(text)) return -1;
        for (int i = 0; i < cb.Items.Count; i++)
        {
            string s = (cb.Items[i] as ComboBoxItem)?.Content?.ToString()
                ?? cb.Items[i]?.ToString() ?? "";
            if (string.Equals(s, text, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private static void SetComboIndex(ComboBox cb, int idx)
    {
        if (idx >= 0 && idx < cb.Items.Count && cb.SelectedIndex != idx)
            cb.SelectedIndex = idx;
    }

    private CaptureProfile CaptureProfileFromUi()
    {
        double gop = GopBox.Value;
        if (double.IsNaN(gop) || gop <= 0) gop = 2;
        return new CaptureProfile
        {
            ResText = SelectedComboText(ResBox),
            FpsText = SelectedComboText(FpsBox),
            FormatText = SelectedComboText(FormatBox),
            Res = Math.Max(0, ResBox.SelectedIndex),
            Fps = Math.Max(0, FpsBox.SelectedIndex),
            Format = Math.Max(0, FormatBox.SelectedIndex),
            Codec = Math.Max(0, CodecBox.SelectedIndex),
            Profile = Math.Max(0, ProfileBox.SelectedIndex),
            Bitrate = Math.Max(0, BitrateBox.SelectedIndex),
            Hw = Math.Max(0, HwBox.SelectedIndex),
            RateMode = Math.Max(0, RateBox.SelectedIndex),
            GopSeconds = gop,
            Preset = Math.Max(0, PresetBox.SelectedIndex),
            Tune = Math.Max(0, TuneBox.SelectedIndex),
            Bframes = Math.Max(0, BframesBox.SelectedIndex),
            Multipass = Math.Max(0, MultipassBox.SelectedIndex),
            Cq = CqValues[Math.Max(0, Math.Min(CqValues.Length - 1, CqBox.SelectedIndex))],
            VideoEncoder = _videoEncoder,
            AudioMode = Math.Max(0, AudioModeBox.SelectedIndex),
            Abr = Math.Max(0, AbrBox.SelectedIndex),
            AudioRate = Math.Max(0, AudioRateBox.SelectedIndex),
            AudioChannels = Math.Max(0, AudioChannelsBox.SelectedIndex),
            AudioBits = Math.Max(0, AudioBitsBox.SelectedIndex),
            ColorSpace = Math.Max(0, ColorSpaceBox.SelectedIndex),
            ColorRange = Math.Max(0, ColorRangeBox.SelectedIndex),
            Container = Math.Max(0, ContBox.SelectedIndex),
            Duration = Math.Max(0, DurBox.SelectedIndex),
            Split = Math.Max(0, SplitBox.SelectedIndex),
            FileTemplate = _fileTemplate
        };
    }

    private void ApplyProfile(CaptureProfile p)
    {
        if (_proc != null)
        {
            Log("Perfil não aplicado durante a gravação.");
            return;
        }

        _loadingSettings = true;
        try
        {
            int ri = FindComboIndex(ResBox, p.ResText);
            if (ri < 0) ri = p.Res;
            if (ri >= 0 && ri < ResBox.Items.Count) ResBox.SelectedIndex = ri;
            UpdateFpsForRes();

            int fi = FindComboIndex(FpsBox, p.FpsText);
            if (fi < 0) fi = p.Fps;
            if (fi >= 0 && fi < FpsBox.Items.Count) FpsBox.SelectedIndex = fi;

            int mi = FindComboIndex(FormatBox, p.FormatText);
            if (mi < 0) mi = p.Format;
            if (mi >= 0 && mi < FormatBox.Items.Count) FormatBox.SelectedIndex = mi;

            SetComboIndex(CodecBox, p.Codec);
            SetComboIndex(ProfileBox, p.Profile);
            SetComboIndex(BitrateBox, p.Bitrate);
            SetComboIndex(HwBox, p.Hw);
            SetComboIndex(RateBox, p.RateMode);
            SetComboIndex(PresetBox, p.Preset);
            SetComboIndex(TuneBox, p.Tune);
            SetComboIndex(BframesBox, p.Bframes);
            SetComboIndex(MultipassBox, p.Multipass);
            int cq = Array.IndexOf(CqValues, p.Cq);
            if (cq >= 0) SetComboIndex(CqBox, cq);
            GopBox.Value = p.GopSeconds > 0 ? p.GopSeconds : 2;

            SetComboIndex(AudioModeBox, p.AudioMode);
            SetComboIndex(AbrBox, p.Abr);
            SetComboIndex(AudioRateBox, p.AudioRate);
            SetComboIndex(AudioChannelsBox, p.AudioChannels);
            SetComboIndex(AudioBitsBox, p.AudioBits);
            SetComboIndex(ColorSpaceBox, p.ColorSpace);
            SetComboIndex(ColorRangeBox, p.ColorRange);
            SetComboIndex(ContBox, p.Container);
            SetComboIndex(DurBox, p.Duration);
            SetComboIndex(SplitBox, p.Split);

            _videoEncoder = p.VideoEncoder;
            _fileTemplate = string.IsNullOrWhiteSpace(p.FileTemplate)
                ? "Captura_{data}_{hora}" : p.FileTemplate;

            UpdateAudioFields();
            UpdateVideoFields();
        }
        catch (Exception ex) { Log("perfil: " + ex.Message); }
        finally { _loadingSettings = false; }

        SaveSettings();
        Log("Perfil aplicado: " + p.Name);
    }

    private void ProfilePresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingProfilePresets) return;
        int idx = ProfilePresetBox.SelectedIndex - 1;
        if (idx < 0 || idx >= _profilePresets.Count) return;
        if (_proc != null)
        {
            Log("Perfil não aplicado durante a gravação.");
            return;
        }
        ApplyProfile(_profilePresets[idx]);
    }

    private async void ProfilePresetSave_Click(object sender, RoutedEventArgs e)
    {
        var box = new TextBox
        {
            Text = $"Perfil {_profilePresets.Count + 1}",
            PlaceholderText = Loc.T("Nome do perfil")
        };
        var dlg = new ContentDialog
        {
            Title = Loc.T("Guardar perfil"),
            Content = box,
            PrimaryButtonText = Loc.T("Guardar"),
            CloseButtonText = Loc.T("Cancelar"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        string name = box.Text.Trim();
        if (name.Length == 0)
        {
            ShowNotice(InfoBarSeverity.Warning, Loc.T("Erro"),
                       Loc.T("O nome do perfil não pode estar vazio."));
            return;
        }

        var p = CaptureProfileFromUi();
        p.Name = name;
        int idx = _profilePresets.FindIndex(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) _profilePresets[idx] = p;
        else _profilePresets.Add(p);

        SaveProfilePresets();
        LoadProfilePresets();
        _loadingProfilePresets = true;
        try
        {
            int sel = _profilePresets.FindIndex(x =>
                string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            ProfilePresetBox.SelectedIndex = sel >= 0 ? sel + 1 : 0;
        }
        finally { _loadingProfilePresets = false; }
        Log("Perfil guardado: " + name);
    }

    private async void ProfilePresetDelete_Click(object sender, RoutedEventArgs e)
    {
        int sel = ProfilePresetBox.SelectedIndex;
        if (sel <= 0 || sel - 1 >= _profilePresets.Count) return;
        int idx = sel - 1;
        string name = _profilePresets[idx].Name;

        var dlg = new ContentDialog
        {
            Title = Loc.T("Eliminar perfil"),
            Content = $"{Loc.T("Eliminar este perfil?")}\n{name}",
            PrimaryButtonText = Loc.T("Eliminar"),
            CloseButtonText = Loc.T("Cancelar"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        _profilePresets.RemoveAt(idx);
        SaveProfilePresets();
        LoadProfilePresets();
        Log("Perfil eliminado: " + name);
    }

    // -----------------------------------------------------------------------
    // Atalhos globais Ctrl+Alt+F9/F10
    // -----------------------------------------------------------------------

    private void InitGlobalHotkeys()
    {
        if (_hotkeyHwnd != IntPtr.Zero) return;
        try
        {
            _hotkeyHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _wndProc = HotkeyWndProc;
            _oldWndProc = SetWindowLongPtr(_hotkeyHwnd, GWLP_WNDPROC,
                Marshal.GetFunctionPointerForDelegate(_wndProc));
            bool a = RegisterHotKey(_hotkeyHwnd, HOTKEY_REC,
                MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_F9);
            bool b = RegisterHotKey(_hotkeyHwnd, HOTKEY_STOP,
                MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_F10);
            Log($"Atalhos globais Ctrl+Alt+F9/F10: {a}/{b}");
        }
        catch (Exception ex) { Log("atalhos globais: " + ex.Message); }
    }

    private IntPtr HotkeyWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_REC)
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_proc == null) BtnRec_Click(this, new RoutedEventArgs());
                });
            else if (id == HOTKEY_STOP)
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_proc != null) StopRec();
                });
            return IntPtr.Zero;
        }
        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    private void UnregisterGlobalHotkeys()
    {
        try
        {
            if (_hotkeyHwnd == IntPtr.Zero) return;
            UnregisterHotKey(_hotkeyHwnd, HOTKEY_REC);
            UnregisterHotKey(_hotkeyHwnd, HOTKEY_STOP);
            if (_oldWndProc != IntPtr.Zero)
                SetWindowLongPtr(_hotkeyHwnd, GWLP_WNDPROC, _oldWndProc);
            _hotkeyHwnd = IntPtr.Zero;
        }
        catch { }
    }

    private void BeginRecordingUi(bool wasPreview, int au)
    {
        if (!wasPreview) return;
        _recPreview = true;
        _pvCts = new CancellationTokenSource();
        var pct = _pvCts.Token;
        ShowPreviewLoading("A ligar...");
        if (PreviewBtnText != null) PreviewBtnText.Text = Loc.T("Parar pré-visualização");
        else BtnPreview.Content = Loc.T("Parar pré-visualização");
        _ = Task.Run(() => PreviewVideoLoop(pct));
        if (PreviewAudioSwitch.IsOn)
        {
            var arates = new[] { 44100, 48000, 96000 };
            var achans = new[] { 2, 1 };
            int rate = au == 0 ? 48000 : arates[Math.Max(0, AudioRateBox.SelectedIndex)];
            int ch   = au == 0 ? 2      : achans[Math.Max(0, AudioChannelsBox.SelectedIndex)];
            StartAudioOutput(rate, ch);
            _ = Task.Run(() => PreviewAudioLoop(pct));
        }
    }

    // -----------------------------------------------------------------------
    // Caminho Media Foundation (como antes, com preview servido pelo motor)
    // -----------------------------------------------------------------------

    private void StartMfRecording(bool wasPreview, int au, bool pcmCapture,
                                  uint wUse, uint hUse, uint fpsUse, string ext)
    {
        _rawMode = false;
        if (au == 5 && _ffmpegPath == null)
        {
            au = 1;
            pcmCapture = false;
            Log("Opus exige FFmpeg; no caminho MF a usar AAC.");
        }
        _pendingAudioCodec = au == 3 ? "mp3" : au == 4 ? "flac" : au == 5 ? "opus" : null;
        if (_pendingAudioCodec != null && _ffmpegPath == null)
        {
            _pendingAudioCodec = null;
            Log("FFmpeg em falta: a gravar PCM.");
        }

        uint gopSec  = (uint)Math.Max(0, Math.Round(GopBox.Value));
        uint bitrate = Bps[Math.Max(0, BitrateBox.SelectedIndex)] * 1_000_000;

        var args = new List<string> { "--out", _out };
        args.AddRange(new[]
        {
            "--w",       wUse.ToString(),
            "--h",       hUse.ToString(),
            "--fps",     fpsUse.ToString(),
            "--bitrate", bitrate.ToString(),
            "--codec",   CodecBox.SelectedIndex == 1 ? "hevc" : "h264",
            "--profile", Profiles[Math.Max(0, ProfileBox.SelectedIndex)],
            "--container", ext
        });
        if (gopSec > 0) args.AddRange(new[] { "--gop", (gopSec * fpsUse).ToString() });
        args.Add("--meter");

        if (HwBox.SelectedIndex  == 1) args.Add("--sw");
        if (RateBox.SelectedIndex != 0)
        {
            args.Add("--vbr");
            if (RateBox.SelectedIndex == 4) Log("MF não suporta sem perdas; a usar VBR.");
        }

        var fmt = GetSelFormat();
        args.AddRange(new[] { "--color-space", ColorSpaceBox.SelectedIndex == 1 ? "2020" : "709" });
        args.AddRange(new[] { "--color-range", ColorRangeBox.SelectedIndex == 1 ? "full" : "limited" });
        if (fmt != "auto") args.AddRange(new[] { "--format", fmt });

        if (au == 0) args.Add("--no-audio");
        else
        {
            var arates = new[] { 44100, 48000, 96000 };
            var achans = new[] { 2, 1 };
            var abitsv = new[] { 24, 16, 32 };
            args.AddRange(new[] {
                "--audio-rate", arates[Math.Max(0, AudioRateBox.SelectedIndex)].ToString(),
                "--audio-channels", achans[Math.Max(0, AudioChannelsBox.SelectedIndex)].ToString()
            });
            if (pcmCapture)
            {
                args.AddRange(new[] { "--audio-bits", abitsv[Math.Max(0, AudioBitsBox.SelectedIndex)].ToString() });
                args.Add("--pcm");
            }
            else
                args.AddRange(new[] { "--audio-kbps", Abr[Math.Max(0, AbrBox.SelectedIndex)].ToString() });
        }

        if (VideoBox.SelectedItem is DeviceItem vd)
            args.AddRange(new[] { "--device", vd.Index.ToString() });

        if (au != 0 && AudioBox.SelectedItem is DeviceItem ad)
            args.AddRange(new[] { "--audio-device", ad.Index.ToString() });

        uint durSec = Durs[Math.Max(0, DurBox.SelectedIndex)];
        if (durSec > 0) args.AddRange(new[] { "--seconds", durSec.ToString() });

        uint splitMin = Splits[Math.Max(0, SplitBox.SelectedIndex)];
        if (splitMin > 0) args.AddRange(new[] { "--split-min", splitMin.ToString() });

        if (wasPreview) { args.AddRange(new[] { "--preview-port", "45901" }); }
        var psi = new ProcessStartInfo(ExePath)
        {
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        lock (_stdoutLock) _stdout.Clear();
        _proc = new Process { StartInfo = psi };

        _proc.OutputDataReceived += (_, ev) =>
        {
            if (ev.Data == null) return;
            if (ev.Data.StartsWith("LVL ", StringComparison.Ordinal))
            {
                if (float.TryParse(ev.Data.AsSpan(4),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var db))
                    DispatcherQueue.TryEnqueue(() => UpdateLevel(db));
                return;
            }
            if (ev.Data.StartsWith("SEG ", StringComparison.Ordinal))
            {
                var seg = ev.Data.Substring(4);
                DispatcherQueue.TryEnqueue(() => Log("Segmento: " + seg));
                return;
            }
            lock (_stdoutLock) _stdout.AppendLine(ev.Data);
        };
        _proc.ErrorDataReceived += (_, ev) =>
        {
            if (ev.Data == null) return;
            if (ev.Data.StartsWith("CAPTURE_", StringComparison.Ordinal))
                ParseEngineLine(ev.Data);
            else
                lock (_stdoutLock) _stdout.AppendLine(ev.Data);
        };

        _proc.Start();
        _proc.BeginOutputReadLine();
        _proc.BeginErrorReadLine();
    }

    // -----------------------------------------------------------------------
    // Caminho B: motor raw → FFmpeg (encode) → mux
    // -----------------------------------------------------------------------

    private void StartFfmpegRecording(bool wasPreview, int au, uint w, uint h, uint fps, string folder)
    {
        // Opção A (MFT + ICodecAPI dentro do motor) fica anotada para o futuro.
        // Aqui o encode é feito pelo FFmpeg; o caminho MF usa o IMFSinkWriter.
        string stem = Path.GetFileNameWithoutExtension(_out);
        _rawVideo = Path.Combine(folder, stem + ".raw.mp4");
        _rawWav   = _rawVideo + ".wav";
        try { if (File.Exists(_rawVideo)) File.Delete(_rawVideo); } catch { }
        try { if (File.Exists(_rawWav))   File.Delete(_rawWav);   } catch { }
        _rawMode    = true;
        _rawAudioMode = au;
        _syncV = -1;
        _syncA = -1;
        _pendingAudioCodec = null;
        lock (_ffLogLock) _ffLog.Clear();

        var args = new List<string>
        {
            "--raw-video", "--out", _rawVideo,
            "--w", w.ToString(), "--h", h.ToString(), "--fps", fps.ToString(),
            "--color-space", ColorSpaceBox.SelectedIndex == 1 ? "2020" : "709",
            "--color-range", ColorRangeBox.SelectedIndex == 1 ? "full" : "limited"
        };
        if (VideoBox.SelectedItem is DeviceItem vd)
            args.AddRange(new[] { "--device", vd.Index.ToString() });

        if (au != 0)
        {
            var arates = new[] { 44100, 48000, 96000 };
            var achans = new[] { 2, 1 };
            var abits  = new[] { 24, 16, 32 };
            args.AddRange(new[]
            {
                "--audio-rate", arates[Math.Max(0, AudioRateBox.SelectedIndex)].ToString(),
                "--audio-channels", achans[Math.Max(0, AudioChannelsBox.SelectedIndex)].ToString(),
                "--audio-bits", abits[Math.Max(0, AudioBitsBox.SelectedIndex)].ToString()
            });
            if (AudioBox.SelectedItem is DeviceItem ad)
                args.AddRange(new[] { "--audio-device", ad.Index.ToString() });
        }
        else args.Add("--no-audio");

        if (wasPreview) args.AddRange(new[] { "--preview-port", "45901" });
        uint durSec = Durs[Math.Max(0, DurBox.SelectedIndex)];
        if (durSec > 0) args.AddRange(new[] { "--seconds", durSec.ToString() });

        var fmtSel = GetSelFormat();
        if (fmtSel != "auto" && fmtSel != "nv12")
            Log("FFmpeg: a capturar NV12 (o formato escolhido aplica-se só ao caminho MF).");

        var psi = new ProcessStartInfo(ExePath)
        {
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            StandardErrorEncoding  = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var engine = new Process { StartInfo = psi };
        engine.ErrorDataReceived += (_, ev) => { if (ev.Data != null) ParseEngineLine(ev.Data); };
        engine.Start();
        engine.BeginErrorReadLine();
        _proc = engine;

        var enc = ResolveEncoder(logFallback: true);
        var encArgs = BuildFfmpegEncodeArgs(w, h, fps, _rawVideo, enc);
        var ff = StartFfmpeg(encArgs, redirectStdin: true);
        _ffProc = ff;
        _pipeTask = Task.Run(() => PumpAsync(engine.StandardOutput.BaseStream, ff.StandardInput.BaseStream));
        Log($"FFmpeg: {enc.Enc} {w}x{h}@{fps} · {RateBox.SelectedIndex switch { 0 => "CBR", 1 => "CQP", 2 => "VBR", 3 => "VBR alvo", _ => "sem perdas" }}");
    }

    private List<string> BuildFfmpegEncodeArgs(uint w, uint h, uint fps, string outPath,
                                               (string Enc, bool Nvenc, bool Hevc) e)
    {
        bool nvenc = e.Nvenc;
        bool hevc  = e.Hevc;
        var a = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "warning",
            "-f", "rawvideo", "-pix_fmt", "nv12",
            "-s", $"{w}x{h}", "-r", fps.ToString(),
            "-i", "pipe:0", "-an",
            "-c:v", e.Enc
        };

        int preset = Math.Clamp(PresetBox.SelectedIndex, 0, 6);
        if (nvenc) a.AddRange(new[] { "-preset", "p" + (preset + 1) });
        else a.AddRange(new[] { "-preset", X264Presets[preset] });

        int rateMode = Math.Clamp(RateBox.SelectedIndex, 0, 4);
        bool lossless = rateMode == 4;
        if (!lossless)
        {
            int tune = Math.Clamp(TuneBox.SelectedIndex, 0, 3);
            if (nvenc)
            {
                if (tune == 0) a.AddRange(new[] { "-tune", "hq" });
                else if (tune == 1) a.AddRange(new[] { "-tune", "ll" });
                else if (tune == 2) a.AddRange(new[] { "-tune", "ull" });
            }
            else if (e.Enc == "libx264" && (tune == 1 || tune == 2))
                a.AddRange(new[] { "-tune", "zerolatency" });
        }
        else if (nvenc)
            a.AddRange(new[] { "-tune", "lossless" });

        a.AddRange(new[] { "-bf", Bframes[Math.Max(0, BframesBox.SelectedIndex)].ToString() });

        if (nvenc && !lossless && MultipassBox.SelectedIndex > 0)
            a.AddRange(new[] { "-multipass", MultipassBox.SelectedIndex == 1 ? "qres" : "fullres" });

        int gopSec = (int)Math.Round(GopBox.Value);
        if (gopSec > 0) a.AddRange(new[] { "-g", (gopSec * (int)fps).ToString() });

        if (!hevc) a.AddRange(new[] { "-profile:v", Profiles[Math.Max(0, ProfileBox.SelectedIndex)] });

        long br = (long)Bps[Math.Max(0, BitrateBox.SelectedIndex)] * 1_000_000L;
        int cq = CqValues[Math.Clamp(CqBox.SelectedIndex, 0, CqValues.Length - 1)];
        switch (rateMode)
        {
            case 0: // CBR
                if (nvenc) a.AddRange(new[] { "-rc", "cbr", "-b:v", br.ToString() });
                else a.AddRange(new[] { "-b:v", br.ToString(), "-minrate", br.ToString(),
                                        "-maxrate", br.ToString(), "-bufsize", (br * 2).ToString() });
                break;
            case 1: // CQP
                if (nvenc) a.AddRange(new[] { "-rc", "constqp", "-qp", cq.ToString() });
                else a.AddRange(new[] { "-qp", cq.ToString() });
                break;
            case 2: // VBR
                if (nvenc) a.AddRange(new[] { "-rc", "vbr", "-b:v", br.ToString() });
                else a.AddRange(new[] { "-b:v", br.ToString() });
                break;
            case 3: // VBR com qualidade alvo
                if (nvenc) a.AddRange(new[] { "-rc", "vbr", "-cq", cq.ToString(), "-b:v", br.ToString() });
                else a.AddRange(new[] { "-crf", cq.ToString(), "-maxrate", br.ToString(),
                                        "-bufsize", (br * 2).ToString() });
                break;
            default: // sem perdas
                if (nvenc) a.AddRange(new[] { "-rc", "constqp", "-qp", "0" });
                else if (e.Enc == "libx265") a.AddRange(new[] { "-x265-params", "lossless=1" });
                else a.AddRange(new[] { "-qp", "0" });
                break;
        }

        a.AddRange(new[] { "-pix_fmt", "yuv420p" });
        bool cs2020 = ColorSpaceBox.SelectedIndex == 1;
        bool full   = ColorRangeBox.SelectedIndex == 1;
        a.AddRange(new[] { "-colorspace", cs2020 ? "bt2020nc" : "bt709" });
        a.AddRange(new[] { "-color_primaries", cs2020 ? "bt2020" : "bt709" });
        a.AddRange(new[] { "-color_trc", cs2020 ? "bt2020-10" : "bt709" });
        a.AddRange(new[] { "-color_range", full ? "pc" : "tv" });
        if (hevc) a.AddRange(new[] { "-tag:v", "hvc1" });
        a.Add(outPath);
        return a;
    }

    private Process StartFfmpeg(List<string> args, bool redirectStdin)
    {
        var psi = new ProcessStartInfo(_ffmpegPath!)
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardInput  = redirectStdin,
            RedirectStandardError  = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        var p = new Process { StartInfo = psi };
        p.ErrorDataReceived += (_, ev) => { if (ev.Data != null) AppendFfLog(ev.Data); };
        p.Start();
        p.BeginErrorReadLine();
        return p;
    }

    private static async Task PumpAsync(Stream src, Stream dst)
    {
        var buf = new byte[1 << 20];
        try
        {
            int n;
            while ((n = await src.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
                await dst.WriteAsync(buf, 0, n).ConfigureAwait(false);
        }
        catch { }
        finally
        {
            try { await dst.FlushAsync().ConfigureAwait(false); } catch { }
            try { dst.Close(); } catch { }
        }
    }

    private void ParseEngineLine(string line)
    {
        if (line.StartsWith("LVL ", StringComparison.Ordinal))
        {
            if (float.TryParse(line.AsSpan(4),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var db))
                DispatcherQueue.TryEnqueue(() => UpdateLevel(db));
            return;
        }
        if (line.StartsWith("CAPTURE_WAIT ", StringComparison.Ordinal)) return;
        if (line.StartsWith("CAPTURE_READY ", StringComparison.Ordinal))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_captureReady) _start = DateTime.Now;
                _captureReady = true;
                if (!_stopRequested) StatusText.Text = Loc.T("A gravar…");
            });
            return;
        }
        if (line.StartsWith("CAPTURE_RECOVERY attempt=", StringComparison.Ordinal))
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, @"attempt=(\d+)\s+max=(\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var attempt) &&
                int.TryParse(m.Groups[2].Value, out var maxAttempts))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _captureRecoveries = (ulong)attempt;
                    _captureHealthText = Loc.T("A tentar recuperar sinal da placa") + $" ({attempt}/{maxAttempts})";
                    Log(_captureHealthText);
                });
            }
            return;
        }
        if (line.StartsWith("CAPTURE_RECOVERED ", StringComparison.Ordinal))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _captureHealthText = "";
                Log(Loc.T("Sinal da placa recuperado"));
                ShowNotice(InfoBarSeverity.Informational, Loc.T("Sinal da placa recuperado"), "");
            });
            return;
        }
        if (line.StartsWith("CAPTURE_SIGNAL_LOST ", StringComparison.Ordinal))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _captureHealthText = Loc.T("Sem sinal da placa; a gravação vai terminar");
                Log(_captureHealthText);
                Log(line);
                ShowNotice(InfoBarSeverity.Warning, Loc.T("Sem sinal da placa"),
                           Loc.T("A gravação vai terminar."), autoClose: false);
                if (!_stopRequested) StopRec();
            });
            return;
        }
        if (line.StartsWith("CAPTURE_FPS ", StringComparison.Ordinal))
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                line, @"source=([0-9]+(?:\.[0-9]+)?)\s+output=([0-9]+(?:\.[0-9]+)?)\s+target=(\d+)(?:\s+dropped=(\d+)\s+repeated=(\d+)\s+recoveries=(\d+))?");
            if (m.Success &&
                double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var sourceFps) &&
                double.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var outputFps) &&
                int.TryParse(m.Groups[3].Value, out var targetFps))
            {
                ulong.TryParse(m.Groups[4].Value, out var dropped);
                ulong.TryParse(m.Groups[5].Value, out var repeated);
                ulong.TryParse(m.Groups[6].Value, out var recoveries);
                DispatcherQueue.TryEnqueue(() =>
                {
                    _captureSourceFps = sourceFps;
                    _captureOutputFps = outputFps;
                    _captureTargetFps = targetFps;
                    _captureDroppedFrames = dropped;
                    _captureRepeatedFrames = repeated;
                    _captureRecoveries = recoveries;
                    _lastFpsUpdate = DateTime.Now;
                });
            }
            return;
        }
        if (line.StartsWith("CAPTURE_METRICS ", StringComparison.Ordinal))
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                line, @"source=(\d+)\s+output=(\d+)\s+dropped=(\d+)\s+repeated=(\d+)\s+recoveries=(\d+)");
            if (m.Success)
                DispatcherQueue.TryEnqueue(() => Log(
                    $"{Loc.T("Métricas")}: {Loc.T("fonte")} {m.Groups[1].Value}, {Loc.T("saída")} {m.Groups[2].Value}, " +
                    $"{m.Groups[3].Value} {Loc.T("descartados")}, {m.Groups[4].Value} {Loc.T("repetidos")}, " +
                    $"{Loc.T("recuperações")} {m.Groups[5].Value}."));
            return;
        }
        if (line.StartsWith("SYNC ", StringComparison.Ordinal))
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, @"v=(-?\d+)\s+a=(-?\d+)");
            if (m.Success)
            {
                _syncV = int.Parse(m.Groups[1].Value);
                _syncA = int.Parse(m.Groups[2].Value);
            }
            return;
        }
        DispatcherQueue.TryEnqueue(() => Log(line));
    }

    private void AppendFfLog(string line)
    {
        lock (_ffLogLock)
        {
            _ffLog.AppendLine(line);
            if (_ffLog.Length > 12000) _ffLog.Remove(0, _ffLog.Length - 12000);
        }
    }

    private string FfLogTail()
    {
        lock (_ffLogLock) return _ffLog.ToString().Trim();
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e) => StopRec();

    private void StopRec()
    {
        var proc = _proc;
        if (proc == null || _stopRequested) return;
        _stopRequested = true;
        try { proc.StandardInput.WriteLine(""); proc.StandardInput.Flush(); } catch { }
        // A interface continua responsiva enquanto o motor faz Flush ao leitor.
        // Se o driver não libertar uma leitura bloqueada, mantemos um limite de segurança.
        _ = Task.Run(() =>
        {
            try
            {
                if (!proc.WaitForExit(10000))
                {
                    DispatcherQueue.TryEnqueue(() => Log("A placa não terminou a leitura; a encerrar o motor."));
                    proc.Kill();
                }
            }
            catch { }
        });
    }

    private async Task FinishRawAsync()
    {
        if (_finalizing) return;
        _finalizing = true;
        var engine = _proc;
        var ff = _ffProc;
        try
        {
            if (_pipeTask != null)
            {
                try { await _pipeTask.WaitAsync(TimeSpan.FromSeconds(20)); } catch { }
            }
            if (ff != null)
            {
                try { await ff.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
                catch { try { ff.Kill(true); } catch { } }
            }
            int ffCode = -1;
            try { ffCode = ff?.ExitCode ?? -1; } catch { }

            bool hasVideo = _rawVideo != null && File.Exists(_rawVideo) && new FileInfo(_rawVideo).Length > 2048;
            bool hasAudio = _rawWav != null && File.Exists(_rawWav) && new FileInfo(_rawWav).Length > 44;
            if (hasAudio) RepairWavHeader(_rawWav!);

            if (!hasVideo)
            {
                Log("FFmpeg: não foram gravados frames." + (ffCode != 0 ? " " + FfLogTail() : ""));
                DeleteRawFiles();
                if (!_closingAfterRec)
                    ShowNotice(InfoBarSeverity.Error, Loc.T("Erro"), Loc.T("Não foram gravados frames."));
                return;
            }

            var muxArgs = BuildMuxArgs(hasAudio);
            var (ok, tail) = await RunFfmpegAsync(muxArgs);
            if (ok && File.Exists(_out) && new FileInfo(_out).Length > 0)
            {
                DeleteRawFiles();
                Log("Terminado: " + _out);
                if (!_closingAfterRec)
                    ShowNotice(InfoBarSeverity.Success, Loc.T("Gravação guardada"), Path.GetFileName(_out));
            }
            else
            {
                Log("Mux FFmpeg falhou: " + tail);
                Log("Temporários mantidos: " + _rawVideo);
                if (!_closingAfterRec)
                    ShowNotice(InfoBarSeverity.Error, Loc.T("Erro"),
                               Loc.T("Falha ao guardar a gravação. Vê o registo."));
            }
        }
        catch (Exception ex)
        {
            Log("finalizar FFmpeg: " + ex.Message);
            if (!_closingAfterRec)
                ShowNotice(InfoBarSeverity.Error, Loc.T("Erro"), ex.Message);
        }
        finally
        {
            _finalizing = false;
            _rawMode = false;
            if (engine != null && !engine.HasExited) { try { engine.Kill(); } catch { } }
            if (engine != null) { try { engine.Dispose(); } catch { } }
            if (ReferenceEquals(_proc, engine)) _proc = null;
            try { ff?.Dispose(); } catch { }
            _ffProc = null;
            _pipeTask = null;
            _timer.Stop();
            BtnRec.IsEnabled  = true;
            BtnStop.IsEnabled = false;
            _captureReady = false;
            _stopRequested = false;
            _captureHealthText = "";
            UpdateLevel(-60);
            StatusText.Text   = Loc.T("Pronto");
            if (_recPreview)
            {
                _recPreview = false;
                try { _pvCts?.Cancel(); } catch { }
                StopPreview();
                if (!_closingAfterRec) BtnPreview_Click(this, new RoutedEventArgs());
            }
            if (_closingAfterRec) { _closingAfterRec = false; Close(); }
        }
    }

    private List<string> BuildMuxArgs(bool hasAudio)
    {
        var a = new List<string> { "-y", "-hide_banner", "-loglevel", "warning", "-i", _rawVideo! };
        int d = (_syncA >= 0 && _syncV >= 0) ? _syncA - _syncV : 0;
        if (hasAudio)
        {
            if (d < -80)
                a.AddRange(new[] { "-ss", (Math.Abs(d) / 1000.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) });
            a.AddRange(new[] { "-i", _rawWav! });
            a.AddRange(new[] { "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy" });
            if (d > 80)
            {
                var achans = new[] { 2, 1 };
                int ch = achans[Math.Max(0, AudioChannelsBox.SelectedIndex)];
                a.AddRange(new[] { "-af", ch == 1 ? $"adelay={d}" : $"adelay={d}|{d}" });
            }
            AddAudioCodecArgs(a);
            if (Math.Abs(d) >= 80) Log($"Sync A/V: vídeo={_syncV}ms áudio={_syncA}ms (Δ={d}ms)");
        }
        else
            a.AddRange(new[] { "-map", "0:v:0", "-c:v", "copy" });
        a.Add(_out);
        return a;
    }

    private void AddAudioCodecArgs(List<string> a)
    {
        int kbps = (int)Abr[Math.Max(0, AbrBox.SelectedIndex)];
        switch (_rawAudioMode)
        {
            case 1: a.AddRange(new[] { "-c:a", "aac", "-b:a", kbps + "k" }); break;
            case 2: a.AddRange(new[] { "-c:a", "copy" }); break;
            case 3: a.AddRange(new[] { "-c:a", "libmp3lame", "-b:a", kbps + "k" }); break;
            case 4: a.AddRange(new[] { "-c:a", "flac" }); break;
            case 5: a.AddRange(new[] { "-c:a", "libopus", "-b:a", kbps + "k" }); break;
        }
    }

    private async Task<(bool Ok, string Tail)> RunFfmpegAsync(List<string> args)
    {
        var sb = new StringBuilder();
        try
        {
            var psi = new ProcessStartInfo(_ffmpegPath!)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = new Process { StartInfo = psi };
            p.ErrorDataReceived += (_, ev) => { if (ev.Data != null) lock (sb) sb.AppendLine(ev.Data); };
            p.Start();
            p.BeginErrorReadLine();
            await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(180));
            string tail;
            lock (sb) tail = sb.ToString().Trim();
            return (p.ExitCode == 0, tail);
        }
        catch (Exception ex)
        {
            string tail;
            lock (sb) tail = sb.ToString().Trim();
            return (false, ex.Message + (tail.Length > 0 ? "\n" + tail : ""));
        }
    }

    private static void RepairWavHeader(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (fi.Length <= 44) return;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
            var hdr = new byte[44];
            int got = fs.Read(hdr, 0, 44);
            if (got < 44) return;
            uint data = (uint)(fi.Length - 44);
            if (BitConverter.ToUInt32(hdr, 40) == data) return;
            fs.Seek(4, SeekOrigin.Begin);
            fs.Write(BitConverter.GetBytes(36u + data), 0, 4);
            fs.Seek(40, SeekOrigin.Begin);
            fs.Write(BitConverter.GetBytes(data), 0, 4);
        }
        catch { }
    }

    private void DeleteRawFiles()
    {
        try { if (_rawVideo != null && File.Exists(_rawVideo)) File.Delete(_rawVideo); } catch { }
        try { if (_rawWav != null && File.Exists(_rawWav)) File.Delete(_rawWav); } catch { }
    }

    /// <summary>
    /// No arranque, trata dos temporários raw que ficaram de uma sessão morta:
    /// apaga os vazios e recupera os que têm vídeo (mux com o WAV, se existir).
    /// </summary>
    private async Task RecoverOrphanTempsAsync()
    {
        try
        {
            string folder = FolderBox.Text.Trim();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            int recovered = 0, removed = 0;
            foreach (var v in Directory.GetFiles(folder, "*.raw.mp4", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var fi = new FileInfo(v);
                    string wav = v + ".wav";
                    bool hasWav = File.Exists(wav) && new FileInfo(wav).Length > 44;
                    if (fi.Length < 65536)
                    {
                        File.Delete(v);
                        if (File.Exists(wav)) File.Delete(wav);
                        removed++;
                        continue;
                    }
                    // Deixa em paz o que ainda pode estar a ser gravado.
                    if (DateTime.Now - fi.LastWriteTime < TimeSpan.FromMinutes(10)) continue;
                    string final = v.Substring(0, v.Length - ".raw.mp4".Length) + ".mp4";
                    if (File.Exists(final)) continue;

                    if (hasWav && _ffmpegPath != null)
                    {
                        var args = new List<string>
                        {
                            "-y", "-hide_banner", "-loglevel", "warning",
                            "-i", v, "-i", wav,
                            "-map", "0:v:0", "-map", "1:a:0",
                            "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
                            final
                        };
                        var (ok, tail) = await RunFfmpegAsync(args);
                        if (ok && File.Exists(final))
                        {
                            File.Delete(v); File.Delete(wav);
                            recovered++;
                            Log("Recuperado: " + Path.GetFileName(final));
                        }
                        else
                            Log("Recuperação falhou: " + Path.GetFileName(v) + " — " +
                                (tail.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "").Trim());
                    }
                    else
                    {
                        File.Move(v, final);
                        if (File.Exists(wav)) File.Delete(wav);
                        recovered++;
                        Log("Recuperado (só vídeo): " + Path.GetFileName(final));
                    }
                }
                catch (Exception ex) { Log("recuperar temporário: " + ex.Message); }
            }

            // WAV sem vídeo correspondente (vídeo apagado/corrompido).
            foreach (var w in Directory.GetFiles(folder, "*.raw.mp4.wav", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    string v = w.Substring(0, w.Length - 4);
                    if (!File.Exists(v) &&
                        DateTime.Now - new FileInfo(w).LastWriteTime > TimeSpan.FromMinutes(10))
                    {
                        File.Delete(w);
                        removed++;
                    }
                }
                catch { }
            }

            if (recovered + removed > 0)
                Log($"Temporários: {recovered} recuperado(s), {removed} removido(s).");
        }
        catch (Exception ex) { Log("limpeza temporários: " + ex.Message); }
    }

    // -----------------------------------------------------------------------
    // Tick do timer (a cada 250 ms enquanto grava)
    // -----------------------------------------------------------------------


    private void FinalizeOutputIfNeeded()
    {
        try
        {
            if (string.IsNullOrEmpty(_out) || File.Exists(_out)) return;
            var part = _out + ".part.mp4";
            if (!File.Exists(part)) return;
            var fallback = Path.ChangeExtension(_out, ".mp4");
            if (File.Exists(fallback)) File.Delete(fallback);
            File.Move(part, fallback);
            Log("Remux não concluído — gravação guardada como " + Path.GetFileName(fallback));
        }
        catch (Exception ex) { Log("finalizar ficheiro: " + ex.Message); }
    }

    private void Tick()
    {
        if (_proc == null) return;
        var now = DateTime.Now;
        var el   = now - (_captureReady ? _start : _waitStart);
        long sz  = 0;
        try
        {
            if (_rawMode)
            {
                if (_rawVideo != null && File.Exists(_rawVideo)) sz += new FileInfo(_rawVideo).Length;
                if (_rawWav != null && File.Exists(_rawWav)) sz += new FileInfo(_rawWav).Length;
            }
            else if (File.Exists(_out)) sz = new FileInfo(_out).Length;
            else
            {
                // MKV/TS: o motor grava para <final>.part.mp4 (e .wav em PCM) antes do remux
                var part = _out + ".part.mp4";
                if (File.Exists(part)) sz += new FileInfo(part).Length;
                var wav = part + ".wav";
                if (File.Exists(wav)) sz += new FileInfo(wav).Length;
            }
        }
        catch { }
        if (_stopRequested)
        {
            StatusText.Text = Loc.T("A terminar") + $"  {sz / 1_048_576.0:0.0} MB";
        }
        else if (!_captureReady)
        {
            var health = string.IsNullOrEmpty(_captureHealthText) ? "" : $"   {_captureHealthText}";
            StatusText.Text = Loc.T("A aguardar sinal da placa") + $"  {el:mm\\:ss}   {sz / 1_048_576.0:0.0} MB{health}";
        }
        else
        {
            string fps = "";
            if (_lastFpsUpdate != DateTime.MinValue && now - _lastFpsUpdate < TimeSpan.FromSeconds(2.5))
            {
                if (_captureSourceFps <= 0.0)
                    fps = $"   {Loc.T("Sem frames recentes")}";
                else
                    fps = $"   {Loc.T("Placa")}: {_captureSourceFps:0.0} → {_captureOutputFps:0.0}/{_captureTargetFps} fps";
                if (_rawMode)
                {
                    fps += $" · {Loc.T("repetidos")} {_captureRepeatedFrames} · {Loc.T("descartados")} {_captureDroppedFrames}";
                    if (_captureRecoveries > 0)
                        fps += $" · {Loc.T("recuperações")} {_captureRecoveries}";
                }
            }
            else if (_lastFpsUpdate != DateTime.MinValue)
                fps = $"   {Loc.T("Sem frames recentes")}";
            var health = string.IsNullOrEmpty(_captureHealthText) ? "" : $"   {_captureHealthText}";
            StatusText.Text = Loc.T("A gravar") + $"  {el:mm\\:ss}   {sz / 1_048_576.0:0.0} MB{fps}{health}";
        }

        if (!_proc.HasExited) return;
        if (_rawMode) { _timer.Stop(); _ = FinishRawAsync(); return; }

        _timer.Stop();
        string tail;
        lock (_stdoutLock) tail = _stdout.ToString().Trim();
        if (tail.Length > 0) Log(tail);
        FinalizeOutputIfNeeded();
        Log("Terminado: " + _out);
        if (!_closingAfterRec && File.Exists(_out))
            ShowNotice(InfoBarSeverity.Success, Loc.T("Gravação guardada"), Path.GetFileName(_out));
        if (_pendingAudioCodec != null)
        {
            var codec = _pendingAudioCodec; var file = _out; _pendingAudioCodec = null;
            _ = Task.Run(() => TranscodeAudio(file, codec));
        }
        _proc.Dispose();
        _proc = null;
        BtnRec.IsEnabled  = true;
        BtnStop.IsEnabled = false;
        _captureReady = false;
        _stopRequested = false;
        _captureHealthText = "";
        StatusText.Text   = Loc.T("Pronto");
        if (_recPreview)
        {
            _recPreview = false;
            try { _pvCts?.Cancel(); } catch { }
            StopPreview();
            if (!_closingAfterRec) BtnPreview_Click(this, new RoutedEventArgs());
        }
        if (_closingAfterRec) { _closingAfterRec = false; Close(); }
    }

    // -----------------------------------------------------------------------
    // Medidor de nível
    // -----------------------------------------------------------------------

    private double _lastLevelDb = -60;

    private void UpdateLevel(float db)
    {
        try
        {
            if (float.IsNaN(db) || float.IsInfinity(db)) db = -60;
            db = Math.Clamp(db, -60f, 0f);
            _lastLevelDb = db;
            double frac = (db + 60.0) / 60.0;
            AudioMeterFill.Width = Math.Max(0, AudioMeterTrack.ActualWidth * frac);
            AudioMeterText.Text = $"{db:0} dB";
        }
        catch { }
    }

    private void AudioMeterTrack_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateLevel((float)_lastLevelDb);
    }

    // -----------------------------------------------------------------------
    // Log
    // -----------------------------------------------------------------------

    private void Log(string s)
    {
        _log.AppendLine(s);
        // Manter ≤ 400 linhas no log para não crescer indefinidamente
        LogBox.Text = _log.ToString();
    }

    // -----------------------------------------------------------------------
    // Settings persistentes
    // -----------------------------------------------------------------------

    private static void LoadLanguageEarly()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var s = System.Text.Json.JsonSerializer.Deserialize<WinUISettings>(File.ReadAllText(SettingsPath));
            if (s != null && !string.IsNullOrEmpty(s.Language)) Loc.Lang = s.Language;
        }
        catch { }
    }

    private static int GopIndexFromSeconds(int sec)
    {
        int best = 0;
        for (int i = 1; i < Gops.Length; i++)
            if (Math.Abs((int)Gops[i] - sec) < Math.Abs((int)Gops[best] - sec)) best = i;
        return best;
    }

    private void SaveSettings()
    {
        if (_loadingSettings) return;
        try
        {
            int rate = Math.Max(0, RateBox.SelectedIndex);
            int gopSec = (int)Math.Round(GopBox.Value);
            var s = new WinUISettings
            {
                VideoDevice = (VideoBox.SelectedItem  as DeviceItem)?.Name ?? "",
                AudioDevice = (AudioBox.SelectedItem  as DeviceItem)?.Name ?? "",
                Codec       = Math.Max(0, CodecBox.SelectedIndex),
                Profile     = Math.Max(0, ProfileBox.SelectedIndex),
                Bitrate     = Math.Max(0, BitrateBox.SelectedIndex),
                Hw          = Math.Max(0, HwBox.SelectedIndex),
                Rate        = rate == 0 ? 0 : 1,
                RateMode    = rate,
                Gop         = GopIndexFromSeconds(gopSec),
                GopSeconds  = gopSec,
                Preset      = Math.Max(0, PresetBox.SelectedIndex),
                Tune        = Math.Max(0, TuneBox.SelectedIndex),
                Bframes     = Math.Max(0, BframesBox.SelectedIndex),
                Multipass   = Math.Max(0, MultipassBox.SelectedIndex),
                Cq          = CqValues[Math.Clamp(CqBox.SelectedIndex, 0, CqValues.Length - 1)],
                VideoEncoder = Math.Clamp(_videoEncoder, 0, 4),
                AudioMode   = Math.Max(0, AudioModeBox.SelectedIndex),
                Abr         = Math.Max(0, AbrBox.SelectedIndex),
                Container   = Math.Max(0, ContBox.SelectedIndex),
                Duration    = Math.Max(0, DurBox.SelectedIndex),
                Split       = Math.Max(0, SplitBox.SelectedIndex),
                Res         = Math.Max(0, ResBox.SelectedIndex),
                Fps         = Math.Max(0, FpsBox.SelectedIndex),
                Format      = Math.Max(0, FormatBox.SelectedIndex),
                AudioRate   = Math.Max(0, AudioRateBox.SelectedIndex),
                AudioChannels = Math.Max(0, AudioChannelsBox.SelectedIndex),
                AudioBits   = Math.Max(0, AudioBitsBox.SelectedIndex),
                ColorSpace  = Math.Max(0, ColorSpaceBox.SelectedIndex),
                ColorRange  = Math.Max(0, ColorRangeBox.SelectedIndex),
                ResText     = (ResBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "",
                FpsText     = (FpsBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "",
                FormatText  = (FormatBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "",
                Backdrop    = _backdrop,
                Accent      = _accent,
                Theme       = _theme,
                AutoPreview = _autoPreview,
                PreviewVolume = _previewVolume,
                ShowInfo    = _showInfo,
                FileTemplate = _fileTemplate,
                Language    = Loc.Lang,
                Folder      = FolderBox.Text,
                UpdateUrl   = string.IsNullOrWhiteSpace(_updateUrl) ? UpdateDefaults.ManifestUrl : _updateUrl,
                UpdateAuto  = _updateAuto
            };
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath,
                System.Text.Json.JsonSerializer.Serialize(s,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void ApplySettings()
    {
        if (!File.Exists(SettingsPath)) return;
        _loadingSettings = true;
        try
        {
            var s = System.Text.Json.JsonSerializer.Deserialize<WinUISettings>(
                        File.ReadAllText(SettingsPath));
            if (s == null) return;
            if (!string.IsNullOrEmpty(s.Language)) Loc.Lang = s.Language;
            _videoEncoder = Math.Clamp(s.VideoEncoder, 0, 4);
            BuildEncoderList();

            SetIdx(CodecBox,     s.Codec);
            SetIdx(ProfileBox,   s.Profile);
            SetIdx(BitrateBox,   s.Bitrate);
            SetIdx(HwBox,        s.Hw);
            int rateMode = s.RateMode >= 0 ? s.RateMode : (s.Rate == 0 ? 0 : 2);
            SetIdx(RateBox,      Math.Clamp(rateMode, 0, 4));
            double gopSec = s.GopSeconds >= 0
                ? s.GopSeconds
                : Gops[Math.Clamp(s.Gop, 0, Gops.Length - 1)];
            GopBox.Value = Math.Clamp(gopSec, 0, 30);
            SetIdx(PresetBox,    Math.Clamp(s.Preset, 0, 6));
            SetIdx(TuneBox,      Math.Clamp(s.Tune, 0, 3));
            SetIdx(BframesBox,   Math.Clamp(s.Bframes, 0, 4));
            SetIdx(MultipassBox, Math.Clamp(s.Multipass, 0, 2));
            int cqIdx = Array.IndexOf(CqValues, s.Cq);
            SetIdx(CqBox, cqIdx >= 0 ? cqIdx : 3);

            SetIdx(AudioModeBox, Math.Clamp(s.AudioMode, 0, AudioModeBox.Items.Count - 1));
            SetIdx(AbrBox,       s.Abr);
            SetIdx(ContBox,      s.Container);
            SetIdx(DurBox,       s.Duration);
            SetIdx(SplitBox,     s.Split);
            SetIdx(ResBox,       s.Res);
            SetIdx(FpsBox,       s.Fps);
            SetIdx(FormatBox,    s.Format);
            SetIdx(AudioRateBox, s.AudioRate);
            SetIdx(AudioChannelsBox, s.AudioChannels);
            SetIdx(AudioBitsBox, s.AudioBits);
            SetIdx(ColorSpaceBox, s.ColorSpace);
            SetIdx(ColorRangeBox, s.ColorRange);
            _backdrop = s.Backdrop;
            _accent = s.Accent;
            _theme = s.Theme;
            _autoPreview = s.AutoPreview;
            _previewVolume = s.PreviewVolume;
            _showInfo = s.ShowInfo;
            _fileTemplate = s.FileTemplate;
            _updateUrl = string.IsNullOrWhiteSpace(s.UpdateUrl) ? UpdateDefaults.ManifestUrl : s.UpdateUrl.Trim();
            _updateAuto = s.UpdateAuto;
            _savedResText = s.ResText;
            _savedFpsText = s.FpsText;
            _savedFmtText = s.FormatText;
            UpdateVideoFields();
            UpdateAudioFields();
            ApplyAppearance();
            try { VolSlider.Value = Math.Clamp(_previewVolume, 0, 1) * 100; FsVolSlider.Value = VolSlider.Value; } catch { }
            try { InfoDevice.Visibility = _showInfo ? Visibility.Visible : Visibility.Collapsed; } catch { }
            if (_autoPreview && _pvProc == null) BtnPreview_Click(this, new RoutedEventArgs());

            if (!string.IsNullOrWhiteSpace(s.Folder)) FolderBox.Text = s.Folder;
            DispatcherQueue.TryEnqueue(() => { try { Loc.Apply(RootGrid); Log("Idioma: " + Loc.Lang); } catch (Exception ex) { Log("loc: " + ex.Message); } });
            BuildTechCards(TechList);
            BuildContributionCards(ModelsList);
            if (!string.IsNullOrEmpty(s.VideoDevice)) SelectByName(VideoBox, s.VideoDevice);
            if (!string.IsNullOrEmpty(s.AudioDevice)) SelectByName(AudioBox, s.AudioDevice);
        }
        catch { }
        finally { _loadingSettings = false; }
    }

    private static void SetIdx(ComboBox box, int idx)
    {
        if (idx >= 0 && idx < box.Items.Count) box.SelectedIndex = idx;
    }
}
