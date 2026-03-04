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

    public class HookCallResult
    {
        public HookResponse? data { get; set; }
        public long lock_wait_ms { get; set; }
        public long connect_ms { get; set; }
        public long roundtrip_ms { get; set; }
        public long total_ms { get; set; }
    }

    public static class HookIpcClient
    {
        private const string PipeName = "ContextMenuProfilerHook";
        private const int LockAcquireTimeoutMs = 5000;
        private const int ConnectTimeoutMs = 500;
        private const int RoundTripTimeoutMs = 3500;
        internal static readonly SemaphoreSlim IpcLock = new SemaphoreSlim(1, 1);

        public static async Task<HookCallResult> GetHookDataAsync(string clsid, string? contextPath = null, string? dllHint = null)
        {
            var result = new HookCallResult();
            var swTotal = Stopwatch.StartNew();
            try
            {
                // Default bait path if none provided
                string path = contextPath ?? Path.Combine(Path.GetTempPath(), "ContextMenuProfiler_probe.txt");
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    try { File.WriteAllText(path, "probe"); } catch {}
                }

                for (int attempt = 0; attempt < 2; attempt++)
                {
                    bool hasLock = false;
                    try
                    {
                        var swLock = Stopwatch.StartNew();
                        hasLock = await IpcLock.WaitAsync(LockAcquireTimeoutMs);
                        swLock.Stop();
                        result.lock_wait_ms += Math.Max(0, (long)swLock.Elapsed.TotalMilliseconds);
                        if (!hasLock)
                        {
                            if (attempt == 0)
                            {
                                await Task.Delay(150);
                                continue;
                            }
                            return result;
                        }

                        using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                        {
                            var swConnect = Stopwatch.StartNew();
                            try 
                            {
                                await client.ConnectAsync(ConnectTimeoutMs);
                            }
                            catch (Exception ex)
                            {
                                swConnect.Stop();
                                result.connect_ms += Math.Max(0, (long)swConnect.Elapsed.TotalMilliseconds);
                                Debug.WriteLine($"[IPC DIAG] Connection Failed: {ex.Message}");
                                if (attempt == 0)
                                {
                                    await Task.Delay(120);
                                    continue;
                                }
                                return result;
                            }
                            swConnect.Stop();
                            result.connect_ms += Math.Max(0, (long)swConnect.Elapsed.TotalMilliseconds);

                            var swRoundTrip = Stopwatch.StartNew();
                            // Format: "CLSID|Path[|DllHint]"
                            string requestStr = string.IsNullOrEmpty(dllHint) ? $"{clsid}|{path}" : $"{clsid}|{path}|{dllHint}";
                            byte[] request = Encoding.UTF8.GetBytes(requestStr);
                            await client.WriteAsync(request, 0, request.Length);

                            byte[] responseBuf = new byte[65536];
                            int read;
                            try
                            {
                                read = await client.ReadAsync(responseBuf, 0, responseBuf.Length).WaitAsync(TimeSpan.FromMilliseconds(RoundTripTimeoutMs));
                            }
                            catch (TimeoutException)
                            {
                                swRoundTrip.Stop();
                                result.roundtrip_ms += Math.Max(0, (long)swRoundTrip.Elapsed.TotalMilliseconds);
                                if (attempt == 0)
                                {
                                    await Task.Delay(120);
                                    continue;
                                }
                                return result;
                            }
                            
                            if (read <= 0)
                            {
                                swRoundTrip.Stop();
                                result.roundtrip_ms += Math.Max(0, (long)swRoundTrip.Elapsed.TotalMilliseconds);
                                if (attempt == 0)
                                {
                                    await Task.Delay(120);
                                    continue;
                                }
                                return result;
                            }

                            string response = Encoding.UTF8.GetString(responseBuf, 0, read).TrimEnd('\0');
                            try
                            {
                                // 寻找第一个 { 和最后一个 } 确保 JSON 完整
                                int start = response.IndexOf('{');
                                int end = response.LastIndexOf('}');
                                if (start != -1 && end != -1 && end > start)
                                {
                                    string json = response.Substring(start, end - start + 1);
                                    result.data = JsonSerializer.Deserialize<HookResponse>(json);
                                    swRoundTrip.Stop();
                                    result.roundtrip_ms += Math.Max(0, (long)swRoundTrip.Elapsed.TotalMilliseconds);
                                    return result;
                                }
                                swRoundTrip.Stop();
                                result.roundtrip_ms += Math.Max(0, (long)swRoundTrip.Elapsed.TotalMilliseconds);
                                if (attempt == 0)
                                {
                                    await Task.Delay(120);
                                    continue;
                                }
                                return result;
                            }
                            catch (JsonException)
                            {
                                swRoundTrip.Stop();
                                result.roundtrip_ms += Math.Max(0, (long)swRoundTrip.Elapsed.TotalMilliseconds);
                                if (attempt == 0)
                                {
                                    await Task.Delay(120);
                                    continue;
                                }
                                return result;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[IPC DIAG] Critical Error: {ex.Message}");
                        if (attempt == 0)
                        {
                            await Task.Delay(120);
                            continue;
                        }
                        return result;
                    }
                    finally
                    {
                        if (hasLock)
                        {
                            IpcLock.Release();
                        }
                    }
                }
                return result;
            }
            finally
            {
                swTotal.Stop();
                result.total_ms = Math.Max(0, (long)swTotal.Elapsed.TotalMilliseconds);
            }
        }

        [Obsolete("Use GetHookDataAsync instead")]
        public static async Task<string[]> GetMenuNamesAsync(string clsid, string? contextPath = null)
        {
            var call = await GetHookDataAsync(clsid, contextPath);
            var data = call.data;
            if (data == null || !data.success || string.IsNullOrEmpty(data.names)) return Array.Empty<string>();
            return data.names.Split('|', StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
