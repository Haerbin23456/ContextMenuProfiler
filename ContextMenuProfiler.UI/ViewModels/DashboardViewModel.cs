using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContextMenuProfiler.UI.Core;
using ContextMenuProfiler.UI.Core.Helpers;
using ContextMenuProfiler.UI.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    public partial class DashboardViewModel : ObservableObject
    {
        private readonly BenchmarkService _benchmarkService;
        private CancellationTokenSource? _filterCts;

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
        private string _selectedCategory = "All";

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
            // Cancel previous filter task
            _filterCts?.Cancel();
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

                if (token.IsCancellationRequested) return;

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
            // Category Match
            bool categoryMatch = SelectedCategory == "All" || result.Category == SelectedCategory;
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
        private string _statusText = "Ready to scan";

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
        private string _realLoadTime = "N/A"; // Display string for Real Shell Benchmark

        private string _lastScanMode = "None"; // "System" or "File"
        private string _lastScanPath = "";
        private long _scanOrderCounter = 0;

        public HookStatus CurrentHookStatus => HookService.Instance.CurrentStatus;

        public string HookStatusMessage
        {
            get
            {
                return CurrentHookStatus switch
                {
                    HookStatus.Active => "Hook Service Active",
                    HookStatus.Injected => "DLL Injected, Waiting for Pipe...",
                    HookStatus.Disconnected => "Hook Disconnected (Injection Required)",
                    _ => "Unknown Status"
                };
            }
        }

        [ObservableProperty]
        private ObservableCollection<CategoryItem> _categories = new();

        public DashboardViewModel()
        {
            _benchmarkService = new BenchmarkService();
            // Removed sync ScanResultsView setup

            // Initialize categories
            Categories = new ObservableCollection<CategoryItem>
            {
                new CategoryItem { Name = "All", Tag = "All", Icon = SymbolRegular.TableMultiple20, IsActive = true },
                new CategoryItem { Name = "Files", Tag = "File", Icon = SymbolRegular.Document20 },
                new CategoryItem { Name = "Folders", Tag = "Folder", Icon = SymbolRegular.Folder20 },
                new CategoryItem { Name = "Background", Tag = "Background", Icon = SymbolRegular.Image20 },
                new CategoryItem { Name = "Drives", Tag = "Drive", Icon = SymbolRegular.HardDrive20 },
                new CategoryItem { Name = "UWP/Modern", Tag = "UWP", Icon = SymbolRegular.Box20 },
                new CategoryItem { Name = "Static Verbs", Tag = "Static", Icon = SymbolRegular.PuzzlePiece20 }
            };

            // Observe Hook status changes to update command availability
            HookService.Instance.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(HookService.CurrentStatus))
                {
                    App.Current.Dispatcher.Invoke(() => {
                        OnPropertyChanged(nameof(CurrentHookStatus));
                        OnPropertyChanged(nameof(HookStatusMessage));
                        ScanSystemCommand.NotifyCanExecuteChanged();
                        PickAndScanFileCommand.NotifyCanExecuteChanged();
                        RefreshCommand.NotifyCanExecuteChanged();
                    });
                }
            };

            // 启动后自动尝试注入
            _ = AutoEnsureHook();
        }

        [RelayCommand]
        private async Task ReconnectHook()
        {
            StatusText = "Reconnecting Hook...";
            IsBusy = true;
            try
            {
                bool injectOk = await HookService.Instance.InjectAsync();
                if (!injectOk)
                {
                    NotificationService.Instance.ShowError("Inject Failed", "Injector or Hook DLL not found, or elevation was denied.");
                    return;
                }
                await Task.Delay(1000); // Give it a second
                await HookService.Instance.GetStatusAsync();
                if (CurrentHookStatus == HookStatus.Active)
                {
                    NotificationService.Instance.ShowSuccess("Connected", "Hook service is now active.");
                }
                else
                {
                    NotificationService.Instance.ShowWarning("Partial Success", "DLL injected, but pipe not responding yet.");
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error("Reconnect Failed", ex);
                NotificationService.Instance.ShowError("Reconnect Failed", ex.Message);
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        private async Task AutoEnsureHook()
        {
            var status = await HookService.Instance.GetStatusAsync();
            if (status == HookStatus.Disconnected)
            {
                StatusText = "Initializing Hook Service...";
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
            if (_lastScanMode == "System")
            {
                await ScanSystem();
            }
            else if (_lastScanMode == "File" && !string.IsNullOrEmpty(_lastScanPath))
            {
                await ScanFile(_lastScanPath);
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteBenchmark))]
        private async Task ScanSystem()
        {
            _lastScanMode = "System";
            StatusText = "Scanning system...";
            IsBusy = true;
            
            App.Current.Dispatcher.Invoke(() =>
            {
                _scanOrderCounter = 0;
                Results.Clear();
                DisplayResults.Clear();
            });
            
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

                StatusText = $"Scan complete. Found {Results.Count} extensions.";
                NotificationService.Instance.ShowSuccess("Scan Complete", $"Found {Results.Count} extensions.");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error("Scan System Failed", ex);
                StatusText = "Scan failed.";
                NotificationService.Instance.ShowError("Scan Failed", ex.Message);
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
            dialog.Title = "Select a file to analyze context menu";
            dialog.Filter = "All files (*.*)|*.*";
            
            if (dialog.ShowDialog() == true)
            {
                await ScanFile(dialog.FileName);
            }
        }

        [RelayCommand]
        private async Task ScanFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            _lastScanMode = "File";
            _lastScanPath = filePath;
            StatusText = $"Scanning: {filePath}";
            IsBusy = true;
            _scanOrderCounter = 0;
            Results.Clear();
            DisplayResults.Clear(); // Clear display
            RealLoadTime = "Measuring...";

            try
            {
                var results = await Task.Run(() =>
                {
                    List<BenchmarkResult> threadResult = null;
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
                    return threadResult;
                });

                if (results != null)
                {
                    // Use InsertSorted logic for consistency and performance
                    foreach (var res in results.OrderByDescending(r => r.TotalTime))
                    {
                        InsertSorted(res);
                    }
                    UpdateStats();
                    StatusText = $"Scan complete. Found {results.Count} extensions.";
                    NotificationService.Instance.ShowSuccess("Scan Complete", $"Found {results.Count} extensions for {System.IO.Path.GetFileName(filePath)}.");
                }

                // Run Real-World Benchmark (Parallel but after discovery to avoid COM conflicts if any)
                await RunRealBenchmark(filePath);
            }
            catch (Exception ex)
            {
                StatusText = "Scan failed.";
                LogService.Instance.Error("File Scan Failed", ex);
                NotificationService.Instance.ShowError("Scan Failed", ex.Message);
                RealLoadTime = "Error";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RunRealBenchmark(string? filePath = null)
        {
            try
            {
                long elapsed = await Task.Run(() => _benchmarkService.RunRealShellBenchmark(filePath));
                if (elapsed >= 0)
                {
                    RealLoadTime = $"{elapsed} ms";
                }
                else
                {
                    RealLoadTime = "Failed";
                }
            }
            catch
            {
                RealLoadTime = "Error";
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
                
                bool newState = item.IsEnabled; 
                
                if (!newState) // User turned it OFF (IsEnabled is now false)
                {
                    // Logic to Disable
                    if ((item.Type == "UWP" || item.Type == "Packaged Extension" || item.Type == "Packaged COM") && item.Clsid.HasValue)
                    {
                        ExtensionManager.SetExtensionBlockStatus(item.Clsid.Value, item.Name, true);
                    }
                    else if (item.RegistryEntries != null && item.RegistryEntries.Count > 0)
                    {
                        foreach (var entry in item.RegistryEntries)
                        {
                            ExtensionManager.DisableRegistryKey(entry.Path);
                        }
                    }
                    item.Status = "Disabled (Pending Restart)";
                }
                else // User turned it ON (IsEnabled is now true)
                {
                    // Logic to Enable
                    if ((item.Type == "UWP" || item.Type == "Packaged Extension" || item.Type == "Packaged COM") && item.Clsid.HasValue)
                    {
                        ExtensionManager.SetExtensionBlockStatus(item.Clsid.Value, item.Name, false);
                    }
                    else if (item.RegistryEntries != null && item.RegistryEntries.Count > 0)
                    {
                        foreach (var entry in item.RegistryEntries)
                        {
                            ExtensionManager.EnableRegistryKey(entry.Path);
                        }
                    }
                    item.Status = "Enabled (Pending Restart)";
                }
                
                UpdateStats();
             }
             catch (Exception ex)
             {
                 LogService.Instance.Error("Toggle Extension Failed", ex);
                 NotificationService.Instance.ShowError("Toggle Failed", ex.Message);
                 // Revert
                 item.IsEnabled = !item.IsEnabled;
             }
        }

        [RelayCommand]
        private void DeleteExtension(BenchmarkResult item)
        {
            if (item == null) return;
            
            if (item.Type == "UWP")
            {
                NotificationService.Instance.ShowWarning("Not Supported", "Deleting UWP extensions is not supported. Use Disable instead.");
                return;
            }

            // Confirm
            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to permanently delete the extension '{item.Name}'?\n\nThis action will remove the registry keys and cannot be undone.", 
                "Confirm Delete", 
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
                
                NotificationService.Instance.ShowSuccess("Deleted", $"Extension '{item.Name}' has been deleted.");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error("Delete Extension Failed", ex);
                NotificationService.Instance.ShowError("Delete Failed", ex.Message);
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
                        NotificationService.Instance.ShowSuccess("Copied", "CLSID copied to clipboard.");
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
                NotificationService.Instance.ShowError("Copy Failed", "Clipboard is locked by another process.");
            }
        }

        [RelayCommand]
        private void OpenInRegistry(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            
            try
            {
                // Registry path might need translation from HKCR to HKLM/HKCU
                string fullPath = path;
                if (path.StartsWith("*\\")) fullPath = "HKEY_CLASSES_ROOT\\" + path;
                else if (!path.Contains("HKEY_")) fullPath = "HKEY_CLASSES_ROOT\\" + path;

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
                NotificationService.Instance.ShowError("Error", "Failed to open registry editor.");
            }
        }

        private void UpdateStats()
        {
            // Simple optimization: only recalculate if results actually changed
            // and use a single pass where possible
            TotalExtensions = Results.Count;
            
            int disabledCount = 0;
            long totalTime = 0;
            long activeTime = 0;
            long disabledTime = 0;

            foreach (var r in Results)
            {
                totalTime += r.TotalTime;
                if (r.IsEnabled)
                {
                    activeTime += r.TotalTime;
                }
                else
                {
                    disabledCount++;
                    disabledTime += r.TotalTime;
                }
            }

            DisabledExtensions = disabledCount;
            ActiveExtensions = TotalExtensions - disabledCount;
            TotalLoadTime = totalTime;
            ActiveLoadTime = activeTime;
            DisabledLoadTime = disabledTime;
        }
    }
}
