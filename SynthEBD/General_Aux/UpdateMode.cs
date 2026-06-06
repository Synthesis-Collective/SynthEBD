using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// Selects the pass performed by an update/migration routine: detect-only versus apply.
    /// </summary>
    public enum UpdateMode
    {
        /// <summary>Report whether an update/migration is needed without modifying anything.</summary>
        Check,
        /// <summary>Apply the update/migration.</summary>
        Perform
    }

}
