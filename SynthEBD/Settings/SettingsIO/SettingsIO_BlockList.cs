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
        public static BlockList LoadBlockList(out bool loadSuccess)
        {
            BlockList loadedList = new BlockList();

            loadSuccess = true;

            if (File.Exists(PatcherSettings.Paths.BlockListPath))
            {
                loadedList = JSONhandler<BlockList>.LoadJSONFile(PatcherSettings.Paths.BlockListPath, out loadSuccess, out string exceptionStr);

                if (!loadSuccess)
                {
                    var loadedZList = JSONhandler<zEBDBlockList>.LoadJSONFile(PatcherSettings.Paths.BlockListPath, out loadSuccess, out string zExceptionStr);
                    if (loadSuccess)
                    {
                        loadedList = zEBDBlockList.ToSynthEBD(loadedZList);
                    }
                    else
                    {
                        Logger.LogError("Could not parse Block List as either SynthEBD or zEBD list. Error: " + exceptionStr);
                    }
                }
            }

            else if (File.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.BlockListPath)))
            {
                loadedList = JSONhandler<BlockList>.LoadJSONFile(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.BlockListPath), out loadSuccess, out string exceptionStr);

                if (!loadSuccess)
                {
                    var loadedZList = JSONhandler<zEBDBlockList>.LoadJSONFile(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.BlockListPath), out loadSuccess, out string zExceptionStr);
                    if (loadSuccess)
                    {
                        loadedList = zEBDBlockList.ToSynthEBD(loadedZList);
                    }
                    else
                    {
                        Logger.LogError("Could not parse Block List as either SynthEBD or zEBD list. Error: " + exceptionStr);
                    }
                }
            }

            return loadedList;
        }

        public static void SaveBlockList(BlockList blockList, out bool saveSuccess)
        {
            JSONhandler<BlockList>.SaveJSONFile(blockList, PatcherSettings.Paths.BlockListPath, out saveSuccess, out string exceptionStr);
            if (!saveSuccess)
            {
                Logger.LogError("Could not save Block List. Error: " + exceptionStr);
                Logger.CallTimedLogErrorWithStatusUpdateAsync("Could not save Block List to " + PatcherSettings.Paths.BlockListPath, ErrorType.Error, 5);
            }
        }
    }
}
