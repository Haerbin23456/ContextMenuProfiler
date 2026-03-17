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

string packageScannerPath = FindFileUpward(@"ContextMenuProfiler.UI\Core\PackageScanner.cs");
string packageScannerSource = File.ReadAllText(packageScannerPath);

string dashboardViewModelPath = FindFileUpward(@"ContextMenuProfiler.UI\ViewModels\DashboardViewModel.cs");
string dashboardViewModelSource = File.ReadAllText(dashboardViewModelPath);

string statusVisibilityConverterPath = FindFileUpward(@"ContextMenuProfiler.UI\Converters\StatusToVisibilityConverter.cs");
string statusVisibilityConverterSource = File.ReadAllText(statusVisibilityConverterPath);

AssertTrue(
    benchmarkServiceSource.Contains("RunBenchmarkAsync(targetPath)", StringComparison.Ordinal),
    "AnalyzeFileUsesPathSpecificBenchmark"
);

AssertTrue(
    !benchmarkServiceSource.Contains("return RunSystemBenchmark(ScanMode.Targeted)", StringComparison.Ordinal),
    "AnalyzeFileDoesNotFallbackToSystemScan"
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

Console.WriteLine("Quality checks passed.");

if (string.Equals(Environment.GetEnvironmentVariable("CMP_LIVE_PROBE"), "1", StringComparison.Ordinal))
{
    var service = new BenchmarkService();
    var results = await service.RunSystemBenchmarkAsync(ScanMode.Targeted);
    var measured = results.Count(r => r.TotalTime > 0);
    var fallback = results.Count(r => string.Equals(r.Status, BenchmarkSemantics.Status.RegistryFallback, StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"Live probe: total={results.Count}, measured={measured}, fallback={fallback}");
}
