using System;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace CapturaStudio;

public partial class App : Application
{
    private Window? _window;
    private static Mutex? _singleInstance;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    public App() { InitializeComponent(); }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstance = new Mutex(true, @"Local\CapturaStudio.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox(IntPtr.Zero, "O Captura Studio já está a correr.", "Captura Studio", 0x40);
            Environment.Exit(0);
            return;
        }
        // Activar tracing de bindings para capturar falhas de recurso XAML
        DebugSettings.IsBindingTracingEnabled = true;
        DebugSettings.BindingFailed += (_, ev) =>
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winui-crash.txt"),
                $"[BINDING] {ev.Message}\n");
        };
        DebugSettings.XamlResourceReferenceFailed += (_, ev) =>
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winui-crash.txt"),
                $"[RESOURCE] key={ev.Message}\n");
        };

        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== WinUI crash ===");
            sb.AppendLine($"Type: {ex.GetType().FullName}");
            sb.AppendLine($"Message: {ex.Message}");
            // XamlParseException em WinUI 3 NÃO expõe LineNumber/LinePosition
            // (ao contrário do WPF); inspeccionamos InnerException para mais contexto
            var inner = ex.InnerException;
            int depth = 0;
            while (inner != null && depth < 5)
            {
                sb.AppendLine($"  Inner[{depth}] {inner.GetType().FullName}: {inner.Message}");
                inner = inner.InnerException;
                depth++;
            }
            sb.AppendLine("--- StackTrace ---");
            sb.AppendLine(ex.ToString());

            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winui-crash.txt");
            System.IO.File.AppendAllText(logPath, "\n" + DateTime.Now.ToString("HH:mm:ss") + "\n" + sb.ToString());
            throw;
        }
    }
}
