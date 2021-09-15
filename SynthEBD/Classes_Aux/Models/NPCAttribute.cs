using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class NPCAttribute
    {
        public NPCAttribute()
        {
            this.Path = "";
            this.Value = "";
        }

        public string Path { get; set; }
        public string Value { get; set; }
    }
}
