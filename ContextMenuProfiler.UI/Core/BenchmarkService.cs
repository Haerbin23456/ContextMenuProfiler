using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using ContextMenuProfiler.UI.Core.Helpers;
using ContextMenuProfiler.UI.Core.Services;

namespace ContextMenuProfiler.UI.Core
{
    public class BenchmarkResult
    {
        public string Name { get; set; } = "";
        public Guid? Clsid { get; set; }
        public BenchmarkStatus Status { get; set; } = BenchmarkSemantics.Status.Unknown;
        public string Type { get; set; } = BenchmarkSemantics.Type.Com; // Legacy COM, UWP, Static
        public string? Path { get; set; }
        public List<RegistryHandlerInfo> RegistryEntries { get; set; } = new List<RegistryHandlerInfo>();
        public long TotalTime { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string? IconLocation { get; set; }
        public string? BinaryPath { get; set; }
        public string? DetailedStatus { get; set; }
        public long InitTime { get; set; }
        public long CreateTime { get; set; }
        public long QueryTime { get; set; }
        public long WallClockTime { get; set; }
        public long LockWaitTime { get; set; }
        public long ConnectTime { get; set; }
        public long IpcRoundTripTime { get; set; }
        public long ScanOrder { get; set; }
        
        // Extended Info
        public string? PackageName { get; set; }
        public string? Version { get; set; }
        public string? InterfaceType { get; set; } // IContextMenu, IExplorerCommand, Static
        public string? ThreadingModel { get; set; }
        public string? FriendlyName { get; set; }
        public string? IconSource { get; set; }
        public string? LocationSummary { get; set; }
        public string Category { get; set; } = BenchmarkSemantics.Category.File;
        public List<string>? ObservedMenuDisplayNames { get; set; }
    }

    internal class ClsidMetadata
    {
        public string Name { get; set; } = "";
        public string BinaryPath { get; set; } = "";
        public string ThreadingModel { get; set; } = "";
        public string FriendlyName { get; set; } = "";
    }

    public class BenchmarkService
    {
        private static readonly bool SkipKnownUnstableHandlers =
            BenchmarkSemantics.IsSkipUnstableHandlersEnabled();

        public List<BenchmarkResult> RunSystemBenchmark(ScanMode mode = ScanMode.Targeted)
        {
            return Task.Run(() => RunSystemBenchmarkAsync(mode)).GetAwaiter().GetResult();
        }

        public List<BenchmarkResult> RunSystemBenchmark(ScanMode mode, string? scanId)
        {
            return Task.Run(() => RunSystemBenchmarkAsync(mode, null, scanId)).GetAwaiter().GetResult();
        }

        public async Task<List<BenchmarkResult>> RunSystemBenchmarkAsync(ScanMode mode = ScanMode.Targeted, IProgress<BenchmarkResult>? progress = null)
        {
            return await RunSystemBenchmarkAsync(mode, progress, null);
        }

        public async Task<List<BenchmarkResult>> RunSystemBenchmarkAsync(ScanMode mode, IProgress<BenchmarkResult>? progress, string? scanId)
        {
            using var fileContext = ShellTestContext.Create(false);
            var scanSw = Stopwatch.StartNew();

            var registryHandlers = RegistryScanner.ScanHandlers(mode);
            var staticVerbs = RegistryScanner.ScanStaticVerbs();

            LogService.Instance.InfoEvent(
                "scan.registry_scan_completed",
                fields: new Dictionary<string, object?>
                {
                    ["scan_id"] = scanId,
                    ["scan_mode"] = mode.ToString(),
                    ["scope"] = "system",
                    ["com_handler_count"] = registryHandlers.Count,
                    ["static_verb_group_count"] = staticVerbs.Count
                });

            var results = await RunBenchmarkCoreAsync(registryHandlers, staticVerbs, null, fileContext.Path, progress, scanId);
            scanSw.Stop();

            LogService.Instance.InfoEvent(
                "scan.service_completed",
                fields: BuildSummaryFields(results, scanSw.ElapsedMilliseconds, scanId, mode.ToString(), "system"));

            return results;
        }

        public List<BenchmarkResult> RunBenchmark(string targetPath, string? scanId)
        {
            return Task.Run(() => RunBenchmarkAsync(targetPath, null, scanId)).GetAwaiter().GetResult();
        }

