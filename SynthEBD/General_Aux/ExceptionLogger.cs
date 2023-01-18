namespace SynthEBD;

public class ExceptionLogger
{
    public static string GetExceptionStack(Exception e)
    {
        return GetExceptionStack(e, "");
    }
    private static string GetExceptionStack(Exception e, string error)
    {
        error += e.Message + Environment.NewLine + e.StackTrace + Environment.NewLine + Environment.NewLine;
        if (e.InnerException != null)
        {
            return GetExceptionStack(e.InnerException, error);
        }
        else
        {
            return error;
        }
    }
}