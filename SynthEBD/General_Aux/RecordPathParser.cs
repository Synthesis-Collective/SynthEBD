using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

namespace SynthEBD
{
    public class RecordPathParser
    {
        public static Object GetSubObject(Object root, string propertyName)
        {
            var property = root.GetType().GetProperty(propertyName);
            if (property != null)
            {
                return property.GetValue(root);
            }
            else
            {
                return null;
            }
        }

        public static bool PropertyIsRecord(Object root)
        {
            return HasProperty(root.GetType(), "FormKey");
        }

        public static bool HasProperty(Type type, string propertyName)
        {
            return type.GetProperty(propertyName) != null;
        }
    }

    public class ParsedPathObj
    {
        public ParsedPathObj()
        {
            this.CurrentObj = null;
            this.RemainingPath = new List<string>();
        }

        public object CurrentObj { get; set; }
        public List<string> RemainingPath { get; set; }
    }
}
