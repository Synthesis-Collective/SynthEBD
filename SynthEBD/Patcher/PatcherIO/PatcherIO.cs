using Microsoft.Extensions.Logging;
using Mutagen.Bethesda.Skyrim;
using System.IO;

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
            CustomMessageBox.DisplayNotificationOK("Could not save text file", "Error: could not save text file to " + path + ". Exception: " + Environment.NewLine + error);
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
            var writeParams = new Mutagen.Bethesda.Plugins.Binary.Parameters.BinaryWriteParameters()
            {
                MastersListOrdering = new Mutagen.Bethesda.Plugins.Binary.Parameters.MastersListOrderingByLoadOrder(environmentProvider.LoadOrder)
            };
            outputMod.WriteToBinary(patchOutputPath, writeParams);
            logger.LogMessage("Wrote output file at " + patchOutputPath + ".");
        }
        catch (Exception e)
        {
            errStr = ExceptionLogger.GetExceptionStack(e);
            logger.LogMessage("Failed to write new patch. Error: " + Environment.NewLine + errStr);
            logger.LogErrorWithStatusUpdate("Could not write output file to " + patchOutputPath, ErrorType.Error); 
        };
    }
    public void TryCopyResourceFile(string sourcePath, string destPath, Logger logger)
    {
        if (!File.Exists(sourcePath))
        {
            logger.LogErrorWithStatusUpdate("Could not find " + sourcePath, ErrorType.Error);
            return;
        }

        try
        {
            PatcherIO.CreateDirectoryIfNeeded(destPath, PatcherIO.PathType.File);
            File.Copy(sourcePath, destPath, true);
        }
        catch
        {
            logger.LogErrorWithStatusUpdate("Could not copy " + sourcePath + "to " + destPath, ErrorType.Error);
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