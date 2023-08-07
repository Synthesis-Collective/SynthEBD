namespace SynthEBD;

public class ExceptionLogger
{
    public static string GetExceptionStack(Exception e)
    {
        return GetExceptionStack(e, "", 0);
    }
    private static string GetExceptionStack(Exception e, string error, int layer)
    {
        error += Environment.NewLine + "======= Layer " + layer.ToString() + ": " + Environment.NewLine + e.Message + Environment.NewLine + e.StackTrace + Environment.NewLine + Environment.NewLine;
        if (e.InnerException != null)
        {
            return GetExceptionStack(e.InnerException, error, layer + 1);
        }
        else
        {
            return error;
        }
    }
}