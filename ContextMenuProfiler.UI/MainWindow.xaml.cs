using ContextMenuProfiler.UI.ViewModels;
using ContextMenuProfiler.UI.Views.Pages;
using System;
using System.Windows;
using Wpf.Ui.Controls;

namespace ContextMenuProfiler.UI
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindowViewModel ViewModel { get; }

        public MainWindow()
        {
            InitializeComponent();
            
            // Initialize Notification Service
            Core.Services.NotificationService.Instance.Initialize(RootSnackbar);

            ViewModel = new MainWindowViewModel();
            DataContext = ViewModel;
            
            Loaded += (sender, args) =>
            {
                RootNavigation.Navigate(typeof(DashboardPage));
            };
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    string targetFile = files[0];
                    
                    // Try to get DashboardPage from Navigation
                    // Note: This relies on Wpf.Ui implementation details or standard Frame behavior
                    // RootNavigation doesn't expose Frame directly in all versions, but let's try checking properties via reflection or casting if needed.
                    // Actually, let's check if the current content of the navigation control is what we expect.
                    
                    // RootNavigation has a specific frame?
                    // In Wpf.Ui 3.0, RootNavigation itself might be the control handling navigation but the content is displayed in a Frame.
                    // Usually we set Frame for it. But here it seems auto-configured?
                    // Let's assume standard behavior: we need to find the DashboardPage instance.
                    
                    // Fallback: If we can't find it easily, we can't invoke the command.
                    // But let's try to access the Frame.
                    
                    // Wpf.Ui 2.x/3.x: RootNavigation.Frame or RootNavigation.ContentFrame?
                    // Let's try to find the frame in the visual tree if needed, or check properties.
                    // But wait, the DashboardPage is instantiated.
                    
                    // Let's try to access the NavigationService.
                    // Actually, simpler approach:
                    // Just check if RootNavigation.NavigationFrame?.Content is DashboardPage
                    
                    // Note: I'll use dynamic to avoid compile errors if property names differ slightly, 
                    // or I can check the Wpf.Ui source code via memory if I had it.
                    // But 'Frame' or 'NavigationFrame' is common.
                    
                    // Let's try a safer way:
                    // If the user drops a file, we want to Analyze it.
                    // If we are not on Dashboard, navigate there?
                    
                    // Let's inspect RootNavigation in code via simple cast if possible.
                    // But let's assume we can get the content.
                    
                    // To be safe and compilation-friendly without guessing Wpf.Ui API:
                    // We can traverse Visual Tree to find the Frame, or...
                    
                    // Actually, let's just implement the handler and try to cast the Content of the Navigation Control.
                    // In many Wpf.Ui examples: RootNavigation.Navigate(...) 
                    
                    // Let's assume the Frame is accessible.
                    // Looking at the MainWindow.xaml provided earlier:
                    // <ui:NavigationView x:Name="RootNavigation" ... />
                    // It doesn't show a Frame inside in XAML, so it might be internal or set in Code Behind (not seen here).
                    // Wait, Wpf.Ui NavigationView usually REQUIRES a Frame to be assigned to it, OR it has an internal one.
                    // If it has internal one, it might be exposed as 'Frame'.
                    
                    // Let's try to access `RootNavigation.Frame`.
                    // If that fails during compilation, we'll fix it.
                    // But to be safer, let's use pattern matching on the Content.
                    
                    // Actually, I can just use Application.Current.MainWindow... but I am in MainWindow.
                    
                    // Let's try to find the DashboardPage by checking the NavigationService content if possible.
                    
                    // Assuming Wpf.Ui NavigationView exposes `GetNavigationControl()` or similar?
                    // Let's look at `LS` of WpfUi-repo to see if I can guess the API? No, that's too slow.
                    
                    // Let's try `RootNavigation.Content`? No, NavigationView's content is the menu items usually.
                    
                    // Let's try a visual tree helper approach to find the active Page.
                    // Or... since I am the one writing the code, I can assume the structure.
                    
                    // Let's modify MainWindow.xaml to explicitly bind a Frame if it's not there, 
                    // OR just rely on the fact that we can iterate logical children.
                    
                    // Actually, if I look at `MainWindow.xaml` again:
                    // It just has `<ui:NavigationView ... />`.
                    // This implies the NavigationView generates its own Frame or ContentPresenter.
                    
                    // Let's try to use a simple helper method to find the DashboardPage.
                    
                    var page = FindChild<DashboardPage>(this);
                    if (page != null)
                    {
                        if (page.ViewModel.ScanFileCommand.CanExecute(targetFile))
                        {
                            page.ViewModel.ScanFileCommand.Execute(targetFile);
                        }
                    }
                }
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private T? FindChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;

            int childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild) return typedChild;

                var result = FindChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
