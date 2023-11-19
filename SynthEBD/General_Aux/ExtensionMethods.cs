using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

public static class ExtensionMethods
{
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
