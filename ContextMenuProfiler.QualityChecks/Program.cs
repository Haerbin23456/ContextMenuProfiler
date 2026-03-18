using System.Reflection;
using ContextMenuProfiler.UI.Core;
using ContextMenuProfiler.UI.Core.Services;

static void AssertEqual(string expected, string actual, string caseName)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{caseName} failed. expected='{expected}', actual='{actual}'");
    }
}

static void AssertTrue(bool condition, string caseName, string? detail = null)
{
    if (!condition)
    {
        throw new InvalidOperationException($"{caseName} failed.{(string.IsNullOrEmpty(detail) ? "" : $" {detail}")}");
    }
}

static string FindFileUpward(string relativePath)
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current != null)
    {
        string candidate = Path.Combine(current.FullName, relativePath);
        if (File.Exists(candidate))
        {
            return candidate;
        }
        current = current.Parent;
    }

    throw new FileNotFoundException($"Could not locate file by relative path: {relativePath}");
}

static void AssertNull(string? value, string caseName)
{
    if (value != null)
    {
        throw new InvalidOperationException($"{caseName} failed. expected null, actual='{value}'");
    }
}

static void AssertSourceDoesNotContainAny(string source, string caseName, params string[] forbiddenLiterals)
{
    var hit = forbiddenLiterals.Where(l => source.Contains($"\"{l}\"", StringComparison.Ordinal)).ToList();
    AssertTrue(hit.Count == 0, caseName, hit.Count == 0 ? null : string.Join(", ", hit));
}

string requestWithoutHint = HookIpcClient.BuildRequest("{00000000-0000-0000-0000-000000000000}", @"C:\Temp\a.txt", null);
AssertEqual("CMP1|AUTO|{00000000-0000-0000-0000-000000000000}|C:\\Temp\\a.txt", requestWithoutHint, "BuildRequestWithoutHint");

string requestWithHint = HookIpcClient.BuildRequest("{00000000-0000-0000-0000-000000000000}", @"C:\Temp\a.txt", @"C:\x\h.dll");
AssertEqual("CMP1|AUTO|{00000000-0000-0000-0000-000000000000}|C:\\Temp\\a.txt|C:\\x\\h.dll", requestWithHint, "BuildRequestWithHint");

string? malformed = HookIpcClient.ExtractJsonEnvelope("ERR|NOT_JSON");
AssertNull(malformed, "ExtractJsonMalformed");

string? wrapped = HookIpcClient.ExtractJsonEnvelope("noise{\"success\":true,\"state\":1}tail");
AssertEqual("{\"success\":true,\"state\":1}", wrapped ?? "", "ExtractJsonWrapped");

AssertEqual(
    BenchmarkSemantics.Category.Background,
    BenchmarkSemantics.ResolveCategoryFromLocations(new[] { "Directory", "Background" }),
    "ResolveCategoryBackgroundPriority"
);

AssertEqual(
    BenchmarkSemantics.Category.Drive,
    BenchmarkSemantics.ResolveCategoryFromLocations(new[] { "Drive", "Directory" }),
    "ResolveCategoryDrivePriority"
);

AssertEqual(
    BenchmarkSemantics.Category.Folder,
    BenchmarkSemantics.ResolveCategoryFromLocations(new[] { "Directory" }),
    "ResolveCategoryFolderHint"
);

AssertEqual(
    BenchmarkSemantics.Category.File,
    BenchmarkSemantics.ResolveCategoryFromLocations(new[] { "All Files" }),
    "ResolveCategoryFileHint"
);

AssertEqual(
    BenchmarkSemantics.Category.File,
    BenchmarkSemantics.ResolveCategoryFromLocations(Array.Empty<string>()),
    "ResolveCategoryDefault"
);

const BindingFlags instanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
var resourcesField = typeof(LocalizationService).GetField("_resources", instanceNonPublic);
AssertTrue(resourcesField != null, "LocalizationResourcesFieldExists");

var localizationService = LocalizationService.Instance;
var resources = resourcesField!.GetValue(localizationService) as Dictionary<string, Dictionary<string, string>>;
AssertTrue(resources != null, "LocalizationResourcesReadable");
AssertTrue(resources!.ContainsKey("en-US"), "LocalizationHasEnglish");
AssertTrue(resources.ContainsKey("zh-CN"), "LocalizationHasChinese");

