using System.Globalization;
using System.Windows.Data;
using System.Windows;

namespace SynthEBD;

/// <summary>Converts a <see cref="BodyShapeSelectionMode"/> to <c>Visible</c> when it is
/// <c>BodySlide</c> (otherwise <c>Collapsed</c>), to show the BodySlide-specific UI.</summary>
public class BodySlideVisibilityConverter : System.Windows.Data.IValueConverter
{
    /// <summary>Returns <c>Visible</c> if the mode is <c>BodySlide</c>, else <c>Collapsed</c>.</summary>
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        bool visibility = false;
        if (value is BodyShapeSelectionMode)
        {
            visibility = (BodyShapeSelectionMode)value == BodyShapeSelectionMode.BodySlide;
        }
        return visibility ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
    /// <summary>Maps a <see cref="System.Windows.Visibility"/> back to a bool (<c>Visible</c> =&gt; true);
    /// note this returns a bool, not the original <see cref="BodyShapeSelectionMode"/>.</summary>
    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        System.Windows.Visibility visibility = (System.Windows.Visibility)value;
        return (visibility == System.Windows.Visibility.Visible);
    }
}
/// <summary>Converts a <see cref="BodyShapeSelectionMode"/> to <c>Visible</c> when it is
/// <c>BodyGen</c> (otherwise <c>Collapsed</c>), to show the BodyGen-specific UI.</summary>
public class BodyGenVisibilityConverter : System.Windows.Data.IValueConverter
{
    /// <summary>Returns <c>Visible</c> if the mode is <c>BodyGen</c>, else <c>Collapsed</c>.</summary>
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        bool visibility = false;
        if (value is BodyShapeSelectionMode)
        {
            visibility = (BodyShapeSelectionMode)value == BodyShapeSelectionMode.BodyGen;
        }
        return visibility ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
    /// <summary>Maps a <see cref="System.Windows.Visibility"/> back to a bool (<c>Visible</c> =&gt; true);
    /// note this returns a bool, not the original <see cref="BodyShapeSelectionMode"/>.</summary>
    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        System.Windows.Visibility visibility = (System.Windows.Visibility)value;
        return (visibility == System.Windows.Visibility.Visible);
    }
}

/// <summary>Converts a <see cref="DrafterTextureSource"/> to <c>Visible</c> when it is <c>Archives</c>,
/// to show the archive-source UI in the config drafter.</summary>
public class ConfigDrafterVisibilityConverterArchive : System.Windows.Data.IValueConverter
{
    /// <summary>Returns <c>Visible</c> if the source is <c>Archives</c>, else <c>Collapsed</c>.</summary>
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        bool visibility = false;
        if (value is DrafterTextureSource)
        {
            visibility = (DrafterTextureSource)value == DrafterTextureSource.Archives;
        }
        return visibility ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
    /// <summary>Maps a <see cref="System.Windows.Visibility"/> back to a bool (<c>Visible</c> =&gt; true).</summary>
    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        System.Windows.Visibility visibility = (System.Windows.Visibility)value;
        return (visibility == System.Windows.Visibility.Visible);
    }
}

/// <summary>Converts a <see cref="DrafterTextureSource"/> to <c>Visible</c> when it is <c>Directories</c>,
/// to show the loose-directory-source UI in the config drafter.</summary>
public class ConfigDrafterVisibilityConverterDirectory : System.Windows.Data.IValueConverter
{
    /// <summary>Returns <c>Visible</c> if the source is <c>Directories</c>, else <c>Collapsed</c>.</summary>
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        bool visibility = false;
        if (value is DrafterTextureSource)
        {
            visibility = (DrafterTextureSource)value == DrafterTextureSource.Directories;
        }
        return visibility ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
    /// <summary>Maps a <see cref="System.Windows.Visibility"/> back to a bool (<c>Visible</c> =&gt; true).</summary>
    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        System.Windows.Visibility visibility = (System.Windows.Visibility)value;
        return (visibility == System.Windows.Visibility.Visible);
    }
}

/// <summary>Converts an <see cref="ExchangeMode"/> to <c>Visible</c> when it is <c>Import</c>, to show
/// the import-specific UI in the BodySlide exchange dialog.</summary>
public class BSImportVisibilityConverter : System.Windows.Data.IValueConverter
{
    /// <summary>Returns <c>Visible</c> if the mode is <c>Import</c>, else <c>Collapsed</c>.</summary>
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        bool visibility = false;
        if (value is ExchangeMode)
        {
            visibility = (ExchangeMode)value == ExchangeMode.Import;
        }
        return visibility ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
    /// <summary>Maps a <see cref="System.Windows.Visibility"/> back to a bool (<c>Visible</c> =&gt; true).</summary>
    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        System.Windows.Visibility visibility = (System.Windows.Visibility)value;
        return (visibility == System.Windows.Visibility.Visible);
    }
}

