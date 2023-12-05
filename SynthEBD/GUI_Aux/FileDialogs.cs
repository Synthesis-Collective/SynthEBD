namespace SynthEBD;

public class FileDialogs
{
    private readonly Logger _logger;
    public FileDialogs(Logger logger)
    {
        _logger = logger;
    }
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