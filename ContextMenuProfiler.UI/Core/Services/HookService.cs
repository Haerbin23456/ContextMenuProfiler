using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading.Tasks;
using ContextMenuProfiler.UI.Core.Services;

namespace ContextMenuProfiler.UI.Core.Services
{
    public enum HookStatus
    {
        Disconnected,
        Injected, // DLL loaded but pipe not responding
        Active    // DLL loaded and pipe responding
    }

    public class HookService : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        private static HookService? _instance;
        public static HookService Instance => _instance ??= new HookService();

        private HookStatus _currentStatus = HookStatus.Disconnected;
        public HookStatus CurrentStatus 
        { 
            get => _currentStatus;
            private set => SetProperty(ref _currentStatus, value);
        }

        /// <summary>
        /// When true, status polling will be less aggressive or skip pipe checks to avoid interference during scans.
        /// </summary>
        public bool IsBusy { get; set; }

        private const string PipeName = "ContextMenuProfilerHook";
        private const string DllName = "ContextMenuProfiler.Hook.dll";
        private const string InjectorName = "ContextMenuProfiler.Injector.exe";

        private HookService() 
        {
            _ = StartStatusPolling();
        }

        private async Task StartStatusPolling()
        {
            while (true)
            {
                try
                {
                    // Avoid status flapping while scan workload is actively using the same pipe.
                    if (IsBusy)
                    {
                        await Task.Delay(5000);
                        continue;
                    }

                    var status = await GetStatusAsync();
                    
                    // Adaptive polling: Poll less frequently if active
                    int delay = status == HookStatus.Active ? 5000 : 2000;
                    await Task.Delay(delay);
                }
                catch 
                {
                    await Task.Delay(2000);
                }
            }
        }

        public async Task<HookStatus> GetStatusAsync()
        {
            // During active scans, keep the last-known active state to avoid false "Disconnected".
            if (IsBusy && CurrentStatus == HookStatus.Active)
            {
                return HookStatus.Active;
            }

            // 1. First, try the pipe. If pipe is active, the DLL is definitely there.
            // This is much faster than checking process modules.
            bool isPipeActive = await CheckPipeAsync();
            if (isPipeActive)
            {
                CurrentStatus = HookStatus.Active;
                return HookStatus.Active;
            }

            // 2. If pipe failed, check if DLL is actually injected.
            // This is the "heavy" check, so we do it only if pipe fails.
            bool isInjected = await Task.Run(() => IsDllInjected());
            
            var status = isInjected ? HookStatus.Injected : HookStatus.Disconnected;
            CurrentStatus = status;
            return status;
        }

        private bool IsDllInjected()
        {
            try
            {
                // Multiple explorer.exe instances may exist.
                // Treat injected as true if any explorer process has the module loaded.
                foreach (var proc in Process.GetProcessesByName("explorer"))
                {
                    try
                    {
                        if (proc.HasExited) continue;
                        if (proc.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Equals(DllName, StringComparison.OrdinalIgnoreCase)))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Ignore per-process access/read failures and continue checking others.
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> CheckPipeAsync()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(700);
                return client.IsConnected;
            }
            catch { return false; }
        }

        public async Task<bool> InjectAsync()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            // The DLL and Injector are usually in the same directory as the EXE in production,
            // or in the project root in dev.
            string injectorPath = FindFile(InjectorName);
            string dllPath = FindFile(DllName);

            if (string.IsNullOrEmpty(injectorPath) || string.IsNullOrEmpty(dllPath))
            {
                LogService.Instance.Error($"Could not find {InjectorName} or {DllName}");
                return false;
            }

            return await RunCommandAsync(injectorPath, $"\"{dllPath}\"");
        }

        public async Task<bool> EjectAsync()
        {
            string injectorPath = FindFile(InjectorName);
            string dllPath = FindFile(DllName);

            if (string.IsNullOrEmpty(injectorPath)) return false;

            // For ejection, we just need the filename, but the injector handles path to name conversion
            return await RunCommandAsync(injectorPath, $"\"{dllPath}\" --eject");
        }

        private string FindFile(string fileName)
        {
            // Check App Dir
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            if (File.Exists(path)) return path;

            // Check Project Root (for dev)
            // Current dir is usually C:\src\ContextMenuProfiler\ContextMenuProfiler.UI\bin\Debug\net8.0-windows...
            // We want to check C:\src\ContextMenuProfiler
            string? current = AppDomain.CurrentDomain.BaseDirectory;
            while (current != null)
            {
                path = Path.Combine(current, fileName);
                if (File.Exists(path)) return path;
                current = Path.GetDirectoryName(current);
            }

            return "";
        }

        private async Task<bool> RunCommandAsync(string fileName, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    UseShellExecute = true, // Use shell to trigger UAC if needed
                    Verb = "runas",         // Require admin for injection
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                
                var proc = Process.Start(psi);
                if (proc == null) return false;
                
                await proc.WaitForExitAsync();
                return proc.ExitCode == 0;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"Failed to run {fileName}", ex);
                return false;
            }
        }
    }
}
