using System;
using System.Globalization;
using System.Windows.Data;
using ContextMenuProfiler.UI.Core;
using Wpf.Ui.Controls;

namespace ContextMenuProfiler.UI.Converters
{
    public class TypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string type)
            {
                if (string.Equals(type, BenchmarkSemantics.Type.Uwp, StringComparison.OrdinalIgnoreCase)) return SymbolRegular.AppGeneric24;
                if (string.Equals(type, BenchmarkSemantics.Type.Com, StringComparison.OrdinalIgnoreCase)) return SymbolRegular.PuzzlePiece24; // Default generic icon
                if (string.Equals(type, BenchmarkSemantics.Type.Static, StringComparison.OrdinalIgnoreCase)) return SymbolRegular.WindowConsole20;
            }
            return SymbolRegular.PuzzlePiece24; // Default fallback
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}