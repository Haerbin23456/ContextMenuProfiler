using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;

namespace ContextMenuProfiler.UI.Core.Services
{
    public class LanguageOption
    {
        public string Code { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    public class LocalizationService : ObservableObject
    {
        private static LocalizationService? _instance;
        public static LocalizationService Instance => _instance ??= new LocalizationService();

        private readonly Dictionary<string, Dictionary<string, string>> _resources = new(StringComparer.OrdinalIgnoreCase);
        private string _currentLanguageCode = "en-US";

        public ReadOnlyCollection<LanguageOption> AvailableLanguages { get; }

        public string CurrentLanguageCode
        {
            get => _currentLanguageCode;
            private set => SetProperty(ref _currentLanguageCode, value);
        }

        public string this[string key]
        {
            get
            {
                if (_resources.TryGetValue(CurrentLanguageCode, out var dict) && dict.TryGetValue(key, out var value))
                {
                    return value;
                }

                if (_resources.TryGetValue("en-US", out var fallback) && fallback.TryGetValue(key, out var fallbackValue))
                {
                    return fallbackValue;
                }

                return key;
            }
        }

        private LocalizationService()
        {
            AvailableLanguages = new ReadOnlyCollection<LanguageOption>(new List<LanguageOption>
            {
                new LanguageOption { Code = "auto", DisplayName = "System Default" },
                new LanguageOption { Code = "en-US", DisplayName = "English" },
                new LanguageOption { Code = "zh-CN", DisplayName = "简体中文" }
            });

            _resources["en-US"] = BuildEnglish();
            _resources["zh-CN"] = BuildChinese();
        }

        public void InitializeFromPreferences()
        {
            var prefs = UserPreferencesService.Load();
            ApplyLanguage(prefs.LanguageCode, false);
        }

        public void SetLanguage(string code)
        {
            ApplyLanguage(code, true);
        }

        private void ApplyLanguage(string code, bool persist)
        {
            code = string.IsNullOrWhiteSpace(code) ? "auto" : code;
            string resolved = ResolveLanguageCode(code);
            bool languageChanged = !string.Equals(CurrentLanguageCode, resolved, StringComparison.OrdinalIgnoreCase);
            if (languageChanged)
            {
                CurrentLanguageCode = resolved;
                OnPropertyChanged("Item[]");
            }

            if (persist)
            {
                string savedCode = UserPreferencesService.Load().LanguageCode;
                if (!string.Equals(savedCode, code, StringComparison.OrdinalIgnoreCase))
                {
                    UserPreferencesService.Save(new UserPreferences { LanguageCode = code });
                }
            }
        }

        private static string ResolveLanguageCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                string system = CultureInfo.CurrentUICulture.Name;
                if (system.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                {
                    return "zh-CN";
                }
                return "en-US";
            }

            if (code.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-CN";
            }

            return "en-US";
        }

        private static Dictionary<string, string> BuildEnglish()
        {
            return new Dictionary<string, string>
            {
                ["App.Title"] = "Context Menu Profiler",
                ["Nav.Dashboard"] = "Dashboard",
                ["Nav.Settings"] = "Settings",
                ["Tray.Home"] = "Home",
                ["Status.VersionFormat"] = "Context Menu Profiler v{0}",
                ["Hook.NotInjected"] = "Hook: Not Injected",
                ["Hook.Inject"] = "Inject Hook",
                ["Hook.InjectedIdle"] = "Hook: Injected (Idle)",
                ["Hook.Eject"] = "Eject Hook",
                ["Hook.Active"] = "Hook: Active",
                ["Settings.Title"] = "Settings",
                ["Settings.SystemTools"] = "System Tools",
                ["Settings.RestartExplorer"] = "Restart Explorer",
                ["Settings.RestartExplorerDesc"] = "Restarts Windows Explorer process. Useful when shell extensions are stuck or not loading.",
                ["Settings.Restart"] = "Restart",
                ["Settings.Language"] = "Language",
                ["Settings.LanguageDesc"] = "Change UI language. Applies immediately.",
                ["Dialog.ConfirmRestart.Title"] = "Confirm Restart",
                ["Dialog.ConfirmRestart.Message"] = "Are you sure you want to restart Windows Explorer?\nThis will temporarily close all folder windows and the taskbar.",
                ["Dialog.Error.Title"] = "Error",
                ["Dialog.Error.RestartExplorer"] = "Failed to restart Explorer: {0}",
                ["Dashboard.ScanSystem"] = "Scan System",
                ["Dashboard.AnalyzeFile"] = "Analyze File",
                ["Dashboard.Refresh"] = "Refresh (Re-scan)",
                ["Dashboard.DeepScan"] = "Deep Scan",
                ["Dashboard.DeepScanTip"] = "Scan all file extensions (Slower)",
                ["Dashboard.SearchExtensions"] = "Search extensions...",
                ["Dashboard.RealWorldLoad"] = "Real-World Load",
                ["Dashboard.TotalMenuTime"] = "Total Menu Time",
                ["Dashboard.TotalExtensions"] = "Total Extensions",
                ["Dashboard.Active"] = "Active",
                ["Dashboard.HookWarning"] = "The Hook service is required for accurate load time measurement. If it's disconnected, please try to reconnect.",
                ["Dashboard.ReconnectInject"] = "Reconnect / Inject",
                ["Dashboard.PerfBreakdown"] = "Performance Breakdown",
                ["Dashboard.PerfEstimated"] = "Performance data estimated (Hook unavailable)",
                ["Dashboard.Create"] = "Create:",
                ["Dashboard.Initialize"] = "Initialize:",
                ["Dashboard.Query"] = "Query:",
                ["Dashboard.WallClock"] = "Wall Clock:",
                ["Dashboard.Total"] = "Total:",
                ["Dashboard.Diagnostics"] = "Diagnostics",
                ["Dashboard.LockWait"] = "Lock Wait:",
                ["Dashboard.PipeConnect"] = "Pipe Connect:",
                ["Dashboard.IpcRoundTrip"] = "IPC Round Trip:",
                ["Dashboard.Label.Clsid"] = "CLSID:",
                ["Dashboard.Label.Binary"] = "Binary:",
                ["Dashboard.Label.Details"] = "Details:",
                ["Dashboard.Label.Interface"] = "Interface: ",
                ["Dashboard.Label.IconSource"] = "Icon Source: ",
                ["Dashboard.Label.Threading"] = "Threading: ",
                ["Dashboard.Label.RegistryName"] = "Registry Name: ",
                ["Dashboard.Label.Registry"] = "Registry:",
                ["Dashboard.Label.Package"] = "Package:",
                ["Dashboard.Value.Unknown"] = "Unknown",
                ["Dashboard.Value.UnknownWithClsid"] = "Unknown ({0})",
                ["Dashboard.Value.None"] = "None",
                ["Dashboard.Value.ManifestAppLogo"] = "Manifest (App Logo)",
                ["Dashboard.Action.Copy"] = "Copy",
                ["Dashboard.Action.DeletePermanently"] = "Delete Permanently",
                ["Dashboard.Sort.LoadDesc"] = "Load Time (High to Low)",
                ["Dashboard.Sort.LoadAsc"] = "Load Time (Low to High)",
                ["Dashboard.Sort.NameAsc"] = "Name (A-Z)",
                ["Dashboard.Sort.Latest"] = "Latest Scanned First",
                ["Dashboard.FilterMeasuredOnly"] = "Measured only",
                ["Dashboard.FilterMeasuredOnlyTip"] = "Show only entries with valid measured load time (> 0 ms)",
                ["Dashboard.Status.ScanningSystem"] = "Scanning system...",
                ["Dashboard.Status.ScanningFile"] = "Scanning: {0}",
                ["Dashboard.Status.ScanComplete"] = "Scan complete. Found {0} extensions.",
                ["Dashboard.Status.ScanFailed"] = "Scan failed.",
                ["Dashboard.Status.DisabledPendingRestart"] = "Disabled (Pending Restart)",
                ["Dashboard.Status.EnabledPendingRestart"] = "Enabled (Pending Restart)",
                ["Dashboard.Status.ReconnectingHook"] = "Reconnecting Hook...",
                ["Dashboard.Status.InitializingHook"] = "Initializing Hook Service...",
                ["Dashboard.Status.Ready"] = "Ready to scan",
                ["Dashboard.Status.Unknown"] = "Unknown Status",
                ["Dashboard.Detail.StaticNotMeasured"] = "Static shell verbs do not go through Hook COM probing and are displayed as not measured.",
                ["Dashboard.Detail.SkippedKnownUnstable"] = "Skipped Hook invocation for a known unstable system handler to avoid scan-wide IPC stalls.",
                ["Dashboard.Detail.OrphanedMissingDll"] = "The file '{0}' was not found on disk. This extension is likely corrupted or uninstalled.",
                ["Dashboard.Detail.HookLoadedNoMenu"] = "The extension was loaded by the Hook service but it did not provide any context menu items for the test context.",
                ["Dashboard.Detail.HookProbeTimeoutWithError"] = "Hook service timed out while probing this extension. Error: {0}",
                ["Dashboard.Detail.HookLoadErrorWithError"] = "The Hook service failed to load this extension. Error: {0}",
                ["Dashboard.Detail.HookResponseTimeoutFallback"] = "Hook service response timed out for this extension. Data is based on registry scan only.",
                ["Dashboard.Detail.HookUnavailableFallback"] = "The Hook service could not be reached or failed to process this extension. Data is based on registry scan only.",
                ["Dashboard.Notify.ScanComplete.Title"] = "Scan Complete",
                ["Dashboard.Notify.ScanComplete.Message"] = "Found {0} extensions.",
                ["Dashboard.Notify.ScanCompleteForFile.Message"] = "Found {0} extensions for {1}.",
                ["Dashboard.Notify.ScanFailed.Title"] = "Scan Failed",
                ["Dashboard.Notify.InjectFailed.Title"] = "Inject Failed",
                ["Dashboard.Notify.InjectFailed.Message"] = "Injector or Hook DLL not found, or elevation was denied.",
                ["Dashboard.Notify.HookConnected.Title"] = "Connected",
                ["Dashboard.Notify.HookConnected.Message"] = "Hook service is now active.",
                ["Dashboard.Notify.HookPartial.Title"] = "Partial Success",
                ["Dashboard.Notify.HookPartial.Message"] = "DLL injected, but pipe not responding yet.",
                ["Dashboard.Notify.ReconnectFailed.Title"] = "Reconnect Failed",
                ["Dashboard.Notify.ToggleFailed.Title"] = "Toggle Failed",
                ["Dashboard.Notify.DeleteNotSupported.Title"] = "Not Supported",
                ["Dashboard.Notify.DeleteNotSupported.Message"] = "Deleting UWP extensions is not supported. Use Disable instead.",
                ["Dashboard.Notify.DeleteSuccess.Title"] = "Deleted",
                ["Dashboard.Notify.DeleteSuccess.Message"] = "Extension '{0}' has been deleted.",
                ["Dashboard.Notify.DeleteFailed.Title"] = "Delete Failed",
                ["Dashboard.Notify.CopySuccess.Title"] = "Copied",
                ["Dashboard.Notify.CopySuccess.Message"] = "CLSID copied to clipboard.",
                ["Dashboard.Notify.CopyFailed.Title"] = "Copy Failed",
                ["Dashboard.Notify.CopyFailed.Message"] = "Clipboard is locked by another process.",
                ["Dashboard.Notify.OpenRegistryFailed.Title"] = "Error",
                ["Dashboard.Notify.OpenRegistryFailed.Message"] = "Failed to open registry editor.",
                ["Dashboard.Dialog.ConfirmDelete.Title"] = "Confirm Delete",
                ["Dashboard.Dialog.ConfirmDelete.Message"] = "Are you sure you want to permanently delete the extension '{0}'?\n\nThis action will remove the registry keys and cannot be undone.",
                ["Dashboard.Dialog.SelectFileTitle"] = "Select a file to analyze context menu",
                ["Dashboard.Dialog.AllFilesFilter"] = "All files (*.*)|*.*",
                ["Dashboard.RealLoad.Measuring"] = "Measuring...",
                ["Dashboard.RealLoad.Failed"] = "Failed",
                ["Dashboard.RealLoad.Error"] = "Error",
                ["Dashboard.Category.All"] = "All",
                ["Dashboard.Category.Files"] = "Files",
                ["Dashboard.Category.Folders"] = "Folders",
                ["Dashboard.Category.Background"] = "Background",
                ["Dashboard.Category.Drives"] = "Drives",
                ["Dashboard.Category.UwpModern"] = "UWP/Modern",
                ["Dashboard.Category.StaticVerbs"] = "Static Verbs"
            };
        }

        private static Dictionary<string, string> BuildChinese()
        {
            return new Dictionary<string, string>
            {
                ["App.Title"] = "右键菜单分析器",
                ["Nav.Dashboard"] = "仪表盘",
                ["Nav.Settings"] = "设置",
                ["Tray.Home"] = "主页",
                ["Status.VersionFormat"] = "右键菜单分析器 v{0}",
                ["Hook.NotInjected"] = "Hook：未注入",
                ["Hook.Inject"] = "注入 Hook",
                ["Hook.InjectedIdle"] = "Hook：已注入（空闲）",
                ["Hook.Eject"] = "卸载 Hook",
                ["Hook.Active"] = "Hook：已连接",
                ["Settings.Title"] = "设置",
                ["Settings.SystemTools"] = "系统工具",
                ["Settings.RestartExplorer"] = "重启资源管理器",
                ["Settings.RestartExplorerDesc"] = "重启 Windows 资源管理器进程。适用于 Shell 扩展卡住或未加载的情况。",
                ["Settings.Restart"] = "重启",
                ["Settings.Language"] = "语言",
                ["Settings.LanguageDesc"] = "切换界面语言，立即生效。",
                ["Dialog.ConfirmRestart.Title"] = "确认重启",
                ["Dialog.ConfirmRestart.Message"] = "确定要重启 Windows 资源管理器吗？\n这会暂时关闭所有文件夹窗口和任务栏。",
                ["Dialog.Error.Title"] = "错误",
                ["Dialog.Error.RestartExplorer"] = "重启资源管理器失败：{0}",
                ["Dashboard.ScanSystem"] = "扫描系统",
                ["Dashboard.AnalyzeFile"] = "分析文件",
                ["Dashboard.Refresh"] = "刷新（重新扫描）",
                ["Dashboard.DeepScan"] = "深度扫描",
                ["Dashboard.DeepScanTip"] = "扫描全部文件扩展（更慢）",
                ["Dashboard.SearchExtensions"] = "搜索扩展...",
                ["Dashboard.RealWorldLoad"] = "真实加载",
                ["Dashboard.TotalMenuTime"] = "菜单总耗时",
                ["Dashboard.TotalExtensions"] = "扩展总数",
                ["Dashboard.Active"] = "已启用",
                ["Dashboard.HookWarning"] = "准确测量加载时间需要 Hook 服务。如果断开，请尝试重新连接。",
                ["Dashboard.ReconnectInject"] = "重连 / 注入",
                ["Dashboard.PerfBreakdown"] = "性能明细",
                ["Dashboard.PerfEstimated"] = "性能数据为估算值（Hook 不可用）",
                ["Dashboard.Create"] = "创建：",
                ["Dashboard.Initialize"] = "初始化：",
                ["Dashboard.Query"] = "查询：",
                ["Dashboard.WallClock"] = "端到端：",
                ["Dashboard.Total"] = "合计：",
                ["Dashboard.Diagnostics"] = "诊断",
                ["Dashboard.LockWait"] = "锁等待：",
                ["Dashboard.PipeConnect"] = "管道连接：",
                ["Dashboard.IpcRoundTrip"] = "IPC 往返：",
                ["Dashboard.Label.Clsid"] = "CLSID：",
                ["Dashboard.Label.Binary"] = "二进制：",
                ["Dashboard.Label.Details"] = "详情：",
                ["Dashboard.Label.Interface"] = "接口：",
                ["Dashboard.Label.IconSource"] = "图标来源：",
                ["Dashboard.Label.Threading"] = "线程模型：",
                ["Dashboard.Label.RegistryName"] = "注册表名称：",
                ["Dashboard.Label.Registry"] = "注册表：",
                ["Dashboard.Label.Package"] = "包：",
                ["Dashboard.Value.Unknown"] = "未知",
                ["Dashboard.Value.UnknownWithClsid"] = "未知 ({0})",
                ["Dashboard.Value.None"] = "无",
                ["Dashboard.Value.ManifestAppLogo"] = "清单（应用图标）",
                ["Dashboard.Action.Copy"] = "复制",
                ["Dashboard.Action.DeletePermanently"] = "永久删除",
                ["Dashboard.Sort.LoadDesc"] = "加载时间（高到低）",
                ["Dashboard.Sort.LoadAsc"] = "加载时间（低到高）",
                ["Dashboard.Sort.NameAsc"] = "名称（A-Z）",
                ["Dashboard.Sort.Latest"] = "最近扫描优先",
                ["Dashboard.FilterMeasuredOnly"] = "仅看已测量",
                ["Dashboard.FilterMeasuredOnlyTip"] = "仅显示有有效测量耗时（> 0 ms）的项目",
                ["Dashboard.Status.ScanningSystem"] = "正在扫描系统...",
                ["Dashboard.Status.ScanningFile"] = "正在扫描：{0}",
                ["Dashboard.Status.ScanComplete"] = "扫描完成，共找到 {0} 个扩展。",
                ["Dashboard.Status.ScanFailed"] = "扫描失败。",
                ["Dashboard.Status.DisabledPendingRestart"] = "已禁用（待重启）",
                ["Dashboard.Status.EnabledPendingRestart"] = "已启用（待重启）",
                ["Dashboard.Status.ReconnectingHook"] = "正在重连 Hook...",
                ["Dashboard.Status.InitializingHook"] = "正在初始化 Hook 服务...",
                ["Dashboard.Status.Ready"] = "准备开始扫描",
                ["Dashboard.Status.Unknown"] = "未知状态",
                ["Dashboard.Detail.StaticNotMeasured"] = "静态命令不会经过 Hook COM 探测流程，因此显示为未测量。",
                ["Dashboard.Detail.SkippedKnownUnstable"] = "为避免扫描期间出现 IPC 卡顿，已跳过已知不稳定系统处理器的 Hook 探测。",
                ["Dashboard.Detail.OrphanedMissingDll"] = "磁盘上未找到文件“{0}”。该扩展可能已损坏或被卸载。",
                ["Dashboard.Detail.HookLoadedNoMenu"] = "该扩展已被 Hook 服务加载，但在当前测试上下文中未提供任何右键菜单项。",
                ["Dashboard.Detail.HookProbeTimeoutWithError"] = "Hook 服务在探测该扩展时超时。错误：{0}",
                ["Dashboard.Detail.HookLoadErrorWithError"] = "Hook 服务加载该扩展失败。错误：{0}",
                ["Dashboard.Detail.HookResponseTimeoutFallback"] = "Hook 服务响应该扩展超时。当前数据仅基于注册表扫描。",
                ["Dashboard.Detail.HookUnavailableFallback"] = "Hook 服务不可达或处理该扩展失败。当前数据仅基于注册表扫描。",
                ["Dashboard.Notify.ScanComplete.Title"] = "扫描完成",
                ["Dashboard.Notify.ScanComplete.Message"] = "共找到 {0} 个扩展。",
                ["Dashboard.Notify.ScanCompleteForFile.Message"] = "已为 {1} 找到 {0} 个扩展。",
                ["Dashboard.Notify.ScanFailed.Title"] = "扫描失败",
                ["Dashboard.Notify.InjectFailed.Title"] = "注入失败",
                ["Dashboard.Notify.InjectFailed.Message"] = "未找到 Injector 或 Hook DLL，或管理员授权被拒绝。",
                ["Dashboard.Notify.HookConnected.Title"] = "已连接",
                ["Dashboard.Notify.HookConnected.Message"] = "Hook 服务已激活。",
                ["Dashboard.Notify.HookPartial.Title"] = "部分成功",
                ["Dashboard.Notify.HookPartial.Message"] = "DLL 已注入，但管道暂未响应。",
                ["Dashboard.Notify.ReconnectFailed.Title"] = "重连失败",
                ["Dashboard.Notify.ToggleFailed.Title"] = "切换失败",
                ["Dashboard.Notify.DeleteNotSupported.Title"] = "不支持",
                ["Dashboard.Notify.DeleteNotSupported.Message"] = "不支持删除 UWP 扩展，请使用禁用功能。",
                ["Dashboard.Notify.DeleteSuccess.Title"] = "删除成功",
                ["Dashboard.Notify.DeleteSuccess.Message"] = "扩展“{0}”已删除。",
                ["Dashboard.Notify.DeleteFailed.Title"] = "删除失败",
                ["Dashboard.Notify.CopySuccess.Title"] = "已复制",
                ["Dashboard.Notify.CopySuccess.Message"] = "CLSID 已复制到剪贴板。",
                ["Dashboard.Notify.CopyFailed.Title"] = "复制失败",
                ["Dashboard.Notify.CopyFailed.Message"] = "剪贴板被其他进程占用。",
                ["Dashboard.Notify.OpenRegistryFailed.Title"] = "错误",
                ["Dashboard.Notify.OpenRegistryFailed.Message"] = "打开注册表编辑器失败。",
                ["Dashboard.Dialog.ConfirmDelete.Title"] = "确认删除",
                ["Dashboard.Dialog.ConfirmDelete.Message"] = "确定要永久删除扩展“{0}”吗？\n\n此操作将删除对应注册表项，且不可恢复。",
                ["Dashboard.Dialog.SelectFileTitle"] = "选择要分析右键菜单的文件",
                ["Dashboard.Dialog.AllFilesFilter"] = "所有文件 (*.*)|*.*",
                ["Dashboard.RealLoad.Measuring"] = "测量中...",
                ["Dashboard.RealLoad.Failed"] = "失败",
                ["Dashboard.RealLoad.Error"] = "错误",
                ["Dashboard.Category.All"] = "全部",
                ["Dashboard.Category.Files"] = "文件",
                ["Dashboard.Category.Folders"] = "文件夹",
                ["Dashboard.Category.Background"] = "背景",
                ["Dashboard.Category.Drives"] = "驱动器",
                ["Dashboard.Category.UwpModern"] = "UWP/现代扩展",
                ["Dashboard.Category.StaticVerbs"] = "静态命令"
            };
        }
    }
}
