using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;
public class EditorIDHandler
{
    public static string GetEditorIDSafely(IMajorRecordGetter getter)
    {
        return getter.EditorID ?? (getter.FormKey.ToString() + " (No EditorID)");
    }
    public static string GetEditorIDSafely<TType>(FormKey formKey) where TType : class, IMajorRecordGetter
    {
        if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<TType>(formKey, out var getter))
        {
            if (getter.EditorID != null)
            {
                return getter.EditorID;
            }
            else
            {
                return getter.FormKey.ToString() + " (No EditorID)";
            }
        }
        else
        {
            return formKey.ToString() + " (Not In Current Load Order)";
        }
    }
}
