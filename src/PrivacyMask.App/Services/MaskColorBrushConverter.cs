using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PrivacyMask.Core.Models;

namespace PrivacyMask.App.Services;

public sealed class MaskColorBrushConverter : IValueConverter
{
    private static readonly IReadOnlyDictionary<MaskColorOption, SolidColorBrush> Brushes =
        Enum.GetValues<MaskColorOption>().ToDictionary(value => value, value =>
        {
            var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value.ToString()));
            brush.Freeze();
            return brush;
        });
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is MaskColorOption option ? Brushes[option] : System.Windows.DependencyProperty.UnsetValue;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}
