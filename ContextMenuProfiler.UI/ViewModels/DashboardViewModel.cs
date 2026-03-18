using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContextMenuProfiler.UI.Core;
using ContextMenuProfiler.UI.Core.Helpers;
using ContextMenuProfiler.UI.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace ContextMenuProfiler.UI.ViewModels
{
    public partial class CategoryItem : ObservableObject
    {
        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private string _tag = "";

        [ObservableProperty]
        private SymbolRegular _icon;

        [ObservableProperty]
        private bool _isActive;
    }

    public partial class DashboardViewModel : ObservableObject, IDisposable
    {
        private enum LastScanMode
        {
            None,
            System,
            File
        }

        private readonly BenchmarkService _benchmarkService;
        private readonly PropertyChangedEventHandler _localizationChangedHandler;
        private readonly PropertyChangedEventHandler _hookServiceChangedHandler;
        private CancellationTokenSource? _filterCts;
        private bool _disposed;

        [ObservableProperty]
        private ObservableCollection<BenchmarkResult> _displayResults = new();

        [ObservableProperty]
        private ObservableCollection<BenchmarkResult> _results = new();

        partial void OnResultsChanged(ObservableCollection<BenchmarkResult> value)
        {
            _ = ApplyFilterAsync();
        }

        [ObservableProperty]
        private string _searchText = "";

        partial void OnSearchTextChanged(string value)
        {
            _ = ApplyFilterAsync();
        }

        [ObservableProperty]
        private string _selectedCategory = BenchmarkSemantics.FilterCategory.All;

        [ObservableProperty]
        private int _selectedCategoryIndex = 0;

        partial void OnSelectedCategoryIndexChanged(int value)
        {
            if (value >= 0 && value < Categories.Count)
            {
                SelectedCategory = Categories[value].Tag;
            }
        }

        partial void OnSelectedCategoryChanged(string value)
        {
            _ = ApplyFilterAsync();
        }

        private async Task ApplyFilterAsync()
        {
            if (_disposed)
            {
                return;
            }

            // Cancel previous filter task
            _filterCts?.Cancel();
            _filterCts?.Dispose();
            _filterCts = new CancellationTokenSource();
            var token = _filterCts.Token;

            try
            {
                // 0. Get a stable snapshot of results on UI thread before background processing
                var snapshot = App.Current.Dispatcher.Invoke(() => Results.ToList());

                // 1. Pre-filter in background thread using the snapshot
                var matched = await Task.Run(() => 
                {
                    var query = snapshot.Where(r => 
                    {
                        if (token.IsCancellationRequested) return false;
                        return MatchesFilter(r);
                    });

                    // Apply Sorting using the same logic as InsertSorted
                    var comparer = CurrentComparer;
                    return query.ToList().OrderBy(r => r, new ComparisonComparer<BenchmarkResult>(comparer)).ToList();
                }, token);

                if (token.IsCancellationRequested || _disposed) return;

                // 2. Clear current display
                DisplayResults.Clear();

                // 3. Smooth non-blocking distribution
                // DispatcherPriority.Background ensures that each 'Add' yields to user input/scrolling
                foreach (var item in matched)
                {
                    if (token.IsCancellationRequested) break;
                    
                    await App.Current.Dispatcher.InvokeAsync(() => 
                    {
                        if (!token.IsCancellationRequested)
                        {
                            DisplayResults.Add(item);
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch (OperationCanceledException) { }
        }

        private bool MatchesFilter(BenchmarkResult result)
        {
            if (ShowMeasuredOnly && result.TotalTime <= 0)
            {
                return false;
            }

            // Category Match
            bool categoryMatch = BenchmarkSemantics.IsCategoryMatch(SelectedCategory, result.Category);
            if (!categoryMatch) return false;

            // Search Match
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            return result.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                   (result.Path != null && result.Path.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        [ObservableProperty]
        private int _selectedSortIndex = 0; // 0: Time Desc, 1: Time Asc, 2: Name

        partial void OnSelectedSortIndexChanged(int value)
        {
            _ = ApplyFilterAsync();
        }

        [ObservableProperty]
        private string _statusText = LocalizationService.Instance["Dashboard.Status.Ready"];

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
        [NotifyCanExecuteChangedFor(nameof(ScanSystemCommand))]
        [NotifyCanExecuteChangedFor(nameof(PickAndScanFileCommand))]
        private bool _isBusy = false;

        partial void OnIsBusyChanged(bool value)
        {
            HookService.Instance.IsBusy = value;
        }

        [ObservableProperty]
        private int _totalExtensions = 0;

        [ObservableProperty]
        private int _disabledExtensions = 0;
        
        [ObservableProperty]
        private int _activeExtensions = 0;

        [ObservableProperty]
        private long _totalLoadTime = 0;

        [ObservableProperty]
        private long _activeLoadTime = 0;

        [ObservableProperty]
        private long _disabledLoadTime = 0;

        [ObservableProperty]
        private bool _useDeepScan = false;

        [ObservableProperty]
        private bool _showMeasuredOnly = false;

        partial void OnShowMeasuredOnlyChanged(bool value)
        {
            _ = ApplyFilterAsync();
        }

        [ObservableProperty]
        private string _realLoadTime = LocalizationService.Instance["Dashboard.Value.None"]; // Display string for Real Shell Benchmark

        private LastScanMode _lastScanMode = LastScanMode.None;
        private string _lastScanPath = "";
        private long _scanOrderCounter = 0;

        public HookStatus CurrentHookStatus => HookService.Instance.CurrentStatus;

        public string HookStatusMessage
        {
            get
            {
                return CurrentHookStatus switch
                {
                    HookStatus.Active => LocalizationService.Instance["Hook.Active"],
                    HookStatus.Injected => LocalizationService.Instance["Hook.InjectedIdle"],
                    HookStatus.Disconnected => LocalizationService.Instance["Hook.NotInjected"],
                    _ => LocalizationService.Instance["Dashboard.Status.Unknown"]
                };
            }
        }

        [ObservableProperty]
        private ObservableCollection<CategoryItem> _categories = new();

        public DashboardViewModel()
        {
            _benchmarkService = new BenchmarkService();
            // Removed sync ScanResultsView setup

            _localizationChangedHandler = OnLocalizationChanged;
            _hookServiceChangedHandler = OnHookServicePropertyChanged;

            // Initialize categories
            ApplyLocalizedCategoryNames();
            LocalizationService.Instance.PropertyChanged += _localizationChangedHandler;

            // Observe Hook status changes to update command availability
            HookService.Instance.PropertyChanged += _hookServiceChangedHandler;

            // 启动后自动尝试注入
            _ = AutoEnsureHook();
        }

        private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            if (e.PropertyName == "Item[]")
            {
                ApplyLocalizedCategoryNames();
                OnPropertyChanged(nameof(CurrentHookStatus));
                OnPropertyChanged(nameof(HookStatusMessage));
                if (!IsBusy)
                {
                    StatusText = LocalizationService.Instance["Dashboard.Status.Ready"];
                }
            }
        }

        private void OnHookServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            if (e.PropertyName == nameof(HookService.CurrentStatus))
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    OnPropertyChanged(nameof(CurrentHookStatus));
                    OnPropertyChanged(nameof(HookStatusMessage));
                    ScanSystemCommand.NotifyCanExecuteChanged();
                    PickAndScanFileCommand.NotifyCanExecuteChanged();
                    RefreshCommand.NotifyCanExecuteChanged();
                });
            }
        }

        [RelayCommand]
        private async Task ReconnectHook()
        {
            StatusText = LocalizationService.Instance["Dashboard.Status.ReconnectingHook"];
            IsBusy = true;
            try
            {
                bool injectOk = await HookService.Instance.InjectAsync();
                if (!injectOk)
                {
                    NotificationService.Instance.ShowError(
                        LocalizationService.Instance["Dashboard.Notify.InjectFailed.Title"],
                        LocalizationService.Instance["Dashboard.Notify.InjectFailed.Message"]);
                    return;
                }
                await Task.Delay(1000); // Give it a second
                await HookService.Instance.GetStatusAsync();
                if (CurrentHookStatus == HookStatus.Active)
                {
                    NotificationService.Instance.ShowSuccess(
                        LocalizationService.Instance["Dashboard.Notify.HookConnected.Title"],
                        LocalizationService.Instance["Dashboard.Notify.HookConnected.Message"]);
                }
                else
                {
                    NotificationService.Instance.ShowWarning(
                        LocalizationService.Instance["Dashboard.Notify.HookPartial.Title"],
                        LocalizationService.Instance["Dashboard.Notify.HookPartial.Message"]);
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error("Reconnect Failed", ex);
                NotificationService.Instance.ShowError(LocalizationService.Instance["Dashboard.Notify.ReconnectFailed.Title"], ex.Message);
            }
            finally
            {
                IsBusy = false;
                StatusText = LocalizationService.Instance["Dashboard.Status.Ready"];
            }
        }

        private async Task AutoEnsureHook()
        {
            var status = await HookService.Instance.GetStatusAsync();
            if (status == HookStatus.Disconnected)
            {
                StatusText = LocalizationService.Instance["Dashboard.Status.InitializingHook"];
                await HookService.Instance.InjectAsync();
            }
        }

        private bool CanExecuteBenchmark()
        {
            return !IsBusy && HookService.Instance.CurrentStatus == HookStatus.Active;
        }

        [RelayCommand(CanExecute = nameof(CanExecuteBenchmark))]
        private async Task Refresh()
        {
            if (_lastScanMode == LastScanMode.System)
            {
                await ScanSystem();
            }
            else if (_lastScanMode == LastScanMode.File && !string.IsNullOrEmpty(_lastScanPath))
            {
                await ScanFile(_lastScanPath);
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteBenchmark))]
        private async Task ScanSystem()
        {
            BeginScanSession(
                LastScanMode.System,
                LocalizationService.Instance["Dashboard.Status.ScanningSystem"]);
            
            try
            {
                int pendingUiUpdates = 0;
                bool producerCompleted = false;
                var uiDrainTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                void TryCompleteUiDrain()
                {
                    if (producerCompleted && Volatile.Read(ref pendingUiUpdates) == 0)
                    {
                        uiDrainTcs.TrySetResult(true);
                    }
                }

                var progressAction = new Action<BenchmarkResult>(result =>
                {
                    // Use Background priority to ensure UI remains smooth during scan
                    Interlocked.Increment(ref pendingUiUpdates);
                    var dispatcherTask = App.Current.Dispatcher.InvokeAsync(() => 
                    {
                        InsertSorted(result);
                        UpdateStats();
                    }, System.Windows.Threading.DispatcherPriority.Background).Task;

                    _ = dispatcherTask.ContinueWith(t =>
                    {
                        Interlocked.Decrement(ref pendingUiUpdates);
                        if (t.IsFaulted && t.Exception != null)
                        {
                            LogService.Instance.Error("Progress UI update failed", t.Exception);
                        }
                        TryCompleteUiDrain();
                    }, TaskScheduler.Default);
                });

                var mode = UseDeepScan ? ScanMode.Full : ScanMode.Targeted;
                await Task.Run(async () => await _benchmarkService.RunSystemBenchmarkAsync(mode, new Progress<BenchmarkResult>(progressAction)));
                producerCompleted = true;
                TryCompleteUiDrain();
                await uiDrainTcs.Task;

                // Ensure summary values are refreshed even if no progress callback was emitted.
                UpdateStats();

                NotifyScanComplete(Results.Count);
            }
            catch (Exception ex)
            {
                HandleScanFailure("Scan System Failed", ex, setRealLoadError: false);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void InsertSorted(BenchmarkResult newItem)
        {
            if (newItem.ScanOrder <= 0)
            {
                newItem.ScanOrder = Interlocked.Increment(ref _scanOrderCounter);
            }

            // Use BinarySearch-based extension for maximum efficiency
            Results.InsertSorted(newItem, CurrentComparer);

            // Sync to display collection if it matches current filter
            if (MatchesFilter(newItem))
            {
                DisplayResults.InsertSorted(newItem, CurrentComparer);
            }
        }

        private Comparison<BenchmarkResult> CurrentComparer => SelectedSortIndex switch
        {
            0 => (a, b) => b.TotalTime.CompareTo(a.TotalTime), // Time Desc
            1 => (a, b) => a.TotalTime.CompareTo(b.TotalTime), // Time Asc
            2 => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase), // Name
            3 => (a, b) => b.ScanOrder.CompareTo(a.ScanOrder), // Latest Scanned First
            _ => (a, b) => b.TotalTime.CompareTo(a.TotalTime)
        };

        [RelayCommand(CanExecute = nameof(CanExecuteBenchmark))]
        private async Task PickAndScanFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Title = LocalizationService.Instance["Dashboard.Dialog.SelectFileTitle"];
            dialog.Filter = LocalizationService.Instance["Dashboard.Dialog.AllFilesFilter"];
            
            if (dialog.ShowDialog() == true)
            {
                await ScanFile(dialog.FileName);
            }
        }

        [RelayCommand]
        private async Task ScanFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            BeginScanSession(
                LastScanMode.File,
                string.Format(LocalizationService.Instance["Dashboard.Status.ScanningFile"], filePath),
                filePath);

            try
            {
                var results = await RunFileBenchmarkInStaAsync(filePath);

                if (results.Count > 0)
                {
                    // Use InsertSorted logic for consistency and performance
                    foreach (var res in results.OrderByDescending(r => r.TotalTime))
                    {
                        InsertSorted(res);
                    }
                    UpdateStats();
                    NotifyScanComplete(results.Count, filePath);
                }

            }
            catch (Exception ex)
            {
                HandleScanFailure("File Scan Failed", ex, setRealLoadError: true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void NotifyScanComplete(int resultCount, string? filePath = null)
        {
            StatusText = string.Format(LocalizationService.Instance["Dashboard.Status.ScanComplete"], resultCount);

            string message = string.IsNullOrEmpty(filePath)
                ? string.Format(LocalizationService.Instance["Dashboard.Notify.ScanComplete.Message"], resultCount)
                : string.Format(
                    LocalizationService.Instance["Dashboard.Notify.ScanCompleteForFile.Message"],
                    resultCount,
                    System.IO.Path.GetFileName(filePath));

            NotificationService.Instance.ShowSuccess(
                LocalizationService.Instance["Dashboard.Notify.ScanComplete.Title"],
                message);
        }

        private void HandleScanFailure(string logMessage, Exception ex, bool setRealLoadError)
        {
            LogService.Instance.Error(logMessage, ex);
            StatusText = LocalizationService.Instance["Dashboard.Status.ScanFailed"];
            NotificationService.Instance.ShowError(LocalizationService.Instance["Dashboard.Notify.ScanFailed.Title"], ex.Message);

            if (setRealLoadError)
            {
                RealLoadTime = LocalizationService.Instance["Dashboard.RealLoad.Error"];
            }
        }

        private void BeginScanSession(LastScanMode mode, string scanningStatusText, string scanPath = "")
        {
            _lastScanMode = mode;
            _lastScanPath = scanPath;
            StatusText = scanningStatusText;
            IsBusy = true;
            RealLoadTime = LocalizationService.Instance["Dashboard.RealLoad.Measuring"];

            App.Current.Dispatcher.Invoke(() =>
            {
                _scanOrderCounter = 0;
                Results.Clear();
                DisplayResults.Clear();
            });
        }

        private async Task<List<BenchmarkResult>> RunFileBenchmarkInStaAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                List<BenchmarkResult>? threadResult = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        threadResult = _benchmarkService.RunBenchmark(filePath);
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Error("Background File Scan Error", ex);
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
                return threadResult ?? new List<BenchmarkResult>();
            });
        }

        private void ApplyLocalizedCategoryNames()
        {
            Categories = new ObservableCollection<CategoryItem>
            {
                new CategoryItem { Name = LocalizationService.Instance["Dashboard.Category.All"], Tag = BenchmarkSemantics.FilterCategory.All, Icon = SymbolRegular.TableMultiple20, IsActive = true },
                new CategoryItem { Name = LocalizationService.Instance["Dashboard.Category.Files"], Tag = BenchmarkSemantics.Category.File, Icon = SymbolRegular.Document20 },
                new CategoryItem { Name = LocalizationService.Instance["Dashboard.Category.Folders"], Tag = BenchmarkSemantics.Category.Folder, Icon = SymbolRegular.Folder20 },
                new CategoryItem { Name = LocalizationService.Instance["Dashboard.Category.Background"], Tag = BenchmarkSemantics.Category.Background, Icon = SymbolRegular.Image20 },
                new CategoryItem { Name = LocalizationService.Instance["Dashboard.Category.Drives"], Tag = BenchmarkSemantics.Category.Drive, Icon = SymbolRegular.HardDrive20 },
                new CategoryItem { Name = LocalizationService.Instance["Dashboard.Category.UwpModern"], Tag = BenchmarkSemantics.Category.Uwp, Icon = SymbolRegular.Box20 },
                new CategoryItem { Name = LocalizationService.Instance["Dashboard.Category.StaticVerbs"], Tag = BenchmarkSemantics.Category.Static, Icon = SymbolRegular.PuzzlePiece20 }
            };
            if (SelectedCategoryIndex < 0 || SelectedCategoryIndex >= Categories.Count)
            {
                SelectedCategoryIndex = 0;
            }
        }

        private static void ApplyRegistryExtensionState(BenchmarkResult item, bool shouldEnable)
        {
            if (item.RegistryEntries == null || item.RegistryEntries.Count == 0)
            {
                return;
            }

            foreach (var entry in item.RegistryEntries)
            {
                if (shouldEnable)
                {
                    ExtensionManager.EnableRegistryKey(entry.Path);
                }
                else
                {
                    ExtensionManager.DisableRegistryKey(entry.Path);
                }
            }
        }
        
        [RelayCommand]
        private void ToggleExtension(BenchmarkResult item)
        {
             if (item == null) return;
             
             try
             {
                // Note: The UI ToggleSwitch binds TwoWay to IsEnabled. 
                // When this command is executed (e.g. by Click), the property might already be updated or not.
                // We rely on the Command execution.
                
                bool shouldEnable = item.IsEnabled;

                if (BenchmarkSemantics.IsPackagedExtensionType(item.Type) && item.Clsid.HasValue)
                {
                    ExtensionManager.SetExtensionBlockStatus(item.Clsid.Value, item.Name, !shouldEnable);
                }
                else
                {
                    ApplyRegistryExtensionState(item, shouldEnable);
                }

                item.Status = shouldEnable
                    ? LocalizationService.Instance["Dashboard.Status.EnabledPendingRestart"]
                    : LocalizationService.Instance["Dashboard.Status.DisabledPendingRestart"];
                
                UpdateStats();
             }
             catch (Exception ex)
             {
                 LogService.Instance.Error("Toggle Extension Failed", ex);
                 NotificationService.Instance.ShowError(LocalizationService.Instance["Dashboard.Notify.ToggleFailed.Title"], ex.Message);
                 // Revert
                 item.IsEnabled = !item.IsEnabled;
             }
        }

        [RelayCommand]
        private void DeleteExtension(BenchmarkResult item)
        {
            if (item == null) return;
            
            if (BenchmarkSemantics.IsPackagedExtensionType(item.Type))
            {
                NotificationService.Instance.ShowWarning(
                    LocalizationService.Instance["Dashboard.Notify.DeleteNotSupported.Title"],
                    LocalizationService.Instance["Dashboard.Notify.DeleteNotSupported.Message"]);
                return;
            }

            // Confirm
            var result = System.Windows.MessageBox.Show(
                string.Format(LocalizationService.Instance["Dashboard.Dialog.ConfirmDelete.Message"], item.Name),
                LocalizationService.Instance["Dashboard.Dialog.ConfirmDelete.Title"],
                System.Windows.MessageBoxButton.YesNo, 
                System.Windows.MessageBoxImage.Warning);
                
            if (result != System.Windows.MessageBoxResult.Yes) return;

            try
            {
                if (item.RegistryEntries != null && item.RegistryEntries.Count > 0)
                {
                    foreach (var entry in item.RegistryEntries)
                    {
                        ExtensionManager.DeleteRegistryKey(entry.Path);
                    }
                }
                
                // Remove from collections
                Results.Remove(item);
                DisplayResults.Remove(item);
                UpdateStats();
                
                NotificationService.Instance.ShowSuccess(
                    LocalizationService.Instance["Dashboard.Notify.DeleteSuccess.Title"],
                    string.Format(LocalizationService.Instance["Dashboard.Notify.DeleteSuccess.Message"], item.Name));
            }
            catch (Exception ex)
            {
                LogService.Instance.Error("Delete Extension Failed", ex);
                NotificationService.Instance.ShowError(LocalizationService.Instance["Dashboard.Notify.DeleteFailed.Title"], ex.Message);
            }
        }

        [RelayCommand]
        private async Task CopyClsid(BenchmarkResult item)
        {
            if (item?.Clsid != null)
            {
                string clsid = item.Clsid.Value.ToString("B");
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        Clipboard.SetText(clsid);
                        NotificationService.Instance.ShowSuccess(
                            LocalizationService.Instance["Dashboard.Notify.CopySuccess.Title"],
                            LocalizationService.Instance["Dashboard.Notify.CopySuccess.Message"]);
                        return;
                    }
                    catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.ErrorCode == 0x800401D0)
                    {
                        // CLIPBRD_E_CANT_OPEN - Wait and retry
                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Error("Clipboard Copy Failed", ex);
                        break;
                    }
                }
                NotificationService.Instance.ShowError(
                    LocalizationService.Instance["Dashboard.Notify.CopyFailed.Title"],
                    LocalizationService.Instance["Dashboard.Notify.CopyFailed.Message"]);
            }
        }

        [RelayCommand]
        private void OpenInRegistry(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            
            try
            {
                string fullPath = RegistryPathHelper.NormalizeForRegedit(path);
                if (string.IsNullOrEmpty(fullPath))
                {
                    return;
                }

                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit"))
                {
                    key.SetValue("LastKey", fullPath);
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "regedit.exe",
                    UseShellExecute = true,
                    Verb = "runas" // Ensure it requests elevation
                });
            }
            catch (Exception)
            {
                NotificationService.Instance.ShowError(
                    LocalizationService.Instance["Dashboard.Notify.OpenRegistryFailed.Title"],
                    LocalizationService.Instance["Dashboard.Notify.OpenRegistryFailed.Message"]);
            }
        }

        private void UpdateStats()
        {
            var stats = BenchmarkStatisticsCalculator.Calculate(Results);

            TotalExtensions = stats.TotalExtensions;
            DisabledExtensions = stats.DisabledExtensions;
            ActiveExtensions = stats.ActiveExtensions;
            TotalLoadTime = stats.TotalLoadTime;
            ActiveLoadTime = stats.ActiveLoadTime;
            DisabledLoadTime = stats.DisabledLoadTime;
            RealLoadTime = stats.RealLoadTimeMs > 0
                ? $"{stats.RealLoadTimeMs} ms"
                : LocalizationService.Instance["Dashboard.Value.None"];
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            LocalizationService.Instance.PropertyChanged -= _localizationChangedHandler;
            HookService.Instance.PropertyChanged -= _hookServiceChangedHandler;

            _filterCts?.Cancel();
            _filterCts?.Dispose();
            _filterCts = null;

            GC.SuppressFinalize(this);
        }
    }
}
