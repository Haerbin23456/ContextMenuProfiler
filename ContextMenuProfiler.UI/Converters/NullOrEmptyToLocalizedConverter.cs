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
