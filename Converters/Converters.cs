using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AkariOSCompanion.Converters;

/// <summary>Inverts a boolean (used to enable Cancel only when something is selected, etc.).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is bool b && !b;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is bool b && !b;
}

/// <summary>True when an int is greater than zero (e.g. "Install Selected" enabled state).</summary>
public sealed class CountToBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is int i && i > 0;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Maps a download category name to its accent brush.</summary>
public sealed class CategoryToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, Brush> Map = new()
    {
        ["Browsers"]  = New("#EC5862"),
        ["Comms"]     = New("#E0A23A"),
        ["Dev"]       = New("#6AA8EC"),
        ["Gaming"]    = New("#D6589E"),
        ["Utilities"] = New("#4FC9A8"),
    };

    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is string s && Map.TryGetValue(s, out var b) ? b : New("#EC5862");

    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;

    private static SolidColorBrush New(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}

/// <summary>Returns "Selected" when true, "+ Add" when false (Downloads card footer).</summary>
public sealed class SelectedToLabelConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is bool b && b ? "Selected" : "+ Add";
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>True when a chip's own category (value 0) equals the VM's selected category (value 1).</summary>
public sealed class CategoryActiveConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c)
        => values.Length == 2 && Equals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c)
        => Array.Empty<object>();
}
