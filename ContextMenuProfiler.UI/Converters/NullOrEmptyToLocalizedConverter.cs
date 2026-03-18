using ContextMenuProfiler.UI.Core;
using ContextMenuProfiler.UI.Core.Services;
using System;
using System.Globalization;
using System.Windows.Data;

namespace ContextMenuProfiler.UI.Converters
{
    public class NullOrEmptyToLocalizedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (string.Equals(text, BenchmarkSemantics.IconSource.ManifestAppLogo, StringComparison.Ordinal))
                {
                    return LocalizationService.Instance["Dashboard.Value.ManifestAppLogo"];
                }

                return text;
            }

            string key = parameter?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return LocalizationService.Instance[key];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
