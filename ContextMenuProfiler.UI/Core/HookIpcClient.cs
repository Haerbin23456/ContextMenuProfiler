using System;
using System.IO;
using System.IO.Pipes;
using System.Buffers.Binary;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.Text.Json;
using ContextMenuProfiler.UI.Core.Services;

namespace ContextMenuProfiler.UI.Core
{
    public class HookResponse
    {
        public bool success { get; set; }
        public string? code { get; set; }
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
        public string? ipc_error { get; set; }
    }

    public static class HookIpcClient
    {
        internal static readonly SemaphoreSlim IpcLock = new SemaphoreSlim(
            HookIpcSemantics.Runtime.MaxConcurrentCalls,
            HookIpcSemantics.Runtime.MaxConcurrentCalls);

        public static async Task<HookCallResult> GetHookDataAsync(string clsid, string? contextPath = null, string? dllHint = null, string? scanId = null)
        {
            var result = new HookCallResult();
            var swTotal = Stopwatch.StartNew();
            int attempts = 0;
            try
            {
                // Default bait path if none provided
                string path = contextPath ?? Path.Combine(Path.GetTempPath(), HookIpcSemantics.Runtime.ProbeFileName);
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    try { File.WriteAllText(path, HookIpcSemantics.Runtime.ProbeFileContent); } catch {}
                }

                for (int attempt = 0; attempt < HookIpcSemantics.Runtime.MaxAttempts; attempt++)
                {
                    attempts = attempt + 1;
                    try
                    {
                        if (await TryProbeWithLockAsync(clsid, path, dllHint, result, scanId, attempt + 1))
                        {
                            return result;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.ipc_error = "critical_error";
                        LogService.Instance.ErrorEvent(
                            "ipc.probe_critical_error",
                            ex: ex,
                            fields: new Dictionary<string, object?>
                            {
                                ["scan_id"] = scanId,
                                ["clsid"] = clsid,
                                ["attempt"] = attempt + 1,
                                ["ipc_error"] = result.ipc_error
                            });
                    }

                    if (await DelayForRetryAsync(attempt))
                    {
                        continue;
                    }

                    return result;
                }
                return result;
            }
            finally
            {
                swTotal.Stop();
                result.total_ms = Math.Max(0, (long)swTotal.Elapsed.TotalMilliseconds);

                LogService.Instance.InfoEvent(
                    "ipc.probe_completed",
                    fields: new Dictionary<string, object?>
                    {
                        ["scan_id"] = scanId,
                        ["clsid"] = clsid,
                        ["attempts"] = attempts,
                        ["success"] = result.data?.success == true,
                        ["probe_outcome"] = ResolveProbeOutcome(result),
                        ["hook_success"] = result.data?.success,
                        ["hook_code"] = result.data?.code,
                        ["hook_error"] = result.data?.error,
                        ["ipc_error"] = result.ipc_error,
                        ["lock_wait_ms"] = result.lock_wait_ms,
                        ["connect_ms"] = result.connect_ms,
                        ["roundtrip_ms"] = result.roundtrip_ms,
                        ["total_ms"] = result.total_ms
                    });
            }
        }

        private static async Task<bool> TryProbeWithLockAsync(string clsid, string path, string? dllHint, HookCallResult result, string? scanId, int attempt)
        {
            bool hasLock = false;
            var swLock = Stopwatch.StartNew();
            try
            {
                await IpcLock.WaitAsync();
                hasLock = true;
                swLock.Stop();
                result.lock_wait_ms += Math.Max(0, (long)swLock.Elapsed.TotalMilliseconds);
                return await TryProbeOnceAsync(clsid, path, dllHint, result, scanId, attempt);
            }
            finally
            {
                if (swLock.IsRunning)
                {
                    swLock.Stop();
                }

                if (hasLock)
                {
                    IpcLock.Release();
                }
            }
        }

