using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class BodyShapeDescriptor
    {
        public BodyShapeDescriptor()
        {
            this.Category = "";
            this.Value = "";
            this.DispString = "";
        }
        public string Category { get; set; }
        public string Value { get; set; }
        public string DispString { get; set; }
    }
}
