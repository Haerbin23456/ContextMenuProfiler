using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContextMenuProfiler.UI.Core.Services;
using System.Diagnostics;
using System.Windows;
using System;
using System.Collections.ObjectModel;

namespace ContextMenuProfiler.UI.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<LanguageOption> _languageOptions = new();

        [ObservableProperty]
        private LanguageOption? _selectedLanguage;

        public SettingsViewModel()
        {
            LanguageOptions = new ObservableCollection<LanguageOption>(LocalizationService.Instance.AvailableLanguages);
            string savedCode = UserPreferencesService.Load().LanguageCode;
            SelectedLanguage = LanguageOptions.FirstOrDefault(l => l.Code.Equals(savedCode, StringComparison.OrdinalIgnoreCase))
                               ?? LanguageOptions.FirstOrDefault(l => l.Code == "auto");
        }

        partial void OnSelectedLanguageChanged(LanguageOption? value)
        {
            if (value == null) return;
            LocalizationService.Instance.SetLanguage(value.Code);
        }

        [RelayCommand]
        private void RestartExplorer()
        {
            if (MessageBox.Show(LocalizationService.Instance["Dialog.ConfirmRestart.Message"], LocalizationService.Instance["Dialog.ConfirmRestart.Title"], MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    foreach (var process in Process.GetProcessesByName("explorer"))
                    {
                        process.Kill();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format(LocalizationService.Instance["Dialog.Error.RestartExplorer"], ex.Message), LocalizationService.Instance["Dialog.Error.Title"]);
                }
            }
        }
    }
}
