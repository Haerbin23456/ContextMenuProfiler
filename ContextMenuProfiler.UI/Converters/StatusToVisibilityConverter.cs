using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ContextMenuProfiler.UI.Core;
using ContextMenuProfiler.UI.Core.Services;

namespace ContextMenuProfiler.UI.Converters
{
    public enum StatusVisibilityMode
    {
        Default,
        NotActive,
        NotUwp,
        Fallback,
        Inverse
    }

    public class StatusToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            StatusVisibilityMode mode = ResolveMode(parameter);

            if (mode == StatusVisibilityMode.NotActive && value is HookStatus hookStatus)
            {
                return hookStatus != HookStatus.Active ? Visibility.Visible : Visibility.Collapsed;
            }

            if (mode == StatusVisibilityMode.NotUwp)
            {
                string? type = value as string;
                return !BenchmarkSemantics.IsPackagedExtensionType(type) ? Visibility.Visible : Visibility.Collapsed;
            }

            if (mode == StatusVisibilityMode.Fallback)
            {
                string? statusStr = value as string;
                bool isFallback = BenchmarkSemantics.IsFallbackLikeStatus(statusStr);
                return isFallback ? Visibility.Visible : Visibility.Collapsed;
            }

            if (value is string status)
            {
                bool isWarning = BenchmarkSemantics.IsWarningLikeStatus(status);
                
                if (mode == StatusVisibilityMode.Inverse)
                {
                    return isWarning ? Visibility.Collapsed : Visibility.Visible;
                }
                
                return isWarning ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        private static StatusVisibilityMode ResolveMode(object? parameter)
        {
            if (parameter is StatusVisibilityMode typedMode)
            {
                return typedMode;
            }

            if (parameter is string raw
                && Enum.TryParse(raw, ignoreCase: true, out StatusVisibilityMode parsedMode))
            {
                return parsedMode;
            }

            return StatusVisibilityMode.Default;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}