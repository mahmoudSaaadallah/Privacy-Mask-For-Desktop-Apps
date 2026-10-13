using System.Globalization;
using System.Windows.Data;

namespace PrivacyMask.App.Services;

public sealed class EnumSelectionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Equals(value, parameter);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? parameter : System.Windows.Data.Binding.DoNothing;
}
