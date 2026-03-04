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
        public string Status { get; set; } = "Unknown";
        public string Type { get; set; } = "COM"; // Legacy COM, UWP, Static
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
        
        // Extended Info
        public string? PackageName { get; set; }
        public string? Version { get; set; }
        public string? InterfaceType { get; set; } // IContextMenu, IExplorerCommand, Static
        public string? ThreadingModel { get; set; }
        public string? FriendlyName { get; set; }
        public string? IconSource { get; set; }
        public string? LocationSummary { get; set; }
        public string Category { get; set; } = "File";
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
        private sealed class HookProbeState
        {
            private int _consecutiveTransportFailures;
            private int _skipBudget;

            public void MarkSuccess()
            {
                Interlocked.Exchange(ref _consecutiveTransportFailures, 0);
                Interlocked.Exchange(ref _skipBudget, 0);
            }

            public void MarkTransportFailure()
            {
                int failures = Interlocked.Increment(ref _consecutiveTransportFailures);
                // Avoid an all-or-nothing global fallback. Throttle briefly, then probe again.
                if (failures >= 6)
                {
                    Interlocked.Exchange(ref _consecutiveTransportFailures, 0);
                    Interlocked.Exchange(ref _skipBudget, 6);
                }
            }

            public bool ShouldTemporarilySkipProbe()
            {
                while (true)
                {
                    int budget = Interlocked.CompareExchange(ref _skipBudget, 0, 0);
                    if (budget <= 0) return false;
                    if (Interlocked.CompareExchange(ref _skipBudget, budget - 1, budget) == budget) return true;
                }
            }
        }

        public List<BenchmarkResult> RunSystemBenchmark(ScanMode mode = ScanMode.Targeted)
        {
            // Use Task.Run to avoid deadlocks on UI thread when waiting for async tasks
            return Task.Run(() => RunSystemBenchmarkAsync(mode)).GetAwaiter().GetResult();
        }

        public async Task<List<BenchmarkResult>> RunSystemBenchmarkAsync(ScanMode mode = ScanMode.Targeted, IProgress<BenchmarkResult>? progress = null)
        {
            var allResults = new ConcurrentBag<BenchmarkResult>();
            var resultsMap = new ConcurrentDictionary<Guid, BenchmarkResult>();
            // Keep parallelism conservative: shell extension probing happens in Explorer
            // and aggressive fan-out can cause system stutter.
            int maxParallelism = mode == ScanMode.Full ? 2 : 3;
            var semaphore = new SemaphoreSlim(maxParallelism);
            var hookProbeState = new HookProbeState();
            
            using (var fileContext = ShellTestContext.Create(false))
            using (var folderContext = ShellTestContext.Create(true))
            {
                string driveContextPath = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
                if (!driveContextPath.EndsWith("\\", StringComparison.Ordinal)) driveContextPath += "\\";
                if (!Directory.Exists(driveContextPath)) driveContextPath = folderContext.Path;

                // 1. Scan UWP/Packaged extensions first.
                // If a legacy COM handler destabilizes hook later, we still keep UWP measurements.
                var uwpTasks = PackageScanner.ScanPackagedExtensions(null)
                    .Where(r => r.Clsid.HasValue)
                    .Select(async uwpResult =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        Guid clsid = uwpResult.Clsid!.Value;
                        if (!resultsMap.TryAdd(clsid, uwpResult)) return;

                        uwpResult.Category = "UWP";
                        await EnrichBenchmarkResultAsync(uwpResult, fileContext.Path, folderContext.Path, hookProbeState);

                        uwpResult.IsEnabled = !ExtensionManager.IsExtensionBlocked(clsid);
                        uwpResult.LocationSummary = "Modern Shell (UWP)";

                        allResults.Add(uwpResult);
                        progress?.Report(uwpResult);
                    }
                    finally { semaphore.Release(); }
                });

                await Task.WhenAll(uwpTasks);

                // 2. Scan All Registry Handlers (COM)
                var registryHandlers = RegistryScanner.ScanHandlers(mode);
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
                            Type = "COM",
                            RegistryEntries = handlerInfos.ToList(),
                            Name = meta.Name,
                            BinaryPath = meta.BinaryPath,
                            ThreadingModel = meta.ThreadingModel,
                            FriendlyName = meta.FriendlyName
                        };

                        if (string.IsNullOrEmpty(result.Name)) result.Name = $"Unknown ({clsid})";
                        if (!resultsMap.TryAdd(clsid, result)) return;

                        result.Category = DetermineCategory(result.RegistryEntries.Select(e => e.Location));
                        string primaryContextPath = PickPrimaryContext(result, fileContext.Path, folderContext.Path, driveContextPath);
                        string secondaryContextPath = PickSecondaryContext(result, fileContext.Path, folderContext.Path, driveContextPath);

                        await EnrichBenchmarkResultAsync(result, primaryContextPath, secondaryContextPath, hookProbeState);

                        bool isBlocked = ExtensionManager.IsExtensionBlocked(clsid);
                        bool hasDisabledPath = result.RegistryEntries.Any(e => e.Location.Contains("[Disabled]"));
                        result.IsEnabled = !isBlocked && !hasDisabledPath;
                        result.LocationSummary = string.Join(", ", result.RegistryEntries.Select(e => e.Location).Distinct());

                        allResults.Add(result);
                        progress?.Report(result);
                    }
                    finally { semaphore.Release(); }
                });

                await Task.WhenAll(comTasks);

                // 3. Scan Static Verbs
                var staticVerbs = RegistryScanner.ScanStaticVerbs();
                foreach (var verbEntry in staticVerbs)
                {
                    string key = verbEntry.Key;
                    var paths = verbEntry.Value;
                    string name = key.Split('|')[0];
                    string command = key.Split('|')[1];

                    var verbResult = new BenchmarkResult
                    {
                        Name = name,
                        Type = "Static",
                        Status = "OK",
                        BinaryPath = ExtractExecutablePath(command),
                        RegistryEntries = paths.Select(p => new RegistryHandlerInfo
                        {
                            Path = p,
                            Location = $"Registry (Shell) - {p.Split('\\')[0]}"
                        }).ToList(),
                        InterfaceType = "Static Verb",
                        TotalTime = 0,
                        Category = "Static"
                    };

                    bool anyDisabled = paths.Any(p => p.Split('\\').Last().StartsWith("-"));
                    verbResult.IsEnabled = !anyDisabled;
                    verbResult.LocationSummary = string.Join(", ", verbResult.RegistryEntries.Select(e => e.Location).Distinct());
                    verbResult.IconLocation = ResolveStaticVerbIcon(paths.First(), verbResult.BinaryPath);

                    allResults.Add(verbResult);
                    progress?.Report(verbResult);
                }

                return allResults.ToList();
            }
        }

        private static bool IsBetterHookData(HookResponse? candidate, HookResponse? baseline)
        {
            if (candidate == null) return false;
            if (baseline == null) return true;

            if (candidate.success && !baseline.success) return true;

            if (candidate.success && baseline.success)
            {
                bool candidateHasNames = !string.IsNullOrEmpty(candidate.names);
                bool baselineHasNames = !string.IsNullOrEmpty(baseline.names);
                if (candidateHasNames && !baselineHasNames) return true;
            }

            return false;
        }

        private static bool IsMscoreeShim(string? binaryPath)
        {
            if (string.IsNullOrWhiteSpace(binaryPath)) return false;
            return binaryPath.Equals("mscoree.dll", StringComparison.OrdinalIgnoreCase) ||
                   binaryPath.EndsWith("\\mscoree.dll", StringComparison.OrdinalIgnoreCase) ||
                   binaryPath.EndsWith("/mscoree.dll", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeWellKnownBinaryPath(string? binaryPath)
        {
            if (string.IsNullOrWhiteSpace(binaryPath)) return "";
            if (Path.IsPathRooted(binaryPath)) return binaryPath;

            // mscoree is a system shim dll; registry commonly stores only file name.
            if (binaryPath.Equals("mscoree.dll", StringComparison.OrdinalIgnoreCase))
            {
                var systemPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mscoree.dll");
                if (File.Exists(systemPath)) return systemPath;
            }

            return binaryPath;
        }

        private static string PickPrimaryContext(BenchmarkResult result, string fileContextPath, string folderContextPath, string driveContextPath)
        {
            return result.Category switch
            {
                "Folder" => folderContextPath,
                "Background" => folderContextPath,
                "Drive" => driveContextPath,
                _ => fileContextPath
            };
        }

        private static string PickSecondaryContext(BenchmarkResult result, string fileContextPath, string folderContextPath, string driveContextPath)
        {
            return result.Category switch
            {
                "Folder" => fileContextPath,
                "Background" => fileContextPath,
                "Drive" => folderContextPath,
                _ => folderContextPath
            };
        }

        private async Task EnrichBenchmarkResultAsync(BenchmarkResult result, string contextPath, string? secondaryContextPath, HookProbeState hookProbeState)
        {
            if (!result.Clsid.HasValue) return;

            if (hookProbeState.ShouldTemporarilySkipProbe())
            {
                if (result.Status == "Unknown" || result.Status == "OK")
                {
                    result.Status = "Registry Fallback";
                    result.DetailedStatus = "Hook service is temporarily throttled after repeated timeouts. This item was measured in fallback mode.";
                }
                return;
            }

            // Normalize known shim paths first so existence checks are accurate.
            result.BinaryPath = NormalizeWellKnownBinaryPath(result.BinaryPath);

            // Managed COM shim handlers (mscoree) are a known source of explorer stalls in probe mode.
            // Skip active hook probing for stability.
            if (result.Type == "COM" && IsMscoreeShim(result.BinaryPath))
            {
                result.Status = "Registry Fallback";
                result.DetailedStatus = "Skipped active timing for managed COM shim (mscoree.dll) to avoid Explorer freezes. Displayed metadata is from registry.";
                return;
            }

            // Check for Orphaned / Missing DLL
            if (!string.IsNullOrEmpty(result.BinaryPath) && !File.Exists(result.BinaryPath))
            {
                result.Status = "Orphaned / Missing DLL";
                result.DetailedStatus = $"The file '{result.BinaryPath}' was not found on disk. This extension is likely corrupted or uninstalled.";
            }

            var hookData = await HookIpcClient.GetHookDataAsync(result.Clsid.Value.ToString("B"), contextPath, result.BinaryPath);

            // Retry once on transport failure (server can be briefly busy because pipe handling is serialized).
            if (hookData == null)
            {
                await Task.Delay(120);
                hookData = await HookIpcClient.GetHookDataAsync(result.Clsid.Value.ToString("B"), contextPath, result.BinaryPath);
            }

            // Context-sensitive retry: many handlers are file/folder/drive specific.
            bool shouldTrySecondaryContext =
                !string.IsNullOrWhiteSpace(secondaryContextPath) &&
                !string.Equals(contextPath, secondaryContextPath, StringComparison.OrdinalIgnoreCase) &&
                (hookData == null || (hookData.success && string.IsNullOrEmpty(hookData.names)));

            if (shouldTrySecondaryContext)
            {
                var secondProbe = await HookIpcClient.GetHookDataAsync(result.Clsid.Value.ToString("B"), secondaryContextPath!, result.BinaryPath);
                if (hookData == null || IsBetterHookData(secondProbe, hookData))
                {
                    hookData = secondProbe;
                }
            }

            if (hookData != null && hookData.success)
            {
                hookProbeState.MarkSuccess();
                result.InterfaceType = hookData.@interface;
                if (!string.IsNullOrEmpty(hookData.names))
                {
                    result.Name = hookData.names.Replace("|", ", ");
                    if (result.Status == "Unknown") result.Status = "Verified via Hook";
                }
                else if (result.Status == "Unknown")
                {
                    result.Status = "Hook Loaded (No Menu)";
                    result.DetailedStatus = "The extension was loaded by the Hook service but it did not provide any context menu items for the test context.";
                }
                
                string? winnerIcon = null;
                if (!string.IsNullOrEmpty(hookData.reg_icon) && (hookData.reg_icon.Contains(",") || hookData.reg_icon.ToLower().EndsWith(".ico")))
                    winnerIcon = hookData.reg_icon;
                if (winnerIcon == null && !string.IsNullOrEmpty(hookData.icons))
                    winnerIcon = hookData.icons.Split('|').FirstOrDefault(i => !string.IsNullOrEmpty(i) && i != "NONE");
                
                if (winnerIcon != null) result.IconLocation = winnerIcon;
                
                result.CreateTime = (long)hookData.create_ms;
                result.InitTime = (long)hookData.init_ms;
                result.QueryTime = (long)hookData.query_ms;
                result.TotalTime = result.CreateTime + result.InitTime + result.QueryTime;
            }
            else if (hookData != null && !hookData.success)
            {
                result.Status = "Load Error";
                result.DetailedStatus = $"The Hook service failed to load this extension. Error: {hookData.error ?? "Unknown Error"}";
            }
            else if (hookData == null)
            {
                hookProbeState.MarkTransportFailure();
                // Preserve explicit hard-failure statuses, otherwise surface fallback clearly.
                if (!string.Equals(result.Status, "Load Error", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(result.Status, "Orphaned / Missing DLL", StringComparison.OrdinalIgnoreCase))
                {
                    result.Status = "Registry Fallback";
                    result.DetailedStatus = "The Hook service could not be reached or timed out. Timing data is unavailable for this item.";
                }
            }
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
                    meta.FriendlyName = key.GetValue("FriendlyName") as string ?? "";

                    // Try InprocServer32
                    using (var serverKey = key.OpenSubKey("InprocServer32"))
                    {
                        if (serverKey != null)
                        {
                            meta.BinaryPath = serverKey.GetValue("") as string ?? "";
                            meta.ThreadingModel = serverKey.GetValue("ThreadingModel") as string ?? "";
                        }
                    }

                    // If no path, check TreatAs (Alias)
                    if (string.IsNullOrEmpty(meta.BinaryPath))
                    {
                        string? treatAs = key.OpenSubKey("TreatAs")?.GetValue("") as string;
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
                        string? appId = key.GetValue("AppID") as string;
                        if (!string.IsNullOrEmpty(appId))
                        {
                            using (var appKey = Registry.ClassesRoot.OpenSubKey($@"AppID\{appId}"))
                            {
                                string? dllSurrogate = appKey?.GetValue("DllSurrogate") as string;
                                meta.BinaryPath = dllSurrogate != null && string.IsNullOrEmpty(dllSurrogate) 
                                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "dllhost.exe")
                                    : (dllSurrogate ?? "");
                            }
                        }
                    }
                }
                else
                {
                    // Check Packaged COM
                    using (var pkgKey = Registry.ClassesRoot.OpenSubKey($@"PackagedCom\ClassIndex\{clsidB}"))
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
                using (var pkgKey = Registry.ClassesRoot.OpenSubKey($@"PackagedCom\Package"))
                {
                    if (pkgKey == null) return null;
                    foreach (var pkgName in pkgKey.GetSubKeyNames())
                    {
                        using (var clsKey = pkgKey.OpenSubKey($@"{pkgName}\Class\{clsidB}"))
                        {
                            string? name = clsKey?.GetValue("DisplayName") as string;
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
                    string? icon = key?.GetValue("Icon") as string;
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
                using (var key = Registry.ClassesRoot.OpenSubKey($@"Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\PackageRepository\Packages\{packageFullName}"))
                {
                    string? installPath = key?.GetValue("Path") as string;
                    if (string.IsNullOrEmpty(installPath)) return null;

                    // Now find the relative DLL path from PackagedCom\Package
                    // We need the short name (Package Family Name or part of full name)
                    string packageId = packageFullName.Split('_')[0];
                    using (var pkgKey = Registry.ClassesRoot.OpenSubKey($@"PackagedCom\Package"))
                    {
                        if (pkgKey != null)
                        {
                            foreach (var name in pkgKey.GetSubKeyNames())
                            {
                                if (name.StartsWith(packageId, StringComparison.OrdinalIgnoreCase))
                                {
                                    using (var clsKey = pkgKey.OpenSubKey($@"{name}\Class\{clsid:B}"))
                                    {
                                        string? relDllPath = clsKey?.GetValue("DllPath") as string;
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

        public List<BenchmarkResult> RunBenchmark(string targetPath)
        {
            return RunSystemBenchmark(ScanMode.Targeted); // Simplified for now
        }

        public long RunRealShellBenchmark(string? filePath = null) => 0;

        private string DetermineCategory(IEnumerable<string> locations)
        {
            var locs = locations.ToList();
            if (locs.Any(l => l.Contains("Background"))) return "Background";
            if (locs.Any(l => l.Contains("Drive"))) return "Drive";
            if (locs.Any(l => l.Contains("Directory") || l.Contains("Folder"))) return "Folder";
            if (locs.Any(l => l.Contains("All Files") || l.Contains("Extension") || l.Contains("All File System Objects"))) return "File";
            
            return "File"; // Default
        }
    }
}
