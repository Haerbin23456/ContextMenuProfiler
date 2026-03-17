using ContextMenuProfiler.UI.Core;

static void AssertEqual(string expected, string actual, string caseName)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{caseName} failed. expected='{expected}', actual='{actual}'");
    }
}

static void AssertNull(string? value, string caseName)
{
    if (value != null)
    {
        throw new InvalidOperationException($"{caseName} failed. expected null, actual='{value}'");
    }
}

string requestWithoutHint = HookIpcClient.BuildRequest("{00000000-0000-0000-0000-000000000000}", @"C:\Temp\a.txt", null);
AssertEqual("CMP1|AUTO|{00000000-0000-0000-0000-000000000000}|C:\\Temp\\a.txt", requestWithoutHint, "BuildRequestWithoutHint");

string requestWithHint = HookIpcClient.BuildRequest("{00000000-0000-0000-0000-000000000000}", @"C:\Temp\a.txt", @"C:\x\h.dll");
AssertEqual("CMP1|AUTO|{00000000-0000-0000-0000-000000000000}|C:\\Temp\\a.txt|C:\\x\\h.dll", requestWithHint, "BuildRequestWithHint");

string? malformed = HookIpcClient.ExtractJsonEnvelope("ERR|NOT_JSON");
AssertNull(malformed, "ExtractJsonMalformed");

string? wrapped = HookIpcClient.ExtractJsonEnvelope("noise{\"success\":true,\"state\":1}tail");
AssertEqual("{\"success\":true,\"state\":1}", wrapped ?? "", "ExtractJsonWrapped");

Console.WriteLine("Quality checks passed.");

if (string.Equals(Environment.GetEnvironmentVariable("CMP_LIVE_PROBE"), "1", StringComparison.Ordinal))
{
    var service = new BenchmarkService();
    var results = await service.RunSystemBenchmarkAsync(ScanMode.Targeted);
    var measured = results.Count(r => r.TotalTime > 0);
    var fallback = results.Count(r => string.Equals(r.Status, "Registry Fallback", StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"Live probe: total={results.Count}, measured={measured}, fallback={fallback}");
}
