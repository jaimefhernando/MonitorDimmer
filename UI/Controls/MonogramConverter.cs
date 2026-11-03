using System.Globalization;
using System.Windows.Data;

namespace MonitorDimmer.UI.Controls;

/// <summary>Two letters standing in for a game's cover: "Elden Ring" → "ER", "Among Us" → "AU".</summary>
public sealed class MonogramConverter : IValueConverter
{
    public static MonogramConverter Instance { get; } = new();

    public static string Of(string name)
    {
        var words = name.Split(new[] { ' ', '-', ':', '_', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0])).ToList();
        if (words.Count == 0) return "?";
        if (words.Count == 1) return (words[0].Length >= 2 ? words[0][..2] : words[0]).ToUpperInvariant();
        return (words[0][..1] + words[1][..1]).ToUpperInvariant();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Of(value as string ?? "");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
