using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class TrimPath
    {
        public TrimPath()
        {
            this.Extension = "";
            this.PathToTrim = "";
        }

        public string Extension { get; set; }
        public string PathToTrim { get; set; }
    }
}
