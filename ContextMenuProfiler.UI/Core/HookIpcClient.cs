using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;

namespace ContextMenuProfiler.UI.Core
{
    public class HookResponse
    {
        public bool success { get; set; }
        public string? @interface { get; set; }
        public string? names { get; set; }
        public string? icons { get; set; }
        public string? reg_icon { get; set; }
        public string? error { get; set; }
        public double create_ms { get; set; }
        public double init_ms { get; set; }
        public double query_ms { get; set; }
        public double title_ms { get; set; }
        public int state { get; set; }
    }

    public static class HookIpcClient
    {
        private const string PipeName = "ContextMenuProfilerHook";
        private const int ConnectTimeoutMs = 500;
        private const int RequestTimeoutMs = 2500;
        private static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public static async Task<HookResponse?> GetHookDataAsync(string clsid, string? contextPath = null, string? dllHint = null)
        {
            // Default bait path if none provided
            string path = contextPath ?? Path.Combine(Path.GetTempPath(), "ContextMenuProfiler_probe.txt");
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                try { File.WriteAllText(path, "probe"); } catch {}
            }

            bool lockTaken = false;
            try
            {
                await _lock.WaitAsync();
                lockTaken = true;

                using var timeoutCts = new CancellationTokenSource(RequestTimeoutMs);
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    try 
                    {
                        await client.ConnectAsync(ConnectTimeoutMs, timeoutCts.Token);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[IPC DIAG] Connection Failed: {ex.Message}");
                        return null;
                    }

                    // Format: "CLSID|Path[|DllHint]"
                    string requestStr = string.IsNullOrEmpty(dllHint) ? $"{clsid}|{path}" : $"{clsid}|{path}|{dllHint}";
                    byte[] request = Encoding.UTF8.GetBytes(requestStr);
                    await client.WriteAsync(request, 0, request.Length, timeoutCts.Token);
                    await client.FlushAsync(timeoutCts.Token);

                    byte[] responseBuf = new byte[65536];
                    int read = await client.ReadAsync(responseBuf, 0, responseBuf.Length, timeoutCts.Token);
                    
                    if (read <= 0) return null;

                    string response = Encoding.UTF8.GetString(responseBuf, 0, read).TrimEnd('\0');
                    try
                    {
                        // 寻找第一个 { 和最后一个 } 确保 JSON 完整
                        int start = response.IndexOf('{');
                        int end = response.LastIndexOf('}');
                        if (start != -1 && end != -1 && end > start)
                        {
                            string json = response.Substring(start, end - start + 1);
                            return JsonSerializer.Deserialize<HookResponse>(json);
                        }
                        return null;
                    }
                    catch (JsonException)
                    {
                        return null;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[IPC DIAG] Request timed out.");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IPC DIAG] Critical Error: {ex.Message}");
                return null;
            }
            finally
            {
                if (lockTaken) _lock.Release();
            }
        }

        [Obsolete("Use GetHookDataAsync instead")]
        public static async Task<string[]> GetMenuNamesAsync(string clsid, string? contextPath = null)
        {
            var data = await GetHookDataAsync(clsid, contextPath);
            if (data == null || !data.success || string.IsNullOrEmpty(data.names)) return Array.Empty<string>();
            return data.names.Split('|', StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
