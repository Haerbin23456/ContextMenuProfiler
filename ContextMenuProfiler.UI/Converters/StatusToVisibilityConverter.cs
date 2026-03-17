using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ContextMenuProfiler.UI.Core;
using ContextMenuProfiler.UI.Core.Services;

namespace ContextMenuProfiler.UI.Converters
{
    public class StatusToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string? param = parameter as string;

            if (param == "NotActive" && value is HookStatus hookStatus)
            {
                return hookStatus != HookStatus.Active ? Visibility.Visible : Visibility.Collapsed;
            }

            if (param == "NotUWP")
            {
                string? type = value as string;
                return !BenchmarkSemantics.IsPackagedExtensionType(type) ? Visibility.Visible : Visibility.Collapsed;
            }

            if (param == "Fallback")
            {
                string? statusStr = value as string;
                bool isFallback = BenchmarkSemantics.IsFallbackLikeStatus(statusStr);
                return isFallback ? Visibility.Visible : Visibility.Collapsed;
            }

            if (value is string status)
            {
                bool isWarning = BenchmarkSemantics.IsWarningLikeStatus(status);
                
                if (param == "Inverse")
                {
                    return isWarning ? Visibility.Collapsed : Visibility.Visible;
                }
                
                return isWarning ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}