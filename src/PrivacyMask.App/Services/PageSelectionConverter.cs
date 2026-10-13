using System.Globalization;
using System.Windows.Data;

namespace PrivacyMask.App.Services;

public sealed class PageSelectionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return values.Length == 2 && values[0] is ViewModels.DesktopPage selected
            && values[1] is ViewModels.DesktopPage page && selected == page;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
