using Microsoft.Extensions.Logging;
using Mutagen.Bethesda.Skyrim;
using System.IO;

namespace SynthEBD;

public class PatcherIO
{
    private readonly Logger _logger;
    public PatcherIO(Logger logger)
    {
        _logger = logger;
    }
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

    public async Task WriteTextFile(string path, string contents)
    {
        var file = CreateDirectoryIfNeeded(path, PathType.File);

        try
        {
            await File.WriteAllTextAsync(file.FullName, contents);
        }
        catch
        {
            _logger.LogError("Could not create file at " + path);
        }
    }
    public async Task WriteTextFile(string path, List<string> contents)
    {
        await WriteTextFile(path, string.Join(Environment.NewLine, contents));
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
            var error = ExceptionLogger.GetExceptionStack(e, "");
            CustomMessageBox.DisplayNotificationOK("Could not save text file", "Error: could not save text file to " + path + ". Exception: " + Environment.NewLine + error);
        }
    }

    public void WritePatch(string patchOutputPath, SkyrimMod outputMod)
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
                errStr = ExceptionLogger.GetExceptionStack(e, errStr);
                _logger.LogMessage("Failed to delete previous version of patch. Error: " + Environment.NewLine + errStr);
                _logger.LogErrorWithStatusUpdate("Could not write output file to " + patchOutputPath, ErrorType.Error);
                return;
            }
        }

        try
        {
            var writeParams = new Mutagen.Bethesda.Plugins.Binary.Parameters.BinaryWriteParameters()
            {
                MastersListOrdering = new Mutagen.Bethesda.Plugins.Binary.Parameters.MastersListOrderingByLoadOrder(PatcherEnvironmentProvider.Instance.Environment.LoadOrder)
            };
            outputMod.WriteToBinary(patchOutputPath, writeParams);
            _logger.LogMessage("Wrote output file at " + patchOutputPath + ".");
        }
        catch (Exception e)
        {
            errStr = ExceptionLogger.GetExceptionStack(e, errStr);
            _logger.LogMessage("Failed to write new patch. Error: " + Environment.NewLine + errStr);
            _logger.LogErrorWithStatusUpdate("Could not write output file to " + patchOutputPath, ErrorType.Error); 
        };
    }
    public void TryCopyResourceFile(string sourcePath, string destPath)
    {
        if (!File.Exists(sourcePath))
        {
            _logger.LogErrorWithStatusUpdate("Could not find " + sourcePath, ErrorType.Error);
            return;
        }

        try
        {
            PatcherIO.CreateDirectoryIfNeeded(destPath, PatcherIO.PathType.File);
            File.Copy(sourcePath, destPath, true);
        }
        catch
        {
            _logger.LogErrorWithStatusUpdate("Could not copy " + sourcePath + "to " + destPath, ErrorType.Error);
        }
    }

    public bool TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception e)
            {
                _logger.LogErrorWithStatusUpdate("Could not delete file - see log", ErrorType.Warning);
                string error = ExceptionLogger.GetExceptionStack(e, "");
                _logger.LogMessage("Could not delete file: " + path + Environment.NewLine + error);
                return false;
            }
        }
        return true;
    }

    public bool TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch (Exception e)
            {
                _logger.LogErrorWithStatusUpdate("Could not delete directory - see log", ErrorType.Warning);
                string error = ExceptionLogger.GetExceptionStack(e, "");
                _logger.LogMessage("Could not delete directory: " + path + Environment.NewLine + error);
                return false;
            }
        }
        return true;
    }
}