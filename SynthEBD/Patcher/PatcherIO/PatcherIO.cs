using Microsoft.Extensions.Logging;
using Mutagen.Bethesda.Skyrim;
using System.IO;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Analysis;
using Mutagen.Bethesda.Plugins.Analysis.DI;
using Mutagen.Bethesda.Plugins.Exceptions;
using Noggog;

namespace SynthEBD;

/// <summary>File/directory IO helpers used across the patcher's output stage: ensuring directories exist, writing text files, writing the output plugin, and copying/deleting resource files.</summary>
public class PatcherIO
{
    /// <summary>Whether a path refers to a file or a directory.</summary>
    public enum PathType
    {
        /// <summary>The path is a file (its parent directory is created).</summary>
        File,
        /// <summary>The path is a directory (created directly).</summary>
        Directory
    }
    /// <summary>Ensures the directory for <paramref name="path"/> exists — the parent directory for a file path, or the directory itself — creating it if needed.</summary>
    /// <returns>The <see cref="FileInfo"/> (for a file path) or <see cref="DirectoryInfo"/> (for a directory path).</returns>
    public static dynamic CreateDirectoryIfNeeded(string path, PathType type)
    {
        if (type == PathType.File)
        {
            FileInfo file = new FileInfo(path);
            file.Directory.Create(); // If the directory already exists, this method does nothing.
            return file;
        }
        else
        {
            DirectoryInfo directory = new DirectoryInfo(path);
            directory.Create();
            return directory;
        }
    }

    /// <summary>Writes text to a file (creating its directory first), logging an error via <paramref name="logger"/> on failure.</summary>
    public static async Task WriteTextFile(string path, string contents, Logger logger)
    {
        var file = CreateDirectoryIfNeeded(path, PathType.File);

        try
        {
            await File.WriteAllTextAsync(file.FullName, contents);
        }
        catch (Exception e)
        {
            logger.LogError("Could not create file at " + path + "because: " + Environment.NewLine + ExceptionLogger.GetExceptionStack(e));
        }
    }
    /// <summary>Writes a list of lines (joined by newlines) to a file via the string overload.</summary>
    public static async Task WriteTextFile(string path, List<string> contents, Logger logger)
    {
        await WriteTextFile(path, string.Join(Environment.NewLine, contents), logger);
    }

    /// <summary>Writes text to a file, surfacing any failure via a <see cref="MessageWindow"/> dialog (used where no logger is available).</summary>
    public static async Task WriteTextFileStatic(string path, string contents)
    {
        var file = CreateDirectoryIfNeeded(path, PathType.File);

        try
        {
            await File.WriteAllTextAsync(file.FullName, contents);
        }
        catch(Exception e)
        {
            var error = ExceptionLogger.GetExceptionStack(e);
            MessageWindow.DisplayNotificationOK("Could not save text file", "Error: could not save text file to " + path + ". Exception: " + Environment.NewLine + error);
        }
    }

