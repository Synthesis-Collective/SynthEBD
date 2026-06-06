using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

/// <summary>Reflection-based extension helpers usable on any object.</summary>
public static class ExtensionMethods
{
    /// <summary>
    /// Reflectively reads and returns the value of the named public property from
    /// <paramref name="obj"/>.
    /// </summary>
    /// <typeparam name="T">Statically known type whose property metadata is inspected.</typeparam>
    /// <param name="obj">Instance to read the property value from.</param>
    /// <param name="propertyName">Name of a public property declared on <typeparamref name="T"/>.</param>
    /// <returns>The property's current value, typed as <see langword="dynamic"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when no such property exists on <typeparamref name="T"/>.</exception>
    /// <remarks>
    /// Despite the name, this returns the property's <em>current</em> value on the supplied instance; it
    /// yields a "default" only when callers pass a freshly default-constructed object. Lookup uses the
    /// static type <typeparamref name="T"/> (runtime-only members are not seen) and reflects on every
    /// call without caching.
    /// </remarks>
    public static dynamic GetDefaultValue<T>(this T obj, string propertyName)
    {
        PropertyInfo propertyInfo = typeof(T).GetProperty(propertyName);

        if (propertyInfo != null)
        {
            object defaultValue = propertyInfo.GetValue(obj);
            return defaultValue;
        }

        // Handle the case where the property doesn't exist.
        throw new ArgumentException($"Property '{propertyName}' not found in class {typeof(T).Name}");
    }
}