        public List<BenchmarkResult> RunBenchmark(string targetPath)
        {
            return Task.Run(() => RunBenchmarkAsync(targetPath)).GetAwaiter().GetResult();
        }

        public async Task<List<BenchmarkResult>> RunBenchmarkAsync(string targetPath, IProgress<BenchmarkResult>? progress = null)
        {
            return await RunBenchmarkAsync(targetPath, progress, null);
        }

        public async Task<List<BenchmarkResult>> RunBenchmarkAsync(string targetPath, IProgress<BenchmarkResult>? progress, string? scanId)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                LogService.Instance.Warning("RunBenchmarkAsync received an empty target path; returning no results.");
                return new List<BenchmarkResult>();
            }

            var scanSw = Stopwatch.StartNew();

            var registryHandlers = RegistryScanner.ScanHandlersForPath(targetPath);
            var staticVerbs = RegistryScanner.ScanStaticVerbsForPath(targetPath);

            LogService.Instance.InfoEvent(
                "scan.registry_scan_completed",
                fields: new Dictionary<string, object?>
                {
                    ["scan_id"] = scanId,
                    ["scan_mode"] = "file",
                    ["scope"] = "file",
                    ["target_path"] = targetPath,
                    ["com_handler_count"] = registryHandlers.Count,
                    ["static_verb_group_count"] = staticVerbs.Count
                });

            var results = await RunBenchmarkCoreAsync(registryHandlers, staticVerbs, targetPath, targetPath, progress, scanId);
            scanSw.Stop();

            LogService.Instance.InfoEvent(
                "scan.service_completed",
                fields: BuildSummaryFields(results, scanSw.ElapsedMilliseconds, scanId, "file", "file", targetPath));

            return results;
        }

        private async Task<List<BenchmarkResult>> RunBenchmarkCoreAsync(
            Dictionary<Guid, List<RegistryHandlerInfo>> registryHandlers,
            Dictionary<string, List<string>> staticVerbs,
            string? packageTargetPath,
            string hookContextPath,
            IProgress<BenchmarkResult>? progress,
            string? scanId)
        {
            var coreSw = Stopwatch.StartNew();
            var allResults = new ConcurrentBag<BenchmarkResult>();
            var resultsMap = new ConcurrentDictionary<Guid, BenchmarkResult>();
            var semaphore = new SemaphoreSlim(BenchmarkSemantics.Runtime.MaxParallelProbeTasks);

            LogService.Instance.InfoEvent(
                "scan.benchmark_core_started",
                fields: new Dictionary<string, object?>
                {
                    ["scan_id"] = scanId,
                    ["com_handler_count"] = registryHandlers.Count,
                    ["static_verb_group_count"] = staticVerbs.Count,
                    ["has_package_target"] = !string.IsNullOrWhiteSpace(packageTargetPath)
                });

            var phaseSw = Stopwatch.StartNew();
            await ProcessComHandlersAsync(registryHandlers, hookContextPath, allResults, resultsMap, semaphore, progress, scanId);
            phaseSw.Stop();
            LogService.Instance.InfoEvent(
                "scan.phase_completed",
                fields: new Dictionary<string, object?>
                {
                    ["scan_id"] = scanId,
                    ["phase"] = "com_handlers",
                    ["duration_ms"] = phaseSw.ElapsedMilliseconds,
                    ["result_count"] = allResults.Count
                });

            phaseSw.Restart();
            ProcessStaticVerbEntries(staticVerbs, allResults, progress, scanId);
            phaseSw.Stop();
            LogService.Instance.InfoEvent(
                "scan.phase_completed",
                fields: new Dictionary<string, object?>
                {
                    ["scan_id"] = scanId,
                    ["phase"] = "static_verbs",
                    ["duration_ms"] = phaseSw.ElapsedMilliseconds,
                    ["result_count"] = allResults.Count
                });

            phaseSw.Restart();
            await ProcessPackagedExtensionsAsync(packageTargetPath, hookContextPath, allResults, resultsMap, semaphore, progress, scanId);
            phaseSw.Stop();
            LogService.Instance.InfoEvent(
                "scan.phase_completed",
                fields: new Dictionary<string, object?>
                {
                    ["scan_id"] = scanId,
                    ["phase"] = "packaged_extensions",
                    ["duration_ms"] = phaseSw.ElapsedMilliseconds,
                    ["result_count"] = allResults.Count
                });

            coreSw.Stop();
            LogService.Instance.InfoEvent(
                "scan.benchmark_core_completed",
                fields: new Dictionary<string, object?>
                {
                    ["scan_id"] = scanId,
                    ["duration_ms"] = coreSw.ElapsedMilliseconds,
                    ["result_count"] = allResults.Count
                });

            return allResults.ToList();
        }

