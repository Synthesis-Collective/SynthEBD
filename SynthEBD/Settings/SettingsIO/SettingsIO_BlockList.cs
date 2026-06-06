using System.IO;

namespace SynthEBD;

/// <summary>
/// Loads and saves the NPC/plugin <see cref="BlockList"/> to/from JSON, transparently converting a legacy
/// <see cref="zEBDBlockList"/> when the modern format fails to parse. Handles primary/fallback path resolution.
/// </summary>
public class SettingsIO_BlockList
{
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    /// <summary>Injects the logger and path resolver.</summary>
    public SettingsIO_BlockList(Logger logger, SynthEBDPaths paths)
    {
        _logger = logger;
        _paths = paths;
    }
    /// <summary>
    /// Loads the block list from the primary path, then the fallback path. Reads from disk. If the modern
    /// format fails to parse, retries as a legacy <see cref="zEBDBlockList"/> and converts it. Returns a fresh
    /// default if no file is present.
    /// </summary>
    /// <param name="loadSuccess">Set true if loaded (in either format), false if both parses failed.</param>
    /// <returns>The loaded or default <see cref="BlockList"/>.</returns>
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

    /// <summary>
    /// Writes the block list to its configured path. Writes to disk; on failure logs and raises a timed
    /// status-update error.
    /// </summary>
    /// <param name="blockList">The block list to save.</param>
    /// <param name="saveSuccess">Set true on success, false on save failure.</param>
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