var enKeys = resources["en-US"].Keys.ToHashSet(StringComparer.Ordinal);
var zhKeys = resources["zh-CN"].Keys.ToHashSet(StringComparer.Ordinal);

var missingInChinese = enKeys.Except(zhKeys, StringComparer.Ordinal).ToList();
var missingInEnglish = zhKeys.Except(enKeys, StringComparer.Ordinal).ToList();

AssertTrue(
    missingInChinese.Count == 0,
    "LocalizationMissingInChinese",
    missingInChinese.Count == 0 ? null : string.Join(", ", missingInChinese.Take(10))
);

AssertTrue(
    missingInEnglish.Count == 0,
    "LocalizationMissingInEnglish",
    missingInEnglish.Count == 0 ? null : string.Join(", ", missingInEnglish.Take(10))
);

string[] criticalKeys =
{
    "App.Title",
    "Hook.Active",
    "Hook.NotInjected",
    "Dashboard.ScanSystem",
    "Dashboard.AnalyzeFile",
    "Dashboard.Status.ScanComplete"
};

foreach (var key in criticalKeys)
{
    AssertTrue(resources["en-US"].TryGetValue(key, out var enValue) && !string.IsNullOrWhiteSpace(enValue), $"LocalizationEnglishValue:{key}");
    AssertTrue(resources["zh-CN"].TryGetValue(key, out var zhValue) && !string.IsNullOrWhiteSpace(zhValue), $"LocalizationChineseValue:{key}");
}

string benchmarkServicePath = FindFileUpward(@"ContextMenuProfiler.UI\Core\BenchmarkService.cs");
string benchmarkServiceSource = File.ReadAllText(benchmarkServicePath);

string benchmarkSemanticsPath = FindFileUpward(@"ContextMenuProfiler.UI\Core\BenchmarkSemantics.cs");
string benchmarkSemanticsSource = File.ReadAllText(benchmarkSemanticsPath);

string benchmarkStatisticsPath = FindFileUpward(@"ContextMenuProfiler.UI\Core\BenchmarkStatistics.cs");
string benchmarkStatisticsSource = File.ReadAllText(benchmarkStatisticsPath);

string hookIpcClientPath = FindFileUpward(@"ContextMenuProfiler.UI\Core\HookIpcClient.cs");
string hookIpcClientSource = File.ReadAllText(hookIpcClientPath);

string hookIpcSemanticsPath = FindFileUpward(@"ContextMenuProfiler.UI\Core\HookIpcSemantics.cs");
string hookIpcSemanticsSource = File.ReadAllText(hookIpcSemanticsPath);

string packageScannerPath = FindFileUpward(@"ContextMenuProfiler.UI\Core\PackageScanner.cs");
string packageScannerSource = File.ReadAllText(packageScannerPath);

string dashboardViewModelPath = FindFileUpward(@"ContextMenuProfiler.UI\ViewModels\DashboardViewModel.cs");
string dashboardViewModelSource = File.ReadAllText(dashboardViewModelPath);

string statusVisibilityConverterPath = FindFileUpward(@"ContextMenuProfiler.UI\Converters\StatusToVisibilityConverter.cs");
string statusVisibilityConverterSource = File.ReadAllText(statusVisibilityConverterPath);

string typeToIconConverterPath = FindFileUpward(@"ContextMenuProfiler.UI\Converters\TypeToIconConverter.cs");
string typeToIconConverterSource = File.ReadAllText(typeToIconConverterPath);

string dashboardPageXamlPath = FindFileUpward(@"ContextMenuProfiler.UI\Views\Pages\DashboardPage.xaml");
string dashboardPageXamlSource = File.ReadAllText(dashboardPageXamlPath);

AssertTrue(
    benchmarkServiceSource.Contains("RunBenchmarkAsync(targetPath)", StringComparison.Ordinal),
    "AnalyzeFileUsesPathSpecificBenchmark"
);

AssertTrue(
    !benchmarkServiceSource.Contains("return RunSystemBenchmark(ScanMode.Targeted)", StringComparison.Ordinal),
    "AnalyzeFileDoesNotFallbackToSystemScan"
);

