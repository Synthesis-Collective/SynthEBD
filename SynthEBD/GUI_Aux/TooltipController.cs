using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public sealed class TooltipController : INotifyPropertyChanged
    {
        private static TooltipController instance;
        private static object lockObj = new Object();

        public event PropertyChangedEventHandler PropertyChanged;

        private TooltipController() { }

        public bool DisplayToolTips { get; set; }
        public static TooltipController Instance
        {
            get
            {
                lock (lockObj)
                {
                    if (instance == null)
                    {
                        instance = new TooltipController();
                        instance.DisplayToolTips = true;
                    }
                }
                return instance;
            }
        }
    }
}
