using System.IO;

namespace SynthEBD;

public class SettingsIO_BlockList
{
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    public SettingsIO_BlockList(Logger logger, SynthEBDPaths paths)
    {
        _logger = logger;
        _paths = paths;
    }
    public BlockList LoadBlockList(out bool loadSuccess)
    {
        _logger.LogStartupEventStart("Loading BlockList from disk");
        BlockList loadedList = new BlockList();

        loadSuccess = true;

        if (File.Exists(_paths.BlockListPath))
        {
            loadedList = JSONhandler<BlockList>.LoadJSONFile(_paths.BlockListPath, out loadSuccess, out string exceptionStr);

            if (!loadSuccess)
            {
                var loadedZList = JSONhandler<zEBDBlockList>.LoadJSONFile(_paths.BlockListPath, out loadSuccess, out string zExceptionStr);
                if (loadSuccess)
                {
                    loadedList = loadedZList.ToSynthEBD();
                }
                else
                {
                    _logger.LogError("Could not parse Block List as either SynthEBD or zEBD list. Error: " + exceptionStr);
                }
            }
        }

        else if (File.Exists(_paths.GetFallBackPath(_paths.BlockListPath)))
        {
            loadedList = JSONhandler<BlockList>.LoadJSONFile(_paths.GetFallBackPath(_paths.BlockListPath), out loadSuccess, out string exceptionStr);

            if (!loadSuccess)
            {
                var loadedZList = JSONhandler<zEBDBlockList>.LoadJSONFile(_paths.GetFallBackPath(_paths.BlockListPath), out loadSuccess, out string zExceptionStr);
                if (loadSuccess)
                {
                    loadedList = loadedZList.ToSynthEBD();
                }
                else
                {
                    _logger.LogError("Could not parse Block List as either SynthEBD or zEBD list. Error: " + exceptionStr);
                }
            }
        }
        _logger.LogStartupEventEnd("Loading BlockList from disk");
        return loadedList;
    }

    public void SaveBlockList(BlockList blockList, out bool saveSuccess)
    {
        JSONhandler<BlockList>.SaveJSONFile(blockList, _paths.BlockListPath, out saveSuccess, out string exceptionStr);
        if (!saveSuccess)
        {
            _logger.LogError("Could not save Block List. Error: " + exceptionStr);
            _logger.CallTimedLogErrorWithStatusUpdateAsync("Could not save Block List to " + _paths.BlockListPath, ErrorType.Error, 5);
        }
    }
}