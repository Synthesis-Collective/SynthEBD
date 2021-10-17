using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    class SettingsIO_BlockList
    {
        public static BlockList LoadBlockList(Paths paths)
        {
            BlockList loadedList = new BlockList();

            if (File.Exists(paths.BlockListPath))
            {
                try
                {
                    loadedList = DeserializeFromJSON<BlockList>.loadJSONFile(paths.BlockListPath);
                }
                catch
                {
                    try
                    {
                        var loadedZList = DeserializeFromJSON<zEBDBlockList>.loadJSONFile(paths.BlockListPath);
                        loadedList = zEBDBlockList.ToSynthEBD(loadedZList);
                    }
                    catch
                    {
                        // Warn User
                    }
                }
            }

            return loadedList;
        }
    }
}
