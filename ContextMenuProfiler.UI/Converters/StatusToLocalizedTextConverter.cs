using System;
using System.Globalization;
using System.Windows.Data;
using ContextMenuProfiler.UI.Core;

namespace ContextMenuProfiler.UI.Converters
{
    public class StatusToLocalizedTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is BenchmarkStatus status)
            {
                return BenchmarkSemantics.GetLocalizedStatusText(status);
            }

            if (value is string statusText && BenchmarkSemantics.TryParseStatus(statusText, out BenchmarkStatus parsedStatus))
            {
                return BenchmarkSemantics.GetLocalizedStatusText(parsedStatus);
            }

            return BenchmarkSemantics.GetLocalizedStatusText(BenchmarkStatus.Unknown);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
