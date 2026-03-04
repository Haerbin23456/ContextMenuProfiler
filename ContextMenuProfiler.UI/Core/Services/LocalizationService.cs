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
                ["Status.Version"] = "Context Menu Profiler v1.0",
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
                ["Dashboard.Value.None"] = "None",
                ["Dashboard.Action.Copy"] = "Copy",
                ["Dashboard.Action.DeletePermanently"] = "Delete Permanently",
                ["Dashboard.Sort.LoadDesc"] = "Load Time (High to Low)",
                ["Dashboard.Sort.LoadAsc"] = "Load Time (Low to High)",
                ["Dashboard.Sort.NameAsc"] = "Name (A-Z)",
                ["Dashboard.Sort.Latest"] = "Latest Scanned First",
                ["Dashboard.Status.ScanningSystem"] = "Scanning system...",
                ["Dashboard.Status.ScanningFile"] = "Scanning: {0}",
                ["Dashboard.Status.ScanComplete"] = "Scan complete. Found {0} extensions.",
                ["Dashboard.Status.ScanFailed"] = "Scan failed.",
                ["Dashboard.Status.Ready"] = "Ready to scan",
                ["Dashboard.Status.Unknown"] = "Unknown Status",
                ["Dashboard.Notify.ScanComplete.Title"] = "Scan Complete",
                ["Dashboard.Notify.ScanComplete.Message"] = "Found {0} extensions.",
                ["Dashboard.Notify.ScanCompleteForFile.Message"] = "Found {0} extensions for {1}.",
                ["Dashboard.Notify.ScanFailed.Title"] = "Scan Failed",
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
                ["Status.Version"] = "右键菜单分析器 v1.0",
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
                ["Dashboard.Value.None"] = "无",
                ["Dashboard.Action.Copy"] = "复制",
                ["Dashboard.Action.DeletePermanently"] = "永久删除",
                ["Dashboard.Sort.LoadDesc"] = "加载时间（高到低）",
                ["Dashboard.Sort.LoadAsc"] = "加载时间（低到高）",
                ["Dashboard.Sort.NameAsc"] = "名称（A-Z）",
                ["Dashboard.Sort.Latest"] = "最近扫描优先",
                ["Dashboard.Status.ScanningSystem"] = "正在扫描系统...",
                ["Dashboard.Status.ScanningFile"] = "正在扫描：{0}",
                ["Dashboard.Status.ScanComplete"] = "扫描完成，共找到 {0} 个扩展。",
                ["Dashboard.Status.ScanFailed"] = "扫描失败。",
                ["Dashboard.Status.Ready"] = "准备开始扫描",
                ["Dashboard.Status.Unknown"] = "未知状态",
                ["Dashboard.Notify.ScanComplete.Title"] = "扫描完成",
                ["Dashboard.Notify.ScanComplete.Message"] = "共找到 {0} 个扩展。",
                ["Dashboard.Notify.ScanCompleteForFile.Message"] = "已为 {1} 找到 {0} 个扩展。",
                ["Dashboard.Notify.ScanFailed.Title"] = "扫描失败",
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
