using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// Persisted per-NPC head-part assignment for one head-part type, recording the chosen part (or that
    /// it was deliberately randomized to none) so the choice stays consistent across patcher runs.
    /// </summary>
    public class HeadPartConsistency
    {
        public FormKey FormKey { get; set; }
        public string EditorID { get; set; }
        public bool RandomizedToNone { get; set; } = false;
        public bool Initialized { get; set; } = false;
    }
}