        private async Task ProcessComHandlersAsync(
            Dictionary<Guid, List<RegistryHandlerInfo>> registryHandlers,
            string hookContextPath,
            ConcurrentBag<BenchmarkResult> allResults,
            ConcurrentDictionary<Guid, BenchmarkResult> resultsMap,
            SemaphoreSlim semaphore,
            IProgress<BenchmarkResult>? progress,
            string? scanId)
        {
            var comTasks = registryHandlers.Select(clsidEntry =>
                RunWithSemaphoreAsync(semaphore, async () =>
                {
                    var clsid = clsidEntry.Key;
                    var handlerInfos = clsidEntry.Value;
                    var meta = QueryClsidMetadata(clsid);
                    var result = new BenchmarkResult
                    {
                        Clsid = clsid,
                        Type = BenchmarkSemantics.Type.Com,
                        RegistryEntries = handlerInfos.ToList(),
                        Name = meta.Name,
                        BinaryPath = meta.BinaryPath,
                        ThreadingModel = meta.ThreadingModel,
                        FriendlyName = meta.FriendlyName
                    };

                    if (string.IsNullOrEmpty(result.Name))
                    {
                        result.Name = string.Format(
                            LocalizationService.Instance["Dashboard.Value.UnknownWithClsid"],
                            clsid);
                    }

                    if (!resultsMap.TryAdd(clsid, result))
                    {
                        return;
                    }

                    result.Category = DetermineCategory(result.RegistryEntries.Select(e => e.Location));
                    await ProcessMeasuredResultAsync(result, hookContextPath, allResults, progress, scanId);
                }));

            await Task.WhenAll(comTasks);
        }

        private void ProcessStaticVerbEntries(
            Dictionary<string, List<string>> staticVerbs,
            ConcurrentBag<BenchmarkResult> allResults,
            IProgress<BenchmarkResult>? progress,
            string? scanId)
        {
            foreach (var verbEntry in staticVerbs)
            {
                var verbResult = CreateStaticVerbResult(verbEntry.Key, verbEntry.Value);
                if (verbResult == null)
                {
                    continue;
                }

                AddAndReportResult(allResults, verbResult, progress);
                LogService.Instance.InfoEvent(
                    "scan.item_processed",
                    fields: BuildItemFields(verbResult, scanId, "static"));
            }
        }

        private async Task ProcessPackagedExtensionsAsync(
            string? packageTargetPath,
            string hookContextPath,
            ConcurrentBag<BenchmarkResult> allResults,
            ConcurrentDictionary<Guid, BenchmarkResult> resultsMap,
            SemaphoreSlim semaphore,
            IProgress<BenchmarkResult>? progress,
            string? scanId)
        {
            var uwpTasks = PackageScanner.ScanPackagedExtensions(packageTargetPath)
                .Where(r => r.Clsid.HasValue)
                .Select(uwpResult =>
                    RunWithSemaphoreAsync(semaphore, async () =>
                    {
                        if (!resultsMap.TryAdd(uwpResult.Clsid!.Value, uwpResult))
                        {
                            return;
                        }

                        uwpResult.Category = BenchmarkSemantics.Category.Uwp;
                        await ProcessMeasuredResultAsync(uwpResult, hookContextPath, allResults, progress, scanId);
                    }));

            await Task.WhenAll(uwpTasks);
        }

        private static async Task RunWithSemaphoreAsync(SemaphoreSlim semaphore, Func<Task> action)
        {
            await semaphore.WaitAsync();
            try
            {
                await action();
            }
            finally
            {
                semaphore.Release();
            }
        }

