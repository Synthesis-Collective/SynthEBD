using System.Windows.Markup;

namespace SynthEBD;

/// <summary>XAML markup extension that supplies the set of values of an enum type as an
/// <c>ItemsSource</c> (e.g. for a ComboBox), avoiding an ObjectDataProvider. For nullable enum types it
/// prepends a leading empty slot so the binding can represent "no selection".</summary>
public class EnumBindingSourceExtension : MarkupExtension // https://brianlagunas.com/a-better-way-to-data-bind-enums-in-wpf/
{
    private Type _enumType;
    /// <summary>The enum type whose values are provided. May be a nullable enum; setting a non-enum
    /// (non-nullable-enum) type throws <see cref="ArgumentException"/>.</summary>
    public Type EnumType
    {
        get { return this._enumType; }
        set
        {
            if (value != this._enumType)
            {
                if (null != value)
                {
                    Type enumType = Nullable.GetUnderlyingType(value) ?? value;

                    if (!enumType.IsEnum)
                        throw new ArgumentException("Type must be for an Enum.");
                }

                this._enumType = value;
            }
        }
    }

    /// <summary>Parameterless constructor for XAML usage where <see cref="EnumType"/> is set as an attribute.</summary>
    public EnumBindingSourceExtension() { }

    /// <summary>Constructs the extension with the enum type supplied positionally.</summary>
    public EnumBindingSourceExtension(Type enumType)
    {
        this.EnumType = enumType;
    }

    /// <summary>Returns the enum's values; for a nullable enum, returns an array one element longer with a
    /// leading default/empty entry. Throws <see cref="InvalidOperationException"/> if no type was set.</summary>
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (null == this._enumType)
            throw new InvalidOperationException("The EnumType must be specified.");

        Type actualEnumType = Nullable.GetUnderlyingType(this._enumType) ?? this._enumType;
        Array enumValues = Enum.GetValues(actualEnumType);

        if (actualEnumType == this._enumType)
            return enumValues;

        Array tempArray = Array.CreateInstance(actualEnumType, enumValues.Length + 1);
        enumValues.CopyTo(tempArray, 1);
        return tempArray;
    }
}