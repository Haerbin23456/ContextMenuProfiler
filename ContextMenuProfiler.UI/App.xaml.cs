using System.Configuration;
using System.Data;
using System.Windows;
using System.IO;
using Wpf.Ui.Appearance;
using ContextMenuProfiler.UI.Core.Services;

namespace ContextMenuProfiler.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LocalizationService.Instance.InitializeFromPreferences();
        LogService.Instance.Info("Application Started");
        CleanTempFiles();
        CleanHookOutputFiles();
    }

    private void CleanTempFiles()
    {
        try
        {
            string tempPath = System.IO.Path.GetTempPath();
            string[] files = Directory.GetFiles(tempPath, "ContextMenuProfiler_probe_*");
            foreach (var file in files)
            {
                try { File.Delete(file); } catch { }
            }
            
            string[] dirs = Directory.GetDirectories(tempPath, "ContextMenuProfiler_probe_*");
            foreach (var dir in dirs)
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("Failed to clean temp files", ex);
        }
    }

    private void CleanHookOutputFiles()
    {
        try
        {
            string? hookDllPath = FindFileUpward("ContextMenuProfiler.Hook.dll");
            if (string.IsNullOrEmpty(hookDllPath)) return;

            string hookDir = Path.GetDirectoryName(hookDllPath)!;
            string iconDir = Path.Combine(hookDir, "icons");
            if (Directory.Exists(iconDir))
            {
                Directory.Delete(iconDir, true);
            }

            string hookLog = Path.Combine(hookDir, "hook_internal.log");
            if (File.Exists(hookLog))
            {
                var fi = new FileInfo(hookLog);
                if (fi.Length > 5 * 1024 * 1024)
                {
                    File.Delete(hookLog);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("Failed to clean hook output files", ex);
        }
    }

    private static string? FindFileUpward(string fileName)
    {
        string? current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, fileName);
            if (File.Exists(candidate)) return candidate;
            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogService.Instance.Error("Dispatcher Unhandled Exception", e.Exception);
        NotificationService.Instance.ShowError("Application Error", e.Exception.Message);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogService.Instance.Error("Domain Unhandled Exception", e.ExceptionObject as Exception);
    }
}
