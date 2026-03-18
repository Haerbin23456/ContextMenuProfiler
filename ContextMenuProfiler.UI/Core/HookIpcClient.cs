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
        internal static readonly SemaphoreSlim IpcLock = new SemaphoreSlim(
            HookIpcSemantics.Runtime.MaxConcurrentCalls,
            HookIpcSemantics.Runtime.MaxConcurrentCalls);

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

                for (int attempt = 0; attempt < HookIpcSemantics.Runtime.MaxAttempts; attempt++)
                {
                    bool hasLock = false;
                    try
                    {
                        var swLock = Stopwatch.StartNew();
                        await IpcLock.WaitAsync();
                        hasLock = true;
                        swLock.Stop();
                        result.lock_wait_ms += Math.Max(0, (long)swLock.Elapsed.TotalMilliseconds);

                        using (var client = new NamedPipeClientStream(".", HookIpcSemantics.Runtime.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                        {
                            var swConnect = Stopwatch.StartNew();
                            try 
                            {
                                await client.ConnectAsync(HookIpcSemantics.Runtime.ConnectTimeoutMs);
                            }
                            catch (Exception ex)
                            {
                                swConnect.Stop();
                                result.connect_ms += Math.Max(0, (long)swConnect.Elapsed.TotalMilliseconds);
                                Debug.WriteLine($"[IPC DIAG] Connection Failed: {ex.Message}");
                                if (attempt < HookIpcSemantics.Runtime.MaxAttempts - 1)
                                {
                                    await Task.Delay(HookIpcSemantics.Runtime.RetryDelayMs);
                                    continue;
                                }
                                return result;
                            }
                            swConnect.Stop();
                            result.connect_ms += Math.Max(0, (long)swConnect.Elapsed.TotalMilliseconds);
                            try
                            {
                                client.ReadMode = PipeTransmissionMode.Message;
                            }
                            catch
                            {
                            }

                            var swRoundTrip = Stopwatch.StartNew();
                            using var roundTripCts = new CancellationTokenSource(HookIpcSemantics.Runtime.RoundTripTimeoutMs);
                            string requestStr = BuildRequest(clsid, path, dllHint);
                            byte[] request = Encoding.UTF8.GetBytes(requestStr);
                            await client.WriteAsync(request, 0, request.Length, roundTripCts.Token);
                            await client.FlushAsync(roundTripCts.Token);

                            string response;
                            try
                            {
                                response = await ReadResponseAsync(client, roundTripCts.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                swRoundTrip.Stop();
                                result.roundtrip_ms += Math.Max(0, (long)swRoundTrip.Elapsed.TotalMilliseconds);
                                if (attempt < HookIpcSemantics.Runtime.MaxAttempts - 1)
                                {
                                    await Task.Delay(HookIpcSemantics.Runtime.RetryDelayMs);
                                    continue;
                                }
                                return result;
                            }

                            if (string.IsNullOrWhiteSpace(response))
                            {
                                swRoundTrip.Stop();
                                result.roundtrip_ms += Math.Max(0, (long)swRoundTrip.Elapsed.TotalMilliseconds);
                                if (attempt < HookIpcSemantics.Runtime.MaxAttempts - 1)
                                {
                                    await Task.Delay(HookIpcSemantics.Runtime.RetryDelayMs);
                                    continue;
                                }
                                return result;
                            }
                            try
                            {
                                string? json = ExtractJsonEnvelope(response);
                                if (!string.IsNullOrEmpty(json))
                                {
                                    result.data = JsonSerializer.Deserialize<HookResponse>(json);
                                    swRoundTrip.Stop();
                                    result.roundtrip_ms += Math.Max(0, (long)swRoundTrip.Elapsed.TotalMilliseconds);
                                    return result;
                                }
                                swRoundTrip.Stop();
                                result.roundtrip_ms += Math.Max(0, (long)swRoundTrip.Elapsed.TotalMilliseconds);
                                if (attempt < HookIpcSemantics.Runtime.MaxAttempts - 1)
                                {
                                    await Task.Delay(HookIpcSemantics.Runtime.RetryDelayMs);
                                    continue;
                                }
                                return result;
                            }
                            catch (JsonException)
                            {
                                swRoundTrip.Stop();
                                result.roundtrip_ms += Math.Max(0, (long)swRoundTrip.Elapsed.TotalMilliseconds);
                                if (attempt < HookIpcSemantics.Runtime.MaxAttempts - 1)
                                {
                                    await Task.Delay(HookIpcSemantics.Runtime.RetryDelayMs);
                                    continue;
                                }
                                return result;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[IPC DIAG] Critical Error: {ex.Message}");
                        if (attempt < HookIpcSemantics.Runtime.MaxAttempts - 1)
                        {
                            await Task.Delay(HookIpcSemantics.Runtime.RetryDelayMs);
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

        internal static string BuildRequest(string clsid, string path, string? dllHint)
        {
            string header =
                $"{HookIpcSemantics.Protocol.VersionPrefix}{HookIpcSemantics.Protocol.FieldDelimiter}{HookIpcSemantics.Protocol.ModeAuto}";

            if (string.IsNullOrEmpty(dllHint))
            {
                return $"{header}{HookIpcSemantics.Protocol.FieldDelimiter}{clsid}{HookIpcSemantics.Protocol.FieldDelimiter}{path}";
            }

            return $"{header}{HookIpcSemantics.Protocol.FieldDelimiter}{clsid}{HookIpcSemantics.Protocol.FieldDelimiter}{path}{HookIpcSemantics.Protocol.FieldDelimiter}{dllHint}";
        }

        internal static string? ExtractJsonEnvelope(string response)
        {
            int start = response.IndexOf('{');
            int end = response.LastIndexOf('}');
            if (start == -1 || end == -1 || end <= start) return null;
            return response.Substring(start, end - start + 1);
        }

        private static async Task<string> ReadResponseAsync(NamedPipeClientStream client, CancellationToken token)
        {
            var sb = new StringBuilder(1024);
            byte[] buffer = new byte[4096];
            while (true)
            {
                int read = await client.ReadAsync(buffer, 0, buffer.Length, token);
                if (read <= 0) break;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                bool isMessageComplete = false;
                try
                {
                    isMessageComplete = client.IsMessageComplete;
                }
                catch
                {
                    isMessageComplete = read < buffer.Length;
                }
                if (isMessageComplete) break;
            }
            return sb.ToString().TrimEnd('\0');
        }
    }
}
