using System.Globalization;
using System.Windows;
using System.Windows.Data;
using TcpUdpTester.Models;

namespace TcpUdpTester.Converters;

/// <summary>SendMode と文字列パラメータが一致するときのみ Visible を返す</summary>
[ValueConversion(typeof(SendMode), typeof(Visibility))]
public sealed class SendModeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SendMode mode && parameter is string param)
            return mode.ToString() == param ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
