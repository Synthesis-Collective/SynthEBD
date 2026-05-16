using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;

namespace SynthEBD;

/// <summary>One-way converter that maps an enum value to its [Description] attribute text, or the
/// raw name when no attribute is present. Bound to <c>ToolTip</c> on a ComboBox's ItemContainerStyle
/// to surface per-option help text without hard-coding the text in XAML. Falls back to ToString()
/// so enums without attributes still render harmlessly.</summary>
public sealed class EnumDescriptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return string.Empty;
        var enumType = value.GetType();
        if (!enumType.IsEnum) return value.ToString() ?? string.Empty;

        var name = Enum.GetName(enumType, value);
        if (name == null) return value.ToString() ?? string.Empty;

        var field = enumType.GetField(name, BindingFlags.Public | BindingFlags.Static);
        var attr = field?.GetCustomAttribute<DescriptionAttribute>();
        return attr?.Description ?? name;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
