using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// Settings-schema version markers, ordered oldest to newest. Consumed by the
    /// settings-update system to decide which migrations to apply when loading configs
    /// written by older releases. Suffixes approximate the originating SynthEBD version.
    /// </summary>
    public enum Version
    {
        /// <summary>Earliest tracked schema (0.9.x lineage).</summary>
        v090,
        /// <summary>Schema revision from the 1.0.1.x series.</summary>
        v1012,
        /// <summary>Schema revision from the 1.0.3.x series.</summary>
        v1038
    }
}
