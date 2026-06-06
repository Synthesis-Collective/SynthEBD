using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// Implemented by items that expose a user-facing display string, so they can be shown
    /// uniformly in lists, combo boxes, etc.
    /// </summary>
    public interface IHasLabel
    {
        /// <summary>The human-readable label shown for this item in the UI.</summary>
        string Label { get; set; }
    }
}