        private static void AddAndReportResult(
            ConcurrentBag<BenchmarkResult> allResults,
            BenchmarkResult result,
            IProgress<BenchmarkResult>? progress)
        {
            allResults.Add(result);
            progress?.Report(result);
        }

        private async Task ProcessMeasuredResultAsync(
            BenchmarkResult result,
            string hookContextPath,
            ConcurrentBag<BenchmarkResult> allResults,
            IProgress<BenchmarkResult>? progress,
            string? scanId)
        {
            await EnrichBenchmarkResultAsync(result, hookContextPath, scanId);
            result.IsEnabled = ResolveEnabledState(result);
            result.LocationSummary = ResolveLocationSummary(result);
            AddAndReportResult(allResults, result, progress);

            LogService.Instance.InfoEvent(
                "scan.item_processed",
                fields: BuildItemFields(result, scanId, "measured"));
        }

        private static bool ResolveEnabledState(BenchmarkResult result)
        {
            bool isBlocked = result.Clsid.HasValue && ExtensionManager.IsExtensionBlocked(result.Clsid.Value);
            if (isBlocked)
            {
                return false;
            }

            if (BenchmarkSemantics.IsRegistryManagedExtensionType(result.Type))
            {
                return !result.RegistryEntries.Any(e => BenchmarkSemantics.IsDisabledRegistryLocation(e.Location));
            }

            return true;
        }

        private static string ResolveLocationSummary(BenchmarkResult result)
        {
            if (BenchmarkSemantics.IsPackagedExtensionType(result.Type))
            {
                return BenchmarkSemantics.LocationSummary.ModernShellUwp;
            }

            return string.Join(", ", result.RegistryEntries.Select(e => e.Location).Distinct());
        }

        private BenchmarkResult? CreateStaticVerbResult(string key, List<string> paths)
        {
            if (!BenchmarkSemantics.TryParseStaticVerbUniqueKey(key, out string name, out string command))
            {
                LogService.Instance.Warning($"Skip malformed static verb entry key: '{key}'");
                return null;
            }

            var result = new BenchmarkResult
            {
                Name = name,
                Type = BenchmarkSemantics.Type.Static,
                Status = BenchmarkSemantics.Status.StaticNotMeasured,
                BinaryPath = ExtractExecutablePath(command),
                RegistryEntries = paths.Select(p => new RegistryHandlerInfo
                {
                    Path = p,
                    Location = BenchmarkSemantics.BuildStaticVerbRegistryLocation(p)
                }).ToList(),
                InterfaceType = BenchmarkSemantics.InterfaceType.StaticVerb,
                DetailedStatus = LocalizationService.Instance["Dashboard.Detail.StaticNotMeasured"],
                TotalTime = 0,
                Category = BenchmarkSemantics.Category.Static
            };

            result.IsEnabled = !paths.Any(BenchmarkSemantics.IsStaticVerbRegistryPathDisabled);
            result.LocationSummary = string.Join(", ", result.RegistryEntries.Select(e => e.Location).Distinct());
            result.IconLocation = ResolveStaticVerbIcon(paths.First(), result.BinaryPath);
            return result;
        }

        private async Task EnrichBenchmarkResultAsync(BenchmarkResult result, string contextPath, string? scanId)
        {
            if (!result.Clsid.HasValue) return;

            if (SkipKnownUnstableHandlers && IsKnownUnstableHandler(result))
            {
                MarkAsSkippedKnownUnstable(result);
                return;
            }

            // Check for Orphaned / Missing DLL
            if (!string.IsNullOrEmpty(result.BinaryPath) && !File.Exists(result.BinaryPath))
            {
                result.Status = BenchmarkSemantics.Status.OrphanedMissingDll;
                result.DetailedStatus = string.Format(
                    LocalizationService.Instance["Dashboard.Detail.OrphanedMissingDll"],
                    result.BinaryPath);
            }

            var hookCall = await HookIpcClient.GetHookDataAsync(result.Clsid.Value.ToString("B"), contextPath, result.BinaryPath, scanId);
            var hookData = hookCall.data;
            result.WallClockTime = hookCall.total_ms;
            result.LockWaitTime = hookCall.lock_wait_ms;
            result.ConnectTime = hookCall.connect_ms;
            result.IpcRoundTripTime = hookCall.roundtrip_ms;

            if (hookData?.success == true)
            {
                ApplyHookSuccessResult(result, hookData);
            }
            else if (hookData != null)
            {
                ApplyHookErrorResult(result, hookData);
            }
            else
            {
                ApplyHookUnavailableFallback(result, hookCall.roundtrip_ms, hookCall.ipc_error);
            }
        }