/// <summary>Converts a <see cref="bool"/> to a grid-row <see cref="GridLength"/>: <c>Auto</c> when true,
/// zero height (collapsed row) when false. Used to show/hide grid rows.</summary>
//https://stackoverflow.com/questions/2502178/hide-grid-row-in-wpf
// visibility hidden if binding is FALSE
[ValueConversion(typeof(bool), typeof(GridLength))]
public class BoolToGridRowHeightConverter : IValueConverter
{
    /// <summary>Returns an <c>Auto</c> <see cref="GridLength"/> when <paramref name="value"/> is true,
    /// otherwise a zero-height length.</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return ((bool)value == true) ? new GridLength(1, GridUnitType.Auto) : new GridLength(0);
    }

    /// <summary>Not used; returns <c>null</c>.</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {    // Don't need any convert back
        return null;
    }
}

/// <summary>Converts an <see cref="int"/> to <c>Visible</c> when it equals the (int-parsed)
/// <c>ConverterParameter</c>, else <c>Collapsed</c>. Used to show exactly one item's editor at a time.</summary>
// Visibility.Visible when the int value matches the converter parameter
// (parsed as int). Used by the CharacterViewer's per-light editor to show
// exactly one light's editor at a time.
[ValueConversion(typeof(int), typeof(Visibility))]
public class IntEqualsToVisibilityConverter : IValueConverter
{
    /// <summary>Returns <c>Visible</c> if the bound int equals the parameter int, else <c>Collapsed</c>.</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int v = value is int i ? i : System.Convert.ToInt32(value);
        int p = parameter is int pi ? pi : int.Parse((string)parameter, culture);
        return v == p ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Not used; returns <c>null</c>.</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null!;
}

/// <summary>Converts an enum to <c>Visible</c> when its name matches the <c>ConverterParameter</c>, or
/// (with a leading <c>!</c>) when it does NOT match; else <c>Collapsed</c>. Comparison is ordinal on the
/// enum's <c>ToString()</c>.</summary>
// Shows an element only when an enum value matches (or, with a leading '!', does NOT match) the
// ConverterParameter name. Used by the Key Vertices grid to swap the box-coordinate text for a region
// dropdown on Region-strategy rows: parameter "Region" reveals the dropdown, "!Region" reveals the text.
[ValueConversion(typeof(Enum), typeof(Visibility))]
public class EnumEqualsToVisibilityConverter : IValueConverter
{
    /// <summary>Returns <c>Visible</c>/<c>Collapsed</c> based on whether the enum name matches the
    /// parameter (or, with a leading <c>!</c>, does not match).</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string param = (parameter as string) ?? "";
        bool negate = param.StartsWith("!", StringComparison.Ordinal);
        if (negate) param = param.Substring(1);
        bool matches = value != null && string.Equals(value.ToString(), param, StringComparison.Ordinal);
        bool visible = negate ? !matches : matches;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Not used; returns <c>null</c>.</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null!;
}

/// <summary>Converts a <see cref="bool"/> to <see cref="Visibility"/>, with the mapping direction
/// selected by the <c>ConverterParameter</c> name (<c>Normal</c> or <c>Inverted</c>).</summary>
//https://stackoverflow.com/a/2427307
[ValueConversion(typeof(bool), typeof(Visibility))]
public class InvertableBooleanToVisibilityConverter : IValueConverter
{
    /// <summary>Parameter values controlling whether the bool-to-visibility mapping is normal or inverted.</summary>
    enum Parameters
    {
        Normal, Inverted
    }

    /// <summary>Returns <c>Visible</c>/<c>Collapsed</c> from the bool; inverted when the parameter is
    /// <c>Inverted</c>.</summary>
    public object Convert(object value, Type targetType,
                          object parameter, CultureInfo culture)
    {
        var boolValue = (bool)value;
        var direction = (Parameters)Enum.Parse(typeof(Parameters), (string)parameter);

        if (direction == Parameters.Inverted)
            return !boolValue ? Visibility.Visible : Visibility.Collapsed;

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Not used; returns <c>null</c>.</summary>
    public object ConvertBack(object value, Type targetType,
        object parameter, CultureInfo culture)
    {
        return null;
    }
}