AssertTrue(
    benchmarkSemanticsSource.Contains("MaxParallelProbeTasks = 8", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("IpcTimeoutLikeRoundtripThresholdMs = 1900", StringComparison.Ordinal),
    "BenchmarkSemanticsDefinesRuntimeProbeConstants"
);

AssertTrue(
    benchmarkStatisticsSource.Contains("public static class BenchmarkStatisticsCalculator", StringComparison.Ordinal)
    && benchmarkStatisticsSource.Contains("public static BenchmarkStatistics Calculate", StringComparison.Ordinal),
    "BenchmarkStatisticsCalculatorExists"
);

AssertTrue(
    hookIpcSemanticsSource.Contains("VersionPrefix = \"CMP1\"", StringComparison.Ordinal)
    && hookIpcSemanticsSource.Contains("ModeAuto = \"AUTO\"", StringComparison.Ordinal)
    && hookIpcSemanticsSource.Contains("PipeName = \"ContextMenuProfilerHook\"", StringComparison.Ordinal)
    && hookIpcSemanticsSource.Contains("ProbeFileName = \"ContextMenuProfiler_probe.txt\"", StringComparison.Ordinal)
    && hookIpcSemanticsSource.Contains("ProbeFileContent = \"probe\"", StringComparison.Ordinal)
    && hookIpcSemanticsSource.Contains("InitialResponseCapacity = 1024", StringComparison.Ordinal)
    && hookIpcSemanticsSource.Contains("ReadChunkSize = 4096", StringComparison.Ordinal),
    "HookIpcSemanticsDefinesProtocolAndPipeConstants"
);

AssertTrue(
    hookIpcClientSource.Contains("HookIpcSemantics.Runtime.MaxConcurrentCalls", StringComparison.Ordinal)
    && hookIpcClientSource.Contains("HookIpcSemantics.Runtime.MaxAttempts", StringComparison.Ordinal)
    && hookIpcClientSource.Contains("HookIpcSemantics.Runtime.RetryDelayMs", StringComparison.Ordinal)
    && hookIpcClientSource.Contains("HookIpcSemantics.Protocol.VersionPrefix", StringComparison.Ordinal)
    && hookIpcClientSource.Contains("HookIpcSemantics.Runtime.ProbeFileName", StringComparison.Ordinal)
    && hookIpcClientSource.Contains("HookIpcSemantics.Runtime.ProbeFileContent", StringComparison.Ordinal)
    && hookIpcClientSource.Contains("HookIpcSemantics.Runtime.InitialResponseCapacity", StringComparison.Ordinal)
    && hookIpcClientSource.Contains("HookIpcSemantics.Runtime.ReadChunkSize", StringComparison.Ordinal)
    && hookIpcClientSource.Contains("ShouldRetry(attempt)", StringComparison.Ordinal)
    && hookIpcClientSource.Contains("private static async Task<bool> DelayForRetryAsync", StringComparison.Ordinal)
    && hookIpcClientSource.Contains("await DelayForRetryAsync(attempt)", StringComparison.Ordinal),
    "HookIpcClientUsesIpcSemanticsConstants"
);

AssertTrue(
    !hookIpcClientSource.Contains("private const string ProtocolPrefix", StringComparison.Ordinal)
    && !hookIpcClientSource.Contains("private const string ProtocolMode", StringComparison.Ordinal)
    && !hookIpcClientSource.Contains("private const string PipeName", StringComparison.Ordinal)
    && !hookIpcClientSource.Contains("private const int ConnectTimeoutMs", StringComparison.Ordinal)
    && !hookIpcClientSource.Contains("private const int RoundTripTimeoutMs", StringComparison.Ordinal)
    && !hookIpcClientSource.Contains("Task.Delay(80)", StringComparison.Ordinal)
    && !hookIpcClientSource.Contains("attempt == 0", StringComparison.Ordinal)
    && !hookIpcClientSource.Contains("ContextMenuProfiler_probe.txt", StringComparison.Ordinal)
    && !hookIpcClientSource.Contains("new StringBuilder(1024)", StringComparison.Ordinal)
    && !hookIpcClientSource.Contains("new byte[4096]", StringComparison.Ordinal)
    && !hookIpcClientSource.Contains("if (ShouldRetry(attempt))", StringComparison.Ordinal),
    "HookIpcClientNoLegacyInlineProtocolRuntimeLiterals"
);

AssertTrue(
    benchmarkServiceSource.Contains("BenchmarkSemantics.Runtime.MaxParallelProbeTasks", StringComparison.Ordinal),
    "BenchmarkServiceUsesMaxParallelProbeTasksConstant"
);

AssertTrue(
    benchmarkServiceSource.Contains("BenchmarkSemantics.Runtime.IpcTimeoutLikeRoundtripThresholdMs", StringComparison.Ordinal),
    "BenchmarkServiceUsesIpcTimeoutThresholdConstant"
);

AssertTrue(
    benchmarkServiceSource.Contains("private static bool TryParseStaticVerbKey", StringComparison.Ordinal)
    && benchmarkServiceSource.Contains("TryParseStaticVerbKey(key, out string name, out string command)", StringComparison.Ordinal),
    "BenchmarkServiceUsesStaticVerbKeyParser"
);

AssertTrue(
    !benchmarkServiceSource.Contains("new SemaphoreSlim(8)", StringComparison.Ordinal)
    && !benchmarkServiceSource.Contains("hookCall.roundtrip_ms >= 1900", StringComparison.Ordinal)
    && !benchmarkServiceSource.Contains("key.Split('|')[1]", StringComparison.Ordinal),
    "BenchmarkServiceNoRuntimeMagicNumericLiterals"
);

string[] forbiddenStatusMagicLiterals =
{
    "Registry Fallback",
    "Load Error",
    "Orphaned / Missing DLL",
    "IPC Timeout",
    "Verified via Hook",
    "Hook Loaded (No Menu)",
    "Skipped (Known Unstable)"
};

AssertSourceDoesNotContainAny(benchmarkServiceSource, "BenchmarkServiceNoStatusMagicLiterals", forbiddenStatusMagicLiterals);
AssertSourceDoesNotContainAny(packageScannerSource, "PackageScannerNoStatusMagicLiterals", forbiddenStatusMagicLiterals);
AssertSourceDoesNotContainAny(dashboardViewModelSource, "DashboardViewModelNoStatusMagicLiterals", forbiddenStatusMagicLiterals);
AssertSourceDoesNotContainAny(statusVisibilityConverterSource, "StatusVisibilityConverterNoStatusMagicLiterals", forbiddenStatusMagicLiterals);

string[] forbiddenInterfaceAndLocationLiterals =
{
    "Static Verb",
    "Skipped",
    "Modern Shell (UWP)",
    "[Disabled]"
};

AssertSourceDoesNotContainAny(
    benchmarkServiceSource,
    "BenchmarkServiceNoInterfaceLocationMagicLiterals",
    forbiddenInterfaceAndLocationLiterals
);

AssertTrue(
    !dashboardViewModelSource.Contains("private static class CategoryTag", StringComparison.Ordinal),
    "DashboardViewModelNoCategoryTagDuplication"
);

AssertTrue(
    dashboardViewModelSource.Contains("BenchmarkSemantics.IsCategoryMatch", StringComparison.Ordinal),
    "DashboardViewModelUsesCentralizedCategoryMatch"
);

AssertTrue(
    dashboardViewModelSource.Contains("BenchmarkSemantics.IsPackagedExtensionType(item.Type)", StringComparison.Ordinal),
    "DashboardViewModelUsesPackagedTypeSemanticHelper"
);

AssertTrue(
    dashboardViewModelSource.Contains("BenchmarkStatisticsCalculator.Calculate(Results)", StringComparison.Ordinal),
    "DashboardViewModelUsesStatisticsCalculator"
);

AssertTrue(
    !dashboardViewModelSource.Contains("foreach (var r in Results)", StringComparison.Ordinal),
    "DashboardViewModelNoManualStatsAggregationLoop"
);

AssertTrue(
    !dashboardViewModelSource.Contains("string.Equals(item.Type, BenchmarkSemantics.Type.Uwp", StringComparison.Ordinal),
    "DashboardViewModelNoUwpOnlyDeleteCheck"
);

AssertTrue(
    benchmarkServiceSource.Contains("BenchmarkSemantics.IsRegistryManagedExtensionType(result.Type)", StringComparison.Ordinal),
    "BenchmarkServiceUsesRegistryManagedSemanticHelper"
);

AssertTrue(
    typeToIconConverterSource.Contains("BenchmarkSemantics.IsPackagedExtensionType(type)", StringComparison.Ordinal),
    "TypeToIconConverterUsesPackagedTypeSemanticHelper"
);

AssertTrue(
    dashboardViewModelSource.Contains("BeginScanSession(", StringComparison.Ordinal)
    && dashboardViewModelSource.Contains("LastScanMode.System", StringComparison.Ordinal)
    && dashboardViewModelSource.Contains("LastScanMode.File", StringComparison.Ordinal),
    "DashboardViewModelUsesScanSessionInitializer"
);

AssertTrue(
    dashboardViewModelSource.Contains("private async Task<List<BenchmarkResult>> RunFileBenchmarkInStaAsync", StringComparison.Ordinal)
    && dashboardViewModelSource.Contains("RunFileBenchmarkInStaAsync(filePath)", StringComparison.Ordinal),
    "DashboardViewModelUsesStaFileBenchmarkHelper"
);

AssertTrue(
    dashboardViewModelSource.Contains("class DashboardViewModel : ObservableObject, IDisposable", StringComparison.Ordinal),
    "DashboardViewModelImplementsDisposable"
);

AssertTrue(
    dashboardViewModelSource.Contains("LocalizationService.Instance.PropertyChanged += _localizationChangedHandler", StringComparison.Ordinal)
    && dashboardViewModelSource.Contains("LocalizationService.Instance.PropertyChanged -= _localizationChangedHandler", StringComparison.Ordinal),
    "DashboardViewModelLocalizationSubscriptionBalanced"
);

AssertTrue(
    dashboardViewModelSource.Contains("HookService.Instance.PropertyChanged += _hookServiceChangedHandler", StringComparison.Ordinal)
    && dashboardViewModelSource.Contains("HookService.Instance.PropertyChanged -= _hookServiceChangedHandler", StringComparison.Ordinal),
    "DashboardViewModelHookSubscriptionBalanced"
);

AssertTrue(
    dashboardViewModelSource.Contains("_filterCts?.Dispose();", StringComparison.Ordinal)
    && dashboardViewModelSource.Contains("public void Dispose()", StringComparison.Ordinal),
    "DashboardViewModelDisposesFilterCts"
);

string[] forbiddenLegacyStatusConverterBindings =
{
    "StatusToVisibilityConverter}, ConverterParameter=NotActive",
    "StatusToVisibilityConverter}, ConverterParameter=Fallback",
    "StatusToVisibilityConverter}, ConverterParameter=NotUWP",
    "StatusToVisibilityConverter}, ConverterParameter=Inverse"
};

foreach (var legacyBinding in forbiddenLegacyStatusConverterBindings)
{
    AssertTrue(
        !dashboardPageXamlSource.Contains(legacyBinding, StringComparison.Ordinal),
        $"DashboardPageNoLegacyStatusConverterBinding:{legacyBinding}"
    );
}

AssertTrue(
    dashboardPageXamlSource.Contains("StatusVisibilityMode.NotActive", StringComparison.Ordinal)
    && dashboardPageXamlSource.Contains("StatusVisibilityMode.Fallback", StringComparison.Ordinal)
    && dashboardPageXamlSource.Contains("StatusVisibilityMode.NotPackaged", StringComparison.Ordinal),
    "DashboardPageUsesTypedStatusVisibilityModes"
);

Console.WriteLine("Quality checks passed.");

if (string.Equals(Environment.GetEnvironmentVariable("CMP_LIVE_PROBE"), "1", StringComparison.Ordinal))
{
    var service = new BenchmarkService();
    var results = await service.RunSystemBenchmarkAsync(ScanMode.Targeted);
    var measured = results.Count(r => r.TotalTime > 0);
    var fallback = results.Count(r => string.Equals(r.Status, BenchmarkSemantics.Status.RegistryFallback, StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"Live probe: total={results.Count}, measured={measured}, fallback={fallback}");
}
