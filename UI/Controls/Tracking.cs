using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Shadeplay.UI.Controls;

/// <summary>
/// Letter-spaced labels ("NOW DIMMING FOR"). WPF has no letter-spacing, so a hair space goes
/// between the letters; screen readers get the text without them.
/// <code>&lt;TextBlock c:Tracking.Text="NOW DIMMING FOR" /&gt;</code>
/// </summary>
public static class Tracking
{
    const char HairSpace = ' ';

    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Tracking), new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);

    static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        string text = e.NewValue as string ?? "";
        tb.Text = Spread(text);
        AutomationProperties.SetName(tb, text);
    }

    static string Spread(string text)
    {
        var sb = new StringBuilder(text.Length * 2);
        var letters = StringInfo.GetTextElementEnumerator(text);
        bool first = true;
        while (letters.MoveNext())
        {
            if (!first) sb.Append(HairSpace);
            sb.Append(letters.GetTextElement());
            first = false;
        }
        return sb.ToString();
    }
}