    /// <summary>Writes the generated output plugin to disk (deleting any previous version first), honoring the load order.
    /// When <paramref name="autoSplit"/> is set, chains Mutagen's <c>WithAutoSplit()</c> so an output that would exceed
    /// Skyrim's 255-master limit is split into &lt;name&gt;.esp/&lt;name&gt;_2.esp/... instead of throwing; otherwise a
    /// too-many-masters overflow surfaces an error with a SkyPatcher-mode / Split-Output hint. The commented-out block is
    /// a superseded manual multi-plugin split fallback.</summary>
    public static void WritePatch(string patchOutputPath, ISkyrimMod outputMod, Logger logger, IEnvironmentStateProvider environmentProvider, bool autoSplit)
    {
        string errStr = "";
        if (File.Exists(patchOutputPath))
        {
            try
            {
                File.Delete(patchOutputPath);
            }
            catch (Exception e)
            {
                errStr = ExceptionLogger.GetExceptionStack(e);
                logger.LogMessage("Failed to delete previous version of patch. Error: " + Environment.NewLine + errStr);
                logger.LogErrorWithStatusUpdate("Could not write output file to " + patchOutputPath, ErrorType.Error);
                return;
            }
        }

        try
        {
            logger.LogMessage("Writing output file to " + patchOutputPath + ".");
            try
            {
                // WithAutoSplit() first attempts a normal single-file write and only splits into
                // <name>.esp/<name>_2.esp/... if the output would exceed Skyrim's 255-master limit,
                // so the common (non-overflow) case is unchanged. When disabled, an overflow throws
                // TooManyMastersException as before.
                if (autoSplit)
                {
                    outputMod.BeginWrite
                        .ToPath(patchOutputPath)
                        .WithLoadOrder(environmentProvider.LoadOrder)
                        .WithAutoSplit()
                        .Write();
                }
                else
                {
                    outputMod.BeginWrite
                        .ToPath(patchOutputPath)
                        .WithLoadOrder(environmentProvider.LoadOrder)
                        .Write();
                }
            }
            catch (TooManyMastersException)
            {
                logger.CallTimedLogErrorWithStatusUpdateAsync(
                    "Error: Too many masters for a single plugin file. Please try enabling SkyPatcher Mode in SynthEBD's Texture and/or Height menus, or enable \"Split Output if Over Master Limit\" in General Settings.",
                    ErrorType.Error,
                    5);
            }
            /*
            catch (TooManyMastersException)
            {
                logger.LogMessage(
                    "Too many masters for a single plugin file. Attempting to split the output to multiple plugins: ");
                MultiModFileSplitter splitter = new();
                var splitOutputs = splitter.Split<ISkyrimMod, ISkyrimModGetter>(outputMod, 255);

                logger.LogMessage("New output files: " + string.Join(", ",
                    splitOutputs
                        .Select(x => x.ModKey.FileName + " (" + x.ModHeader.MasterReferences.Count + " Masters)")
                        .ToArray()));

                string? parentDir = Path.GetDirectoryName(patchOutputPath);
                if (parentDir == null)
                {
                    logger.LogError(
                        "Failed to patch - could not write to expected path's directory: " + patchOutputPath);
                    return;
                }

                foreach (var splitMod in splitOutputs)
                {
                    var outputPath = Path.Combine(parentDir, splitMod.ModKey.FileName);
                    splitMod.BeginWrite
                        .ToPath(outputPath)
                        .WithLoadOrder(environmentProvider.LoadOrder)
                        .Write();
                }
            }*/
        }
        catch (Exception e)
        {
            errStr = ExceptionLogger.GetExceptionStack(e);
            logger.LogMessage("Failed to write new patch. Error: " + Environment.NewLine + errStr);
            logger.LogErrorWithStatusUpdate("Could not write output file to " + patchOutputPath, ErrorType.Error);
        }
    }

    /// <summary>
    /// After an auto-split write, the surrogate/duplicated records the SkyPatcher .ini points at may have
    /// moved from "&lt;name&gt;.esp" into "&lt;name&gt;_2.esp"/etc. (their local FormID is preserved, only the
    /// plugin changes). Reads the written split files back and returns a map from each original
    /// output-plugin FormKey to its true post-split FormKey, or <c>null</c> when the output was not split
    /// (the common case, where the .ini needs no remapping). Donor-plugin references are never in the map,
    /// so they are left untouched by the caller.
    /// </summary>
    public static IReadOnlyDictionary<FormKey, FormKey>? BuildSplitFormKeyRemap(ISkyrimMod outputMod, string patchOutputPath, IEnvironmentStateProvider environmentProvider, Logger logger)
    {
        var outputModKey = outputMod.ModKey;

        List<FilePath> splitFiles;
        try
        {
            splitFiles = MultiModFileAnalysis.GetSplitModFiles(new ModPath(outputModKey, patchOutputPath));
        }
        catch (Exception ex)
        {
            // GetSplitModFiles throws on an inconsistent on-disk state; fall back to no remap.
            logger.LogMessage("Could not enumerate split output files for SkyPatcher remap: " + ex.Message);
            return null;
        }

        if (splitFiles.Count <= 1)
        {
            return null; // Not split - the in-memory FormKeys are already correct.
        }

        var remap = new Dictionary<FormKey, FormKey>();
        foreach (var fp in splitFiles)
        {
            string filePath = fp;
            var fileModKey = ModKey.FromFileName(Path.GetFileName(filePath));

            // The base file keeps the original ModKey, so its records still resolve as
            // "<name>.esp|ID" - no remap needed for those.
            if (fileModKey.Equals(outputModKey)) continue;

            try
            {
                using var mod = SkyrimMod.CreateFromBinaryOverlay(filePath, environmentProvider.SkyrimVersion);
                foreach (var rec in mod.EnumerateMajorRecords())
                {
                    // Only records mastered to this split file were created in the output plugin
                    // (surrogate NPCs + duplicated/surrogate armors). Overrides keep their donor
                    // ModKey and must be left alone.
                    if (!rec.FormKey.ModKey.Equals(fileModKey)) continue;
                    remap[new FormKey(outputModKey, rec.FormKey.ID)] = rec.FormKey;
                }
            }
            catch (Exception ex)
            {
                logger.LogMessage("Could not read split output file '" + filePath + "' for SkyPatcher remap: " + ex.Message);
            }
        }

        if (remap.Count > 0)
        {
            logger.LogMessage("Auto-split relocated " + remap.Count + " output record(s); remapped SkyPatcher .ini references across " + splitFiles.Count + " files.");
            return remap;
        }
        return null;
    }

