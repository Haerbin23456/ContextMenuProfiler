using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        public string Status { get; set; } = BenchmarkSemantics.Status.Unknown;
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

        public async Task<List<BenchmarkResult>> RunSystemBenchmarkAsync(ScanMode mode = ScanMode.Targeted, IProgress<BenchmarkResult>? progress = null)
        {
            using var fileContext = ShellTestContext.Create(false);
            var registryHandlers = RegistryScanner.ScanHandlers(mode);
            var staticVerbs = RegistryScanner.ScanStaticVerbs();
            return await RunBenchmarkCoreAsync(registryHandlers, staticVerbs, null, fileContext.Path, progress);
        }

        public List<BenchmarkResult> RunBenchmark(string targetPath)
        {
            return Task.Run(() => RunBenchmarkAsync(targetPath)).GetAwaiter().GetResult();
        }

        public async Task<List<BenchmarkResult>> RunBenchmarkAsync(string targetPath, IProgress<BenchmarkResult>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return await RunSystemBenchmarkAsync(ScanMode.Targeted, progress);
            }

            var registryHandlers = RegistryScanner.ScanHandlersForPath(targetPath);
            var staticVerbs = RegistryScanner.ScanStaticVerbsForPath(targetPath);
            return await RunBenchmarkCoreAsync(registryHandlers, staticVerbs, targetPath, targetPath, progress);
        }

        private async Task<List<BenchmarkResult>> RunBenchmarkCoreAsync(
            Dictionary<Guid, List<RegistryHandlerInfo>> registryHandlers,
            Dictionary<string, List<string>> staticVerbs,
            string? packageTargetPath,
            string hookContextPath,
            IProgress<BenchmarkResult>? progress)
        {
            var allResults = new ConcurrentBag<BenchmarkResult>();
            var resultsMap = new ConcurrentDictionary<Guid, BenchmarkResult>();
            var semaphore = new SemaphoreSlim(BenchmarkSemantics.Runtime.MaxParallelProbeTasks);

            var comTasks = registryHandlers.Select(async clsidEntry =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var clsid = clsidEntry.Key;
                    var handlerInfos = clsidEntry.Value;

                    if (resultsMap.ContainsKey(clsid)) return;
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

                    resultsMap[clsid] = result;
                    result.Category = DetermineCategory(result.RegistryEntries.Select(e => e.Location));

                    await EnrichBenchmarkResultAsync(result, hookContextPath);

                    bool isBlocked = ExtensionManager.IsExtensionBlocked(clsid);
                    bool hasDisabledPath = result.RegistryEntries.Any(e => BenchmarkSemantics.IsDisabledRegistryLocation(e.Location));
                    result.IsEnabled = !isBlocked && !hasDisabledPath;
                    result.LocationSummary = string.Join(", ", result.RegistryEntries.Select(e => e.Location).Distinct());

                    allResults.Add(result);
                    progress?.Report(result);
                }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(comTasks);

            foreach (var verbEntry in staticVerbs)
            {
                string key = verbEntry.Key;
                var paths = verbEntry.Value;

                if (!BenchmarkSemantics.TryParseStaticVerbUniqueKey(key, out string name, out string command))
                {
                    LogService.Instance.Warning($"Skip malformed static verb entry key: '{key}'");
                    continue;
                }

                var verbResult = new BenchmarkResult
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

                bool anyDisabled = paths.Any(BenchmarkSemantics.IsStaticVerbRegistryPathDisabled);
                verbResult.IsEnabled = !anyDisabled;
                verbResult.LocationSummary = string.Join(", ", verbResult.RegistryEntries.Select(e => e.Location).Distinct());
                verbResult.IconLocation = ResolveStaticVerbIcon(paths.First(), verbResult.BinaryPath);

                allResults.Add(verbResult);
                progress?.Report(verbResult);
            }

            var uwpTasks = PackageScanner.ScanPackagedExtensions(packageTargetPath)
                .Where(r => r.Clsid.HasValue && !resultsMap.ContainsKey(r.Clsid.Value))
                .Select(async uwpResult =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        uwpResult.Category = BenchmarkSemantics.Category.Uwp;
                        await EnrichBenchmarkResultAsync(uwpResult, hookContextPath);

                        uwpResult.IsEnabled = !ExtensionManager.IsExtensionBlocked(uwpResult.Clsid!.Value);
                        uwpResult.LocationSummary = BenchmarkSemantics.LocationSummary.ModernShellUwp;

                        allResults.Add(uwpResult);
                        progress?.Report(uwpResult);
                    }
                    finally { semaphore.Release(); }
                });

            await Task.WhenAll(uwpTasks);

            return allResults.ToList();
        }

        private async Task EnrichBenchmarkResultAsync(BenchmarkResult result, string contextPath)
        {
            if (!result.Clsid.HasValue) return;

            if (SkipKnownUnstableHandlers && IsKnownUnstableHandler(result))
            {
                result.Status = BenchmarkSemantics.Status.SkippedKnownUnstable;
                result.DetailedStatus = LocalizationService.Instance["Dashboard.Detail.SkippedKnownUnstable"];
                result.InterfaceType = BenchmarkSemantics.InterfaceType.Skipped;
                result.CreateTime = 0;
                result.InitTime = 0;
                result.QueryTime = 0;
                result.TotalTime = 0;
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

            var hookCall = await HookIpcClient.GetHookDataAsync(result.Clsid.Value.ToString("B"), contextPath, result.BinaryPath);
            var hookData = hookCall.data;
            result.WallClockTime = hookCall.total_ms;
            result.LockWaitTime = hookCall.lock_wait_ms;
            result.ConnectTime = hookCall.connect_ms;
            result.IpcRoundTripTime = hookCall.roundtrip_ms;

            if (hookData != null && hookData.success)
            {
                result.InterfaceType = hookData.@interface;
                if (!string.IsNullOrEmpty(hookData.names))
                {
                    // Keep packaged/UWP display names stable to avoid garbled menu-title replacements.
                    if (BenchmarkSemantics.IsRegistryManagedExtensionType(result.Type))
                    {
                        result.Name = hookData.names.Replace(
                            HookIpcSemantics.Response.MultiValueDelimiter.ToString(),
                            ", ");
                    }
                    if (result.Status == BenchmarkSemantics.Status.Unknown) result.Status = BenchmarkSemantics.Status.VerifiedViaHook;
                }
                else if (result.Status == BenchmarkSemantics.Status.Unknown || result.Status == BenchmarkSemantics.Status.Ok)
                {
                    result.Status = BenchmarkSemantics.Status.HookLoadedNoMenu;
                    result.DetailedStatus = LocalizationService.Instance["Dashboard.Detail.HookLoadedNoMenu"];
                }
                
                string? winnerIcon = null;
                if (!string.IsNullOrEmpty(hookData.reg_icon)
                    && (hookData.reg_icon.Contains(BenchmarkSemantics.IconLocation.IconResourceIndexSeparator)
                        || hookData.reg_icon.EndsWith(BenchmarkSemantics.IconFileExtension.Ico, StringComparison.OrdinalIgnoreCase)))
                    winnerIcon = hookData.reg_icon;
                if (winnerIcon == null && !string.IsNullOrEmpty(hookData.icons))
                    winnerIcon = hookData.icons
                        .Split(HookIpcSemantics.Response.MultiValueDelimiter)
                        .FirstOrDefault(i =>
                            !string.IsNullOrEmpty(i)
                            && !string.Equals(i, HookIpcSemantics.Response.NoIconToken, StringComparison.OrdinalIgnoreCase));
                
                if (winnerIcon != null) result.IconLocation = winnerIcon;
                
                result.CreateTime = (long)hookData.create_ms;
                result.InitTime = (long)hookData.init_ms;
                result.QueryTime = (long)hookData.query_ms;
                result.TotalTime = result.CreateTime + result.InitTime + result.QueryTime;
            }
            else if (hookData != null && !hookData.success)
            {
                string hookError = hookData.error ?? LocalizationService.Instance["Dashboard.Value.Unknown"];

                if (BenchmarkSemantics.IsTimeoutLikeError(hookData.error))
                {
                    result.Status = BenchmarkSemantics.Status.IpcTimeout;
                    result.DetailedStatus = string.Format(
                        LocalizationService.Instance["Dashboard.Detail.HookProbeTimeoutWithError"],
                        hookError);
                }
                else
                {
                    result.Status = BenchmarkSemantics.Status.LoadError;
                    result.DetailedStatus = string.Format(
                        LocalizationService.Instance["Dashboard.Detail.HookLoadErrorWithError"],
                        hookError);
                }
            }
            else if (hookData == null)
            {
                if (result.Status != BenchmarkSemantics.Status.LoadError && result.Status != BenchmarkSemantics.Status.OrphanedMissingDll)
                {
                    if (hookCall.roundtrip_ms >= BenchmarkSemantics.Runtime.IpcTimeoutLikeRoundtripThresholdMs)
                    {
                        result.Status = BenchmarkSemantics.Status.IpcTimeout;
                        result.DetailedStatus = LocalizationService.Instance["Dashboard.Detail.HookResponseTimeoutFallback"];
                    }
                    else
                    {
                        result.Status = BenchmarkSemantics.Status.RegistryFallback;
                        result.DetailedStatus = LocalizationService.Instance["Dashboard.Detail.HookUnavailableFallback"];
                    }
                }
            }
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
            var meta = new ClsidMetadata();
            string clsidB = clsid.ToString("B");

            // Prevent infinite recursion or too deep nesting
            if (depth >= 3) return meta;

            using (var key = ShellUtils.OpenClsidKey(clsidB))
            {
                if (key != null)
                {
                    meta.Name = key.GetValue("") as string ?? "";
                    meta.FriendlyName = key.GetValue(ComRegistrySemantics.FriendlyNameValueName) as string ?? "";

                    // Try InprocServer32
                    using (var serverKey = key.OpenSubKey(ComRegistrySemantics.InprocServer32SubKeyName))
                    {
                        if (serverKey != null)
                        {
                            meta.BinaryPath = serverKey.GetValue("") as string ?? "";
                            meta.ThreadingModel = serverKey.GetValue(ComRegistrySemantics.ThreadingModelValueName) as string ?? "";
                        }
                    }

                    // If no path, check TreatAs (Alias)
                    if (string.IsNullOrEmpty(meta.BinaryPath))
                    {
                        string? treatAs = key.OpenSubKey(ComRegistrySemantics.TreatAsSubKeyName)?.GetValue("") as string;
                        if (!string.IsNullOrEmpty(treatAs) && Guid.TryParse(treatAs, out Guid otherGuid) && otherGuid != clsid)
                        {
                            var otherMeta = QueryClsidMetadata(otherGuid, depth + 1);
                            if (string.IsNullOrEmpty(meta.Name)) meta.Name = otherMeta.Name;
                            meta.BinaryPath = otherMeta.BinaryPath;
                            meta.ThreadingModel = otherMeta.ThreadingModel;
                        }
                    }

                    // If still no path, check AppID (Surrogates)
                    if (string.IsNullOrEmpty(meta.BinaryPath))
                    {
                        string? appId = key.GetValue(ComRegistrySemantics.AppIdValueName) as string;
                        if (!string.IsNullOrEmpty(appId))
                        {
                            using (var appKey = Registry.ClassesRoot.OpenSubKey(ComRegistrySemantics.BuildAppIdPath(appId)))
                            {
                                string? dllSurrogate = appKey?.GetValue(ComRegistrySemantics.DllSurrogateValueName) as string;
                                meta.BinaryPath = dllSurrogate != null && string.IsNullOrEmpty(dllSurrogate) 
                                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), ComRegistrySemantics.DllHostExecutableName)
                                    : (dllSurrogate ?? "");
                            }
                        }
                    }
                }
                else
                {
                    // Check Packaged COM
                    using (var pkgKey = Registry.ClassesRoot.OpenSubKey(ComRegistrySemantics.BuildPackagedComClassIndexPath(clsidB)))
                    {
                        string? packageFullName = pkgKey?.GetValue("") as string;
                        if (!string.IsNullOrEmpty(packageFullName))
                        {
                            meta.BinaryPath = ResolvePackageDllPath(packageFullName, clsid) ?? "";
                            meta.Name = QueryPackagedDisplayName(clsidB) ?? "";
                        }
                    }
                }
            }

            meta.Name = ShellUtils.ResolveMuiString(meta.Name);
            if (string.IsNullOrEmpty(meta.Name) && !string.IsNullOrEmpty(meta.BinaryPath))
                meta.Name = Path.GetFileName(meta.BinaryPath);

            return meta;
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
    }
}
