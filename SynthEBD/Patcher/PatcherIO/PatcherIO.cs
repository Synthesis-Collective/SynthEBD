using Microsoft.Extensions.Logging;
using Mutagen.Bethesda.Skyrim;
using System.IO;
using Mutagen.Bethesda.Plugins.Analysis.DI;
using Mutagen.Bethesda.Plugins.Exceptions;

namespace SynthEBD;

public class PatcherIO
{
    public enum PathType
    {
        File,
        Directory
    }
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
    public static async Task WriteTextFile(string path, List<string> contents, Logger logger)
    {
        await WriteTextFile(path, string.Join(Environment.NewLine, contents), logger);
    }

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

    public static void WritePatch(string patchOutputPath, ISkyrimMod outputMod, Logger logger, IEnvironmentStateProvider environmentProvider)
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
                outputMod.BeginWrite
                    .ToPath(patchOutputPath)
                    .WithLoadOrder(environmentProvider.LoadOrder)
                    .Write();
            }
            catch (TooManyMastersException)
            {
                logger.CallTimedLogErrorWithStatusUpdateAsync(
                    "Error: Too many masters for a single plugin file. Please try enabling SkyPatcher Mode in SynthEBD's Texture and/or Height menus",
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

    public bool TryCopyResourceFile(string sourcePath, string destPath, Logger logger)
    {
        return TryCopyResourceFile(sourcePath, destPath, logger, out _);
    }
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