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

static string ReadSource(string relativePath)
{
    return File.ReadAllText(FindFileUpward(relativePath));
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

static void AssertSourceContains(string source, string requiredLiteral, string caseName)
{
    AssertTrue(source.Contains(requiredLiteral, StringComparison.Ordinal), caseName);
}

static void AssertSourceNotContains(string source, string forbiddenLiteral, string caseName)
{
    AssertTrue(!source.Contains(forbiddenLiteral, StringComparison.Ordinal), caseName);
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
    "Dashboard.Status.ScanComplete",
    "Dashboard.Value.UnknownWithClsid"
};

foreach (var key in criticalKeys)
{
    AssertTrue(resources["en-US"].TryGetValue(key, out var enValue) && !string.IsNullOrWhiteSpace(enValue), $"LocalizationEnglishValue:{key}");
    AssertTrue(resources["zh-CN"].TryGetValue(key, out var zhValue) && !string.IsNullOrWhiteSpace(zhValue), $"LocalizationChineseValue:{key}");
}

string benchmarkServiceSource = ReadSource(@"ContextMenuProfiler.UI\Core\BenchmarkService.cs");
string benchmarkSemanticsSource = ReadSource(@"ContextMenuProfiler.UI\Core\BenchmarkSemantics.cs");
string benchmarkStatisticsSource = ReadSource(@"ContextMenuProfiler.UI\Core\BenchmarkStatistics.cs");
string hookIpcClientSource = ReadSource(@"ContextMenuProfiler.UI\Core\HookIpcClient.cs");
string hookIpcSemanticsSource = ReadSource(@"ContextMenuProfiler.UI\Core\HookIpcSemantics.cs");
string registryScannerSource = ReadSource(@"ContextMenuProfiler.UI\Core\RegistryScanner.cs");
string packageScannerSource = ReadSource(@"ContextMenuProfiler.UI\Core\PackageScanner.cs");
string dashboardViewModelSource = ReadSource(@"ContextMenuProfiler.UI\ViewModels\DashboardViewModel.cs");
string registryPathHelperSource = ReadSource(@"ContextMenuProfiler.UI\Core\Helpers\RegistryPathHelper.cs");
string statusVisibilityConverterSource = ReadSource(@"ContextMenuProfiler.UI\Converters\StatusToVisibilityConverter.cs");
string typeToIconConverterSource = ReadSource(@"ContextMenuProfiler.UI\Converters\TypeToIconConverter.cs");
string dashboardPageXamlSource = ReadSource(@"ContextMenuProfiler.UI\Views\Pages\DashboardPage.xaml");

AssertSourceContains(benchmarkServiceSource, "RunBenchmarkAsync(targetPath)", "AnalyzeFileUsesPathSpecificBenchmark");
AssertSourceNotContains(benchmarkServiceSource, "return RunSystemBenchmark(ScanMode.Targeted)", "AnalyzeFileDoesNotFallbackToSystemScan");

AssertTrue(
    benchmarkSemanticsSource.Contains("MaxParallelProbeTasks = 8", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("IpcTimeoutLikeRoundtripThresholdMs = 1900", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("HookReconnectStabilizationDelayMs = 1000", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("ClipboardRetryAttempts = 5", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("ClipboardRetryDelayMs = 100", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("ClipboardCantOpenHResult = 0x800401D0", StringComparison.Ordinal),
    "BenchmarkSemanticsDefinesRuntimeProbeConstants"
);

AssertTrue(
    benchmarkSemanticsSource.Contains("private static int ResolveLocationPriority", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("private static bool ContainsAnyLocationHint", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("resolvedPriority switch", StringComparison.Ordinal),
    "BenchmarkSemanticsUsesPriorityBasedLocationResolution"
);

AssertTrue(
    benchmarkSemanticsSource.Contains("StaticVerbRegistryShellPrefix = \"Registry (Shell) - \"", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("StaticVerbDisabledKeyPrefix = \"-\"", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("BuildStaticVerbRegistryLocation", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("IsStaticVerbRegistryPathDisabled", StringComparison.Ordinal),
    "BenchmarkSemanticsDefinesStaticVerbRegistryHelpers"
);

AssertTrue(
    benchmarkSemanticsSource.Contains("Timeout = \"Timeout\"", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("public static bool IsTimeoutLikeError", StringComparison.Ordinal),
    "BenchmarkSemanticsDefinesTimeoutErrorHelper"
);

AssertTrue(
    benchmarkSemanticsSource.Contains("SkipUnstableHandlersEnvVar = \"CMP_SKIP_UNSTABLE_HANDLERS\"", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("public static bool IsSkipUnstableHandlersEnabled", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("public static bool ContainsKnownUnstableHandlerToken", StringComparison.Ordinal),
    "BenchmarkSemanticsDefinesUnstableHandlerHelpers"
);

AssertTrue(
    benchmarkSemanticsSource.Contains("public static class RegistryLocationLabel", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("DisabledSuffix = \" [Disabled]\"", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("BuildDisabledRegistryLocationLabel", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("BuildExtensionRegistryLocationLabel", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("BuildProgIdRegistryLocationLabel", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("BuildRegistryHandlerLocation", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("public static class RegistryPathPattern", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("AllFilesHandlers = @\"*\\shellex\\ContextMenuHandlers\"", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("BuildSystemFileAssociationHandlers", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("BuildProgIdHandlers", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("BuildSystemFileAssociationShell", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("BuildProgIdShell", StringComparison.Ordinal),
    "BenchmarkSemanticsDefinesRegistryScannerLocationHelpers"
);

AssertTrue(
    benchmarkSemanticsSource.Contains("public static class StaticVerb", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("UniqueKeySeparator = '|'", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("public static bool IsIgnoredStaticVerbName", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("public static string BuildStaticVerbUniqueKey", StringComparison.Ordinal)
    && benchmarkSemanticsSource.Contains("public static bool TryParseStaticVerbUniqueKey", StringComparison.Ordinal),
    "BenchmarkSemanticsDefinesStaticVerbKeyHelpers"
);

AssertTrue(
    registryScannerSource.Contains("BenchmarkSemantics.RegistryLocationLabel.AllFiles", StringComparison.Ordinal)
    && registryScannerSource.Contains("BenchmarkSemantics.BuildDisabledRegistryLocationLabel", StringComparison.Ordinal)
    && registryScannerSource.Contains("BenchmarkSemantics.BuildExtensionRegistryLocationLabel", StringComparison.Ordinal)
    && registryScannerSource.Contains("BenchmarkSemantics.BuildProgIdRegistryLocationLabel", StringComparison.Ordinal)
    && registryScannerSource.Contains("BenchmarkSemantics.BuildRegistryHandlerLocation", StringComparison.Ordinal)
    && registryScannerSource.Contains("BenchmarkSemantics.RegistryPathPattern.AllFilesHandlers", StringComparison.Ordinal)
    && registryScannerSource.Contains("BenchmarkSemantics.RegistryPathPattern.BuildSystemFileAssociationHandlers", StringComparison.Ordinal)
    && registryScannerSource.Contains("BenchmarkSemantics.RegistryPathPattern.BuildProgIdHandlers", StringComparison.Ordinal)
    && registryScannerSource.Contains("BenchmarkSemantics.RegistryPathPattern.AllFilesShell", StringComparison.Ordinal)
    && registryScannerSource.Contains("BenchmarkSemantics.RegistryPathPattern.BuildSystemFileAssociationShell", StringComparison.Ordinal)
    && registryScannerSource.Contains("BenchmarkSemantics.RegistryPathPattern.BuildProgIdShell", StringComparison.Ordinal)
    && registryScannerSource.Contains("BenchmarkSemantics.IsIgnoredStaticVerbName(verbName)", StringComparison.Ordinal)
    && registryScannerSource.Contains("BenchmarkSemantics.StaticVerb.CommandSubKeyName", StringComparison.Ordinal)
    && registryScannerSource.Contains("BenchmarkSemantics.StaticVerb.MuiVerbValueName", StringComparison.Ordinal)
    && registryScannerSource.Contains("BenchmarkSemantics.BuildStaticVerbUniqueKey(displayName, command)", StringComparison.Ordinal),
    "RegistryScannerUsesSemanticLocationBuilders"
);

AssertTrue(
    !registryScannerSource.Contains("\"All Files (*)\"", StringComparison.Ordinal)
    && !registryScannerSource.Contains("\"Directory [Disabled]\"", StringComparison.Ordinal)
    && !registryScannerSource.Contains("\"Extension (", StringComparison.Ordinal)
    && !registryScannerSource.Contains("\"ProgID (", StringComparison.Ordinal)
    && !registryScannerSource.Contains("@\"*\\shellex\\ContextMenuHandlers\"", StringComparison.Ordinal)
    && !registryScannerSource.Contains("@\"Directory\\shell\"", StringComparison.Ordinal)
    && !registryScannerSource.Contains("SystemFileAssociations\\", StringComparison.Ordinal)
    && !registryScannerSource.Contains("verbName.Equals(\"Attributes\"", StringComparison.Ordinal)
    && !registryScannerSource.Contains("verbName.Equals(\"AnyCode\"", StringComparison.Ordinal),
    "RegistryScannerNoInlineLocationLabelLiterals"
);

AssertTrue(
    !benchmarkSemanticsSource.Contains("bool hasDrive = false;", StringComparison.Ordinal)
    && !benchmarkSemanticsSource.Contains("bool hasFolder = false;", StringComparison.Ordinal)
    && !benchmarkSemanticsSource.Contains("bool hasFile = false;", StringComparison.Ordinal),
    "BenchmarkSemanticsNoLegacyLocationFlagResolution"
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
    && hookIpcSemanticsSource.Contains("ReadChunkSize = 4096", StringComparison.Ordinal)
    && hookIpcSemanticsSource.Contains("MultiValueDelimiter = '|'", StringComparison.Ordinal)
    && hookIpcSemanticsSource.Contains("NoIconToken = \"NONE\"", StringComparison.Ordinal),
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
    && hookIpcClientSource.Contains("HookIpcSemantics.Response.MultiValueDelimiter", StringComparison.Ordinal)
    && hookIpcClientSource.Contains("ShouldRetry(attempt)", StringComparison.Ordinal)
    && hookIpcClientSource.Contains("private static async Task<bool> DelayForRetryAsync", StringComparison.Ordinal)
    && hookIpcClientSource.Contains("await DelayForRetryAsync(attempt)", StringComparison.Ordinal)
    && hookIpcClientSource.Contains("private static void CompleteRoundTrip", StringComparison.Ordinal)
    && hookIpcClientSource.Contains("private static async Task<bool> CompleteRoundTripAndRetryAsync", StringComparison.Ordinal)
    && hookIpcClientSource.Contains("await CompleteRoundTripAndRetryAsync(swRoundTrip, result, attempt)", StringComparison.Ordinal),
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
    && !hookIpcClientSource.Contains("if (ShouldRetry(attempt))", StringComparison.Ordinal)
    && !hookIpcClientSource.Contains("data.names.Split('|',", StringComparison.Ordinal)
    && !hookIpcClientSource.Contains("result.roundtrip_ms += Math.Max(0, (long)swRoundTrip.Elapsed.TotalMilliseconds);\r\n                                if (await DelayForRetryAsync(attempt))", StringComparison.Ordinal),
    "HookIpcClientNoLegacyInlineProtocolRuntimeLiterals"
);

AssertTrue(
    benchmarkServiceSource.Contains("BenchmarkSemantics.Runtime.MaxParallelProbeTasks", StringComparison.Ordinal),
    "BenchmarkServiceUsesMaxParallelProbeTasksConstant"
);

AssertTrue(
    benchmarkServiceSource.Contains("LocalizationService.Instance[\"Dashboard.Value.UnknownWithClsid\"]", StringComparison.Ordinal)
    && !benchmarkServiceSource.Contains("Unknown (", StringComparison.Ordinal),
    "BenchmarkServiceUsesLocalizedUnknownWithClsidName"
);

AssertTrue(
    dashboardViewModelSource.Contains("BenchmarkSemantics.Runtime.HookReconnectStabilizationDelayMs", StringComparison.Ordinal)
    && dashboardViewModelSource.Contains("BenchmarkSemantics.Runtime.ClipboardRetryAttempts", StringComparison.Ordinal)
    && dashboardViewModelSource.Contains("BenchmarkSemantics.Runtime.ClipboardRetryDelayMs", StringComparison.Ordinal)
    && dashboardViewModelSource.Contains("BenchmarkSemantics.Runtime.ClipboardCantOpenHResult", StringComparison.Ordinal),
    "DashboardViewModelUsesRuntimeRetryTimingConstants"
);

AssertTrue(
    !dashboardViewModelSource.Contains("Task.Delay(1000)", StringComparison.Ordinal)
    && !dashboardViewModelSource.Contains("for (int i = 0; i < 5; i++)", StringComparison.Ordinal)
    && !dashboardViewModelSource.Contains("Task.Delay(100)", StringComparison.Ordinal)
    && !dashboardViewModelSource.Contains("0x800401D0", StringComparison.Ordinal),
    "DashboardViewModelNoInlineRetryTimingLiterals"
);

AssertTrue(
    benchmarkServiceSource.Contains("BenchmarkSemantics.Runtime.IpcTimeoutLikeRoundtripThresholdMs", StringComparison.Ordinal),
    "BenchmarkServiceUsesIpcTimeoutThresholdConstant"
);

AssertTrue(
    benchmarkServiceSource.Contains("HookIpcSemantics.Response.MultiValueDelimiter", StringComparison.Ordinal)
    && benchmarkServiceSource.Contains("HookIpcSemantics.Response.NoIconToken", StringComparison.Ordinal),
    "BenchmarkServiceUsesHookIpcResponseSemantics"
);

AssertTrue(
    !benchmarkServiceSource.Contains("hookData.icons.Split('|')", StringComparison.Ordinal)
    && !benchmarkServiceSource.Contains("i != \"NONE\"", StringComparison.Ordinal),
    "BenchmarkServiceNoLegacyHookResponseDelimiterLiterals"
);

AssertTrue(
    benchmarkServiceSource.Contains("BenchmarkSemantics.TryParseStaticVerbUniqueKey(key, out string name, out string command)", StringComparison.Ordinal)
    && !benchmarkServiceSource.Contains("private static bool TryParseStaticVerbKey", StringComparison.Ordinal),
    "BenchmarkServiceUsesStaticVerbKeyParser"
);

AssertTrue(
    benchmarkServiceSource.Contains("BenchmarkSemantics.BuildStaticVerbRegistryLocation(p)", StringComparison.Ordinal)
    && benchmarkServiceSource.Contains("paths.Any(BenchmarkSemantics.IsStaticVerbRegistryPathDisabled)", StringComparison.Ordinal),
    "BenchmarkServiceUsesStaticVerbRegistrySemanticHelpers"
);

AssertTrue(
    benchmarkServiceSource.Contains("BenchmarkSemantics.IsTimeoutLikeError(hookData.error)", StringComparison.Ordinal)
    && !benchmarkServiceSource.Contains("hookData.error.Contains(\"Timeout\"", StringComparison.Ordinal),
    "BenchmarkServiceUsesTimeoutErrorSemanticHelper"
);

AssertTrue(
    benchmarkServiceSource.Contains("BenchmarkSemantics.IsSkipUnstableHandlersEnabled()", StringComparison.Ordinal)
    && benchmarkServiceSource.Contains("BenchmarkSemantics.ContainsKnownUnstableHandlerToken", StringComparison.Ordinal)
    && !benchmarkServiceSource.Contains("CMP_SKIP_UNSTABLE_HANDLERS", StringComparison.Ordinal),
    "BenchmarkServiceUsesUnstableHandlerSemanticHelpers"
);

AssertTrue(
    !benchmarkServiceSource.Contains("PintoStartScreen", StringComparison.Ordinal)
    && !benchmarkServiceSource.Contains("NvcplDesktopContext", StringComparison.Ordinal)
    && !benchmarkServiceSource.Contains("NvAppDesktopContext", StringComparison.Ordinal)
    && !benchmarkServiceSource.Contains("NVIDIA CPL Context Menu Extension", StringComparison.Ordinal),
    "BenchmarkServiceNoInlineUnstableHandlerTokens"
);

AssertTrue(
    !benchmarkServiceSource.Contains("Registry (Shell) -", StringComparison.Ordinal)
    && !benchmarkServiceSource.Contains("p.Split('\\\\')[0]", StringComparison.Ordinal)
    && !benchmarkServiceSource.Contains("p.Split('\\\\').Last().StartsWith(\"-\")", StringComparison.Ordinal),
    "BenchmarkServiceNoInlineStaticVerbRegistryLiterals"
);

AssertTrue(
    !benchmarkServiceSource.Contains("new SemaphoreSlim(8)", StringComparison.Ordinal)
    && !benchmarkServiceSource.Contains("hookCall.roundtrip_ms >= 1900", StringComparison.Ordinal)
    && !benchmarkServiceSource.Contains("key.Split('|')[1]", StringComparison.Ordinal),
    "BenchmarkServiceNoRuntimeMagicNumericLiterals"
);

string[] forbiddenStatusMagicLiterals =
{
    BenchmarkSemantics.Status.RegistryFallback,
    BenchmarkSemantics.Status.LoadError,
    BenchmarkSemantics.Status.OrphanedMissingDll,
    BenchmarkSemantics.Status.IpcTimeout,
    BenchmarkSemantics.Status.VerifiedViaHook,
    BenchmarkSemantics.Status.HookLoadedNoMenu,
    BenchmarkSemantics.Status.SkippedKnownUnstable
};

AssertSourceDoesNotContainAny(benchmarkServiceSource, "BenchmarkServiceNoStatusMagicLiterals", forbiddenStatusMagicLiterals);
AssertSourceDoesNotContainAny(packageScannerSource, "PackageScannerNoStatusMagicLiterals", forbiddenStatusMagicLiterals);
AssertSourceDoesNotContainAny(dashboardViewModelSource, "DashboardViewModelNoStatusMagicLiterals", forbiddenStatusMagicLiterals);
AssertSourceDoesNotContainAny(statusVisibilityConverterSource, "StatusVisibilityConverterNoStatusMagicLiterals", forbiddenStatusMagicLiterals);

string[] forbiddenDetailedStatusLiterals =
{
    resources["en-US"]["Dashboard.Detail.StaticNotMeasured"],
    resources["en-US"]["Dashboard.Detail.SkippedKnownUnstable"],
    resources["en-US"]["Dashboard.Detail.HookLoadedNoMenu"],
    resources["en-US"]["Dashboard.Detail.HookResponseTimeoutFallback"],
    resources["en-US"]["Dashboard.Detail.HookUnavailableFallback"]
};

AssertSourceDoesNotContainAny(
    benchmarkServiceSource,
    "BenchmarkServiceNoHardcodedDetailedStatusLiterals",
    forbiddenDetailedStatusLiterals
);

string[] requiredDetailLocalizationKeysInBenchmarkService =
{
    "Dashboard.Detail.StaticNotMeasured",
    "Dashboard.Detail.SkippedKnownUnstable",
    "Dashboard.Detail.OrphanedMissingDll",
    "Dashboard.Detail.HookLoadedNoMenu",
    "Dashboard.Detail.HookProbeTimeoutWithError",
    "Dashboard.Detail.HookLoadErrorWithError",
    "Dashboard.Detail.HookResponseTimeoutFallback",
    "Dashboard.Detail.HookUnavailableFallback"
};

foreach (var key in requiredDetailLocalizationKeysInBenchmarkService)
{
    AssertTrue(
        benchmarkServiceSource.Contains($"[\"{key}\"]", StringComparison.Ordinal),
        $"BenchmarkServiceUsesLocalizedDetailKey:{key}"
    );
}

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
    dashboardViewModelSource.Contains("private void NotifyScanComplete", StringComparison.Ordinal)
    && dashboardViewModelSource.Contains("NotifyScanComplete(Results.Count)", StringComparison.Ordinal)
    && dashboardViewModelSource.Contains("NotifyScanComplete(results.Count, filePath)", StringComparison.Ordinal),
    "DashboardViewModelUsesScanCompleteHelper"
);

AssertTrue(
    dashboardViewModelSource.Contains("private void HandleScanFailure", StringComparison.Ordinal)
    && dashboardViewModelSource.Contains("HandleScanFailure(\"Scan System Failed\"", StringComparison.Ordinal)
    && dashboardViewModelSource.Contains("HandleScanFailure(\"File Scan Failed\"", StringComparison.Ordinal),
    "DashboardViewModelUsesScanFailureHelper"
);

AssertTrue(
    registryPathHelperSource.Contains("public static class RegistryPathHelper", StringComparison.Ordinal)
    && registryPathHelperSource.Contains("NormalizeForRegedit", StringComparison.Ordinal)
    && registryPathHelperSource.Contains("ClassesRootPrefix", StringComparison.Ordinal),
    "RegistryPathHelperExists"
);

AssertTrue(
    dashboardViewModelSource.Contains("RegistryPathHelper.NormalizeForRegedit(path)", StringComparison.Ordinal),
    "DashboardViewModelUsesRegistryPathHelper"
);

AssertTrue(
    !dashboardViewModelSource.Contains("path.StartsWith(\"*\\\\\")", StringComparison.Ordinal)
    && !dashboardViewModelSource.Contains("HKEY_CLASSES_ROOT\\\\", StringComparison.Ordinal),
    "DashboardViewModelNoInlineRegistryPathNormalization"
);

AssertTrue(
    dashboardViewModelSource.Contains("class DashboardViewModel : ObservableObject, IDisposable", StringComparison.Ordinal),
    "DashboardViewModelImplementsDisposable"
);

AssertTrue(
    dashboardViewModelSource.Contains("if (token.IsCancellationRequested || _disposed) return;", StringComparison.Ordinal),
    "DashboardViewModelGuardsFilterUpdatesAfterDispose"
);

AssertTrue(
    dashboardViewModelSource.Contains("private void OnLocalizationChanged", StringComparison.Ordinal)
    && dashboardViewModelSource.Contains("private void OnHookServicePropertyChanged", StringComparison.Ordinal)
    && dashboardViewModelSource.Contains("if (_disposed)", StringComparison.Ordinal),
    "DashboardViewModelGuardsEventHandlersAfterDispose"
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
