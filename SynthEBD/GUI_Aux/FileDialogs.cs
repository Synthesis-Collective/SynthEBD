namespace SynthEBD;

/// <summary>UI helper for file operations that need a confirmation dialog, currently deletion.</summary>
public class FileDialogs
{
    private readonly Logger _logger;
    public FileDialogs(Logger logger)
    {
        _logger = logger;
    }
    /// <summary>Prompts the user to confirm, then attempts to permanently delete the file at
    /// <paramref name="path"/>. Deletion failures are logged (as a warning) rather than thrown.</summary>
    /// <param name="path">The file path to delete.</param>
    /// <param name="filetype">A human-readable noun for the file inserted into the prompt text.</param>
    /// <returns><c>true</c> if the user confirmed and deletion succeeded (or the file was absent);
    /// <c>false</c> if the user declined or deletion threw.</returns>
    public bool ConfirmFileDeletion(string path, string filetype)
    {
        if (MessageWindow.DisplayNotificationYesNo("Confirm Deletion", "Are you sure you want to permanently delete this " + filetype + "?"))
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
                return true;
            }
            catch
            {
                _logger.CallTimedLogErrorWithStatusUpdateAsync("Could not delete file at " + path, ErrorType.Warning, 5);
                return false;
            }
        }
        else
        {
            return false;
        }
    }
}