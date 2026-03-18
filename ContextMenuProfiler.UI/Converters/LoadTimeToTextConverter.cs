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

            if (ShouldShowNa(values[1], ms))
            {
                return noneText;
            }

            return $"{ms} ms";
        }

        private static bool ShouldShowNa(object? statusValue, long ms)
        {
            if (ms > 0) return false;

            if (statusValue is BenchmarkStatus status)
            {
                return BenchmarkSemantics.IsFallbackLikeStatus(status)
                    || BenchmarkSemantics.IsNotMeasuredLikeStatus(status);
            }

            string statusText = statusValue?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(statusText)) return true;

            return BenchmarkSemantics.IsFallbackLikeStatus(statusText)
                || BenchmarkSemantics.IsNotMeasuredLikeStatus(statusText);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