        private static void MarkAsSkippedKnownUnstable(BenchmarkResult result)
        {
            result.Status = BenchmarkSemantics.Status.SkippedKnownUnstable;
            result.DetailedStatus = LocalizationService.Instance["Dashboard.Detail.SkippedKnownUnstable"];
            result.InterfaceType = BenchmarkSemantics.InterfaceType.Skipped;
            result.CreateTime = 0;
            result.InitTime = 0;
            result.QueryTime = 0;
            result.TotalTime = 0;
        }

        private static void ApplyHookSuccessResult(BenchmarkResult result, HookResponse hookData)
        {
            result.InterfaceType = hookData.@interface;
            if (!string.IsNullOrEmpty(hookData.names))
            {
                result.ObservedMenuDisplayNames = ParseHookMenuDisplayNames(hookData.names);

                if (BenchmarkSemantics.IsRegistryManagedExtensionType(result.Type))
                {
                    result.Name = string.Join(
                        ", ",
                        result.ObservedMenuDisplayNames);
                }

                if (result.Status == BenchmarkSemantics.Status.Unknown)
                {
                    result.Status = BenchmarkSemantics.Status.VerifiedViaHook;
                }
            }
            else if (result.Status == BenchmarkSemantics.Status.Unknown || result.Status == BenchmarkSemantics.Status.Ok)
            {
                result.Status = BenchmarkSemantics.Status.HookLoadedNoMenu;
                result.DetailedStatus = LocalizationService.Instance["Dashboard.Detail.HookLoadedNoMenu"];
            }

            string? winnerIcon = ResolveHookIconLocation(hookData);
            if (winnerIcon != null)
            {
                result.IconLocation = winnerIcon;
            }

            result.CreateTime = (long)hookData.create_ms;
            result.InitTime = (long)hookData.init_ms;
            result.QueryTime = (long)hookData.query_ms;
            result.TotalTime = result.CreateTime + result.InitTime + result.QueryTime;
        }

