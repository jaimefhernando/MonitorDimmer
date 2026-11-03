using System.Globalization;
using System.Windows.Data;

namespace Shadeplay.UI.Controls;

/// <summary>
/// Binds a segmented RadioButton to one value of an enum setting:
/// <c>IsChecked="{Binding Method, Converter={StaticResource EnumMatch}, ConverterParameter=Brightness}"</c>.
/// </summary>
public sealed class EnumMatch : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true || parameter is not string name) return Binding.DoNothing;
        // Also works for a bool setting: ConverterParameter=False checks the "off" segment
        return targetType.IsEnum ? Enum.Parse(targetType, name) : System.Convert.ChangeType(name, targetType, CultureInfo.InvariantCulture);
    }
}