        private static async Task<bool> TryProbeOnceAsync(string clsid, string path, string? dllHint, HookCallResult result, string? scanId, int attempt)
        {
            using var client = new NamedPipeClientStream(".", HookIpcSemantics.Runtime.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            var swConnect = Stopwatch.StartNew();
            try
            {
                await client.ConnectAsync(HookIpcSemantics.Runtime.ConnectTimeoutMs);
            }
            catch (Exception ex)
            {
                swConnect.Stop();
                result.connect_ms += Math.Max(0, (long)swConnect.Elapsed.TotalMilliseconds);
                result.ipc_error = "connect_failed";
                LogService.Instance.WarningEvent(
                    "ipc.probe_connect_failed",
                    ex: ex,
                    fields: new Dictionary<string, object?>
                    {
                        ["scan_id"] = scanId,
                        ["clsid"] = clsid,
                        ["attempt"] = attempt,
                        ["connect_ms"] = result.connect_ms,
                        ["ipc_error"] = result.ipc_error
                    });
                return false;
            }

            swConnect.Stop();
            result.connect_ms += Math.Max(0, (long)swConnect.Elapsed.TotalMilliseconds);

            string requestStr = BuildRequest(clsid, path, dllHint);
            byte[] requestPayload = Encoding.UTF8.GetBytes(requestStr);
            return await TryProbeFramedAsync(client, requestPayload, result, scanId, clsid, attempt);
        }

        private static async Task<bool> TryProbeFramedAsync(NamedPipeClientStream client, byte[] requestPayload, HookCallResult result, string? scanId, string clsid, int attempt)
        {
            var swRoundTrip = Stopwatch.StartNew();
            using var roundTripCts = new CancellationTokenSource(HookIpcSemantics.Runtime.RoundTripTimeoutMs);
            byte[] responsePayload;

            try
            {
                await WriteFrameAsync(client, requestPayload, roundTripCts.Token);
                responsePayload = await ReadFrameAsync(client, roundTripCts.Token);
            }
            catch (Exception)
            {
                result.ipc_error = "framed_exchange_failed";
                CompleteRoundTrip(swRoundTrip, result);

                LogService.Instance.WarningEvent(
                    "ipc.probe_framed_exchange_failed",
                    fields: new Dictionary<string, object?>
                    {
                        ["scan_id"] = scanId,
                        ["clsid"] = clsid,
                        ["attempt"] = attempt,
                        ["roundtrip_ms"] = result.roundtrip_ms,
                        ["ipc_error"] = result.ipc_error
                    });
                return false;
            }

            string response = Encoding.UTF8.GetString(responsePayload).TrimEnd('\0');
            if (!TryDeserializeHookResponse(response, out var parsedResponse))
            {
                result.ipc_error = "invalid_json_response";
                CompleteRoundTrip(swRoundTrip, result);

                LogService.Instance.WarningEvent(
                    "ipc.probe_invalid_json_response",
                    fields: new Dictionary<string, object?>
                    {
                        ["scan_id"] = scanId,
                        ["clsid"] = clsid,
                        ["attempt"] = attempt,
                        ["roundtrip_ms"] = result.roundtrip_ms,
                        ["ipc_error"] = result.ipc_error
                    });
                return false;
            }

            result.ipc_error = null;
            result.data = parsedResponse;
            CompleteRoundTrip(swRoundTrip, result);
            return true;
        }

        private static bool TryDeserializeHookResponse(string response, out HookResponse? parsed)
        {
            parsed = null;
            if (string.IsNullOrWhiteSpace(response))
            {
                return false;
            }

            try
            {
                parsed = JsonSerializer.Deserialize<HookResponse>(response);
                return parsed != null;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        [Obsolete("Use GetHookDataAsync instead")]
        public static async Task<string[]> GetMenuNamesAsync(string clsid, string? contextPath = null)
        {
            var call = await GetHookDataAsync(clsid, contextPath);
            var data = call.data;
            if (data == null || !data.success || string.IsNullOrEmpty(data.names)) return Array.Empty<string>();
            return data.names.Split(HookIpcSemantics.Response.MultiValueDelimiter, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string ResolveProbeOutcome(HookCallResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.ipc_error))
            {
                return "ipc_error";
            }

            if (result.data == null)
            {
                return "unknown";
            }

            return result.data.success ? "ok" : "hook_error";
        }

        private static bool ShouldRetry(int attempt)
        {
            return attempt < HookIpcSemantics.Runtime.MaxAttempts - 1;
        }

        private static async Task<bool> DelayForRetryAsync(int attempt)
        {
            if (!ShouldRetry(attempt))
            {
                return false;
            }

            await Task.Delay(HookIpcSemantics.Runtime.RetryDelayMs);
            return true;
        }

        private static void CompleteRoundTrip(Stopwatch swRoundTrip, HookCallResult result)
        {
            swRoundTrip.Stop();
            result.roundtrip_ms += Math.Max(0, (long)swRoundTrip.Elapsed.TotalMilliseconds);
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

        private static async Task WriteFrameAsync(NamedPipeClientStream client, byte[] payload, CancellationToken token)
        {
            if (payload.Length > HookIpcSemantics.Runtime.MaxRequestBytes)
            {
                throw new InvalidDataException("IPC request is too large.");
            }

            byte[] header = new byte[HookIpcSemantics.Runtime.FrameHeaderBytes];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);

            await client.WriteAsync(header, 0, header.Length, token);
            await client.WriteAsync(payload, 0, payload.Length, token);
            await client.FlushAsync(token);
        }

        private static async Task<byte[]> ReadFrameAsync(NamedPipeClientStream client, CancellationToken token)
        {
            byte[] header = new byte[HookIpcSemantics.Runtime.FrameHeaderBytes];
            if (!await ReadExactAsync(client, header, header.Length, token))
            {
                return Array.Empty<byte>();
            }

            int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (payloadLength < 0 || payloadLength > HookIpcSemantics.Runtime.MaxResponseBytes)
            {
                throw new InvalidDataException("IPC response length is invalid.");
            }

            if (payloadLength == 0)
            {
                return Array.Empty<byte>();
            }

            byte[] payload = new byte[payloadLength];
            if (!await ReadExactAsync(client, payload, payloadLength, token))
            {
                throw new EndOfStreamException("IPC response ended before the frame payload was fully read.");
            }

            return payload;
        }

        private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, int bytesToRead, CancellationToken token)
        {
            int offset = 0;
            while (offset < bytesToRead)
            {
                int read = await stream.ReadAsync(buffer, offset, bytesToRead - offset, token);
                if (read <= 0)
                {
                    return false;
                }

                offset += read;
            }

            return true;
        }
    }
}