        private static List<string> ParseHookMenuDisplayNames(string names)
        {
            return names
                .Split(HookIpcSemantics.Response.MultiValueDelimiter)
                .Select(n => n.Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static string? ResolveHookIconLocation(HookResponse hookData)
        {
            if (!string.IsNullOrEmpty(hookData.reg_icon)
                && (hookData.reg_icon.Contains(BenchmarkSemantics.IconLocation.IconResourceIndexSeparator)
                    || hookData.reg_icon.EndsWith(BenchmarkSemantics.IconFileExtension.Ico, StringComparison.OrdinalIgnoreCase)))
            {
                return hookData.reg_icon;
            }

            if (string.IsNullOrEmpty(hookData.icons))
            {
                return null;
            }

            return hookData.icons
                .Split(HookIpcSemantics.Response.MultiValueDelimiter)
                .FirstOrDefault(i =>
                    !string.IsNullOrEmpty(i)
                    && !string.Equals(i, HookIpcSemantics.Response.NoIconToken, StringComparison.OrdinalIgnoreCase));
        }

        private static void ApplyHookErrorResult(BenchmarkResult result, HookResponse hookData)
        {
            string hookError = BuildHookErrorDetails(hookData.error, hookData.code);

            if (IsTimeoutLikeHookFailure(hookData))
            {
                result.Status = BenchmarkSemantics.Status.IpcTimeout;
                result.DetailedStatus = string.Format(
                    LocalizationService.Instance["Dashboard.Detail.HookProbeTimeoutWithError"],
                    hookError);
                return;
            }

            result.Status = BenchmarkSemantics.Status.LoadError;
            result.DetailedStatus = string.Format(
                LocalizationService.Instance["Dashboard.Detail.HookLoadErrorWithError"],
                hookError);
        }

        private static void ApplyHookUnavailableFallback(BenchmarkResult result, long roundTripMs, string? ipcError)
        {
            if (result.Status == BenchmarkSemantics.Status.LoadError || result.Status == BenchmarkSemantics.Status.OrphanedMissingDll)
            {
                return;
            }

            if (roundTripMs >= BenchmarkSemantics.Runtime.IpcTimeoutLikeRoundtripThresholdMs)
            {
                result.Status = BenchmarkSemantics.Status.IpcTimeout;
                result.DetailedStatus = AttachIpcReason(
                    LocalizationService.Instance["Dashboard.Detail.HookResponseTimeoutFallback"],
                    ipcError);
                return;
            }

            result.Status = BenchmarkSemantics.Status.RegistryFallback;
            result.DetailedStatus = AttachIpcReason(
                LocalizationService.Instance["Dashboard.Detail.HookUnavailableFallback"],
                ipcError);
        }

        private static bool IsTimeoutLikeHookFailure(HookResponse hookData)
        {
            if (string.Equals(hookData.code, "E_TIMEOUT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hookData.code, "E_REQ_HEADER", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hookData.code, "E_REQ_READ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return BenchmarkSemantics.IsTimeoutLikeError(hookData.error);
        }

        private static string BuildHookErrorDetails(string? error, string? code)
        {
            string resolvedError = string.IsNullOrWhiteSpace(error)
                ? LocalizationService.Instance["Dashboard.Value.Unknown"]
                : error;

            if (string.IsNullOrWhiteSpace(code))
            {
                return resolvedError;
            }

            return $"{code}: {resolvedError}";
        }

        private static string AttachIpcReason(string detail, string? ipcError)
        {
            if (string.IsNullOrWhiteSpace(ipcError))
            {
                return detail;
            }

            return $"{detail} ({ipcError})";
        }

        private static bool IsKnownUnstableHandler(BenchmarkResult result)
        {
            if (BenchmarkSemantics.ContainsKnownUnstableHandlerToken(result.Name))
            {
                return true;
            }

            if (BenchmarkSemantics.ContainsKnownUnstableHandlerToken(result.FriendlyName))
            {
                return true;
            }

            if (result.RegistryEntries != null)
            {
                foreach (var entry in result.RegistryEntries)
                {
                    if (BenchmarkSemantics.ContainsKnownUnstableHandlerToken(entry.Location)
                        || BenchmarkSemantics.ContainsKnownUnstableHandlerToken(entry.Path))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private ClsidMetadata QueryClsidMetadata(Guid clsid, int depth = 0)
        {
            if (depth >= 3)
            {
                return new ClsidMetadata();
            }

            string clsidB = clsid.ToString("B");
            var meta = TryQueryRegisteredClsidMetadata(clsid, clsidB, depth)
                ?? TryQueryPackagedClsidMetadata(clsid, clsidB)
                ?? new ClsidMetadata();

            return NormalizeClsidMetadata(meta);
        }

        private ClsidMetadata? TryQueryRegisteredClsidMetadata(Guid clsid, string clsidB, int depth)
        {
            using var key = ShellUtils.OpenClsidKey(clsidB);
            if (key == null)
            {
                return null;
            }

            var meta = new ClsidMetadata();
            meta.Name = key.GetValue("") as string ?? "";
            meta.FriendlyName = key.GetValue(ComRegistrySemantics.FriendlyNameValueName) as string ?? "";
            PopulateFromInprocServerKey(key, meta);

            if (string.IsNullOrEmpty(meta.BinaryPath))
            {
                PopulateFromTreatAsAlias(clsid, key, meta, depth);
            }

            if (string.IsNullOrEmpty(meta.BinaryPath))
            {
                PopulateFromAppIdSurrogate(key, meta);
            }

            return meta;
        }

        private ClsidMetadata? TryQueryPackagedClsidMetadata(Guid clsid, string clsidB)
        {
            using var pkgKey = Registry.ClassesRoot.OpenSubKey(ComRegistrySemantics.BuildPackagedComClassIndexPath(clsidB));
            string? packageFullName = pkgKey?.GetValue("") as string;
            if (string.IsNullOrEmpty(packageFullName))
            {
                return null;
            }

            return new ClsidMetadata
            {
                BinaryPath = ResolvePackageDllPath(packageFullName, clsid) ?? "",
                Name = QueryPackagedDisplayName(clsidB) ?? ""
            };
        }

        private static ClsidMetadata NormalizeClsidMetadata(ClsidMetadata meta)
        {
            meta.Name = ShellUtils.ResolveMuiString(meta.Name);
            if (string.IsNullOrEmpty(meta.Name) && !string.IsNullOrEmpty(meta.BinaryPath))
            {
                meta.Name = Path.GetFileName(meta.BinaryPath);
            }

            return meta;
        }

        private static void PopulateFromInprocServerKey(RegistryKey clsidKey, ClsidMetadata meta)
        {
            using var serverKey = clsidKey.OpenSubKey(ComRegistrySemantics.InprocServer32SubKeyName);
            if (serverKey == null)
            {
                return;
            }

            meta.BinaryPath = serverKey.GetValue("") as string ?? "";
            meta.ThreadingModel = serverKey.GetValue(ComRegistrySemantics.ThreadingModelValueName) as string ?? "";
        }

        private void PopulateFromTreatAsAlias(Guid originalClsid, RegistryKey clsidKey, ClsidMetadata meta, int depth)
        {
            string? treatAs = clsidKey.OpenSubKey(ComRegistrySemantics.TreatAsSubKeyName)?.GetValue("") as string;
            if (string.IsNullOrEmpty(treatAs) || !Guid.TryParse(treatAs, out Guid otherGuid) || otherGuid == originalClsid)
            {
                return;
            }

            var otherMeta = QueryClsidMetadata(otherGuid, depth + 1);
            if (string.IsNullOrEmpty(meta.Name))
            {
                meta.Name = otherMeta.Name;
            }

            meta.BinaryPath = otherMeta.BinaryPath;
            meta.ThreadingModel = otherMeta.ThreadingModel;
        }

        private static void PopulateFromAppIdSurrogate(RegistryKey clsidKey, ClsidMetadata meta)
        {
            string? appId = clsidKey.GetValue(ComRegistrySemantics.AppIdValueName) as string;
            if (string.IsNullOrEmpty(appId))
            {
                return;
            }

            using var appKey = Registry.ClassesRoot.OpenSubKey(ComRegistrySemantics.BuildAppIdPath(appId));
            string? dllSurrogate = appKey?.GetValue(ComRegistrySemantics.DllSurrogateValueName) as string;
            meta.BinaryPath = dllSurrogate != null && string.IsNullOrEmpty(dllSurrogate)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), ComRegistrySemantics.DllHostExecutableName)
                : (dllSurrogate ?? "");
        }

        private string? QueryPackagedDisplayName(string clsidB)
        {
            try
            {
                using (var pkgKey = Registry.ClassesRoot.OpenSubKey(ComRegistrySemantics.PackagedComPackagePrefix))
                {
                    if (pkgKey == null) return null;
                    foreach (var pkgName in pkgKey.GetSubKeyNames())
                    {
                        using (var clsKey = pkgKey.OpenSubKey(ComRegistrySemantics.BuildPackagedComPackageClassPath(pkgName, clsidB)))
                        {
                            string? name = clsKey?.GetValue(ComRegistrySemantics.DisplayNameValueName) as string;
                            if (!string.IsNullOrEmpty(name)) return name;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"Failed to resolve packaged display name for CLSID {clsidB}", ex);
            }
            return null;
        }

        private string? ExtractExecutablePath(string command)
        {
            if (string.IsNullOrEmpty(command)) return null;
            
            // Handle quoted paths: "C:\Path\To\Exe.exe" --args
            if (command.StartsWith("\""))
            {
                int nextQuote = command.IndexOf("\"", 1);
                if (nextQuote > 0) return command.Substring(1, nextQuote - 1);
            }

            // Handle unquoted paths with spaces: C:\Path\To\Exe.exe --args
            // This is harder, we'll just take the first part until space
            string firstPart = command.Split(' ')[0];
            if (File.Exists(firstPart)) return firstPart;

            return command;
        }

        private string? ResolveStaticVerbIcon(string regPath, string? exePath)
        {
            try
            {
                using (var key = Registry.ClassesRoot.OpenSubKey(regPath))
                {
                    string? icon = key?.GetValue(BenchmarkSemantics.StaticVerb.IconValueName) as string;
                    if (!string.IsNullOrEmpty(icon)) return icon;
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"ResolveStaticVerbIcon failed for {regPath}", ex);
            }
            return exePath;
        }

        private string? ResolvePackageDllPath(string packageFullName, Guid clsid)
        {
            try
            {
                // Look up package installation path
                using (var key = Registry.ClassesRoot.OpenSubKey(ComRegistrySemantics.BuildPackageRepositoryPath(packageFullName)))
                {
                    string? installPath = key?.GetValue(ComRegistrySemantics.PackageInstallPathValueName) as string;
                    if (string.IsNullOrEmpty(installPath)) return null;

                    // Now find the relative DLL path from PackagedCom\Package
                    // We need the short name (Package Family Name or part of full name)
                    string packageId = ComRegistrySemantics.ExtractPackageIdPrefix(packageFullName);
                    using (var pkgKey = Registry.ClassesRoot.OpenSubKey(ComRegistrySemantics.PackagedComPackagePrefix))
                    {
                        if (pkgKey != null)
                        {
                            foreach (var name in pkgKey.GetSubKeyNames())
                            {
                                if (name.StartsWith(packageId, StringComparison.OrdinalIgnoreCase))
                                {
                                    using (var clsKey = pkgKey.OpenSubKey(ComRegistrySemantics.BuildPackagedComPackageClassPath(name, clsid.ToString("B"))))
                                    {
                                        string? relDllPath = clsKey?.GetValue(ComRegistrySemantics.DllPathValueName) as string;
                                        if (!string.IsNullOrEmpty(relDllPath))
                                        {
                                            return Path.Combine(installPath, relDllPath);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    return installPath; // Fallback to root
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"ResolvePackageDllPath failed for {packageFullName}", ex);
                return null;
            }
        }

        public long RunRealShellBenchmark(string? filePath = null)
            => BenchmarkSemantics.Runtime.RealShellBenchmarkUnsupportedMs;

        private string DetermineCategory(IEnumerable<string> locations)
        {
            return BenchmarkSemantics.ResolveCategoryFromLocations(locations);
        }

        private static Dictionary<string, object?> BuildSummaryFields(
            List<BenchmarkResult> results,
            long durationMs,
            string? scanId,
            string scanMode,
            string scope,
            string? targetPath = null)
        {
            int measuredCount = results.Count(r => r.TotalTime > 0);
            int fallbackCount = results.Count(r => BenchmarkSemantics.IsFallbackLikeStatus(r.Status));

            return new Dictionary<string, object?>
            {
                ["scan_id"] = scanId,
                ["scan_mode"] = scanMode,
                ["scope"] = scope,
                ["target_path"] = targetPath,
                ["duration_ms"] = durationMs,
                ["result_count"] = results.Count,
                ["measured_count"] = measuredCount,
                ["fallback_count"] = fallbackCount
            };
        }

        private static Dictionary<string, object?> BuildItemFields(BenchmarkResult result, string? scanId, string source)
        {
            return new Dictionary<string, object?>
            {
                ["scan_id"] = scanId,
                ["source"] = source,
                ["clsid"] = result.Clsid?.ToString("B"),
                ["name"] = result.Name,
                ["friendly_name"] = result.FriendlyName,
                ["observed_menu_display_names"] = result.ObservedMenuDisplayNames,
                ["observed_menu_display_name_count"] = result.ObservedMenuDisplayNames?.Count ?? 0,
                ["type"] = result.Type,
                ["status"] = result.Status,
                ["total_ms"] = result.TotalTime,
                ["create_ms"] = result.CreateTime,
                ["init_ms"] = result.InitTime,
                ["query_ms"] = result.QueryTime,
                ["wall_clock_ms"] = result.WallClockTime,
                ["lock_wait_ms"] = result.LockWaitTime,
                ["connect_ms"] = result.ConnectTime,
                ["roundtrip_ms"] = result.IpcRoundTripTime,
                ["is_enabled"] = result.IsEnabled,
                ["category"] = result.Category,
                ["registry_entry_count"] = result.RegistryEntries?.Count ?? 0
            };
        }
    }
}
