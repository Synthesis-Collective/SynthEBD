namespace SynthEBD;

public class ExceptionLogger
{
    public static string GetExceptionStack(Exception e)
    {
        return GetExceptionStack(e, "", 0);
    }
    private static string GetExceptionStack(Exception e, string error, int layer)
    {
        if (e is not ReactiveUI.UnhandledErrorException || e.InnerException is null) // skip logging the top layer since it is not specific to SynthEBD
        {
            error += Environment.NewLine + "======= Layer " + layer.ToString() + ": " + Environment.NewLine + e.Message + Environment.NewLine + e.StackTrace + Environment.NewLine + Environment.NewLine;
        }
        
        if (e as Mutagen.Bethesda.Plugins.Exceptions.TooManyMastersException != null)
        {
            var tooManyMastersEx = e as Mutagen.Bethesda.Plugins.Exceptions.TooManyMastersException;

            error += "Current Masters:" + Environment.NewLine + String.Join(Environment.NewLine, tooManyMastersEx.Masters.Select(x => x.FileName).ToArray()) + Environment.NewLine + Environment.NewLine;
        }

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