using System;
using System.Globalization;
using System.Windows.Data;
using ContextMenuProfiler.UI.Core;
using ContextMenuProfiler.UI.Core.Services;

namespace ContextMenuProfiler.UI.Converters
{
    public class LoadTimeToTextConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string noneText = LocalizationService.Instance["Dashboard.Value.None"];
            if (values.Length < 2) return noneText;

            long ms = 0;
            if (values[0] is long l) ms = l;
            if (values[0] is int i) ms = i;

            string status = values[1]?.ToString() ?? string.Empty;

            if (ShouldShowNa(status, ms))
            {
                return noneText;
            }

            return $"{ms} ms";
        }

        private static bool ShouldShowNa(string status, long ms)
        {
            if (ms > 0) return false;

            if (string.IsNullOrWhiteSpace(status)) return true;

            return BenchmarkSemantics.IsFallbackLikeStatus(status) ||
                     status.Contains("Not Measured", StringComparison.OrdinalIgnoreCase) ||
                     status.Contains("Unsupported", StringComparison.OrdinalIgnoreCase) ||
                   status.Contains("No Menu", StringComparison.OrdinalIgnoreCase);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