    /// <summary>Copies a resource file to a destination (overwriting), returning whether it succeeded.</summary>
    public bool TryCopyResourceFile(string sourcePath, string destPath, Logger logger)
    {
        return TryCopyResourceFile(sourcePath, destPath, logger, out _);
    }
    /// <summary>Copies a resource file to a destination (creating the directory, overwriting), reporting failure via <paramref name="errorStr"/> and the logger.</summary>
    public bool TryCopyResourceFile(string sourcePath, string destPath, Logger logger, out string errorStr)
    {
        if (!File.Exists(sourcePath))
        {
            errorStr = "Could not find " + sourcePath;
            logger.LogErrorWithStatusUpdate(errorStr, ErrorType.Error);
            return false;
        }

        try
        {
            PatcherIO.CreateDirectoryIfNeeded(destPath, PatcherIO.PathType.File);
            File.Copy(sourcePath, destPath, true);
            errorStr = String.Empty;
            return true;
        }
        catch (Exception e)
        {
            logger.LogErrorWithStatusUpdate("Could not copy " + sourcePath + "to " + destPath, ErrorType.Error);
            errorStr = ExceptionLogger.GetExceptionStack(e);
            return false;
        }
    }

    /// <summary>Deletes a file if it exists, returning false (and logging) on failure.</summary>
    public bool TryDeleteFile(string path, Logger logger)
    {
        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception e)
            {
                logger.LogErrorWithStatusUpdate("Could not delete file - see log", ErrorType.Warning);
                string error = ExceptionLogger.GetExceptionStack(e);
                logger.LogMessage("Could not delete file: " + path + Environment.NewLine + error);
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Deletes the Scripts, Seq, SKSE, and SynthEBD subfolders under the output
    /// data folder. Skipped entirely when the output folder is the same as the
    /// game Data folder to avoid destroying game files.
    /// </summary>
    public void ClearPreviousScriptOutputs(string outputDataFolder, string dataFolderPath, Logger logger)
    {
        if (string.Equals(Path.GetFullPath(outputDataFolder), Path.GetFullPath(dataFolderPath), StringComparison.OrdinalIgnoreCase))
        {
            logger.LogMessage("Output folder matches the Data folder — skipping script output cleanup to avoid deleting game files.");
            return;
        }

        string[] subfolders = { "Scripts", "Seq", "SKSE", "SynthEBD" };
        foreach (var subfolder in subfolders)
        {
            string path = Path.Combine(outputDataFolder, subfolder);
            TryDeleteDirectory(path, logger);
        }
    }

    /// <summary>Recursively deletes a directory if it exists, returning false (and logging) on failure.</summary>
    public bool TryDeleteDirectory(string path, Logger logger)
    {
        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch (Exception e)
            {
                logger.LogErrorWithStatusUpdate("Could not delete directory - see log", ErrorType.Warning);
                string error = ExceptionLogger.GetExceptionStack(e);
                logger.LogMessage("Could not delete directory: " + path + Environment.NewLine + error);
                return false;
            }
        }
        return true;
    }
}