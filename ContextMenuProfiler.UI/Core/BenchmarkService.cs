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
        private static readonly bool SkipKnownUnstableHandlers =
            string.Equals(Environment.GetEnvironmentVariable("CMP_SKIP_UNSTABLE_HANDLERS"), "1", StringComparison.OrdinalIgnoreCase);

        private static readonly string[] KnownUnstableHandlerTokens =
        {
            "PintoStartScreen",
            "NvcplDesktopContext",
            "NvAppDesktopContext",
            "NVIDIA CPL Context Menu Extension"
        };

        public List<BenchmarkResult> RunSystemBenchmark(ScanMode mode = ScanMode.Targeted)
        {
            return Task.Run(() => RunSystemBenchmarkAsync(mode)).GetAwaiter().GetResult();
        }

        public async Task<List<BenchmarkResult>> RunSystemBenchmarkAsync(ScanMode mode = ScanMode.Targeted, IProgress<BenchmarkResult>? progress = null)
        {
            bool useFolderContext = mode == ScanMode.Full ? false : false;
            using var fileContext = ShellTestContext.Create(useFolderContext);
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
            var semaphore = new SemaphoreSlim(8);

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

                    resultsMap[clsid] = result;
                    result.Category = DetermineCategory(result.RegistryEntries.Select(e => e.Location));

                    await EnrichBenchmarkResultAsync(result, hookContextPath);

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
                    Status = "Static (Not Measured)",
                    BinaryPath = ExtractExecutablePath(command),
                    RegistryEntries = paths.Select(p => new RegistryHandlerInfo
                    {
                        Path = p,
                        Location = $"Registry (Shell) - {p.Split('\\')[0]}"
                    }).ToList(),
                    InterfaceType = "Static Verb",
                    DetailedStatus = "Static shell verbs do not go through Hook COM probing and are displayed as not measured.",
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

            var uwpTasks = PackageScanner.ScanPackagedExtensions(packageTargetPath)
                .Where(r => r.Clsid.HasValue && !resultsMap.ContainsKey(r.Clsid.Value))
                .Select(async uwpResult =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        uwpResult.Category = "UWP";
                        await EnrichBenchmarkResultAsync(uwpResult, hookContextPath);

                        uwpResult.IsEnabled = !ExtensionManager.IsExtensionBlocked(uwpResult.Clsid!.Value);
                        uwpResult.LocationSummary = "Modern Shell (UWP)";

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
                result.Status = "Skipped (Known Unstable)";
                result.DetailedStatus = "Skipped Hook invocation for a known unstable system handler to avoid scan-wide IPC stalls.";
                result.InterfaceType = "Skipped";
                result.CreateTime = 0;
                result.InitTime = 0;
                result.QueryTime = 0;
                result.TotalTime = 0;
                return;
            }

            // Check for Orphaned / Missing DLL
            if (!string.IsNullOrEmpty(result.BinaryPath) && !File.Exists(result.BinaryPath))
            {
                result.Status = "Orphaned / Missing DLL";
                result.DetailedStatus = $"The file '{result.BinaryPath}' was not found on disk. This extension is likely corrupted or uninstalled.";
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
                    if (!string.Equals(result.Type, "UWP", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Name = hookData.names.Replace("|", ", ");
                    }
                    if (result.Status == "Unknown") result.Status = "Verified via Hook";
                }
                else if (result.Status == "Unknown" || result.Status == "OK")
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
                if (!string.IsNullOrEmpty(hookData.error) && hookData.error.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
                {
                    result.Status = "IPC Timeout";
                    result.DetailedStatus = $"Hook service timed out while probing this extension. Error: {hookData.error}";
                }
                else
                {
                    result.Status = "Load Error";
                    result.DetailedStatus = $"The Hook service failed to load this extension. Error: {hookData.error ?? "Unknown Error"}";
                }
            }
            else if (hookData == null)
            {
                if (result.Status != "Load Error" && result.Status != "Orphaned / Missing DLL")
                {
                    if (hookCall.roundtrip_ms >= 1900)
                    {
                        result.Status = "IPC Timeout";
                        result.DetailedStatus = "Hook service response timed out for this extension. Data is based on registry scan only.";
                    }
                    else
                    {
                        result.Status = "Registry Fallback";
                        result.DetailedStatus = "The Hook service could not be reached or failed to process this extension. Data is based on registry scan only.";
                    }
                }
            }
        }

        private static bool IsKnownUnstableHandler(BenchmarkResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.Name) &&
                KnownUnstableHandlerTokens.Any(token => result.Name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(result.FriendlyName) &&
                KnownUnstableHandlerTokens.Any(token => result.FriendlyName.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (result.RegistryEntries != null)
            {
                foreach (var entry in result.RegistryEntries)
                {
                    if ((!string.IsNullOrWhiteSpace(entry.Location) &&
                         KnownUnstableHandlerTokens.Any(token => entry.Location.Contains(token, StringComparison.OrdinalIgnoreCase))) ||
                        (!string.IsNullOrWhiteSpace(entry.Path) &&
                         KnownUnstableHandlerTokens.Any(token => entry.Path.Contains(token, StringComparison.OrdinalIgnoreCase))))
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

        public long RunRealShellBenchmark(string? filePath = null) => -1;

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
